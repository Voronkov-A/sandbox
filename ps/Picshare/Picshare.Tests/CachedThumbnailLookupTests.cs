using System.Net;
using System.Reflection;
using Picshare.Services;

namespace Picshare.Tests;

public sealed class CachedThumbnailLookupTests
{
    static CachedThumbnailLookupTests() => TestRenderingPlatform.EnsureInitialized();
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Lookup_PrefersMatchingDecodedDetailedThumbnailAndFallsBackToFast()
    {
        using var temp = new TempDirectory();
        var cache = new ImageCacheService(temp.Path);
        var handler = new CountingHandler();
        using var client = new HttpClient(handler);
        var fast = await cache.LoadFastThumbnailBitmapAsync("album", "photo", "https://example.invalid/fast", client,
            AlbumImageCacheReadMode.Eager, CancellationToken.None);
        var detailed = await cache.LoadDetailedThumbnailBitmapAsync("album", "photo", "photo-full.jpg", "https://example.invalid/full", client,
            120, 50, AlbumImageCacheReadMode.Eager, AlbumImageCacheReadMode.Eager, CancellationToken.None);
        var requests = handler.RequestCount;
        using var hit = cache.TryAcquireCachedThumbnail("album", "photo", 120, 50);
        Assert.NotNull(hit);
        Assert.Same(detailed.Bitmap.Bitmap, hit.Bitmap);
        using var resized = cache.TryAcquireCachedThumbnail("album", "photo", 240, 100);
        Assert.NotNull(resized);
        Assert.Same(fast.Bitmap.Bitmap, resized.Bitmap);
        Assert.Null(cache.TryAcquireCachedThumbnail("other-album", "photo", 120, 50));
        Assert.Null(cache.TryAcquireCachedThumbnail("album", "other-photo", 120, 50));
        Assert.Equal(requests, handler.RequestCount);
        fast.Bitmap.Dispose();
        detailed.Bitmap.Dispose();
        await cache.ClearAlbumAsync("album");
        Assert.True(hit.Bitmap.PixelSize.Width > 0); // The lightweight card's lease survives eviction.
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EncodedMemoryOrDiskCacheWithoutDecodedBitmap_RemainsAMiss(bool encodedInMemory)
    {
        using var temp = new TempDirectory();
        var cache = new ImageCacheService(temp.Path)
        {
            Limits = AlbumImageCacheLimits.Default with { FastThumbnailMemoryBytes = encodedInMemory ? 1024 * 1024 : 0 }
        };
        var handler = new CountingHandler();
        using var client = new HttpClient(handler);
        Assert.True(await cache.WarmFastThumbnailAsync("album", "photo", "https://example.invalid/fast", client,
            AlbumImageCacheReadMode.Eager, CancellationToken.None));
        Assert.Null(cache.TryAcquireCachedThumbnail("album", "photo", 120, 50));
        Assert.Equal(1, handler.RequestCount);
        var bitmapCache = (System.Collections.IDictionary)typeof(ImageCacheService)
            .GetField("_bitmapCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
        Assert.Empty(bitmapCache.Keys.Cast<object>());
        await cache.ClearAlbumAsync("album");
    }

    [Fact]
    public async Task BusyBitmapCache_SkipsTheLookupWithoutWaitingForTheLock()
    {
        using var temp = new TempDirectory();
        var cache = new ImageCacheService(temp.Path);
        var gate = typeof(ImageCacheService).GetField("_memoryCacheSync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            lock (gate)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            Assert.Null(cache.TryAcquireCachedThumbnail("album", "photo", 120, 50));
            Assert.False(holder.IsCompleted); // Lookup returned while the other thread still owns the lock.
        }
        finally { release.Set(); }
        await holder;
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "Picshare"))); // Misses don't touch the filesystem.
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(PngBytes) });
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "picshare-cached-thumbnail-tests", Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
