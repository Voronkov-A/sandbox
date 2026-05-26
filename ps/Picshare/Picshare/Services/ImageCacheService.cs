namespace Picshare.Services;

public sealed class ImageCacheService
{
    private readonly string _rootPath;

    public ImageCacheService()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.GetTempPath();
        }

        _rootPath = Path.Combine(basePath, "Picshare", "cache", "images");
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
            await using (var remoteStream = await httpClient.GetStreamAsync(downloadUrl, cancellationToken))
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

    public Task ClearAsync()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }
}
