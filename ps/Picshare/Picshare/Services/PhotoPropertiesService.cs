using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Picshare.Models;

namespace Picshare.Services;

public sealed record PhotoFileProperties(long? SizeBytes, int Width, int Height);

public sealed class PhotoPropertiesService(HttpClient httpClient, Func<CancellationToken, Task<string>> getAccessToken)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PhotoFileProperties> LoadAsync(PhotoReference photo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = new Uri(photo.DownloadUrl, UriKind.Absolute);
        if (uri.IsFile)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = File.OpenRead(uri.LocalPath);
                var size = stream.Length;
                var dimensions = PhotoThumbnailGenerator.GetImageSize(stream);
                cancellationToken.ThrowIfCancellationRequested();
                return new PhotoFileProperties(size, dimensions.Width, dimensions.Height);
            }, cancellationToken);
        }

        var fileId = photo.DriveFileId;
        if (string.IsNullOrWhiteSpace(fileId) && GoogleDriveMediaHandler.TryGetMediaFileId(uri, out var legacyId))
            fileId = legacyId;
        if (!string.IsNullOrWhiteSpace(fileId))
        {
            // Request metadata only; never download or decode the original to inspect a Drive file.
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}?fields=size,imageMediaMetadata(width,height)&supportsAllDrives=true");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await getAccessToken(cancellationToken));
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            var metadata = await JsonSerializer.DeserializeAsync<DrivePhotoMetadata>(content, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Google Drive returned empty photo metadata.");
            return new PhotoFileProperties(metadata.Size is >= 0 ? metadata.Size : null,
                metadata.ImageMediaMetadata?.Width is > 0 ? metadata.ImageMediaMetadata.Width : photo.Width,
                metadata.ImageMediaMetadata?.Height is > 0 ? metadata.ImageMediaMetadata.Height : photo.Height);
        }

        // Preserve support for albums with ordinary HTTP image URLs, without reading the body.
        using var sourceResponse = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        sourceResponse.EnsureSuccessStatusCode();
        if (sourceResponse.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("The photo source returned a web page instead of an image.");
        return new PhotoFileProperties(sourceResponse.Content.Headers.ContentLength, photo.Width, photo.Height);
    }

    private sealed record DrivePhotoMetadata
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long? Size { get; init; }
        public DriveImageDimensions? ImageMediaMetadata { get; init; }
    }

    private sealed record DriveImageDimensions
    {
        public int Width { get; init; }
        public int Height { get; init; }
    }
}
