namespace Picshare.Services;

using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;
using System.Security.Cryptography;
using System.Text;

public sealed class ImageCacheService
{
    private static readonly SemaphoreSlim DecodeGate = new(2);
    private const int MaxCachePathSegmentLength = 180;
    private const int MaxCacheExtensionLength = 32;
    private readonly object _memoryCacheSync = new();
    private readonly Dictionary<MemoryCacheKey, EncodedMemoryCacheEntry> _memoryCache = new();
    private readonly Dictionary<MemoryCacheKey, BitmapMemoryCacheEntry> _bitmapCache = new();
    private HashSet<MemoryCacheKey> _priority1OriginalImageKeys = new();
    private HashSet<MemoryCacheKey> _priority2OriginalImageKeys = new();
    private HashSet<MemoryCacheKey> _priority1OriginalImageDiskKeys = new();
    private HashSet<MemoryCacheKey> _priority2OriginalImageDiskKeys = new();
    private readonly string _rootPath;
    private AlbumImageCacheLimits _limits = AlbumImageCacheLimits.Default;

    public ImageCacheService(string? localStorageRootPath = null)
    {
        var basePath = GetLocalStorageRootPath(localStorageRootPath);
        _rootPath = Path.Combine(basePath, "Picshare", "cache", "images");
    }

    public AlbumImageCacheLimits Limits
    {
        get => _limits;
        set
        {
            if (_limits == value)
            {
                return;
            }

            _limits = value;
            TrimAllMemoryCaches();
            TrimAllDiskCaches();
        }
    }

    public bool HasCacheRoom(string albumId, AlbumImageCacheKind kind)
    {
        var hasMemoryRoom = GetMemoryCacheSize(albumId, kind) < Limits.GetMemoryBytes(kind);
        var hasDiskRoom = IsDiskCachingEnabled(kind) && GetDiskCacheSize(albumId, kind) < Limits.GetDiskBytes(kind);
        return hasMemoryRoom || hasDiskRoom;
    }

    public void SetOriginalImageCachePrioritySets(
        string albumId,
        IEnumerable<string> priority1CacheFileNames,
        IEnumerable<string> priority2CacheFileNames)
    {
        var priority2 = priority2CacheFileNames
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => CreateOriginalImagePriorityKey(albumId, fileName))
            .ToHashSet();
        var priority2Disk = priority2CacheFileNames
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => CreateOriginalImageDiskPriorityKey(albumId, fileName))
            .ToHashSet();
        var priority1 = priority1CacheFileNames
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => CreateOriginalImagePriorityKey(albumId, fileName))
            .Where(key => !priority2.Contains(key))
            .ToHashSet();
        var priority1Disk = priority1CacheFileNames
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => CreateOriginalImageDiskPriorityKey(albumId, fileName))
            .Where(key => !priority2Disk.Contains(key))
            .ToHashSet();

        lock (_memoryCacheSync)
        {
            _priority1OriginalImageKeys = priority1;
            _priority2OriginalImageKeys = priority2;
            _priority1OriginalImageDiskKeys = priority1Disk;
            _priority2OriginalImageDiskKeys = priority2Disk;
        }
    }

    public async Task<AlbumFastThumbnailBitmapLoadResult> LoadFastThumbnailBitmapAsync(
        string albumId,
        string photoId,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        return await LoadFastThumbnailBitmapCoreAsync(
            albumId,
            photoId,
            downloadUrl,
            httpClient,
            readMode,
            retryInvalidCacheEntry: true,
            cancellationToken);
    }

    private async Task<AlbumFastThumbnailBitmapLoadResult> LoadFastThumbnailBitmapCoreAsync(
        string albumId,
        string photoId,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        bool retryInvalidCacheEntry,
        CancellationToken cancellationToken)
    {
        var cacheFileName = GetFastThumbnailCacheFileName(photoId);
        try
        {
            var result = await GetBitmapImageAsync(
                albumId,
                cacheFileName,
                AlbumImageCacheKind.FastThumbnail,
                readMode,
                () => OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken),
                cancellationToken);

            return new AlbumFastThumbnailBitmapLoadResult(result.Bitmap, result.IsCached);
        }
        catch (AlbumImageCacheEntryInvalidException ex)
            when (retryInvalidCacheEntry &&
                ex.Kind == AlbumImageCacheKind.FastThumbnail &&
                readMode != AlbumImageCacheReadMode.Lookup)
        {
            return await LoadFastThumbnailBitmapCoreAsync(
                albumId,
                photoId,
                downloadUrl,
                httpClient,
                readMode,
                retryInvalidCacheEntry: false,
                cancellationToken);
        }
    }

    public async Task<AlbumDetailedThumbnailBitmapLoadResult> LoadDetailedThumbnailBitmapAsync(
        string albumId,
        string photoId,
        string originalCacheFileName,
        string originalDownloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode detailedReadMode,
        AlbumImageCacheReadMode originalReadMode,
        CancellationToken cancellationToken)
    {
        return await LoadDetailedThumbnailBitmapAsync(
            albumId,
            photoId,
            originalCacheFileName,
            originalDownloadUrl,
            httpClient,
            220,
            150,
            detailedReadMode,
            originalReadMode,
            cancellationToken);
    }

    public async Task<AlbumDetailedThumbnailBitmapLoadResult> LoadDetailedThumbnailBitmapAsync(
        string albumId,
        string photoId,
        string originalCacheFileName,
        string originalDownloadUrl,
        HttpClient httpClient,
        int maxPixelWidth,
        int maxPixelHeight,
        AlbumImageCacheReadMode detailedReadMode,
        AlbumImageCacheReadMode originalReadMode,
        CancellationToken cancellationToken)
    {
        return await LoadDetailedThumbnailBitmapCoreAsync(
            albumId,
            photoId,
            originalCacheFileName,
            originalDownloadUrl,
            httpClient,
            maxPixelWidth,
            maxPixelHeight,
            detailedReadMode,
            originalReadMode,
            retryInvalidOriginalCacheEntry: true,
            cancellationToken);
    }

    private async Task<AlbumDetailedThumbnailBitmapLoadResult> LoadDetailedThumbnailBitmapCoreAsync(
        string albumId,
        string photoId,
        string originalCacheFileName,
        string originalDownloadUrl,
        HttpClient httpClient,
        int maxPixelWidth,
        int maxPixelHeight,
        AlbumImageCacheReadMode detailedReadMode,
        AlbumImageCacheReadMode originalReadMode,
        bool retryInvalidOriginalCacheEntry,
        CancellationToken cancellationToken)
    {
        var cacheFileName = GetDetailedThumbnailCacheFileName(photoId, maxPixelWidth, maxPixelHeight);
        var originalImageLoaded = false;
        try
        {
            var result = await GetBitmapImageAsync(
                albumId,
                cacheFileName,
                AlbumImageCacheKind.DetailedThumbnail,
                detailedReadMode,
                async () =>
                {
                    using var original = await GetEncodedImageBytesAsync(
                        albumId,
                        originalCacheFileName,
                        AlbumImageCacheKind.OriginalImage,
                        originalReadMode,
                        () => OpenSeekableSourceStreamAsync(originalDownloadUrl, httpClient, cancellationToken),
                        cancellationToken);
                    originalImageLoaded = original.IsCached;
                    return CreateDetailedThumbnailStream(
                        original,
                        albumId,
                        originalCacheFileName,
                        maxPixelWidth,
                        maxPixelHeight,
                        cancellationToken);
                },
                cancellationToken);

            return new AlbumDetailedThumbnailBitmapLoadResult(result.Bitmap, result.IsCached, originalImageLoaded);
        }
        catch (AlbumImageCacheEntryInvalidException ex)
            when (retryInvalidOriginalCacheEntry &&
                (ex.Kind == AlbumImageCacheKind.DetailedThumbnail && detailedReadMode != AlbumImageCacheReadMode.Lookup ||
                 ex.Kind == AlbumImageCacheKind.OriginalImage && originalReadMode != AlbumImageCacheReadMode.Lookup))
        {
            return await LoadDetailedThumbnailBitmapCoreAsync(
                albumId,
                photoId,
                originalCacheFileName,
                originalDownloadUrl,
                httpClient,
                maxPixelWidth,
                maxPixelHeight,
                detailedReadMode,
                originalReadMode,
                retryInvalidOriginalCacheEntry: false,
                cancellationToken);
        }
    }

    public async Task<bool> WarmFastThumbnailAsync(
        string albumId,
        string photoId,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        return await WarmEncodedImageAsync(
            albumId,
            GetFastThumbnailCacheFileName(photoId),
            AlbumImageCacheKind.FastThumbnail,
            readMode,
            () => OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken),
            cancellationToken);
    }

    public async Task<AlbumDetailedThumbnailWarmResult> WarmDetailedThumbnailAsync(
        string albumId,
        string photoId,
        string originalCacheFileName,
        string originalDownloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode detailedReadMode,
        AlbumImageCacheReadMode originalReadMode,
        CancellationToken cancellationToken)
    {
        return await WarmDetailedThumbnailAsync(
            albumId,
            photoId,
            originalCacheFileName,
            originalDownloadUrl,
            httpClient,
            220,
            150,
            detailedReadMode,
            originalReadMode,
            cancellationToken);
    }

    public async Task<AlbumDetailedThumbnailWarmResult> WarmDetailedThumbnailAsync(
        string albumId,
        string photoId,
        string originalCacheFileName,
        string originalDownloadUrl,
        HttpClient httpClient,
        int maxPixelWidth,
        int maxPixelHeight,
        AlbumImageCacheReadMode detailedReadMode,
        AlbumImageCacheReadMode originalReadMode,
        CancellationToken cancellationToken)
    {
        return await WarmDetailedThumbnailCoreAsync(
            albumId,
            photoId,
            originalCacheFileName,
            originalDownloadUrl,
            httpClient,
            maxPixelWidth,
            maxPixelHeight,
            detailedReadMode,
            originalReadMode,
            retryInvalidOriginalCacheEntry: true,
            cancellationToken);
    }

    private async Task<AlbumDetailedThumbnailWarmResult> WarmDetailedThumbnailCoreAsync(
        string albumId,
        string photoId,
        string originalCacheFileName,
        string originalDownloadUrl,
        HttpClient httpClient,
        int maxPixelWidth,
        int maxPixelHeight,
        AlbumImageCacheReadMode detailedReadMode,
        AlbumImageCacheReadMode originalReadMode,
        bool retryInvalidOriginalCacheEntry,
        CancellationToken cancellationToken)
    {
        var originalImageLoaded = false;
        try
        {
            var detailedThumbnailLoaded = await WarmEncodedImageAsync(
                albumId,
                GetDetailedThumbnailCacheFileName(photoId, maxPixelWidth, maxPixelHeight),
                AlbumImageCacheKind.DetailedThumbnail,
                detailedReadMode,
                async () =>
                {
                    using var original = await GetEncodedImageBytesAsync(
                        albumId,
                        originalCacheFileName,
                        AlbumImageCacheKind.OriginalImage,
                        originalReadMode,
                        () => OpenSeekableSourceStreamAsync(originalDownloadUrl, httpClient, cancellationToken),
                        cancellationToken);
                    originalImageLoaded = original.IsCached;
                    return CreateDetailedThumbnailStream(
                        original,
                        albumId,
                        originalCacheFileName,
                        maxPixelWidth,
                        maxPixelHeight,
                        cancellationToken);
                },
                cancellationToken);
            return new AlbumDetailedThumbnailWarmResult(detailedThumbnailLoaded, originalImageLoaded);
        }
        catch (AlbumImageCacheEntryInvalidException ex)
            when (retryInvalidOriginalCacheEntry &&
                ex.Kind == AlbumImageCacheKind.OriginalImage &&
                originalReadMode != AlbumImageCacheReadMode.Lookup)
        {
            return await WarmDetailedThumbnailCoreAsync(
                albumId,
                photoId,
                originalCacheFileName,
                originalDownloadUrl,
                httpClient,
                maxPixelWidth,
                maxPixelHeight,
                detailedReadMode,
                originalReadMode,
                retryInvalidOriginalCacheEntry: false,
                cancellationToken);
        }
    }

    public async Task<bool> WarmOriginalAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        return await WarmOriginalAsync(
            albumId,
            cacheFileName,
            downloadUrl,
            httpClient,
            readMode,
            cachePriority: 0,
            cancellationToken);
    }

    public async Task<bool> WarmOriginalAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        int cachePriority,
        CancellationToken cancellationToken)
    {
        return await WarmEncodedImageAsync(
            albumId,
            cacheFileName,
            AlbumImageCacheKind.OriginalImage,
            readMode,
            cachePriority,
            () => OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken),
            cancellationToken);
    }

    public async Task CopyOriginalToAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        Stream destination,
        CancellationToken cancellationToken)
    {
        AlbumImageCacheLoadResult? cached = null;
        try
        {
            cached = await GetOriginalEncodedBytesAsync(
                albumId,
                cacheFileName,
                downloadUrl,
                httpClient,
                AlbumImageCacheReadMode.Lookup,
                cancellationToken);
        }
        catch (FileNotFoundException)
        {
        }

        if (cached is not null)
        {
            try
            {
                if (!destination.CanSeek)
                {
                    await destination.WriteAsync(cached.Bytes.ReadOnlyMemory, cancellationToken);
                    return;
                }

                var cachedDestinationStart = destination.Position;
                await TransientRetryPolicy.ExecuteAsync(
                    async token =>
                    {
                        destination.Position = cachedDestinationStart;
                        destination.SetLength(cachedDestinationStart);
                        await destination.WriteAsync(cached.Bytes.ReadOnlyMemory, token);
                    },
                    null,
                    cancellationToken);
                return;
            }
            finally
            {
                cached.Dispose();
            }
        }

        if (!destination.CanSeek)
        {
            await using var buffered = await TransientRetryPolicy.ExecuteAsync(
                async token =>
                {
                    await using var source = await OpenSourceStreamAsync(downloadUrl, httpClient, token);
                    var memory = PooledMemoryStreamFactory.GetStream("ImageCacheService.CopyOriginalToAsync");
                    await source.CopyToAsync(memory, token);
                    memory.Position = 0;
                    return memory;
                },
                null,
                cancellationToken);
            await buffered.CopyToAsync(destination, cancellationToken);
            return;
        }

        var destinationStart = destination.Position;
        await TransientRetryPolicy.ExecuteAsync(
            async token =>
            {
                destination.Position = destinationStart;
                destination.SetLength(destinationStart);
                await using var source = await OpenSourceStreamAsync(downloadUrl, httpClient, token);
                await source.CopyToAsync(destination, token);
            },
            null,
            cancellationToken);
    }

    public Task ClearAsync()
    {
        lock (_memoryCacheSync)
        {
            ClearMemoryCacheNoLock();
        }

        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
            DeleteDirectoryContentsBestEffort(_rootPath);
        }
        catch (UnauthorizedAccessException)
        {
            DeleteDirectoryContentsBestEffort(_rootPath);
        }

        return Task.CompletedTask;
    }

    public Task ClearAlbumAsync(string albumId)
    {
        lock (_memoryCacheSync)
        {
            foreach (var key in _memoryCache.Keys
                .Where(key => string.Equals(key.AlbumId, albumId, StringComparison.Ordinal))
                .ToList())
            {
                RemoveMemoryCacheNoLock(key);
            }

            foreach (var key in _bitmapCache.Keys
                .Where(key => string.Equals(key.AlbumId, albumId, StringComparison.Ordinal))
                .ToList())
            {
                RemoveBitmapCacheNoLock(key);
            }
        }

        foreach (var albumPath in GetAlbumCachePathsForClear(albumId))
        {
            try
            {
                if (Directory.Exists(albumPath))
                {
                    Directory.Delete(albumPath, recursive: true);
                }
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
                DeleteDirectoryContentsBestEffort(albumPath);
            }
            catch (UnauthorizedAccessException)
            {
                DeleteDirectoryContentsBestEffort(albumPath);
            }
        }

        return Task.CompletedTask;
    }

    public async Task<AlbumImageBitmapLease> LoadDisplayBitmapAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        int maxPixelWidth,
        int maxPixelHeight,
        CancellationToken cancellationToken)
    {
        var isOriginalSource = IsOriginalCacheFileName(cacheFileName);
        try
        {
            return await LoadUncachedDisplayBitmapAsync(
                albumId,
                cacheFileName,
                downloadUrl,
                httpClient,
                maxPixelWidth,
                maxPixelHeight,
                isOriginalSource,
                cancellationToken);
        }
        catch (AlbumImageCacheEntryInvalidException) when (isOriginalSource)
        {
            return await LoadUncachedDisplayBitmapAsync(
                albumId,
                cacheFileName,
                downloadUrl,
                httpClient,
                maxPixelWidth,
                maxPixelHeight,
                isOriginalSource,
                cancellationToken);
        }
    }

    public async Task<AlbumImageBitmapLease> LoadOriginalBitmapAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        return await LoadOriginalBitmapAsync(
            albumId,
            cacheFileName,
            downloadUrl,
            httpClient,
            AlbumImageCacheReadMode.Eager,
            cancellationToken);
    }

    public async Task<AlbumImageBitmapLease> LoadOriginalBitmapAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        return await LoadOriginalBitmapAsync(
            albumId,
            cacheFileName,
            downloadUrl,
            httpClient,
            readMode,
            cachePriority: 0,
            cancellationToken);
    }

    public async Task<AlbumImageBitmapLease> LoadOriginalBitmapAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        int cachePriority,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await GetBitmapImageAsync(
                albumId,
                cacheFileName,
                AlbumImageCacheKind.OriginalImage,
                readMode,
                cachePriority,
                () => OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken),
                cancellationToken);
            return result.Bitmap;
        }
        catch (AlbumImageCacheEntryInvalidException) when (readMode != AlbumImageCacheReadMode.Lookup)
        {
            var result = await GetBitmapImageAsync(
                albumId,
                cacheFileName,
                AlbumImageCacheKind.OriginalImage,
                readMode,
                cachePriority,
                () => OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken),
                cancellationToken);
            return result.Bitmap;
        }
    }

    private async Task<AlbumImageCacheLoadResult> GetOriginalEncodedBytesAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        return await GetEncodedImageBytesAsync(
            albumId,
            cacheFileName,
            AlbumImageCacheKind.OriginalImage,
            readMode,
            () => OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken),
            cancellationToken);
    }

    private async Task<AlbumImageBitmapCacheLoadResult> GetBitmapImageAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        Func<Task<Stream>> openSourceAsync,
        CancellationToken cancellationToken)
    {
        return await GetBitmapImageAsync(
            albumId,
            cacheFileName,
            kind,
            readMode,
            cachePriority: 0,
            openSourceAsync,
            cancellationToken);
    }

    private async Task<AlbumImageBitmapCacheLoadResult> GetBitmapImageAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        int cachePriority,
        Func<Task<Stream>> openSourceAsync,
        CancellationToken cancellationToken)
    {
        if (TryGetBitmapCache(albumId, cacheFileName, kind, readMode, out var memoryBitmap))
        {
            return new AlbumImageBitmapCacheLoadResult(memoryBitmap, IsCached: true);
        }

        using var result = await GetEncodedImageBytesAsync(
            albumId,
            cacheFileName,
            kind,
            readMode,
            cachePriority,
            openSourceAsync,
            cancellationToken);
        var bitmap = await DecodeCachedBitmapAsync(result, albumId, cacheFileName, kind, cancellationToken);
        if (readMode != AlbumImageCacheReadMode.Lookup &&
            TryAddBitmapCache(albumId, cacheFileName, kind, bitmap, readMode, cachePriority, out var cachedBitmap))
        {
            return new AlbumImageBitmapCacheLoadResult(cachedBitmap, IsCached: true);
        }

        return new AlbumImageBitmapCacheLoadResult(
            AlbumImageBitmapLease.CreateEphemeral(bitmap),
            result.IsCached || IsBitmapCacheStored(albumId, cacheFileName, kind));
    }

    private async Task<bool> WarmEncodedImageAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        Func<Task<Stream>> openSourceAsync,
        CancellationToken cancellationToken)
    {
        return await WarmEncodedImageAsync(
            albumId,
            cacheFileName,
            kind,
            readMode,
            cachePriority: 0,
            openSourceAsync,
            cancellationToken);
    }

    private async Task<bool> WarmEncodedImageAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        int cachePriority,
        Func<Task<Stream>> openSourceAsync,
        CancellationToken cancellationToken)
    {
        if (TryGetEncodedMemoryCache(albumId, cacheFileName, kind, readMode, out var memoryBytes))
        {
            using (memoryBytes)
            {
                if (readMode == AlbumImageCacheReadMode.Lookup ||
                    !ShouldAttemptDiskWarmup(albumId, cacheFileName, kind))
                {
                    return true;
                }

                var diskCached = await AddDiskCacheAsync(albumId, cacheFileName, kind, memoryBytes.Bytes, readMode, cachePriority, cancellationToken);
                return diskCached || IsEncodedMemoryCacheStored(albumId, cacheFileName, kind);
            }
        }

        using var result = await GetEncodedImageBytesAsync(
            albumId,
            cacheFileName,
            kind,
            readMode,
            cachePriority,
            openSourceAsync,
            cancellationToken);

        return result.IsCached;
    }

    private bool ShouldAttemptDiskWarmup(string albumId, string cacheFileName, AlbumImageCacheKind kind)
    {
        var diskLimit = Limits.GetDiskBytes(kind);
        if (!IsDiskCachingEnabled(kind) || diskLimit <= 0)
        {
            return false;
        }

        var diskPath = GetCachedPath(albumId, cacheFileName);
        if (diskPath is null)
        {
            return true;
        }

        try
        {
            return new FileInfo(diskPath).Length > diskLimit;
        }
        catch
        {
            return true;
        }
    }

    private async Task<AlbumImageCacheLoadResult> GetEncodedImageBytesAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        Func<Task<Stream>> openSourceAsync,
        CancellationToken cancellationToken)
    {
        return await GetEncodedImageBytesAsync(
            albumId,
            cacheFileName,
            kind,
            readMode,
            cachePriority: 0,
            openSourceAsync,
            cancellationToken);
    }

    private async Task<AlbumImageCacheLoadResult> GetEncodedImageBytesAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        int cachePriority,
        Func<Task<Stream>> openSourceAsync,
        CancellationToken cancellationToken)
    {
        if (TryGetEncodedMemoryCache(albumId, cacheFileName, kind, readMode, out var memoryBytes))
        {
            return new AlbumImageCacheLoadResult(memoryBytes, true);
        }

        var diskLimit = Limits.GetDiskBytes(kind);
        var diskPath = IsDiskCachingEnabled(kind) && diskLimit > 0
            ? GetCachedPath(albumId, cacheFileName)
            : null;
        if (diskPath is not null)
        {
            try
            {
                if (new FileInfo(diskPath).Length > diskLimit)
                {
                    File.Delete(diskPath);
                    diskPath = null;
                }
            }
            catch (FileNotFoundException)
            {
                diskPath = null;
            }
            catch (DirectoryNotFoundException)
            {
                diskPath = null;
            }
            catch (IOException)
            {
                diskPath = null;
            }
            catch (UnauthorizedAccessException)
            {
                diskPath = null;
            }
        }

        if (diskPath is not null)
        {
            try
            {
                if (readMode == AlbumImageCacheReadMode.Eager)
                {
                    TrySetLastAccessTimeUtc(diskPath, DateTime.UtcNow);
                }

                var diskBytes = await PooledByteBuffer.FromFileAsync(diskPath, cancellationToken);
                if (readMode == AlbumImageCacheReadMode.Eager)
                {
                    TrimDiskCache(albumId, kind, diskLimit, cachePriority);
                }

                if (readMode == AlbumImageCacheReadMode.Eager)
                {
                    if (AddEncodedMemoryCache(albumId, cacheFileName, kind, diskBytes, readMode, cachePriority, out var cacheLease))
                    {
                        diskBytes = null!;
                        return new AlbumImageCacheLoadResult(cacheLease, true);
                    }
                }

                return new AlbumImageCacheLoadResult(diskBytes, true);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        if (readMode == AlbumImageCacheReadMode.Lookup)
        {
            throw new FileNotFoundException("The image is not available in cache.", cacheFileName);
        }

        var bytes = await TransientRetryPolicy.ExecuteAsync(
            async token =>
            {
                await using var source = await openSourceAsync();
                return await PooledByteBuffer.FromStreamAsync(source, token);
            },
            null,
            cancellationToken);
        var diskCached = await AddDiskCacheAsync(albumId, cacheFileName, kind, bytes, readMode, cachePriority, cancellationToken);
        if (AddEncodedMemoryCache(albumId, cacheFileName, kind, bytes, readMode, cachePriority, out var memoryLease))
        {
            bytes = null!;
            return new AlbumImageCacheLoadResult(memoryLease, true);
        }

        return new AlbumImageCacheLoadResult(bytes, diskCached);
    }

    private bool TryGetEncodedMemoryCache(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        out AlbumImageEncodedLease bytes)
    {
        var limit = Limits.GetMemoryBytes(kind);
        if (limit <= 0)
        {
            bytes = null!;
            return false;
        }

        var key = new MemoryCacheKey(albumId, cacheFileName, kind);
        lock (_memoryCacheSync)
        {
            if (_memoryCache.TryGetValue(key, out var entry))
            {
                if (entry.EstimatedBytes > limit)
                {
                    RemoveMemoryCacheNoLock(key);
                    bytes = null!;
                    return false;
                }

                if (readMode == AlbumImageCacheReadMode.Eager)
                {
                    entry.LastAccessUtc = DateTime.UtcNow;
                }

                return entry.TryAcquire(out bytes);
            }
        }

        bytes = null!;
        return false;
    }

    private bool TryGetBitmapCache(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        out AlbumImageBitmapLease bitmap)
    {
        var limit = Limits.GetBitmapMemoryBytes(kind);
        if (limit <= 0)
        {
            bitmap = null!;
            return false;
        }

        var key = new MemoryCacheKey(albumId, cacheFileName, kind);
        lock (_memoryCacheSync)
        {
            if (_bitmapCache.TryGetValue(key, out var entry))
            {
                if (entry.EstimatedBytes > limit)
                {
                    RemoveBitmapCacheNoLock(key);
                    bitmap = null!;
                    return false;
                }

                if (readMode == AlbumImageCacheReadMode.Eager)
                {
                    entry.LastAccessUtc = DateTime.UtcNow;
                }

                return entry.TryAcquire(out bitmap);
            }
        }

        bitmap = null!;
        return false;
    }

    private bool AddEncodedMemoryCache(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        PooledByteBuffer bytes,
        AlbumImageCacheReadMode readMode)
    {
        return AddEncodedMemoryCache(albumId, cacheFileName, kind, bytes, readMode, cachePriority: 0, out _);
    }

    private bool AddEncodedMemoryCache(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        PooledByteBuffer bytes,
        AlbumImageCacheReadMode readMode,
        int cachePriority,
        out AlbumImageEncodedLease lease)
    {
        var limit = Limits.GetMemoryBytes(kind);
        var estimatedBytes = bytes.Length;
        if (limit <= 0 || estimatedBytes > limit || readMode == AlbumImageCacheReadMode.Lookup)
        {
            lease = null!;
            return false;
        }

        var key = new MemoryCacheKey(albumId, cacheFileName, kind);
        lock (_memoryCacheSync)
        {
            _memoryCache.TryGetValue(key, out var existingEntry);
            if (existingEntry is not null && readMode == AlbumImageCacheReadMode.Lazy)
            {
                lease = null!;
                return false;
            }

            var albumKindSize = GetMemoryCacheSizeNoLock(albumId, kind) - (existingEntry?.EstimatedBytes ?? 0);
            if (readMode == AlbumImageCacheReadMode.Lazy && albumKindSize + estimatedBytes > limit)
            {
                lease = null!;
                return false;
            }

            if (readMode == AlbumImageCacheReadMode.Eager && albumKindSize + estimatedBytes > limit)
            {
                TrimMemoryCacheNoLock(albumId, kind, limit - estimatedBytes, cachePriority);
                albumKindSize = GetMemoryCacheSizeNoLock(albumId, kind) - (existingEntry?.EstimatedBytes ?? 0);
                if (albumKindSize + estimatedBytes > limit)
                {
                    lease = null!;
                    return false;
                }
            }

            if (existingEntry is not null)
            {
                RemoveMemoryCacheNoLock(key);
            }

            var entry = new EncodedMemoryCacheEntry(bytes, estimatedBytes, DateTime.UtcNow);
            if (!entry.TryAcquire(out lease))
            {
                lease = null!;
                return false;
            }

            _memoryCache[key] = entry;
            if (readMode == AlbumImageCacheReadMode.Eager)
            {
                TrimMemoryCacheNoLock(albumId, kind, limit, cachePriority);
            }

            return true;
        }
    }

    private bool TryAddBitmapCache(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        Bitmap bitmap,
        AlbumImageCacheReadMode readMode,
        out AlbumImageBitmapLease lease)
    {
        return TryAddBitmapCache(albumId, cacheFileName, kind, bitmap, readMode, cachePriority: 0, out lease);
    }

    private bool TryAddBitmapCache(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        Bitmap bitmap,
        AlbumImageCacheReadMode readMode,
        int cachePriority,
        out AlbumImageBitmapLease lease)
    {
        var limit = Limits.GetBitmapMemoryBytes(kind);
        var estimatedBytes = EstimateBitmapBytes(bitmap);
        if (limit <= 0 || estimatedBytes > limit || readMode == AlbumImageCacheReadMode.Lookup)
        {
            lease = null!;
            return false;
        }

        var key = new MemoryCacheKey(albumId, cacheFileName, kind);
        lock (_memoryCacheSync)
        {
            _bitmapCache.TryGetValue(key, out var existingEntry);
            if (existingEntry is not null)
            {
                if (existingEntry.TryAcquire(out lease))
                {
                    bitmap.Dispose();
                    return true;
                }

                _bitmapCache.Remove(key);
            }

            var albumKindSize = GetBitmapCacheSizeNoLock(albumId, kind) - (existingEntry?.EstimatedBytes ?? 0);
            if (albumKindSize + estimatedBytes > limit)
            {
                if (readMode == AlbumImageCacheReadMode.Lazy)
                {
                    lease = null!;
                    return false;
                }

                TrimBitmapCacheNoLock(albumId, kind, limit - estimatedBytes, cachePriority);
                albumKindSize = GetBitmapCacheSizeNoLock(albumId, kind);
                if (albumKindSize + estimatedBytes > limit)
                {
                    lease = null!;
                    return false;
                }
            }

            var entry = new BitmapMemoryCacheEntry(bitmap, estimatedBytes, DateTime.UtcNow);
            if (!entry.TryAcquire(out lease))
            {
                lease = null!;
                return false;
            }

            _bitmapCache[key] = entry;
            return true;
        }
    }

    private bool IsEncodedMemoryCacheStored(string albumId, string cacheFileName, AlbumImageCacheKind kind)
    {
        lock (_memoryCacheSync)
        {
            return _memoryCache.ContainsKey(new MemoryCacheKey(albumId, cacheFileName, kind));
        }
    }

    private bool IsBitmapCacheStored(string albumId, string cacheFileName, AlbumImageCacheKind kind)
    {
        lock (_memoryCacheSync)
        {
            return IsBitmapCacheStoredNoLock(new MemoryCacheKey(albumId, cacheFileName, kind));
        }
    }

    private bool IsBitmapCacheStoredNoLock(MemoryCacheKey key)
    {
        return _bitmapCache.ContainsKey(key);
    }

    private long GetMemoryCacheSize(string albumId, AlbumImageCacheKind kind)
    {
        lock (_memoryCacheSync)
        {
            return GetMemoryCacheSizeNoLock(albumId, kind);
        }
    }

    private void TrimAllMemoryCaches()
    {
        lock (_memoryCacheSync)
        {
            foreach (var group in _memoryCache.Keys
                .Select(key => new { key.AlbumId, key.Kind })
                .Distinct()
                .ToList())
            {
                TrimMemoryCacheNoLock(group.AlbumId, group.Kind, Limits.GetMemoryBytes(group.Kind));
            }

            foreach (var group in _bitmapCache.Keys
                .Select(key => new { key.AlbumId, key.Kind })
                .Distinct()
                .ToList())
            {
                TrimBitmapCacheNoLock(group.AlbumId, group.Kind, Limits.GetBitmapMemoryBytes(group.Kind));
            }
        }
    }

    private long GetMemoryCacheSizeNoLock(string albumId, AlbumImageCacheKind kind)
    {
        return _memoryCache
            .Where(item => string.Equals(item.Key.AlbumId, albumId, StringComparison.Ordinal) && item.Key.Kind == kind)
            .Sum(item => item.Value.EstimatedBytes);
    }

    private void TrimMemoryCacheNoLock(string albumId, AlbumImageCacheKind kind, long limit)
    {
        TrimMemoryCacheNoLock(albumId, kind, limit, incomingPriority: int.MaxValue);
    }

    private void TrimMemoryCacheNoLock(string albumId, AlbumImageCacheKind kind, long limit, int incomingPriority)
    {
        if (limit <= 0)
        {
            foreach (var key in _memoryCache.Keys
                .Where(key => string.Equals(key.AlbumId, albumId, StringComparison.Ordinal) && key.Kind == kind)
                .Where(key => CanEvictForIncomingPriorityNoLock(key, incomingPriority))
                .ToList())
            {
                RemoveMemoryCacheNoLock(key);
            }

            return;
        }

        while (GetMemoryCacheSizeNoLock(albumId, kind) > limit)
        {
            var oldest = _memoryCache
                .Where(item => string.Equals(item.Key.AlbumId, albumId, StringComparison.Ordinal) && item.Key.Kind == kind)
                .Where(item => CanEvictForIncomingPriorityNoLock(item.Key, incomingPriority))
                .OrderBy(item => item.Value.LastAccessUtc)
                .Select(item => item.Key)
                .FirstOrDefault();

            if (oldest is null)
            {
                return;
            }

            RemoveMemoryCacheNoLock(oldest);
        }
    }

    private void ClearMemoryCacheNoLock()
    {
        foreach (var key in _memoryCache.Keys.ToList())
        {
            RemoveMemoryCacheNoLock(key);
        }

        foreach (var key in _bitmapCache.Keys.ToList())
        {
            RemoveBitmapCacheNoLock(key);
        }
    }

    private void RemoveMemoryCacheNoLock(MemoryCacheKey key)
    {
        if (_memoryCache.Remove(key, out var entry))
        {
            entry.RemoveFromCache();
        }
    }

    private static MemoryCacheKey CreateOriginalImagePriorityKey(string albumId, string cacheFileName)
    {
        return new MemoryCacheKey(albumId, cacheFileName, AlbumImageCacheKind.OriginalImage);
    }

    private static MemoryCacheKey CreateOriginalImageDiskPriorityKey(string albumId, string cacheFileName)
    {
        return new MemoryCacheKey(albumId, GetCacheFileSystemName(cacheFileName), AlbumImageCacheKind.OriginalImage);
    }

    private int GetCachePriorityNoLock(MemoryCacheKey key)
    {
        if (key.Kind != AlbumImageCacheKind.OriginalImage)
        {
            return 0;
        }

        if (_priority2OriginalImageKeys.Contains(key))
        {
            return 2;
        }

        return _priority1OriginalImageKeys.Contains(key) ? 1 : 0;
    }

    private bool CanEvictForIncomingPriorityNoLock(MemoryCacheKey key, int incomingPriority)
    {
        if (key.Kind != AlbumImageCacheKind.OriginalImage || incomingPriority == int.MaxValue)
        {
            return true;
        }

        var existingPriority = GetCachePriorityNoLock(key);
        return incomingPriority <= 0
            ? existingPriority == 0
            : existingPriority < incomingPriority;
    }

    private long GetBitmapCacheSizeNoLock(string albumId, AlbumImageCacheKind kind)
    {
        return _bitmapCache
            .Where(item => string.Equals(item.Key.AlbumId, albumId, StringComparison.Ordinal) && item.Key.Kind == kind)
            .Sum(item => item.Value.EstimatedBytes);
    }

    private void TrimBitmapCacheNoLock(string albumId, AlbumImageCacheKind kind, long limit)
    {
        TrimBitmapCacheNoLock(albumId, kind, limit, incomingPriority: int.MaxValue);
    }

    private void TrimBitmapCacheNoLock(string albumId, AlbumImageCacheKind kind, long limit, int incomingPriority)
    {
        if (limit <= 0)
        {
            foreach (var key in _bitmapCache.Keys
                .Where(key => string.Equals(key.AlbumId, albumId, StringComparison.Ordinal) &&
                    key.Kind == kind &&
                    _bitmapCache[key].ReferenceCount == 0)
                .Where(key => CanEvictForIncomingPriorityNoLock(key, incomingPriority))
                .ToList())
            {
                RemoveBitmapCacheNoLock(key);
            }

            return;
        }

        while (GetBitmapCacheSizeNoLock(albumId, kind) > limit)
        {
            var oldest = _bitmapCache
                .Where(item => string.Equals(item.Key.AlbumId, albumId, StringComparison.Ordinal) && item.Key.Kind == kind)
                .Where(item => item.Value.ReferenceCount == 0)
                .Where(item => CanEvictForIncomingPriorityNoLock(item.Key, incomingPriority))
                .OrderBy(item => item.Value.LastAccessUtc)
                .Select(item => item.Key)
                .FirstOrDefault();

            if (oldest is null)
            {
                return;
            }

            RemoveBitmapCacheNoLock(oldest);
        }
    }

    private void RemoveBitmapCacheNoLock(MemoryCacheKey key)
    {
        if (_bitmapCache.Remove(key, out var entry))
        {
            entry.RemoveFromCache();
        }
    }

    private async Task<bool> AddDiskCacheAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageEncodedLease bytes,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        return await AddDiskCacheAsync(albumId, cacheFileName, kind, bytes, readMode, cachePriority: 0, cancellationToken);
    }

    private async Task<bool> AddDiskCacheAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageEncodedLease bytes,
        AlbumImageCacheReadMode readMode,
        int cachePriority,
        CancellationToken cancellationToken)
    {
        return await AddDiskCacheAsync(albumId, cacheFileName, kind, bytes.Bytes, readMode, cachePriority, cancellationToken);
    }

    private async Task<bool> AddDiskCacheAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        PooledByteBuffer bytes,
        AlbumImageCacheReadMode readMode,
        int cachePriority,
        CancellationToken cancellationToken)
    {
        if (!IsDiskCachingEnabled(kind))
        {
            return false;
        }

        var limit = Limits.GetDiskBytes(kind);
        if (limit <= 0 || bytes.Length > limit)
        {
            return false;
        }

        string albumPath;
        string cachePath;
        try
        {
            albumPath = GetAlbumCachePath(albumId);
            Directory.CreateDirectory(albumPath);
            cachePath = Path.Combine(albumPath, GetCacheFileSystemName(cacheFileName));
            if (File.Exists(cachePath))
            {
                if (new FileInfo(cachePath).Length > limit)
                {
                    try
                    {
                        File.Delete(cachePath);
                    }
                    catch (IOException)
                    {
                        return false;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return false;
                    }
                }
                else
                {
                    if (readMode == AlbumImageCacheReadMode.Eager)
                    {
                        TrySetLastAccessTimeUtc(cachePath, DateTime.UtcNow);
                        TrimDiskCache(albumId, kind, limit, cachePriority);
                    }

                    return true;
                }
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (readMode == AlbumImageCacheReadMode.Lazy && !CanFitLazyDiskCacheEntry(albumId, cachePath, kind, bytes.Length, limit))
        {
            return false;
        }

        if (readMode == AlbumImageCacheReadMode.Eager &&
            !CanFitPriorityDiskCacheEntry(albumId, cachePath, kind, bytes.Length, limit, cachePriority))
        {
            return false;
        }

        var tempPath = Path.Combine(albumPath, $".{Guid.NewGuid():N}.tmp");
        var cached = false;
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(bytes.ReadOnlyMemory, cancellationToken);
            }
            TrySetLastAccessTimeUtc(tempPath, DateTime.UtcNow);
            File.Move(tempPath, cachePath, overwrite: false);
            cached = true;
        }
        catch (IOException) when (File.Exists(cachePath))
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }

            try
            {
                cached = new FileInfo(cachePath).Length <= limit;
            }
            catch
            {
                cached = false;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        if (readMode == AlbumImageCacheReadMode.Eager)
        {
            TrimDiskCache(albumId, kind, limit, cachePriority);
        }

        return cached;
    }

    private long GetDiskCacheSize(string albumId, AlbumImageCacheKind kind)
    {
        var albumPath = GetAlbumCachePath(albumId);
        if (!Directory.Exists(albumPath))
        {
            return 0;
        }

        List<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(albumPath)
                .Where(path => IsCacheFileForKind(path, kind))
                .ToList();
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        var total = 0L;
        foreach (var path in paths)
        {
            try
            {
                total += new FileInfo(path).Length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return total;
    }

    private bool CanFitLazyDiskCacheEntry(
        string albumId,
        string cachePath,
        AlbumImageCacheKind kind,
        long byteCount,
        long limit)
    {
        var existingLength = 0L;
        try
        {
            if (File.Exists(cachePath))
            {
                existingLength = new FileInfo(cachePath).Length;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return GetDiskCacheSize(albumId, kind) - existingLength + byteCount <= limit;
    }

    private bool CanFitPriorityDiskCacheEntry(
        string albumId,
        string cachePath,
        AlbumImageCacheKind kind,
        long byteCount,
        long limit,
        int incomingPriority)
    {
        var existingLength = 0L;
        try
        {
            if (File.Exists(cachePath))
            {
                existingLength = new FileInfo(cachePath).Length;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var targetLimit = limit - byteCount + existingLength;
        TrimDiskCache(albumId, kind, targetLimit, incomingPriority);
        return GetDiskCacheSize(albumId, kind) - existingLength + byteCount <= limit;
    }

    private void TrimAllDiskCaches()
    {
        if (!Directory.Exists(_rootPath))
        {
            return;
        }

        List<string> albumPaths;
        try
        {
            albumPaths = Directory.EnumerateDirectories(_rootPath).ToList();
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var albumPath in albumPaths)
        {
            DeleteLegacyDisplayCacheFiles(albumPath);
            foreach (AlbumImageCacheKind kind in Enum.GetValues<AlbumImageCacheKind>())
            {
                TrimDiskCachePath(
                    albumPath,
                    Path.GetFileName(albumPath),
                    kind,
                    IsDiskCachingEnabled(kind) ? Limits.GetDiskBytes(kind) : 0,
                    incomingPriority: int.MaxValue);
            }
        }
    }

    private static void DeleteLegacyDisplayCacheFiles(string albumPath)
    {
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(albumPath)
                .Where(path => Path.GetFileName(path).Contains(".display.", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch
        {
            return;
        }

        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private void TrimDiskCache(string albumId, AlbumImageCacheKind kind, long limit)
    {
        TrimDiskCache(albumId, kind, limit, incomingPriority: int.MaxValue);
    }

    private void TrimDiskCache(string albumId, AlbumImageCacheKind kind, long limit, int incomingPriority)
    {
        TrimDiskCachePath(GetAlbumCachePath(albumId), albumId, kind, limit, incomingPriority);
    }

    private void TrimDiskCachePath(string albumPath, string albumId, AlbumImageCacheKind kind, long limit, int incomingPriority)
    {
        if (!Directory.Exists(albumPath))
        {
            return;
        }

        if (limit <= 0)
        {
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(albumPath)
                    .Where(path => IsCacheFileForKind(path, kind))
                    .Where(path => CanEvictDiskPathForIncomingPriority(path, albumId, kind, incomingPriority))
                    .ToList();
            }
            catch
            {
                return;
            }

            foreach (var path in paths)
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                }
            }

            return;
        }

        List<DiskCacheFileInfo> files = new();
        try
        {
            var paths = Directory.EnumerateFiles(albumPath)
                .Where(path => IsCacheFileForKind(path, kind))
                .ToList();
            foreach (var path in paths)
            {
                try
                {
                    if (!CanEvictDiskPathForIncomingPriority(path, albumId, kind, incomingPriority))
                    {
                        continue;
                    }

                    var file = new FileInfo(path);
                    files.Add(new DiskCacheFileInfo(file, file.Length, file.LastAccessTimeUtc));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var total = files.Sum(file => file.Length);

        foreach (var file in files.OrderBy(file => file.LastAccessTimeUtc))
        {
            if (total <= limit)
            {
                break;
            }

            try
            {
                file.File.Delete();
                total -= file.Length;
            }
            catch
            {
            }
        }
    }

    private bool CanEvictDiskPathForIncomingPriority(
        string path,
        string albumId,
        AlbumImageCacheKind kind,
        int incomingPriority)
    {
        if (kind != AlbumImageCacheKind.OriginalImage || incomingPriority == int.MaxValue)
        {
            return true;
        }

        var cacheFileName = Path.GetFileName(path);
        var priorityKey = new MemoryCacheKey(albumId, cacheFileName, kind);
        lock (_memoryCacheSync)
        {
            if (kind != AlbumImageCacheKind.OriginalImage)
            {
                return true;
            }

            var existingPriority = _priority2OriginalImageDiskKeys.Contains(priorityKey)
                ? 2
                : _priority1OriginalImageDiskKeys.Contains(priorityKey)
                    ? 1
                    : 0;
            return incomingPriority <= 0
                ? existingPriority == 0
                : existingPriority < incomingPriority;
        }
    }

    private static bool IsCacheFileForKind(string path, AlbumImageCacheKind kind)
    {
        var fileName = Path.GetFileName(path);
        return kind switch
        {
            AlbumImageCacheKind.FastThumbnail => fileName.EndsWith("-thumbnail.jpg", StringComparison.OrdinalIgnoreCase),
            AlbumImageCacheKind.DetailedThumbnail => fileName.EndsWith("-detailed-220x150.jpg", StringComparison.OrdinalIgnoreCase),
            AlbumImageCacheKind.OriginalImage => IsOriginalCacheFileName(fileName) &&
                !fileName.Contains(".display.", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static void DeleteDirectoryContentsBestEffort(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        DeleteDirectoryContentsRecursiveBestEffort(directoryPath);
        TryDeleteDirectory(directoryPath);
    }

    private static void DeleteDirectoryContentsRecursiveBestEffort(string directoryPath)
    {
        foreach (var filePath in EnumerateFileSystemEntriesBestEffort(directoryPath, enumerateDirectories: false))
        {
            TryDeleteFile(filePath);
        }

        foreach (var childDirectoryPath in EnumerateFileSystemEntriesBestEffort(directoryPath, enumerateDirectories: true))
        {
            DeleteDirectoryContentsRecursiveBestEffort(childDirectoryPath);
            TryDeleteDirectory(childDirectoryPath);
        }
    }

    private static IEnumerable<string> EnumerateFileSystemEntriesBestEffort(string directoryPath, bool enumerateDirectories)
    {
        try
        {
            return enumerateDirectories
                ? Directory.EnumerateDirectories(directoryPath).ToList()
                : Directory.EnumerateFiles(directoryPath).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: false);
        }
        catch
        {
        }
    }

    private static void TrySetLastAccessTimeUtc(string path, DateTime value)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, value);
        }
        catch
        {
        }
    }

    private bool IsDiskCachingEnabled(AlbumImageCacheKind kind)
    {
        return Limits.GetDiskBytes(kind) > 0;
    }

    private static string GetFastThumbnailCacheFileName(string photoId)
    {
        return $"{photoId}-thumbnail.jpg";
    }

    private static string GetDetailedThumbnailCacheFileName(string photoId, int maxPixelWidth, int maxPixelHeight)
    {
        return $"{photoId}-detailed-{Math.Max(1, maxPixelWidth)}x{Math.Max(1, maxPixelHeight)}.jpg";
    }

    private static async Task<Bitmap> DecodeBitmapAsync(AlbumImageEncodedLease bytes, CancellationToken cancellationToken)
    {
        await DecodeGate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = bytes.OpenReadStream();
            return new Bitmap(stream);
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    private static Bitmap CloneBitmap(Bitmap bitmap)
    {
        return bitmap.CreateScaledBitmap(bitmap.PixelSize, BitmapInterpolationMode.None);
    }

    private static long EstimateBitmapBytes(Bitmap bitmap)
    {
        return Math.Max(1L, bitmap.PixelSize.Width) * Math.Max(1L, bitmap.PixelSize.Height) * 4L;
    }

    private async Task<Bitmap> DecodeCachedBitmapAsync(
        AlbumImageCacheLoadResult result,
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        CancellationToken cancellationToken)
    {
        try
        {
            return await DecodeBitmapAsync(result.Bytes, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (result.IsCached)
        {
            RemoveCacheEntry(albumId, cacheFileName, kind);
            throw new AlbumImageCacheEntryInvalidException(kind, ex);
        }
    }

    private void RemoveCacheEntry(string albumId, string cacheFileName, AlbumImageCacheKind kind)
    {
        var key = new MemoryCacheKey(albumId, cacheFileName, kind);
        lock (_memoryCacheSync)
        {
            RemoveMemoryCacheNoLock(key);
            RemoveBitmapCacheNoLock(key);
        }

        var cachePath = Path.Combine(
            GetAlbumCachePath(albumId),
            GetCacheFileSystemName(cacheFileName));
        try
        {
            File.Delete(cachePath);
        }
        catch
        {
        }
    }

    private string GetAlbumCachePath(string albumId)
    {
        return Path.Combine(_rootPath, GetAlbumCacheDirectoryName(albumId));
    }

    private IReadOnlyList<string> GetAlbumCachePathsForClear(string albumId)
    {
        return new[]
        {
            GetAlbumCachePath(albumId),
            Path.Combine(_rootPath, SanitizePathSegment(albumId))
        }
        .Distinct(StringComparer.Ordinal)
        .ToList();
    }

    private static string GetAlbumCacheDirectoryName(string albumId)
    {
        return EnsureSafePathSegment($"{SanitizePathSegment(albumId)}.{GetPathHash(albumId)}");
    }

    private static string GetCacheFileSystemName(string cacheFileName)
    {
        const string fastThumbnailSuffix = "-thumbnail.jpg";
        const string detailedThumbnailSuffix = "-detailed-220x150.jpg";

        var sanitized = SanitizePathSegment(cacheFileName);
        var hash = GetPathHash(cacheFileName);
        if (sanitized.EndsWith(fastThumbnailSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return EnsureSafePathSegment($"{sanitized[..^fastThumbnailSuffix.Length]}.{hash}{fastThumbnailSuffix}");
        }

        if (sanitized.EndsWith(detailedThumbnailSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return EnsureSafePathSegment($"{sanitized[..^detailedThumbnailSuffix.Length]}.{hash}{detailedThumbnailSuffix}");
        }

        var extension = Path.GetExtension(sanitized);
        var stem = Path.GetFileNameWithoutExtension(sanitized);
        const string originalSuffix = "-full";
        if (stem.EndsWith(originalSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return EnsureSafePathSegment($"{stem[..^originalSuffix.Length]}.{hash}{originalSuffix}{ShortenCacheExtension(extension)}");
        }

        var cacheFileSystemName = string.IsNullOrEmpty(extension)
            ? $"{sanitized}.{hash}"
            : $"{stem}.{hash}{extension}";
        return EnsureSafePathSegment(cacheFileSystemName);
    }

    private static string ShortenCacheExtension(string extension)
    {
        return extension.Length <= MaxCacheExtensionLength
            ? extension
            : extension[..MaxCacheExtensionLength];
    }

    private static string GetPathHash(string value)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        return hash[..16];
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }

    private static string EnsureSafePathSegment(string value)
    {
        var safe = value.TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(safe))
        {
            return "_";
        }

        var firstPartEnd = safe.IndexOf('.');
        var firstPart = firstPartEnd < 0 ? safe : safe[..firstPartEnd];
        firstPart = firstPart.TrimEnd(' ');
        if (IsWindowsDeviceName(firstPart))
        {
            safe = $"_{safe}";
        }

        return ShortenPathSegment(safe);
    }

    private static string ShortenPathSegment(string value)
    {
        if (value.Length <= MaxCachePathSegmentLength)
        {
            return value;
        }

        var hashMarker = $".{GetPathHash(value)}";
        const int headLength = 80;
        var tailLength = MaxCachePathSegmentLength - headLength - hashMarker.Length;
        return $"{value[..headLength]}{hashMarker}{value[^tailLength..]}";
    }

    private static bool IsWindowsDeviceName(string value)
    {
        if (value.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.Length == 4 &&
            (value.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
             value.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            value[3] >= '1' &&
            value[3] <= '9';
    }

    private string? GetCachedPath(string albumId, string cacheFileName)
    {
        var albumPath = GetAlbumCachePath(albumId);
        var cachePath = Path.Combine(albumPath, GetCacheFileSystemName(cacheFileName));
        return File.Exists(cachePath) ? cachePath : null;
    }

    private async Task<AlbumImageBitmapLease> LoadUncachedDisplayBitmapAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        int maxPixelWidth,
        int maxPixelHeight,
        bool isOriginalSource,
        CancellationToken cancellationToken)
    {
        if (isOriginalSource && CanUseOriginalImageCache())
        {
            var original = await GetBitmapImageAsync(
                albumId,
                cacheFileName,
                AlbumImageCacheKind.OriginalImage,
                AlbumImageCacheReadMode.Eager,
                () => OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken),
                cancellationToken);

            try
            {
                return AlbumImageBitmapLease.CreateEphemeral(
                    CreateDisplayBitmap(
                        original.Bitmap.Bitmap,
                        maxPixelWidth,
                        maxPixelHeight,
                        cancellationToken));
            }
            finally
            {
                original.Bitmap.Dispose();
            }
        }

        return await TransientRetryPolicy.ExecuteAsync(
            async token =>
            {
                await using var source = await OpenSeekableSourceStreamAsync(downloadUrl, httpClient, token);
                await DecodeGate.WaitAsync(token);
                try
                {
                    await using var resizedStream = await Task.Run(
                        () => CreateDisplayImageStream(source, maxPixelWidth, maxPixelHeight, token),
                        token);

                    return AlbumImageBitmapLease.CreateEphemeral(new Bitmap(resizedStream));
                }
                finally
                {
                    DecodeGate.Release();
                }
            },
            null,
            cancellationToken);
    }

    private static Bitmap CreateDisplayBitmap(
        Bitmap original,
        int maxPixelWidth,
        int maxPixelHeight,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetSize = GetScaledPixelSize(original.PixelSize, maxPixelWidth, maxPixelHeight);
        if (targetSize == original.PixelSize)
        {
            return CloneBitmap(original);
        }

        return original.CreateScaledBitmap(targetSize, BitmapInterpolationMode.MediumQuality);
    }

    private static MemoryStream CreateDisplayImageStream(
        Bitmap original,
        int maxPixelWidth,
        int maxPixelHeight,
        CancellationToken cancellationToken)
    {
        using var displayBitmap = CreateDisplayBitmap(original, maxPixelWidth, maxPixelHeight, cancellationToken);
        var output = PooledMemoryStreamFactory.GetStream("ImageCacheService.CreateDisplayImageStream.Bitmap");
        displayBitmap.Save(output, 86);
        output.Position = 0;
        return output;
    }

    private MemoryStream CreateDetailedThumbnailStream(
        AlbumImageCacheLoadResult original,
        string albumId,
        string originalCacheFileName,
        int maxPixelWidth,
        int maxPixelHeight,
        CancellationToken cancellationToken)
    {
        try
        {
            return CreateDisplayImageStream(original.Bytes, Math.Max(1, maxPixelWidth), Math.Max(1, maxPixelHeight), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (original.IsCached)
        {
            RemoveCacheEntry(albumId, originalCacheFileName, AlbumImageCacheKind.OriginalImage);
            throw new AlbumImageCacheEntryInvalidException(AlbumImageCacheKind.OriginalImage, ex);
        }
    }

    private static PixelSize GetScaledPixelSize(PixelSize pixelSize, int maxPixelWidth, int maxPixelHeight)
    {
        var scale = Math.Min(
            1d,
            Math.Min((double)maxPixelWidth / Math.Max(1, pixelSize.Width), (double)maxPixelHeight / Math.Max(1, pixelSize.Height)));
        return new PixelSize(
            Math.Max(1, (int)Math.Round(pixelSize.Width * scale)),
            Math.Max(1, (int)Math.Round(pixelSize.Height * scale)));
    }

    private static bool IsOriginalCacheFileName(string cacheFileName)
    {
        var fileName = Path.GetFileName(cacheFileName);
        if (fileName.EndsWith("-thumbnail.jpg", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("-detailed-220x150.jpg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fileName.EndsWith("-full", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(fileName).EndsWith("-full", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("-full.", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanUseOriginalImageCache()
    {
        return Limits.OriginalImageMemoryBytes > 0 ||
            Limits.OriginalImageBitmapMemoryBytes > 0 ||
            Limits.OriginalImageDiskBytes > 0;
    }

    private static string GetLocalStorageRootPath(string? configuredRootPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredRootPath))
        {
            return configuredRootPath;
        }

        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(basePath) ? Path.GetTempPath() : basePath;
    }

    private static async Task<Stream> OpenSourceStreamAsync(
        string downloadUrl,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return File.OpenRead(uri.LocalPath);
        }

        if (File.Exists(downloadUrl))
        {
            return File.OpenRead(downloadUrl);
        }

        return await httpClient.GetStreamAsync(downloadUrl, cancellationToken);
    }

    private static async Task<Stream> OpenSeekableSourceStreamAsync(
        string downloadUrl,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        return await TransientRetryPolicy.ExecuteAsync(
            async token =>
            {
                var stream = await OpenSourceStreamAsync(downloadUrl, httpClient, token);
                if (stream.CanSeek)
                {
                    return stream;
                }

                await using (stream)
                {
                    var memory = PooledMemoryStreamFactory.GetStream("ImageCacheService.OpenSeekableSourceStreamAsync");
                    await stream.CopyToAsync(memory, token);
                    memory.Position = 0;
                    return memory;
                }
            },
            null,
            cancellationToken);
    }

    private static MemoryStream CreateDisplayImageStream(
        Stream input,
        int maxPixelWidth,
        int maxPixelHeight,
        CancellationToken cancellationToken)
    {
        using var codec = SKCodec.Create(input)
            ?? throw new InvalidOperationException("The cached image could not be decoded.");
        var sourceInfo = codec.Info;

        cancellationToken.ThrowIfCancellationRequested();

        var scale = Math.Min(
            1d,
            Math.Min((double)maxPixelWidth / sourceInfo.Width, (double)maxPixelHeight / sourceInfo.Height));
        var targetWidth = Math.Max(1, (int)Math.Round(sourceInfo.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(sourceInfo.Height * scale));

        SKBitmap? decodedBitmap = null;
        SKBitmap? displayBitmap = null;
        try
        {
            if (scale < 1d)
            {
                var scaledSize = codec.GetScaledDimensions((float)scale);
                var decodeInfo = new SKImageInfo(
                    Math.Max(1, scaledSize.Width),
                    Math.Max(1, scaledSize.Height),
                    SKColorType.Bgra8888,
                    SKAlphaType.Premul);

                decodedBitmap = new SKBitmap(decodeInfo);
                var decodeResult = codec.GetPixels(decodeInfo, decodedBitmap.GetPixels());
                if (decodeResult != SKCodecResult.Success)
                {
                    throw new InvalidOperationException("The cached image could not be decoded.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                displayBitmap = decodeInfo.Width == targetWidth && decodeInfo.Height == targetHeight
                    ? decodedBitmap
                    : decodedBitmap.Resize(new SKImageInfo(targetWidth, targetHeight), SKFilterQuality.Medium)
                        ?? throw new InvalidOperationException("The cached image could not be resized.");
            }
            else
            {
                displayBitmap = SKBitmap.Decode(codec)
                    ?? throw new InvalidOperationException("The cached image could not be decoded.");
            }

            using var image = SKImage.FromBitmap(displayBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 86)
                ?? throw new InvalidOperationException("The cached image could not be encoded.");

            var output = PooledMemoryStreamFactory.GetStream("ImageCacheService.CreateDisplayImageStream.Stream");
            data.SaveTo(output);
            output.Position = 0;
            return output;
        }
        finally
        {
            if (!ReferenceEquals(displayBitmap, decodedBitmap))
            {
                displayBitmap?.Dispose();
            }

            decodedBitmap?.Dispose();
        }
    }

    private static MemoryStream CreateDisplayImageStream(
        AlbumImageEncodedLease input,
        int maxPixelWidth,
        int maxPixelHeight,
        CancellationToken cancellationToken)
    {
        using var stream = input.OpenReadStream();
        return CreateDisplayImageStream(stream, maxPixelWidth, maxPixelHeight, cancellationToken);
    }

    private sealed record MemoryCacheKey(string AlbumId, string CacheFileName, AlbumImageCacheKind Kind);

    private sealed record DiskCacheFileInfo(FileInfo File, long Length, DateTime LastAccessTimeUtc);

    private sealed class AlbumImageCacheLoadResult(AlbumImageEncodedLease bytes, bool isCached) : IDisposable
    {
        public AlbumImageCacheLoadResult(PooledByteBuffer bytes, bool isCached)
            : this(new AlbumImageEncodedLease(bytes, bytes.Dispose), isCached)
        {
        }

        public AlbumImageEncodedLease Bytes { get; } = bytes;

        public bool IsCached { get; } = isCached;

        public void Dispose()
        {
            Bytes.Dispose();
        }
    }

    private sealed record AlbumImageBitmapCacheLoadResult(AlbumImageBitmapLease Bitmap, bool IsCached);

    private sealed class EncodedMemoryCacheEntry(PooledByteBuffer bytes, long estimatedBytes, DateTime lastAccessUtc)
    {
        private readonly object _sync = new();
        private bool _removed;
        private int _referenceCount;

        public PooledByteBuffer Bytes { get; } = bytes;

        public long EstimatedBytes { get; } = estimatedBytes;

        public DateTime LastAccessUtc { get; set; } = lastAccessUtc;

        public bool TryAcquire(out AlbumImageEncodedLease lease)
        {
            lock (_sync)
            {
                if (_removed)
                {
                    lease = null!;
                    return false;
                }

                _referenceCount++;
                lease = new AlbumImageEncodedLease(Bytes, Release);
                return true;
            }
        }

        public void RemoveFromCache()
        {
            var shouldDispose = false;
            lock (_sync)
            {
                if (_removed)
                {
                    return;
                }

                _removed = true;
                shouldDispose = _referenceCount == 0;
            }

            if (shouldDispose)
            {
                Bytes.Dispose();
            }
        }

        private void Release()
        {
            var shouldDispose = false;
            lock (_sync)
            {
                if (_referenceCount <= 0)
                {
                    return;
                }

                _referenceCount--;
                shouldDispose = _removed && _referenceCount == 0;
            }

            if (shouldDispose)
            {
                Bytes.Dispose();
            }
        }
    }

    private sealed class AlbumImageEncodedLease : IDisposable
    {
        private readonly Action _release;
        private PooledByteBuffer? _bytes;

        public AlbumImageEncodedLease(PooledByteBuffer bytes, Action release)
        {
            _bytes = bytes;
            _release = release;
        }

        public PooledByteBuffer Bytes => _bytes ?? throw new ObjectDisposedException(nameof(AlbumImageEncodedLease));

        public ReadOnlyMemory<byte> ReadOnlyMemory => Bytes.ReadOnlyMemory;

        public MemoryStream OpenReadStream()
        {
            var bytes = Bytes;
            return new MemoryStream(bytes.Array, 0, bytes.Length, writable: false);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _bytes, null) is not null)
            {
                _release();
            }
        }
    }

    private sealed class BitmapMemoryCacheEntry(Bitmap bitmap, long estimatedBytes, DateTime lastAccessUtc)
    {
        private readonly object _sync = new();
        private bool _removed;
        private int _referenceCount;

        public Bitmap Bitmap { get; } = bitmap;

        public long EstimatedBytes { get; } = estimatedBytes;

        public DateTime LastAccessUtc { get; set; } = lastAccessUtc;

        public int ReferenceCount
        {
            get
            {
                lock (_sync)
                {
                    return _referenceCount;
                }
            }
        }

        public bool TryAcquire(out AlbumImageBitmapLease lease)
        {
            lock (_sync)
            {
                if (_removed)
                {
                    lease = null!;
                    return false;
                }

                _referenceCount++;
                lease = new AlbumImageBitmapLease(Bitmap, Release);
                return true;
            }
        }

        public void RemoveFromCache()
        {
            var shouldDispose = false;
            lock (_sync)
            {
                if (_removed)
                {
                    return;
                }

                _removed = true;
                shouldDispose = _referenceCount == 0;
            }

            if (shouldDispose)
            {
                Bitmap.Dispose();
            }
        }

        private void Release()
        {
            var shouldDispose = false;
            lock (_sync)
            {
                if (_referenceCount <= 0)
                {
                    return;
                }

                _referenceCount--;
                shouldDispose = _removed && _referenceCount == 0;
            }

            if (shouldDispose)
            {
                Bitmap.Dispose();
            }
        }
    }
}

public sealed class AlbumImageBitmapLease : IDisposable
{
    private readonly Action _release;
    private bool _disposed;

    internal AlbumImageBitmapLease(Bitmap bitmap, Action release)
    {
        Bitmap = bitmap;
        _release = release;
    }

    public Bitmap Bitmap { get; }

    internal static AlbumImageBitmapLease CreateEphemeral(Bitmap bitmap)
    {
        return new AlbumImageBitmapLease(bitmap, bitmap.Dispose);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _release();
    }
}

public enum AlbumImageCacheReadMode
{
    Eager,
    Lazy,
    Lookup
}

public enum AlbumImageCacheKind
{
    FastThumbnail,
    DetailedThumbnail,
    OriginalImage
}

public sealed class AlbumImageCacheEntryInvalidException(AlbumImageCacheKind kind, Exception innerException)
    : Exception("A cached image entry was invalid and has been removed.", innerException)
{
    public AlbumImageCacheKind Kind { get; } = kind;
}

public sealed record AlbumFastThumbnailBitmapLoadResult(AlbumImageBitmapLease Bitmap, bool FastThumbnailLoaded);

public sealed record AlbumDetailedThumbnailBitmapLoadResult(AlbumImageBitmapLease Bitmap, bool DetailedThumbnailLoaded, bool OriginalImageLoaded);

public sealed record AlbumDetailedThumbnailWarmResult(bool DetailedThumbnailLoaded, bool OriginalImageLoaded);

public sealed record AlbumImageCacheLimits(
    long FastThumbnailMemoryBytes,
    long DetailedThumbnailMemoryBytes,
    long OriginalImageMemoryBytes,
    long FastThumbnailBitmapMemoryBytes,
    long DetailedThumbnailBitmapMemoryBytes,
    long OriginalImageBitmapMemoryBytes,
    long FastThumbnailDiskBytes,
    long DetailedThumbnailDiskBytes,
    long OriginalImageDiskBytes)
{
    public static AlbumImageCacheLimits Default { get; } = new(
        Megabytes(256),
        Megabytes(512),
        Megabytes(256),
        Megabytes(64),
        Megabytes(128),
        Megabytes(64),
        Megabytes(512),
        Megabytes(2048),
        Megabytes(2048));

    public AlbumImageCacheLimits(
        long FastThumbnailMemoryBytes,
        long DetailedThumbnailMemoryBytes,
        long OriginalImageMemoryBytes,
        long FastThumbnailDiskBytes,
        long DetailedThumbnailDiskBytes,
        long OriginalImageDiskBytes)
        : this(
            FastThumbnailMemoryBytes,
            DetailedThumbnailMemoryBytes,
            OriginalImageMemoryBytes,
            Megabytes(64),
            Megabytes(128),
            Megabytes(64),
            FastThumbnailDiskBytes,
            DetailedThumbnailDiskBytes,
            OriginalImageDiskBytes)
    {
    }

    public long GetMemoryBytes(AlbumImageCacheKind kind)
    {
        return kind switch
        {
            AlbumImageCacheKind.FastThumbnail => FastThumbnailMemoryBytes,
            AlbumImageCacheKind.DetailedThumbnail => DetailedThumbnailMemoryBytes,
            AlbumImageCacheKind.OriginalImage => OriginalImageMemoryBytes,
            _ => 0
        };
    }

    public long GetDiskBytes(AlbumImageCacheKind kind)
    {
        return kind switch
        {
            AlbumImageCacheKind.FastThumbnail => FastThumbnailDiskBytes,
            AlbumImageCacheKind.DetailedThumbnail => DetailedThumbnailDiskBytes,
            AlbumImageCacheKind.OriginalImage => OriginalImageDiskBytes,
            _ => 0
        };
    }

    public long GetBitmapMemoryBytes(AlbumImageCacheKind kind)
    {
        return kind switch
        {
            AlbumImageCacheKind.FastThumbnail => FastThumbnailBitmapMemoryBytes,
            AlbumImageCacheKind.DetailedThumbnail => DetailedThumbnailBitmapMemoryBytes,
            AlbumImageCacheKind.OriginalImage => OriginalImageBitmapMemoryBytes,
            _ => 0
        };
    }

    public static long Megabytes(int value)
    {
        return Math.Max(0L, value) * 1024L * 1024L;
    }
}
