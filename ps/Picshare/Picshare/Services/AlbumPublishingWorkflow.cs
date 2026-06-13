using Picshare.Models;

namespace Picshare.Services;

public static class AlbumPublishingWorkflow
{
    public static async Task<IReadOnlyList<PhotoReference>> PublishPhotosAsync(
        IReadOnlyList<PhotoUploadSource> photos,
        Func<PreparedAlbumPhoto, CancellationToken, Task<StoredAlbumPhoto>> storePhotoAsync,
        CancellationToken cancellationToken)
    {
        var photoReferences = new List<PhotoReference>();
        foreach (var photo in photos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var photoId = Guid.NewGuid().ToString("N");
            var contentType = GetContentType(photo.FileName);

            await using var thumbnailSourceStream = await photo.OpenReadAsync();
            await using var thumbnailStream = PhotoThumbnailGenerator.CreateJpegThumbnail(thumbnailSourceStream);
            await using var contentStream = await photo.OpenReadAsync();

            var storedPhoto = await storePhotoAsync(
                new PreparedAlbumPhoto(photoId, photo.FileName, contentType, contentStream, thumbnailStream),
                cancellationToken);

            photoReferences.Add(new PhotoReference
            {
                Id = photoId,
                FileName = photo.FileName,
                ContentType = contentType,
                BackendType = storedPhoto.BackendType,
                DriveFileId = storedPhoto.DriveFileId,
                DownloadUrl = storedPhoto.DownloadUrl,
                ThumbnailDriveFileId = storedPhoto.ThumbnailDriveFileId,
                ThumbnailDownloadUrl = storedPhoto.ThumbnailDownloadUrl,
                ThumbnailContentType = PhotoThumbnailGenerator.ContentType
            });
        }

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
