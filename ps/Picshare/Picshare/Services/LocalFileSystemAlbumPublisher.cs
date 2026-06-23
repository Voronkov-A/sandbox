using System.Text.Json;
using Picshare.Models;

namespace Picshare.Services;

public sealed class LocalFileSystemAlbumPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<LocalAlbumPublishResult> PublishAsync(
        LocalAlbumPublishRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Photos.Count == 0)
        {
            throw new InvalidOperationException("Add at least one photo to the album.");
        }

        if (string.IsNullOrWhiteSpace(request.ParentFolderPath))
        {
            throw new InvalidOperationException("Select a local destination folder before creating the album.");
        }

        var albumId = Guid.NewGuid().ToString("N");
        var albumFolderPath = CreateAlbumFolderPath(request.ParentFolderPath, request.Title, albumId);
        var photosFolderPath = Path.Combine(albumFolderPath, "photos");
        var feedbackFolderPath = Path.Combine(albumFolderPath, "feedback");
        Directory.CreateDirectory(photosFolderPath);
        Directory.CreateDirectory(feedbackFolderPath);

        var photoReferences = await AlbumPublishingWorkflow.PublishPhotosAsync(
            request.Photos,
            request.Title,
            LocalUserSettings.DefaultMaximumParallelism,
            async (photo, token) =>
            {
                var storedPath = Path.Combine(photosFolderPath, photo.FileName);
                var thumbnailPath = Path.Combine(photosFolderPath, AlbumPublishingWorkflow.CreateThumbnailFileName(photo.PhotoId));

                await using (var destination = File.Create(storedPath))
                {
                    await photo.ContentStream.CopyToAsync(destination, token);
                }

                await using (var thumbnailDestination = File.Create(thumbnailPath))
                {
                    await photo.ThumbnailStream.CopyToAsync(thumbnailDestination, token);
                }

                return new StoredAlbumPhoto(
                    "local-file",
                    new Uri(storedPath).AbsoluteUri,
                    new Uri(thumbnailPath).AbsoluteUri);
            },
            cancellationToken);

        var manifestFilePath = Path.Combine(albumFolderPath, "album.json");
        var manifest = AlbumPublishingWorkflow.CreateManifest(
            albumId,
            request.Title,
            request.TargetNicePhotoCount,
            "local-file-system",
            "local-file-system",
            request.Author,
            null,
            new LocalFileSystemAlbumDetails
            {
                RootPath = albumFolderPath,
                PhotosFolderPath = photosFolderPath,
                FeedbackFolderPath = feedbackFolderPath,
                ManifestFilePath = manifestFilePath
            },
            photoReferences);

        await using (var stream = File.Create(manifestFilePath))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
        }

        return new LocalAlbumPublishResult
        {
            Manifest = manifest,
            AlbumFolderPath = albumFolderPath,
            PicshareLink = AlbumLinkParser.CreateLocalPicshareLink(manifestFilePath)
        };
    }

    private static string CreateAlbumFolderPath(string parentFolderPath, string title, string albumId)
    {
        var name = SanitizePathSegment(string.IsNullOrWhiteSpace(title) ? "Picshare album" : title.Trim());
        var path = Path.Combine(parentFolderPath, name);
        return Directory.Exists(path) || File.Exists(path)
            ? Path.Combine(parentFolderPath, $"{name}-{albumId[..8]}")
            : path;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Picshare album" : sanitized;
    }
}
