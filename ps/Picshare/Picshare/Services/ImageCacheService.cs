namespace Picshare.Services;

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
    private readonly Dictionary<MemoryCacheKey, MemoryCacheEntry> _memoryCache = new();
    private readonly string _rootPath;
    private AlbumImageCacheLimits _limits = AlbumImageCacheLimits.Default;

    public ImageCacheService(string? localStorageRootPath = null)
    {
        var basePath = GetLocalStorageRootPath(localStorageRootPath);
        _rootPath = Path.Combine(basePath, "Picshare", "cache", "images");
    }

    private bool _cacheThumbnails = true;
    private bool _cacheOriginalImages = true;

    public bool CacheThumbnails
    {
        get => _cacheThumbnails;
        set
        {
            if (_cacheThumbnails == value)
            {
                return;
            }

            _cacheThumbnails = value;
            TrimAllDiskCaches();
        }
    }

    public bool CacheOriginalImages
    {
        get => _cacheOriginalImages;
        set
        {
            if (_cacheOriginalImages == value)
            {
                return;
            }

            _cacheOriginalImages = value;
            TrimAllDiskCaches();
        }
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
            var result = await GetEncodedImageBytesAsync(
                albumId,
                cacheFileName,
                AlbumImageCacheKind.FastThumbnail,
                readMode,
                () => OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken),
                cancellationToken);

            var bitmap = await DecodeCachedBitmapAsync(result, albumId, cacheFileName, AlbumImageCacheKind.FastThumbnail, cancellationToken);
            return new AlbumFastThumbnailBitmapLoadResult(bitmap, result.IsCached);
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
        return await LoadDetailedThumbnailBitmapCoreAsync(
            albumId,
            photoId,
            originalCacheFileName,
            originalDownloadUrl,
            httpClient,
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
        AlbumImageCacheReadMode detailedReadMode,
        AlbumImageCacheReadMode originalReadMode,
        bool retryInvalidOriginalCacheEntry,
        CancellationToken cancellationToken)
    {
        var cacheFileName = GetDetailedThumbnailCacheFileName(photoId);
        var originalImageLoaded = false;
        try
        {
            var result = await GetEncodedImageBytesAsync(
                albumId,
                cacheFileName,
                AlbumImageCacheKind.DetailedThumbnail,
                detailedReadMode,
                async () =>
                {
                    var original = await GetOriginalEncodedBytesAsync(
                        albumId,
                        originalCacheFileName,
                        originalDownloadUrl,
                        httpClient,
                        originalReadMode,
                        cancellationToken);
                    originalImageLoaded = original.IsCached;
                    try
                    {
                        var detailedThumbnail = CreateDisplayImageStream(original.Bytes, 220, 150, cancellationToken);
                        return detailedThumbnail;
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
                },
                cancellationToken);

            var bitmap = await DecodeCachedBitmapAsync(result, albumId, cacheFileName, AlbumImageCacheKind.DetailedThumbnail, cancellationToken);
            return new AlbumDetailedThumbnailBitmapLoadResult(bitmap, result.IsCached, originalImageLoaded);
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
        var result = await GetEncodedImageBytesAsync(
            albumId,
            GetFastThumbnailCacheFileName(photoId),
            AlbumImageCacheKind.FastThumbnail,
            readMode,
            () => OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken),
            cancellationToken);
        return result.IsCached;
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
        return await WarmDetailedThumbnailCoreAsync(
            albumId,
            photoId,
            originalCacheFileName,
            originalDownloadUrl,
            httpClient,
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
        AlbumImageCacheReadMode detailedReadMode,
        AlbumImageCacheReadMode originalReadMode,
        bool retryInvalidOriginalCacheEntry,
        CancellationToken cancellationToken)
    {
        var originalImageLoaded = false;
        try
        {
            var result = await GetEncodedImageBytesAsync(
                albumId,
                GetDetailedThumbnailCacheFileName(photoId),
                AlbumImageCacheKind.DetailedThumbnail,
                detailedReadMode,
                async () =>
                {
                    var original = await GetOriginalEncodedBytesAsync(
                        albumId,
                        originalCacheFileName,
                        originalDownloadUrl,
                        httpClient,
                        originalReadMode,
                        cancellationToken);
                    originalImageLoaded = original.IsCached;
                    try
                    {
                        var detailedThumbnail = CreateDisplayImageStream(original.Bytes, 220, 150, cancellationToken);
                        return detailedThumbnail;
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
                },
                cancellationToken);
            return new AlbumDetailedThumbnailWarmResult(result.IsCached, originalImageLoaded);
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
        var result = await GetOriginalEncodedBytesAsync(albumId, cacheFileName, downloadUrl, httpClient, readMode, cancellationToken);
        return result.IsCached;
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
            if (!destination.CanSeek)
            {
                await destination.WriteAsync(cached.Bytes, cancellationToken);
                return;
            }

            var cachedDestinationStart = destination.Position;
            await TransientRetryPolicy.ExecuteAsync(
                async token =>
                {
                    destination.Position = cachedDestinationStart;
                    destination.SetLength(cachedDestinationStart);
                    await destination.WriteAsync(cached.Bytes, token);
                },
                null,
                cancellationToken);
            return;
        }

        if (!destination.CanSeek)
        {
            await using var buffered = await TransientRetryPolicy.ExecuteAsync(
                async token =>
                {
                    await using var source = await OpenSourceStreamAsync(downloadUrl, httpClient, token);
                    var memory = new MemoryStream();
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
            _memoryCache.Clear();
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
                _memoryCache.Remove(key);
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

    public async Task<Bitmap> LoadDisplayBitmapAsync(
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

    public async Task<Bitmap> LoadOriginalBitmapAsync(
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

    public async Task<Bitmap> LoadOriginalBitmapAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await GetOriginalEncodedBytesAsync(albumId, cacheFileName, downloadUrl, httpClient, readMode, cancellationToken);
            return await DecodeCachedBitmapAsync(result, albumId, cacheFileName, AlbumImageCacheKind.OriginalImage, cancellationToken);
        }
        catch (AlbumImageCacheEntryInvalidException) when (readMode != AlbumImageCacheReadMode.Lookup)
        {
            var result = await GetOriginalEncodedBytesAsync(albumId, cacheFileName, downloadUrl, httpClient, readMode, cancellationToken);
            return await DecodeCachedBitmapAsync(result, albumId, cacheFileName, AlbumImageCacheKind.OriginalImage, cancellationToken);
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

    private async Task<AlbumImageCacheLoadResult> GetEncodedImageBytesAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        Func<Task<Stream>> openSourceAsync,
        CancellationToken cancellationToken)
    {
        if (TryGetMemoryCache(albumId, cacheFileName, kind, readMode, out var memoryBytes))
        {
            return new AlbumImageCacheLoadResult(memoryBytes, IsCached: true);
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

                var diskBytes = await File.ReadAllBytesAsync(diskPath, cancellationToken);
                if (readMode == AlbumImageCacheReadMode.Eager)
                {
                    AddMemoryCache(albumId, cacheFileName, kind, diskBytes, readMode);
                    TrimDiskCache(albumId, kind, diskLimit);
                }

                return new AlbumImageCacheLoadResult(diskBytes, IsCached: true);
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
                await using var memory = new MemoryStream();
                await source.CopyToAsync(memory, token);
                return memory.ToArray();
            },
            null,
            cancellationToken);
        var memoryCached = AddMemoryCache(albumId, cacheFileName, kind, bytes, readMode);
        var diskCached = await AddDiskCacheAsync(albumId, cacheFileName, kind, bytes, readMode, cancellationToken);
        return new AlbumImageCacheLoadResult(bytes, memoryCached || diskCached);
    }

    private bool TryGetMemoryCache(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        out byte[] bytes)
    {
        var limit = Limits.GetMemoryBytes(kind);
        if (limit <= 0)
        {
            bytes = [];
            return false;
        }

        var key = new MemoryCacheKey(albumId, cacheFileName, kind);
        lock (_memoryCacheSync)
        {
            if (_memoryCache.TryGetValue(key, out var entry))
            {
                if (entry.Bytes.LongLength > limit)
                {
                    _memoryCache.Remove(key);
                    bytes = [];
                    return false;
                }

                if (readMode == AlbumImageCacheReadMode.Eager)
                {
                    entry.LastAccessUtc = DateTime.UtcNow;
                }

                bytes = entry.Bytes;
                return true;
            }
        }

        bytes = [];
        return false;
    }

    private bool AddMemoryCache(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        byte[] bytes,
        AlbumImageCacheReadMode readMode)
    {
        var limit = Limits.GetMemoryBytes(kind);
        if (limit <= 0 || bytes.LongLength > limit)
        {
            return false;
        }

        var key = new MemoryCacheKey(albumId, cacheFileName, kind);
        lock (_memoryCacheSync)
        {
            _memoryCache.TryGetValue(key, out var existingEntry);
            if (existingEntry is not null && readMode == AlbumImageCacheReadMode.Lazy)
            {
                return true;
            }

            var albumKindSize = GetMemoryCacheSizeNoLock(albumId, kind) - (existingEntry?.Bytes.LongLength ?? 0);
            if (readMode == AlbumImageCacheReadMode.Lazy && albumKindSize + bytes.LongLength > limit)
            {
                return false;
            }

            _memoryCache[key] = new MemoryCacheEntry(bytes, DateTime.UtcNow);
            if (readMode == AlbumImageCacheReadMode.Eager)
            {
                TrimMemoryCacheNoLock(albumId, kind, limit);
            }

            return true;
        }
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
        }
    }

    private long GetMemoryCacheSizeNoLock(string albumId, AlbumImageCacheKind kind)
    {
        return _memoryCache
            .Where(item => string.Equals(item.Key.AlbumId, albumId, StringComparison.Ordinal) && item.Key.Kind == kind)
            .Sum(item => (long)item.Value.Bytes.Length);
    }

    private void TrimMemoryCacheNoLock(string albumId, AlbumImageCacheKind kind, long limit)
    {
        if (limit <= 0)
        {
            foreach (var key in _memoryCache.Keys
                .Where(key => string.Equals(key.AlbumId, albumId, StringComparison.Ordinal) && key.Kind == kind)
                .ToList())
            {
                _memoryCache.Remove(key);
            }

            return;
        }

        while (GetMemoryCacheSizeNoLock(albumId, kind) > limit)
        {
            var oldest = _memoryCache
                .Where(item => string.Equals(item.Key.AlbumId, albumId, StringComparison.Ordinal) && item.Key.Kind == kind)
                .OrderBy(item => item.Value.LastAccessUtc)
                .Select(item => item.Key)
                .FirstOrDefault();

            if (oldest is null)
            {
                return;
            }

            _memoryCache.Remove(oldest);
        }
    }

    private async Task<bool> AddDiskCacheAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        byte[] bytes,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        if (!IsDiskCachingEnabled(kind))
        {
            return false;
        }

        var limit = Limits.GetDiskBytes(kind);
        if (limit <= 0 || bytes.LongLength > limit)
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
                        TrimDiskCache(albumId, kind, limit);
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

        if (readMode == AlbumImageCacheReadMode.Lazy && !CanFitLazyDiskCacheEntry(albumId, cachePath, kind, bytes.LongLength, limit))
        {
            return false;
        }

        var tempPath = Path.Combine(albumPath, $".{Guid.NewGuid():N}.tmp");
        var cached = false;
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
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
            TrimDiskCache(albumId, kind, limit);
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
                TrimDiskCachePath(albumPath, kind, IsDiskCachingEnabled(kind) ? Limits.GetDiskBytes(kind) : 0);
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
        TrimDiskCachePath(GetAlbumCachePath(albumId), kind, limit);
    }

    private static void TrimDiskCachePath(string albumPath, AlbumImageCacheKind kind, long limit)
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
        return kind switch
        {
            AlbumImageCacheKind.FastThumbnail or AlbumImageCacheKind.DetailedThumbnail => CacheThumbnails,
            AlbumImageCacheKind.OriginalImage => CacheOriginalImages,
            _ => true
        };
    }

    private static string GetFastThumbnailCacheFileName(string photoId)
    {
        return $"{photoId}-thumbnail.jpg";
    }

    private static string GetDetailedThumbnailCacheFileName(string photoId)
    {
        return $"{photoId}-detailed-220x150.jpg";
    }

    private static async Task<Bitmap> DecodeBitmapAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await DecodeGate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new MemoryStream(bytes, writable: false);
            return new Bitmap(stream);
        }
        finally
        {
            DecodeGate.Release();
        }
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
            _memoryCache.Remove(key);
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

    private async Task<Bitmap> LoadUncachedDisplayBitmapAsync(
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
            var original = await GetOriginalEncodedBytesAsync(
                albumId,
                cacheFileName,
                downloadUrl,
                httpClient,
                AlbumImageCacheReadMode.Eager,
                cancellationToken);

            try
            {
                return await CreateDisplayBitmapFromOriginalBytesAsync(
                    original.Bytes,
                    maxPixelWidth,
                    maxPixelHeight,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (original.IsCached)
            {
                RemoveCacheEntry(albumId, cacheFileName, AlbumImageCacheKind.OriginalImage);
                throw new AlbumImageCacheEntryInvalidException(AlbumImageCacheKind.OriginalImage, ex);
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

                    return new Bitmap(resizedStream);
                }
                finally
                {
                    DecodeGate.Release();
                }
            },
            null,
            cancellationToken);
    }

    private static async Task<Bitmap> CreateDisplayBitmapFromOriginalBytesAsync(
        byte[] bytes,
        int maxPixelWidth,
        int maxPixelHeight,
        CancellationToken cancellationToken)
    {
        await DecodeGate.WaitAsync(cancellationToken);
        try
        {
            await using var resizedStream = await Task.Run(
                () => CreateDisplayImageStream(bytes, maxPixelWidth, maxPixelHeight, cancellationToken),
                cancellationToken);

            return new Bitmap(resizedStream);
        }
        finally
        {
            DecodeGate.Release();
        }
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
            CacheOriginalImages && Limits.OriginalImageDiskBytes > 0;
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
                    var memory = new MemoryStream();
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
        using var original = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException("The cached image could not be decoded.");

        cancellationToken.ThrowIfCancellationRequested();

        var scale = Math.Min(
            1d,
            Math.Min((double)maxPixelWidth / original.Width, (double)maxPixelHeight / original.Height));
        var targetWidth = Math.Max(1, (int)Math.Round(original.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(original.Height * scale));

        using var displayBitmap = scale < 1d
            ? original.Resize(new SKImageInfo(targetWidth, targetHeight), SKFilterQuality.Medium)
                ?? throw new InvalidOperationException("The cached image could not be resized.")
            : original.Copy();

        using var image = SKImage.FromBitmap(displayBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 86)
            ?? throw new InvalidOperationException("The cached image could not be encoded.");

        var output = new MemoryStream();
        data.SaveTo(output);
        output.Position = 0;
        return output;
    }

    private static MemoryStream CreateDisplayImageStream(
        byte[] input,
        int maxPixelWidth,
        int maxPixelHeight,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(input, writable: false);
        return CreateDisplayImageStream(stream, maxPixelWidth, maxPixelHeight, cancellationToken);
    }

    private sealed record MemoryCacheKey(string AlbumId, string CacheFileName, AlbumImageCacheKind Kind);

    private sealed record DiskCacheFileInfo(FileInfo File, long Length, DateTime LastAccessTimeUtc);

    private sealed record AlbumImageCacheLoadResult(byte[] Bytes, bool IsCached);

    private sealed class MemoryCacheEntry(byte[] bytes, DateTime lastAccessUtc)
    {
        public byte[] Bytes { get; } = bytes;

        public DateTime LastAccessUtc { get; set; } = lastAccessUtc;
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

public sealed record AlbumFastThumbnailBitmapLoadResult(Bitmap Bitmap, bool FastThumbnailLoaded);

public sealed record AlbumDetailedThumbnailBitmapLoadResult(Bitmap Bitmap, bool DetailedThumbnailLoaded, bool OriginalImageLoaded);

public sealed record AlbumDetailedThumbnailWarmResult(bool DetailedThumbnailLoaded, bool OriginalImageLoaded);

public sealed record AlbumImageCacheLimits(
    long FastThumbnailMemoryBytes,
    long DetailedThumbnailMemoryBytes,
    long OriginalImageMemoryBytes,
    long FastThumbnailDiskBytes,
    long DetailedThumbnailDiskBytes,
    long OriginalImageDiskBytes)
{
    public static AlbumImageCacheLimits Default { get; } = new(
        Megabytes(256),
        Megabytes(512),
        Megabytes(256),
        Megabytes(512),
        Megabytes(2048),
        Megabytes(2048));

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

    public static long Megabytes(int value)
    {
        return Math.Max(0L, value) * 1024L * 1024L;
    }
}
