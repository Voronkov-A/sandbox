namespace Picshare.Services;

using Avalonia.Media.Imaging;
using SkiaSharp;

public sealed class ImageCacheService
{
    private static readonly SemaphoreSlim DecodeGate = new(2);
    private readonly string _rootPath;

    public ImageCacheService(string? localStorageRootPath = null)
    {
        var basePath = GetLocalStorageRootPath(localStorageRootPath);
        _rootPath = Path.Combine(basePath, "Picshare", "cache", "images");
    }

    public bool CacheThumbnails { get; set; } = true;

    public bool CacheOriginalImages { get; set; } = true;

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
            await using (var remoteStream = await OpenSourceStreamAsync(downloadUrl, httpClient, cancellationToken))
            await using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await remoteStream.CopyToAsync(fileStream, cancellationToken);
            }

            File.Move(tempPath, cachePath, overwrite: true);
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
        var cachePath = CacheOriginalImages ? GetCachedPath(albumId, cacheFileName) : null;
        await using var source = cachePath is null
            ? await OpenSourceStreamAsync(downloadUrl, httpClient, cancellationToken)
            : File.OpenRead(cachePath);

        await source.CopyToAsync(destination, cancellationToken);
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task ClearAlbumAsync(string albumId)
    {
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
        if (!CacheOriginalImages)
        {
            await DecodeGate.WaitAsync(cancellationToken);
            try
            {
                await using var stream = await OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken);
                return new Bitmap(stream);
            }
            finally
            {
                DecodeGate.Release();
            }
        }

        var imagePath = await GetOrDownloadAsync(albumId, cacheFileName, downloadUrl, httpClient, cancellationToken);

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
            await DecodeGate.WaitAsync(cancellationToken);
            try
            {
                if (isOriginalSource && !CacheOriginalImages)
                {
                    await using var source = await OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken);
                    await Task.Run(
                        () => CreateDisplayImageFile(source, tempPath, maxPixelWidth, maxPixelHeight, cancellationToken),
                        cancellationToken);
                }
                else
                {
                    var sourcePath = await GetOrDownloadAsync(albumId, cacheFileName, downloadUrl, httpClient, cancellationToken);
                    await Task.Run(
                        () => CreateDisplayImageFile(sourcePath, tempPath, maxPixelWidth, maxPixelHeight, cancellationToken),
                        cancellationToken);
                }
            }
            finally
            {
                DecodeGate.Release();
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

        await DecodeGate.WaitAsync(cancellationToken);
        try
        {
            await using var source = await OpenSeekableSourceStreamAsync(downloadUrl, httpClient, cancellationToken);
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
        var stream = await OpenSourceStreamAsync(downloadUrl, httpClient, cancellationToken);
        if (stream.CanSeek)
        {
            return stream;
        }

        await using (stream)
        {
            var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;
            return memory;
        }
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
}
