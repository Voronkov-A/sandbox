using Picshare.Models;

namespace Picshare.Services;

public static class AlbumPublishingWorkflow
{
    public static async Task<IReadOnlyList<PhotoReference>> PublishPhotosAsync(
        IReadOnlyList<PhotoUploadSource> photos,
        string albumTitle,
        int maximumParallelism,
        Func<PreparedAlbumPhoto, CancellationToken, Task<StoredAlbumPhoto>> storePhotoAsync,
        CancellationToken cancellationToken)
    {
        var photoReferences = new PhotoReference[photos.Count];
        await Parallel.ForEachAsync(
            photos.Select((photo, index) => (Photo: photo, Index: index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, maximumParallelism),
                CancellationToken = cancellationToken
            },
            async (entry, token) =>
            {
                var photo = entry.Photo;
                var storedFileName = CreateStoredPhotoFileName(albumTitle, entry.Index + 1, photo.FileName);
                var contentType = GetContentType(photo.FileName);

                await using var thumbnailSourceStream = await photo.OpenReadAsync();
                await using var thumbnailStream = PhotoThumbnailGenerator.CreateJpegThumbnail(thumbnailSourceStream);
                await using var contentStream = await photo.OpenReadAsync();

                var photoId = Guid.NewGuid().ToString("N");
                var storedPhoto = await storePhotoAsync(
                    new PreparedAlbumPhoto(photoId, storedFileName, contentType, contentStream, thumbnailStream),
                    token);

                photoReferences[entry.Index] = new PhotoReference
                {
                    Id = photoId,
                    FileName = storedFileName,
                    ContentType = contentType,
                    BackendType = storedPhoto.BackendType,
                    DriveFileId = storedPhoto.DriveFileId,
                    DownloadUrl = storedPhoto.DownloadUrl,
                    ThumbnailDriveFileId = storedPhoto.ThumbnailDriveFileId,
                    ThumbnailDownloadUrl = storedPhoto.ThumbnailDownloadUrl,
                    ThumbnailContentType = PhotoThumbnailGenerator.ContentType
                };
            });

        return photoReferences;
    }

    public static AlbumManifest CreateManifest(
        string albumId,
        string title,
        int targetNicePhotoCount,
        string photoBackendType,
        string databaseBackendType,
        FeedbackReviewerIdentity author,
        GoogleDriveAlbumDetails? googleDrive,
        LocalFileSystemAlbumDetails? localFileSystem,
        IReadOnlyList<PhotoReference> photos)
    {
        return new AlbumManifest
        {
            AlbumId = albumId,
            Title = title,
            CreatedAt = DateTimeOffset.UtcNow,
            TargetNicePhotoCount = targetNicePhotoCount,
            PhotoBackendType = photoBackendType,
            DatabaseBackendType = databaseBackendType,
            Author = author,
            GoogleDrive = googleDrive,
            LocalFileSystem = localFileSystem,
            Photos = photos
        };
    }

    public static string CreateThumbnailFileName(string photoId)
    {
        return $"{photoId}-thumbnail.jpg";
    }

    public static string CreateStoredPhotoFileName(string albumTitle, int index, string originalFileName)
    {
        var albumName = Sanitize(string.IsNullOrWhiteSpace(albumTitle) ? "Picshare_album" : albumTitle.Trim());
        var extension = Path.GetExtension(originalFileName);
        return string.IsNullOrWhiteSpace(extension)
            ? $"{albumName}_{index:00000}"
            : $"{albumName}_{index:00000}{extension.ToLowerInvariant()}";
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }
}

public sealed record PreparedAlbumPhoto(
    string PhotoId,
    string FileName,
    string ContentType,
    Stream ContentStream,
    Stream ThumbnailStream);

public sealed record StoredAlbumPhoto(
    string BackendType,
    string DownloadUrl,
    string ThumbnailDownloadUrl,
    string? DriveFileId = null,
    string? ThumbnailDriveFileId = null);
