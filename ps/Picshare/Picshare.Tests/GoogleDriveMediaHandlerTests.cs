using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Picshare.Models;
using Picshare.Services;

namespace Picshare.Tests;

public sealed class GoogleDriveMediaHandlerTests
{
    [Theory]
    [InlineData("https://drive.google.com/uc?export=download&id=photo-id")]
    [InlineData("https://www.googleapis.com/drive/v3/files/photo-id?alt=media")]
    public async Task DriveMedia_UsesAuthenticatedApiForExistingAndNewAlbumUrls(string url)
    {
        var requestedTokens = 0;
        using var client = new HttpClient(new GoogleDriveMediaHandler(_ =>
        {
            requestedTokens++;
            return Task.FromResult("access-token");
        }) { InnerHandler = new Handler(request =>
        {
            Assert.Equal(GoogleDriveRestClient.CreateMediaDownloadUrl("photo-id"), request.RequestUri!.AbsoluteUri);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "access-token"), request.Headers.Authorization);
            return Response("image/jpeg", [255, 216, 255]);
        }) });
        Assert.Equal(new byte[] { 255, 216, 255 }, await client.GetByteArrayAsync(url));
        Assert.Equal(1, requestedTokens);
    }

    [Theory]
    [InlineData("https://example.test/photo.jpg")]
    [InlineData("https://drive.google.com.example.test/uc?id=photo-id")]
    [InlineData("http://drive.google.com/uc?id=photo-id")]
    [InlineData("https://drive.google.com:8443/uc?id=photo-id")]
    public async Task OtherUrls_DoNotReceiveGoogleCredentials(string url)
    {
        using var client = new HttpClient(new GoogleDriveMediaHandler(_ => throw new InvalidOperationException("Must not request credentials"))
        { InnerHandler = new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(url, request.RequestUri!.AbsoluteUri);
            return Response("image/jpeg", [1]);
        }) });
        Assert.Single(await client.GetByteArrayAsync(url));
    }

    [Fact]
    public async Task HtmlResponse_FailsClearlyAndDoesNotRetryAsAnImage()
    {
        var calls = 0;
        using var client = new HttpClient(new GoogleDriveMediaHandler(_ => Task.FromResult("token"))
        { InnerHandler = new Handler(_ => { calls++; return Response("text/html", "<html>Sign in</html>"u8.ToArray()); }) });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetByteArrayAsync("https://drive.google.com/uc?id=photo"));
        Assert.Contains("HTML", error.Message);
        Assert.False(TransientRetryPolicy.IsTransient(error, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ImageCache_RejectsHtmlBeforeWritingItToDiskOrMemory()
    {
        var root = Path.Combine(Path.GetTempPath(), "picshare-html-response-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var calls = 0;
            using var client = new HttpClient(new Handler(_ => { calls++; return Response("application/octet-stream", "  <html>Not an image</html>"u8.ToArray()); }));
            var cache = new ImageCacheService(root);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => cache.LoadFastThumbnailBitmapAsync(
                "album", "photo", "https://example.test/photo.jpg", client, AlbumImageCacheReadMode.Eager, CancellationToken.None));
            Assert.Contains("text page", error.Message);
            Assert.Equal(1, calls);
            Assert.False(Directory.Exists(root) && Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task AlbumLoader_DownloadsManifestThroughTheSameAuthenticatedApi()
    {
        var manifest = new AlbumManifest
        {
            AlbumId = "album", Title = "Test", CreatedAt = DateTimeOffset.UtcNow, TargetNicePhotoCount = 1,
            PhotoBackendType = "google-drive-file", DatabaseBackendType = "google-drive-folder",
            Author = new FeedbackReviewerIdentity { BackendType = "google", UserId = "author" }, Photos = []
        };
        using var client = new HttpClient(new GoogleDriveMediaHandler(_ => Task.FromResult("token"))
        { InnerHandler = new Handler(request =>
        {
            Assert.Equal(GoogleDriveRestClient.CreateMediaDownloadUrl("manifest"), request.RequestUri!.AbsoluteUri);
            Assert.Equal("token", request.Headers.Authorization!.Parameter);
            return Response("application/json", JsonSerializer.SerializeToUtf8Bytes(manifest));
        }) });
        var loaded = await new AlbumLoader(client).LoadFromDriveFileAsync("manifest", CancellationToken.None);
        Assert.Equal("album", loaded.AlbumId);
    }

    private static HttpResponseMessage Response(string mimeType, byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        return response;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
