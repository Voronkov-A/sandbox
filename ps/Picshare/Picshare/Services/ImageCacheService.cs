namespace Picshare.Services;

using Avalonia.Media.Imaging;
using SkiaSharp;

public sealed class ImageCacheService
{
    private static readonly SemaphoreSlim DecodeGate = new(2);
    private readonly object _memoryCacheSync = new();
    private readonly Dictionary<MemoryCacheKey, MemoryCacheEntry> _memoryCache = new();
    private readonly string _rootPath;

    public ImageCacheService(string? localStorageRootPath = null)
    {
        var basePath = GetLocalStorageRootPath(localStorageRootPath);
        _rootPath = Path.Combine(basePath, "Picshare", "cache", "images");
    }

    public bool CacheThumbnails { get; set; } = true;

    public bool CacheOriginalImages { get; set; } = true;

    public AlbumImageCacheLimits Limits { get; set; } = AlbumImageCacheLimits.Default;

    public bool HasCacheRoom(string albumId, AlbumImageCacheKind kind)
    {
        var hasMemoryRoom = GetMemoryCacheSize(albumId, kind) < Limits.GetMemoryBytes(kind);
        var hasDiskRoom = IsDiskCachingEnabled(kind) && GetDiskCacheSize(albumId, kind) < Limits.GetDiskBytes(kind);
        return hasMemoryRoom || hasDiskRoom;
    }

    public async Task<Bitmap> LoadFastThumbnailBitmapAsync(
        string albumId,
        string photoId,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        var cacheFileName = GetFastThumbnailCacheFileName(photoId);
        var bytes = await GetEncodedImageBytesAsync(
            albumId,
            cacheFileName,
            AlbumImageCacheKind.FastThumbnail,
            readMode,
            () => OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken),
            cancellationToken);

        return await DecodeBitmapAsync(bytes, cancellationToken);
    }

    public async Task<Bitmap> LoadDetailedThumbnailBitmapAsync(
        string albumId,
        string photoId,
        string originalCacheFileName,
        string originalDownloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode detailedReadMode,
        AlbumImageCacheReadMode originalReadMode,
        CancellationToken cancellationToken)
    {
        var cacheFileName = GetDetailedThumbnailCacheFileName(photoId);
        var bytes = await GetEncodedImageBytesAsync(
            albumId,
            cacheFileName,
            AlbumImageCacheKind.DetailedThumbnail,
            detailedReadMode,
            async () =>
            {
                var originalBytes = await GetOriginalEncodedBytesAsync(
                    albumId,
                    originalCacheFileName,
                    originalDownloadUrl,
                    httpClient,
                    originalReadMode,
                    cancellationToken);
                return CreateDisplayImageStream(originalBytes, 220, 150, cancellationToken);
            },
            cancellationToken);

        return await DecodeBitmapAsync(bytes, cancellationToken);
    }

    public async Task WarmFastThumbnailAsync(
        string albumId,
        string photoId,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        _ = await GetEncodedImageBytesAsync(
            albumId,
            GetFastThumbnailCacheFileName(photoId),
            AlbumImageCacheKind.FastThumbnail,
            readMode,
            () => OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken),
            cancellationToken);
    }

    public async Task WarmDetailedThumbnailAsync(
        string albumId,
        string photoId,
        string originalCacheFileName,
        string originalDownloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode detailedReadMode,
        AlbumImageCacheReadMode originalReadMode,
        CancellationToken cancellationToken)
    {
        _ = await GetEncodedImageBytesAsync(
            albumId,
            GetDetailedThumbnailCacheFileName(photoId),
            AlbumImageCacheKind.DetailedThumbnail,
            detailedReadMode,
            async () =>
            {
                var originalBytes = await GetOriginalEncodedBytesAsync(
                    albumId,
                    originalCacheFileName,
                    originalDownloadUrl,
                    httpClient,
                    originalReadMode,
                    cancellationToken);
                return CreateDisplayImageStream(originalBytes, 220, 150, cancellationToken);
            },
            cancellationToken);
    }

    public async Task WarmOriginalAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        _ = await GetOriginalEncodedBytesAsync(albumId, cacheFileName, downloadUrl, httpClient, readMode, cancellationToken);
    }

    public async Task<string> GetOrDownloadAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var albumPath = Path.Combine(_rootPath, SanitizePathSegment(albumId));
        Directory.CreateDirectory(albumPath);

        var cachePath = Path.Combine(albumPath, SanitizePathSegment(cacheFileName));
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        var tempPath = Path.Combine(albumPath, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await TransientRetryPolicy.ExecuteAsync(
                async token =>
                {
                    if (File.Exists(cachePath))
                    {
                        return;
                    }

                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }

                    await using (var remoteStream = await OpenSourceStreamAsync(downloadUrl, httpClient, token))
                    await using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        await remoteStream.CopyToAsync(fileStream, token);
                    }

                    File.Move(tempPath, cachePath, overwrite: true);
                },
                null,
                cancellationToken);
            return cachePath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task CopyOriginalToAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        Stream destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var cachedBytes = await GetOriginalEncodedBytesAsync(
                albumId,
                cacheFileName,
                downloadUrl,
                httpClient,
                AlbumImageCacheReadMode.Lookup,
                cancellationToken);
            await destination.WriteAsync(cachedBytes, cancellationToken);
            return;
        }
        catch (FileNotFoundException)
        {
        }

        await using var source = await OpenSourceStreamAsync(downloadUrl, httpClient, cancellationToken);
        await source.CopyToAsync(destination, cancellationToken);
    }

    public Task ClearAsync()
    {
        lock (_memoryCacheSync)
        {
            _memoryCache.Clear();
        }

        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
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

        var albumPath = Path.Combine(_rootPath, SanitizePathSegment(albumId));
        if (Directory.Exists(albumPath))
        {
            Directory.Delete(albumPath, recursive: true);
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
        if (!CacheThumbnails)
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

        var imagePath = await GetOrCreateDisplayImageAsync(
            albumId,
            cacheFileName,
            downloadUrl,
            httpClient,
            maxPixelWidth,
            maxPixelHeight,
            isOriginalSource,
            cancellationToken);

        await DecodeGate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.OpenRead(imagePath);
            return new Bitmap(stream);
        }
        finally
        {
            DecodeGate.Release();
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
        var bytes = await GetOriginalEncodedBytesAsync(albumId, cacheFileName, downloadUrl, httpClient, readMode, cancellationToken);
        return await DecodeBitmapAsync(bytes, cancellationToken);
    }

    private async Task<byte[]> GetOriginalEncodedBytesAsync(
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

    private async Task<byte[]> GetEncodedImageBytesAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        Func<Task<Stream>> openSourceAsync,
        CancellationToken cancellationToken)
    {
        if (TryGetMemoryCache(albumId, cacheFileName, kind, readMode, out var memoryBytes))
        {
            return memoryBytes;
        }

        var diskPath = GetCachedPath(albumId, cacheFileName);
        if (diskPath is not null)
        {
            if (readMode == AlbumImageCacheReadMode.Eager)
            {
                File.SetLastAccessTimeUtc(diskPath, DateTime.UtcNow);
            }

            var diskBytes = await File.ReadAllBytesAsync(diskPath, cancellationToken);
            AddMemoryCache(albumId, cacheFileName, kind, diskBytes, readMode);
            return diskBytes;
        }

        if (readMode == AlbumImageCacheReadMode.Lookup)
        {
            throw new FileNotFoundException("The image is not available in cache.", cacheFileName);
        }

        await using var source = await TransientRetryPolicy.ExecuteAsync(
            async token => await openSourceAsync(),
            null,
            cancellationToken);
        await using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        AddMemoryCache(albumId, cacheFileName, kind, bytes, readMode);
        await AddDiskCacheAsync(albumId, cacheFileName, kind, bytes, readMode, cancellationToken);
        return bytes;
    }

    private bool TryGetMemoryCache(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        AlbumImageCacheReadMode readMode,
        out byte[] bytes)
    {
        var key = new MemoryCacheKey(albumId, cacheFileName, kind);
        lock (_memoryCacheSync)
        {
            if (_memoryCache.TryGetValue(key, out var entry))
            {
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

    private void AddMemoryCache(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        byte[] bytes,
        AlbumImageCacheReadMode readMode)
    {
        var limit = Limits.GetMemoryBytes(kind);
        if (limit <= 0 || bytes.LongLength > limit)
        {
            return;
        }

        var key = new MemoryCacheKey(albumId, cacheFileName, kind);
        lock (_memoryCacheSync)
        {
            var albumKindSize = GetMemoryCacheSizeNoLock(albumId, kind);
            if (readMode == AlbumImageCacheReadMode.Lazy && albumKindSize + bytes.LongLength > limit)
            {
                return;
            }

            _memoryCache[key] = new MemoryCacheEntry(bytes, DateTime.UtcNow);
            if (readMode == AlbumImageCacheReadMode.Eager)
            {
                TrimMemoryCacheNoLock(albumId, kind, limit);
            }
        }
    }

    private long GetMemoryCacheSize(string albumId, AlbumImageCacheKind kind)
    {
        lock (_memoryCacheSync)
        {
            return GetMemoryCacheSizeNoLock(albumId, kind);
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

    private async Task AddDiskCacheAsync(
        string albumId,
        string cacheFileName,
        AlbumImageCacheKind kind,
        byte[] bytes,
        AlbumImageCacheReadMode readMode,
        CancellationToken cancellationToken)
    {
        if (!IsDiskCachingEnabled(kind))
        {
            return;
        }

        var limit = Limits.GetDiskBytes(kind);
        if (limit <= 0 || bytes.LongLength > limit)
        {
            return;
        }

        var albumPath = Path.Combine(_rootPath, SanitizePathSegment(albumId));
        Directory.CreateDirectory(albumPath);
        var cachePath = Path.Combine(albumPath, SanitizePathSegment(cacheFileName));
        if (File.Exists(cachePath))
        {
            if (readMode == AlbumImageCacheReadMode.Eager)
            {
                File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow);
            }

            return;
        }

        if (readMode == AlbumImageCacheReadMode.Lazy && GetDiskCacheSize(albumId, kind) + bytes.LongLength > limit)
        {
            return;
        }

        var tempPath = Path.Combine(albumPath, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
            File.SetLastAccessTimeUtc(tempPath, DateTime.UtcNow);
            File.Move(tempPath, cachePath, overwrite: false);
        }
        catch (IOException) when (File.Exists(cachePath))
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        if (readMode == AlbumImageCacheReadMode.Eager)
        {
            TrimDiskCache(albumId, kind, limit);
        }
    }

    private long GetDiskCacheSize(string albumId, AlbumImageCacheKind kind)
    {
        var albumPath = Path.Combine(_rootPath, SanitizePathSegment(albumId));
        if (!Directory.Exists(albumPath))
        {
            return 0;
        }

        return Directory.EnumerateFiles(albumPath)
            .Where(path => IsCacheFileForKind(path, kind))
            .Select(path => new FileInfo(path).Length)
            .Sum();
    }

    private void TrimDiskCache(string albumId, AlbumImageCacheKind kind, long limit)
    {
        var albumPath = Path.Combine(_rootPath, SanitizePathSegment(albumId));
        if (!Directory.Exists(albumPath))
        {
            return;
        }

        var files = Directory.EnumerateFiles(albumPath)
            .Where(path => IsCacheFileForKind(path, kind))
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.LastAccessTimeUtc)
            .ToList();
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (total <= limit)
            {
                break;
            }

            try
            {
                total -= file.Length;
                file.Delete();
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
            AlbumImageCacheKind.DetailedThumbnail => fileName.EndsWith("-detailed-220x150.jpg", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains(".display.", StringComparison.OrdinalIgnoreCase),
            AlbumImageCacheKind.OriginalImage => fileName.Contains("-full", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
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

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }

    private string? GetCachedPath(string albumId, string cacheFileName)
    {
        var albumPath = Path.Combine(_rootPath, SanitizePathSegment(albumId));
        var cachePath = Path.Combine(albumPath, SanitizePathSegment(cacheFileName));
        return File.Exists(cachePath) ? cachePath : null;
    }

    private async Task<string> GetOrCreateDisplayImageAsync(
        string albumId,
        string cacheFileName,
        string downloadUrl,
        HttpClient httpClient,
        int maxPixelWidth,
        int maxPixelHeight,
        bool isOriginalSource,
        CancellationToken cancellationToken)
    {
        var albumPath = Path.Combine(_rootPath, SanitizePathSegment(albumId));
        Directory.CreateDirectory(albumPath);
        var displayPath = Path.Combine(albumPath, GetDisplayCacheFileName(cacheFileName, maxPixelWidth, maxPixelHeight));
        if (File.Exists(displayPath))
        {
            return displayPath;
        }

        var tempPath = Path.Combine(albumPath, $".{Guid.NewGuid():N}.display.tmp");
        try
        {
            if (isOriginalSource && !CacheOriginalImages)
            {
                await using var source = await OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken);
                await DecodeGate.WaitAsync(cancellationToken);
                try
                {
                    await Task.Run(
                        () => CreateDisplayImageFile(source, tempPath, maxPixelWidth, maxPixelHeight, cancellationToken),
                        cancellationToken);
                }
                finally
                {
                    DecodeGate.Release();
                }
            }
            else
            {
                var sourcePath = await GetOrDownloadAsync(albumId, cacheFileName, downloadUrl, httpClient, cancellationToken);
                await DecodeGate.WaitAsync(cancellationToken);
                try
                {
                    await Task.Run(
                        () => CreateDisplayImageFile(sourcePath, tempPath, maxPixelWidth, maxPixelHeight, cancellationToken),
                        cancellationToken);
                }
                finally
                {
                    DecodeGate.Release();
                }
            }

            try
            {
                File.Move(tempPath, displayPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(displayPath))
            {
                File.Delete(tempPath);
            }

            return displayPath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
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
        if (isOriginalSource && CacheOriginalImages)
        {
            var imagePath = await GetOrDownloadAsync(albumId, cacheFileName, downloadUrl, httpClient, cancellationToken);

            await DecodeGate.WaitAsync(cancellationToken);
            try
            {
                await using var resizedStream = await Task.Run(
                    () => CreateDisplayImageStream(imagePath, maxPixelWidth, maxPixelHeight, cancellationToken),
                    cancellationToken);

                return new Bitmap(resizedStream);
            }
            finally
            {
                DecodeGate.Release();
            }
        }

        await using var source = await OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken);
        await DecodeGate.WaitAsync(cancellationToken);
        try
        {
            await using var resizedStream = await Task.Run(
                () => CreateDisplayImageStream(source, maxPixelWidth, maxPixelHeight, cancellationToken),
                cancellationToken);

            return new Bitmap(resizedStream);
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    private static string GetDisplayCacheFileName(string cacheFileName, int maxPixelWidth, int maxPixelHeight)
    {
        return SanitizePathSegment($"{cacheFileName}.{maxPixelWidth}x{maxPixelHeight}.display.jpg");
    }

    private static bool IsOriginalCacheFileName(string cacheFileName)
    {
        return cacheFileName.Contains("-full", StringComparison.Ordinal);
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

    private static void CreateDisplayImageFile(
        string imagePath,
        string destinationPath,
        int maxPixelWidth,
        int maxPixelHeight,
        CancellationToken cancellationToken)
    {
        using var input = File.OpenRead(imagePath);
        CreateDisplayImageFile(input, destinationPath, maxPixelWidth, maxPixelHeight, cancellationToken);
    }

    private static void CreateDisplayImageFile(
        Stream input,
        string destinationPath,
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

        using var output = File.Open(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        data.SaveTo(output);
    }

    private static MemoryStream CreateDisplayImageStream(
        string imagePath,
        int maxPixelWidth,
        int maxPixelHeight,
        CancellationToken cancellationToken)
    {
        using var input = File.OpenRead(imagePath);
        return CreateDisplayImageStream(input, maxPixelWidth, maxPixelHeight, cancellationToken);
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
