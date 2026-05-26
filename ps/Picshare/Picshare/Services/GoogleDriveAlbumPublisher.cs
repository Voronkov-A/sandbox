using System.Text.Json;
using Picshare.Models;

namespace Picshare.Services;

public sealed class GoogleDriveAlbumPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly Func<string, GoogleDriveRestClient> _clientFactory;

    public GoogleDriveAlbumPublisher()
        : this(accessToken => new GoogleDriveRestClient(accessToken))
    {
    }

    public GoogleDriveAlbumPublisher(Func<string, GoogleDriveRestClient> clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<DriveAlbumPublishResult> PublishAsync(DriveAlbumPublishRequest request, CancellationToken cancellationToken)
    {
        if (request.Photos.Count == 0)
        {
            throw new InvalidOperationException("Add at least one photo to the album.");
        }

        var client = _clientFactory(request.AccessToken);
        var albumId = Guid.NewGuid().ToString("N");
        var folderName = request.Title;

        var albumFolder = await client.CreateFolderAsync(folderName, request.ParentDriveFolderId, cancellationToken);
        var photosFolder = await client.CreateFolderAsync("photos", albumFolder.Id, cancellationToken);
        var feedbackFolder = await client.CreateFolderAsync("feedback", albumFolder.Id, cancellationToken);

        await client.ShareWithAnyoneAsync(albumFolder.Id, "writer", cancellationToken);

        var photoReferences = new List<PhotoReference>();
        foreach (var photo in request.Photos)
        {
            var photoId = Guid.NewGuid().ToString("N");
            var contentType = GetContentType(photo.FileName);

            await using var thumbnailSourceStream = await photo.OpenReadAsync();
            await using var thumbnailStream = PhotoThumbnailGenerator.CreateJpegThumbnail(thumbnailSourceStream);

            await using var stream = await photo.OpenReadAsync();
            var driveFile = await client.UploadFileAsync(
                photo.FileName,
                photosFolder.Id,
                stream,
                contentType,
                cancellationToken);

            var thumbnailFile = await client.UploadFileAsync(
                CreateThumbnailFileName(photoId),
                photosFolder.Id,
                thumbnailStream,
                PhotoThumbnailGenerator.ContentType,
                cancellationToken);

            photoReferences.Add(new PhotoReference
            {
                Id = photoId,
                FileName = photo.FileName,
                ContentType = contentType,
                BackendType = "google-drive-file",
                DriveFileId = driveFile.Id,
                DownloadUrl = GoogleDriveRestClient.CreatePublicDownloadUrl(driveFile.Id),
                ThumbnailDriveFileId = thumbnailFile.Id,
                ThumbnailDownloadUrl = GoogleDriveRestClient.CreatePublicDownloadUrl(thumbnailFile.Id),
                ThumbnailContentType = PhotoThumbnailGenerator.ContentType
            });
        }

        var manifestWithoutId = new AlbumManifest
        {
            AlbumId = albumId,
            Title = request.Title,
            CreatedAt = DateTimeOffset.UtcNow,
            PhotoBackendType = "local-photos-to-google-drive",
            DatabaseBackendType = "google-drive-folder",
            GoogleDrive = new GoogleDriveAlbumDetails
            {
                AlbumFolderId = albumFolder.Id,
                PhotosFolderId = photosFolder.Id,
                FeedbackFolderId = feedbackFolder.Id,
                ManifestFileId = "",
                AlbumFolderUrl = albumFolder.WebViewLink ?? $"https://drive.google.com/drive/folders/{albumFolder.Id}"
            },
            Photos = photoReferences
        };

        await using var placeholderManifestStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifestWithoutId, JsonOptions));
        var manifestFile = await client.UploadFileAsync("album.json", albumFolder.Id, placeholderManifestStream, "application/json", cancellationToken);

        var manifest = manifestWithoutId with
        {
            GoogleDrive = manifestWithoutId.GoogleDrive with
            {
                ManifestFileId = manifestFile.Id
            }
        };

        await using var finalManifestStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
        await client.UpdateFileContentAsync(manifestFile.Id, finalManifestStream, "application/json", cancellationToken);

        return new DriveAlbumPublishResult
        {
            Manifest = manifest,
            AlbumFolderUrl = manifest.GoogleDrive.AlbumFolderUrl,
            PicshareLink = AlbumLinkParser.CreatePicshareLink(manifest.GoogleDrive.ManifestFileId, albumFolder.Id)
        };
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

    private static string CreateThumbnailFileName(string photoId)
    {
        return $"{photoId}-thumbnail.jpg";
    }
}
