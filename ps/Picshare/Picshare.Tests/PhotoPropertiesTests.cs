using System.Net;
using Picshare.Models;
using Picshare.Services;
using Picshare.ViewModels;
using SkiaSharp;

namespace Picshare.Tests;

public sealed class PhotoPropertiesTests
{
    [Theory]
    [InlineData("Album_00001.jpg", "00001")]
    [InlineData("Trip_2026_00234.PNG", "00234")]
    [InlineData("Album_00001", "00001")]
    [InlineData("Album.v2_00031", "00031")]
    [InlineData("DSC0018.jpeg", "0018")]
    [InlineData("123.jpg", "123")]
    [InlineData("sunset.jpg", "sunset.jpg")]
    public void ListCaption_UsesNumberSuffixWithoutChangingTheFilename(string fileName, string expected)
    {
        var photo = new AlbumPhotoViewModel("album", "photo", fileName, "https://example.invalid/photo", "https://example.invalid/thumb");
        Assert.Equal(expected, photo.ListDisplayName);
        Assert.Equal(fileName, photo.FileName);
    }

    [Fact]
    public async Task LocalPhoto_ReadsActualSizeAndDimensionsWithoutNetworkOrCredentials()
    {
        var path = Path.Combine(Path.GetTempPath(), $"picshare-photo-properties-{Guid.NewGuid():N}.png");
        try
        {
            using var bitmap = new SKBitmap(40, 25);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(path, encoded.ToArray());
            using var client = new HttpClient(new CallbackHandler(_ => throw new InvalidOperationException("Unexpected network request.")));
            var service = new PhotoPropertiesService(client, _ => throw new InvalidOperationException("Unexpected credential request."));
            var result = await service.LoadAsync(Reference(new Uri(path).AbsoluteUri, width: 1, height: 1), CancellationToken.None);
            Assert.Equal(new FileInfo(path).Length, result.SizeBytes);
            Assert.Equal(40, result.Width);
            Assert.Equal(25, result.Height);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("\"33554432\"")]
    [InlineData("33554432")]
    public async Task DrivePhoto_ReadsMetadataWithoutDownloadingOrChangingSharedCredentials(string sizeJson)
    {
        var calls = 0;
        using var client = new HttpClient(new CallbackHandler(request =>
        {
            calls++;
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("www.googleapis.com", request.RequestUri.Host);
            Assert.Equal("/drive/v3/files/photo-id", request.RequestUri.AbsolutePath);
            Assert.Contains("fields=size,imageMediaMetadata(width,height)", request.RequestUri.Query);
            Assert.DoesNotContain("alt=media", request.RequestUri.Query);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-access-token", request.Headers.Authorization?.Parameter);
            return Json("{\"size\":" + sizeJson + ",\"imageMediaMetadata\":{\"width\":6000,\"height\":4000}}");
        }));
        var service = new PhotoPropertiesService(client, _ => Task.FromResult("test-access-token"));
        var result = await service.LoadAsync(Reference(GoogleDriveRestClient.CreateMediaDownloadUrl("photo-id"), "photo-id"), CancellationToken.None);
        Assert.Equal(new PhotoFileProperties(33554432, 6000, 4000), result);
        Assert.Equal(1, calls);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task LegacyDriveLink_ResolvesMetadataAndUsesManifestDimensionsWhenMetadataOmitsThem()
    {
        using var client = new HttpClient(new CallbackHandler(request =>
        {
            Assert.Equal("/drive/v3/files/legacy-id", request.RequestUri!.AbsolutePath);
            return Json("{\"size\":\"1024\"}");
        }));
        var service = new PhotoPropertiesService(client, _ => Task.FromResult("test-access-token"));
        var result = await service.LoadAsync(Reference("https://drive.google.com/uc?id=legacy-id&export=download", width: 2048, height: 1536), CancellationToken.None);
        Assert.Equal(new PhotoFileProperties(1024, 2048, 1536), result);
    }

    [Fact]
    public async Task OtherHttpSource_ReadsHeadersOnlyAndDoesNotRequestOrSendGoogleCredentials()
    {
        using var client = new HttpClient(new CallbackHandler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new HeaderOnlyContent() };
        }));
        var service = new PhotoPropertiesService(client, _ => throw new InvalidOperationException("Unexpected credential request."));
        var result = await service.LoadAsync(Reference("https://example.invalid/photo?id=external", width: 40, height: 25), CancellationToken.None);
        Assert.Equal(new PhotoFileProperties(1234, 40, 25), result);
    }

    [Fact]
    public async Task CanceledRequest_DoesNotFetchMetadata()
    {
        using var client = new HttpClient(new CallbackHandler(_ => throw new InvalidOperationException("Unexpected network request.")));
        var service = new PhotoPropertiesService(client, _ => throw new InvalidOperationException("Unexpected credential request."));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadAsync(
            Reference(GoogleDriveRestClient.CreateMediaDownloadUrl("photo-id"), "photo-id"), cancellation.Token));
    }

    [Fact]
    public void Dialog_ShowsFullFilenameHumanReadableAndExactSizeAndPixelDimensions()
    {
        var model = new PhotoPropertiesViewModel("Album_00001.jpg", 40, 25);
        model.Apply(new PhotoFileProperties(1048576, 6000, 4000));
        Assert.Equal("Album_00001.jpg", model.FileName);
        Assert.Equal($"1 MiB ({1048576:N0} bytes)", model.FileSizeText);
        Assert.Equal("6000 × 4000 pixels", model.DimensionsText);
        Assert.False(model.IsStatusVisible);
    }

    [Fact]
    public void UnavailableSource_StopsLoadingAndKeepsKnownDimensions()
    {
        var model = new PhotoPropertiesViewModel("Album_00001.jpg", 40, 25);
        model.ShowError();
        Assert.Equal("Unavailable", model.FileSizeText);
        Assert.Equal("40 × 25 pixels", model.DimensionsText);
        Assert.True(model.IsStatusVisible);
    }

    private static PhotoReference Reference(string url, string? driveId = null, int width = 0, int height = 0)
        => new()
        {
            Id = "photo-id", FileName = "Album_00001.jpg", ContentType = "image/jpeg",
            BackendType = driveId is null ? "local-file" : "google-drive-file", DriveFileId = driveId,
            DownloadUrl = url, ThumbnailDownloadUrl = url, ThumbnailContentType = "image/jpeg",
            Width = width, Height = height
        };

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class CallbackHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(callback(request));
    }

    private sealed class HeaderOnlyContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new InvalidOperationException("The image body must not be read.");
        protected override bool TryComputeLength(out long length) { length = 1234; return true; }
    }
}
