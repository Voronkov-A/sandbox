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

        var photoReferences = await AlbumPublishingWorkflow.PublishPhotosAsync(
            request.Photos,
            async (photo, token) =>
            {
                var driveFile = await client.UploadFileAsync(
                    photo.FileName,
                    photosFolder.Id,
                    photo.ContentStream,
                    photo.ContentType,
                    token);

                var thumbnailFile = await client.UploadFileAsync(
                    AlbumPublishingWorkflow.CreateThumbnailFileName(photo.PhotoId),
                    photosFolder.Id,
                    photo.ThumbnailStream,
                    PhotoThumbnailGenerator.ContentType,
                    token);

                return new StoredAlbumPhoto(
                    "google-drive-file",
                    GoogleDriveRestClient.CreatePublicDownloadUrl(driveFile.Id),
                    GoogleDriveRestClient.CreatePublicDownloadUrl(thumbnailFile.Id),
                    driveFile.Id,
                    thumbnailFile.Id);
            },
            cancellationToken);

        var manifestWithoutId = AlbumPublishingWorkflow.CreateManifest(
            albumId,
            request.Title,
            request.TargetNicePhotoCount,
            "local-photos-to-google-drive",
            "google-drive-folder",
            request.Author,
            new GoogleDriveAlbumDetails
            {
                AlbumFolderId = albumFolder.Id,
                PhotosFolderId = photosFolder.Id,
                FeedbackFolderId = feedbackFolder.Id,
                ManifestFileId = "",
                AlbumFolderUrl = albumFolder.WebViewLink ?? $"https://drive.google.com/drive/folders/{albumFolder.Id}"
            },
            null,
            photoReferences);

        await using var placeholderManifestStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifestWithoutId, JsonOptions));
        var manifestFile = await client.UploadFileAsync("album.json", albumFolder.Id, placeholderManifestStream, "application/json", cancellationToken);

        var manifest = manifestWithoutId with
        {
            GoogleDrive = manifestWithoutId.GoogleDrive! with
            {
                ManifestFileId = manifestFile.Id
            }
        };

        await using var finalManifestStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
        await client.UpdateFileContentAsync(manifestFile.Id, finalManifestStream, "application/json", cancellationToken);

        return new DriveAlbumPublishResult
        {
            Manifest = manifest,
            AlbumFolderUrl = manifest.GoogleDrive!.AlbumFolderUrl,
            PicshareLink = AlbumLinkParser.CreatePicshareLink(manifest.GoogleDrive.ManifestFileId, albumFolder.Id)
        };
    }
}
