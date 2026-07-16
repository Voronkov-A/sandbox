using System.Net;
using Avalonia.Skia;
using Picshare.Services;

namespace Picshare.Tests;

public sealed class ImageCacheServiceWarmupTests
{
    static ImageCacheServiceWarmupTests()
    {
        SkiaPlatform.Initialize();
    }

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public async Task DetailedThumbnailWarmup_DoesNotReportOriginalLoadedWhenOriginalLazyCacheCannotStoreItem()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 1024 * 1024,
                OriginalImageMemoryBytes: 1,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new BytesHandler(PngBytes));

        var result = await imageCache.WarmDetailedThumbnailAsync(
            "album",
            "photo-1",
            "photo-1-full.jpg",
            "https://example.invalid/photo-1.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lazy,
            AlbumImageCacheReadMode.Lazy,
            CancellationToken.None);

        Assert.True(result.DetailedThumbnailLoaded);
        Assert.False(result.OriginalImageLoaded);
    }

    [Fact]
    public async Task DetailedThumbnailWarmup_WarmsDiskWhenMemoryCacheCannotStoreGeneratedThumbnail()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 1,
                OriginalImageMemoryBytes: 1,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 1024 * 1024,
                OriginalImageDiskBytes: 0)
        };
        var handler = new BytesHandler(PngBytes);
        using var httpClient = new HttpClient(handler);

        var result = await imageCache.WarmDetailedThumbnailAsync(
            "album",
            "photo-1",
            "photo-1-full.jpg",
            "https://example.invalid/photo-1.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lazy,
            AlbumImageCacheReadMode.Lazy,
            CancellationToken.None);

        Assert.True(result.DetailedThumbnailLoaded);
        Assert.False(result.OriginalImageLoaded);
        Assert.Equal(1, handler.RequestCount);

        imageCache.Limits = imageCache.Limits with { DetailedThumbnailMemoryBytes = 0 };

        var lookupResult = await imageCache.WarmDetailedThumbnailAsync(
            "album",
            "photo-1",
            "photo-1-full.jpg",
            "https://example.invalid/photo-1.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None);

        Assert.True(lookupResult.DetailedThumbnailLoaded);
        Assert.False(lookupResult.OriginalImageLoaded);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task WarmFastThumbnailAsync_WarmsDiskWhenMemoryCacheAlreadyContainsItem()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 1024 * 1024,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        var handler = new BytesHandler(PngBytes);
        using var httpClient = new HttpClient(handler);

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "photo-1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));

        imageCache.Limits = imageCache.Limits with { FastThumbnailDiskBytes = 1024 * 1024 };

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "photo-1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);

        imageCache.Limits = imageCache.Limits with { FastThumbnailMemoryBytes = 0 };

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "photo-1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task DetailedThumbnailWarmup_RetriesAfterInvalidOriginalCacheEntry()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 1024 * 1024,
                OriginalImageDiskBytes: 1024 * 1024)
        };
        var handler = new BytesHandler(PngBytes);
        using var httpClient = new HttpClient(handler);
        await WriteCacheFileAsync(imageCache, "album", "photo-1-full.jpg", [1, 2, 3, 4]);

        var result = await imageCache.WarmDetailedThumbnailAsync(
            "album",
            "photo-1",
            "photo-1-full.jpg",
            "https://example.invalid/photo-1.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            AlbumImageCacheReadMode.Lazy,
            CancellationToken.None);

        Assert.True(result.DetailedThumbnailLoaded);
        Assert.True(result.OriginalImageLoaded);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task DiskCache_DoesNotShareEntriesBetweenAlbumIdsThatSanitizeToSamePath()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 1024 * 1024,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album:a",
            "photo-1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));

        await Assert.ThrowsAsync<FileNotFoundException>(() => imageCache.WarmFastThumbnailAsync(
            "album?a",
            "photo-1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None));
    }

    [Fact]
    public async Task DiskCache_DoesNotShareFastThumbnailEntriesBetweenPhotoIdsThatSanitizeToSamePath()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 1024 * 1024,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "photo:1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));

        await Assert.ThrowsAsync<FileNotFoundException>(() => imageCache.WarmFastThumbnailAsync(
            "album",
            "photo?1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None));
    }

    [Fact]
    public async Task DiskCache_CanStorePhotoIdsThatWouldGenerateWindowsDeviceNames()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 1024 * 1024,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "CON",
            "https://example.invalid/con-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "CON",
            "https://example.invalid/con-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None));
    }

    [Fact]
    public async Task DiskCache_CanStorePhotoIdsThatWouldGenerateWindowsDeviceNamesWithTrailingSpaces()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 1024 * 1024,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "CON ",
            "https://example.invalid/con-space-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "CON ",
            "https://example.invalid/con-space-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None));
    }

    [Fact]
    public async Task DiskCache_CanStorePhotoIdsThatWouldGenerateWindowsConsoleDeviceNames()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 1024 * 1024,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "CONIN$",
            "https://example.invalid/conin-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "CONIN$",
            "https://example.invalid/conin-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "CONOUT$",
            "https://example.invalid/conout-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));
    }

    [Fact]
    public async Task DiskCache_CanStoreLongPhotoIds()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 1024 * 1024,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        var photoId = new string('a', 300);

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            photoId,
            "https://example.invalid/long-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            photoId,
            "https://example.invalid/long-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None));
    }

    [Fact]
    public async Task OriginalDiskCacheTrim_RemovesOriginalsWithLongExtensions()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 1024 * 1024)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        var cacheFileName = $"photo-full.{new string('a', 300)}";

        Assert.True(await imageCache.WarmOriginalAsync(
            "album",
            cacheFileName,
            "https://example.invalid/photo-original.long-extension",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));
        Assert.NotEmpty(Directory.EnumerateFiles(tempDirectory.Path, "*", SearchOption.AllDirectories));

        imageCache.Limits = imageCache.Limits with { OriginalImageDiskBytes = 0 };

        Assert.Empty(Directory.EnumerateFiles(tempDirectory.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OriginalDiskCacheTrim_RemovesOriginalsWithoutExtensions()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 1024 * 1024)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));

        Assert.True(await imageCache.WarmOriginalAsync(
            "album",
            "photo-full",
            "https://example.invalid/photo-original-no-extension",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));
        Assert.NotEmpty(Directory.EnumerateFiles(tempDirectory.Path, "*", SearchOption.AllDirectories));

        imageCache.Limits = imageCache.Limits with { OriginalImageDiskBytes = 0 };

        Assert.Empty(Directory.EnumerateFiles(tempDirectory.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OriginalDiskCacheTrim_RemovesOriginalsWithCompoundExtensions()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 1024 * 1024)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));

        Assert.True(await imageCache.WarmOriginalAsync(
            "album",
            "photo-full.tar.gz",
            "https://example.invalid/photo-original-compound-extension",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));
        Assert.NotEmpty(Directory.EnumerateFiles(tempDirectory.Path, "*", SearchOption.AllDirectories));

        imageCache.Limits = imageCache.Limits with { OriginalImageDiskBytes = 0 };

        Assert.Empty(Directory.EnumerateFiles(tempDirectory.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OriginalDiskCacheTrim_DoesNotRemoveFastThumbnailsWhosePhotoIdContainsFullMarker()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 1024 * 1024,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 1024 * 1024)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "photo-full.tar",
            "https://example.invalid/photo-full-tar-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));

        imageCache.Limits = imageCache.Limits with { OriginalImageDiskBytes = 0 };

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "photo-full.tar",
            "https://example.invalid/photo-full-tar-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None));
    }

    [Fact]
    public async Task CopyOriginalToAsync_UsesOriginalEncodedMemoryCache()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 1024 * 1024,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        var handler = new BytesHandler([1, 2, 3, 4]);
        using var httpClient = new HttpClient(handler);

        Assert.True(await imageCache.WarmOriginalAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));
        await using var destination = new MemoryStream();

        await imageCache.CopyOriginalToAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original.jpg",
            httpClient,
            destination,
            CancellationToken.None);

        Assert.Equal([1, 2, 3, 4], destination.ToArray());
        Assert.Equal(1, handler.RequestCount);
        Assert.Empty(Directory.EnumerateFiles(tempDirectory.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LazyDiskRead_DoesNotPromoteEntryIntoMemoryCache()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 1024 * 1024,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "photo-1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));

        imageCache.Limits = imageCache.Limits with { FastThumbnailMemoryBytes = 1024 * 1024 };

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "photo-1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lazy,
            CancellationToken.None));

        DeleteCacheFile(imageCache, "album", "photo-1-thumbnail.jpg");

        await Assert.ThrowsAsync<FileNotFoundException>(() => imageCache.WarmFastThumbnailAsync(
            "album",
            "photo-1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None));
    }

    [Fact]
    public async Task EagerDiskRead_TrimsOverLimitAlbumCacheAfterExistingEntryHit()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 4,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        await WriteCacheFileAsync(imageCache, "album", "old-thumbnail.jpg", [1, 1, 1, 1]);
        await WriteCacheFileAsync(imageCache, "album", "current-thumbnail.jpg", [2, 2, 2, 2]);
        SetCacheFileLastAccessTimeUtc(imageCache, "album", "old-thumbnail.jpg", DateTime.UtcNow.AddMinutes(-10));

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "current",
            "https://example.invalid/current-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));

        await Assert.ThrowsAsync<FileNotFoundException>(() => imageCache.WarmFastThumbnailAsync(
            "album",
            "old",
            "https://example.invalid/old-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None));
        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "current",
            "https://example.invalid/current-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Lookup,
            CancellationToken.None));
    }

    [Fact]
    public async Task CopyOriginalToAsync_RetriesRemoteSourceWhenCacheMisses()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new FailThenBytesHandler([1, 2, 3, 4]));
        await using var destination = new MemoryStream();

        await imageCache.CopyOriginalToAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original.jpg",
            httpClient,
            destination,
            CancellationToken.None);

        Assert.Equal([1, 2, 3, 4], destination.ToArray());
        Assert.Empty(Directory.EnumerateFiles(tempDirectory.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CopyOriginalToAsync_RetriesRemoteStreamReadFailures()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 1024 * 1024)
        };
        var handler = new FailReadThenBytesHandler([1, 2, 3, 4]);
        using var httpClient = new HttpClient(handler);
        await using var destination = new MemoryStream();

        await imageCache.CopyOriginalToAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original-read-failure.jpg",
            httpClient,
            destination,
            CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal([1, 2, 3, 4], destination.ToArray());
        Assert.Empty(Directory.EnumerateFiles(tempDirectory.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task WarmFastThumbnailAsync_RetriesSeekableSourceReadFailures()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 1024 * 1024,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        var handler = new FailSeekableReadThenBytesHandler(PngBytes);
        using var httpClient = new HttpClient(handler);

        Assert.True(await imageCache.WarmFastThumbnailAsync(
            "album",
            "photo-1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task LoadFastThumbnailBitmapAsync_RetriesAfterInvalidFastThumbnailCacheEntry()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 1024 * 1024,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        var handler = new BytesHandler(PngBytes);
        using var httpClient = new HttpClient(handler);
        await WriteCacheFileAsync(imageCache, "album", "photo-1-thumbnail.jpg", [1, 2, 3, 4]);

        var result = await imageCache.LoadFastThumbnailBitmapAsync(
            "album",
            "photo-1",
            "https://example.invalid/photo-1-thumb.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None);

        result.Bitmap.Dispose();
        Assert.True(result.FastThumbnailLoaded);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task LoadDetailedThumbnailBitmapAsync_RetriesAfterInvalidDetailedThumbnailCacheEntry()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 1024 * 1024,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 1024 * 1024,
                OriginalImageDiskBytes: 0)
        };
        var handler = new BytesHandler(PngBytes);
        using var httpClient = new HttpClient(handler);
        await WriteCacheFileAsync(imageCache, "album", "photo-1-detailed-220x150.jpg", [1, 2, 3, 4]);

        var result = await imageCache.LoadDetailedThumbnailBitmapAsync(
            "album",
            "photo-1",
            "photo-1-full.jpg",
            "https://example.invalid/photo-1.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            AlbumImageCacheReadMode.Lazy,
            CancellationToken.None);

        result.Bitmap.Dispose();
        Assert.True(result.DetailedThumbnailLoaded);
        Assert.True(result.OriginalImageLoaded);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task LoadDisplayBitmapAsync_RetriesSeekableSourceReadFailures()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        var handler = new FailSeekableReadThenBytesHandler(PngBytes);
        using var httpClient = new HttpClient(handler);

        var bitmap = await imageCache.LoadDisplayBitmapAsync(
            "album",
            "photo-thumbnail.jpg",
            "https://example.invalid/photo-display.jpg",
            httpClient,
            maxPixelWidth: 64,
            maxPixelHeight: 64,
            CancellationToken.None);

        bitmap.Dispose();
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task LoadDisplayBitmapAsync_RetriesAfterInvalidOriginalCacheEntry()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 1024 * 1024)
        };
        var handler = new BytesHandler(PngBytes);
        using var httpClient = new HttpClient(handler);
        await WriteCacheFileAsync(imageCache, "album", "photo-full.jpg", [1, 2, 3, 4]);

        var bitmap = await imageCache.LoadDisplayBitmapAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original.jpg",
            httpClient,
            maxPixelWidth: 64,
            maxPixelHeight: 64,
            CancellationToken.None);

        bitmap.Dispose();
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CopyOriginalToAsync_RewindsPartialDestinationBeforeRetry()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        var handler = new PartialReadFailureThenBytesHandler([9, 9], [1, 2, 3, 4]);
        using var httpClient = new HttpClient(handler);
        await using var destination = new MemoryStream();

        await imageCache.CopyOriginalToAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original-partial-read-failure.jpg",
            httpClient,
            destination,
            CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal([1, 2, 3, 4], destination.ToArray());
    }

    [Fact]
    public async Task CopyOriginalToAsync_RetriesRemoteReadBeforeWritingNonSeekableDestination()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        var handler = new PartialReadFailureThenBytesHandler([9, 9], [1, 2, 3, 4]);
        using var httpClient = new HttpClient(handler);
        await using var destination = new NonSeekableMemoryStream();

        await imageCache.CopyOriginalToAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original-partial-read-failure.jpg",
            httpClient,
            destination,
            CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal([1, 2, 3, 4], destination.ToArray());
    }

    [Fact]
    public async Task CopyOriginalToAsync_RetriesCachedWritesToSeekableDestination()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 1024 * 1024)
        };
        var handler = new BytesHandler([1, 2, 3, 4]);
        using var httpClient = new HttpClient(handler);

        Assert.True(await imageCache.WarmOriginalAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));
        await using var destination = new FailFirstWriteMemoryStream();

        await imageCache.CopyOriginalToAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original.jpg",
            httpClient,
            destination,
            CancellationToken.None);

        Assert.Equal([1, 2, 3, 4], destination.ToArray());
    }

    [Fact]
    public async Task CopyOriginalToAsync_RewindsPartialCachedDestinationBeforeRetry()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 1024 * 1024)
        };
        var handler = new BytesHandler([1, 2, 3, 4]);
        using var httpClient = new HttpClient(handler);

        Assert.True(await imageCache.WarmOriginalAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));
        await using var destination = new PartialThenFailWriteMemoryStream([9, 9]);

        await imageCache.CopyOriginalToAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original.jpg",
            httpClient,
            destination,
            CancellationToken.None);

        Assert.Equal([1, 2, 3, 4], destination.ToArray());
    }

    [Fact]
    public async Task CopyOriginalToAsync_DoesNotTreatCachedDestinationWriteFileNotFoundAsCacheMiss()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 1024 * 1024)
        };
        var handler = new BytesHandler([1, 2, 3, 4]);
        using var httpClient = new HttpClient(handler);

        Assert.True(await imageCache.WarmOriginalAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original.jpg",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None));
        await using var destination = new FileNotFoundWriteMemoryStream();

        await Assert.ThrowsAsync<FileNotFoundException>(() => imageCache.CopyOriginalToAsync(
            "album",
            "photo-full.jpg",
            "https://example.invalid/photo-original.jpg",
            httpClient,
            destination,
            CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ClearAlbumAsync_RemovesLegacyUnhashedAlbumCacheDirectory()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        var legacyAlbumPath = Path.Combine(tempDirectory.Path, "Picshare", "cache", "images", "album");
        Directory.CreateDirectory(legacyAlbumPath);
        await File.WriteAllBytesAsync(Path.Combine(legacyAlbumPath, "photo-thumbnail.jpg"), [1, 2, 3, 4]);

        await imageCache.ClearAlbumAsync("album");

        Assert.False(Directory.Exists(legacyAlbumPath));
    }

    [Fact]
    public async Task ClearAlbumAsync_RemovesLegacyUnhashedAlbumCacheDirectoryForSanitizedAlbumId()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        var legacyAlbumPath = Path.Combine(tempDirectory.Path, "Picshare", "cache", "images", "album_a");
        Directory.CreateDirectory(legacyAlbumPath);
        await File.WriteAllBytesAsync(Path.Combine(legacyAlbumPath, "photo-thumbnail.jpg"), [1, 2, 3, 4]);

        await imageCache.ClearAlbumAsync("album:a");

        Assert.False(Directory.Exists(legacyAlbumPath));
    }

    [Fact]
    public async Task BitmapCache_DoesNotDisposeReferencedBitmapWhenEntryIsRemoved()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 1024 * 1024,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailBitmapMemoryBytes: 4,
                DetailedThumbnailBitmapMemoryBytes: 0,
                OriginalImageBitmapMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        var handler = new BytesHandler(PngBytes);
        using var httpClient = new HttpClient(handler);

        var firstLoad = await imageCache.LoadFastThumbnailBitmapAsync(
            "album",
            "photo-1",
            "https://example.invalid/photo-1.png",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None);
        var cachedBitmap = firstLoad.Bitmap.Bitmap;
        firstLoad.Bitmap.Dispose();

        var cachedLoad = await imageCache.LoadFastThumbnailBitmapAsync(
            "album",
            "photo-1",
            "https://example.invalid/photo-1.png",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
        Assert.Same(cachedBitmap, cachedLoad.Bitmap.Bitmap);
        Assert.Equal(1, GetBitmapCacheEntryCount(imageCache));

        var secondLoad = await imageCache.LoadFastThumbnailBitmapAsync(
            "album",
            "photo-2",
            "https://example.invalid/photo-2.png",
            httpClient,
            AlbumImageCacheReadMode.Eager,
            CancellationToken.None);
        secondLoad.Bitmap.Dispose();

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(1, GetBitmapCacheEntryCount(imageCache));

        await imageCache.ClearAlbumAsync("album");

        Assert.Equal(1, cachedLoad.Bitmap.Bitmap.PixelSize.Width);
        Assert.Equal(1, cachedLoad.Bitmap.Bitmap.PixelSize.Height);
        cachedLoad.Bitmap.Dispose();
        Assert.Equal(0, GetBitmapCacheEntryCount(imageCache));
    }

    private static async Task WriteCacheFileAsync(ImageCacheService imageCache, string albumId, string cacheFileName, byte[] bytes)
    {
        var albumPathMethod = typeof(ImageCacheService).GetMethod(
            "GetAlbumCachePath",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var cacheNameMethod = typeof(ImageCacheService).GetMethod(
            "GetCacheFileSystemName",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(albumPathMethod);
        Assert.NotNull(cacheNameMethod);

        var albumPath = (string)albumPathMethod.Invoke(imageCache, [albumId])!;
        var cacheFileSystemName = (string)cacheNameMethod.Invoke(null, [cacheFileName])!;
        Directory.CreateDirectory(albumPath);
        await File.WriteAllBytesAsync(Path.Combine(albumPath, cacheFileSystemName), bytes);
    }

    private static void DeleteCacheFile(ImageCacheService imageCache, string albumId, string cacheFileName)
    {
        var albumPathMethod = typeof(ImageCacheService).GetMethod(
            "GetAlbumCachePath",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var cacheNameMethod = typeof(ImageCacheService).GetMethod(
            "GetCacheFileSystemName",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(albumPathMethod);
        Assert.NotNull(cacheNameMethod);

        var albumPath = (string)albumPathMethod.Invoke(imageCache, [albumId])!;
        var cacheFileSystemName = (string)cacheNameMethod.Invoke(null, [cacheFileName])!;
        File.Delete(Path.Combine(albumPath, cacheFileSystemName));
    }

    private static void SetCacheFileLastAccessTimeUtc(
        ImageCacheService imageCache,
        string albumId,
        string cacheFileName,
        DateTime lastAccessTimeUtc)
    {
        var albumPathMethod = typeof(ImageCacheService).GetMethod(
            "GetAlbumCachePath",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var cacheNameMethod = typeof(ImageCacheService).GetMethod(
            "GetCacheFileSystemName",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(albumPathMethod);
        Assert.NotNull(cacheNameMethod);

        var albumPath = (string)albumPathMethod.Invoke(imageCache, [albumId])!;
        var cacheFileSystemName = (string)cacheNameMethod.Invoke(null, [cacheFileName])!;
        File.SetLastAccessTimeUtc(Path.Combine(albumPath, cacheFileSystemName), lastAccessTimeUtc);
    }

    private static int GetBitmapCacheEntryCount(ImageCacheService imageCache)
    {
        var field = typeof(ImageCacheService).GetField(
            "_bitmapCache",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        var cache = (System.Collections.IDictionary)field.GetValue(imageCache)!;
        return cache.Count;
    }

    private sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }

    private sealed class FailThenBytesHandler(byte[] bytes) : HttpMessageHandler
    {
        private int _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requestCount) == 1)
            {
                throw new HttpRequestException("Transient failure.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }

    private sealed class SequentialBytesHandler(params byte[][] responses) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Math.Min(Interlocked.Increment(ref _requestCount), responses.Length) - 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responses[index])
            });
        }
    }

    private sealed class FailReadThenBytesHandler(byte[] bytes) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestCount = Interlocked.Increment(ref _requestCount);
            Stream stream = requestCount == 1
                ? new ThrowingReadStream()
                : new MemoryStream(bytes, writable: false);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            });
        }
    }

    private sealed class FailSeekableReadThenBytesHandler(byte[] bytes) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestCount = Interlocked.Increment(ref _requestCount);
            Stream stream = requestCount == 1
                ? new ThrowingSeekableReadStream()
                : new MemoryStream(bytes, writable: false);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            });
        }
    }

    private sealed class PartialReadFailureThenBytesHandler(byte[] firstBytes, byte[] secondBytes) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestCount = Interlocked.Increment(ref _requestCount);
            Stream stream = requestCount == 1
                ? new PartialThenThrowingReadStream(firstBytes)
                : new MemoryStream(secondBytes, writable: false);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            });
        }
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("Transient read failure.");
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new IOException("Transient read failure.");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingSeekableReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => 4;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("Transient seekable read failure.");
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new IOException("Transient seekable read failure.");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => Length + offset,
                _ => Position
            };
            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class PartialThenThrowingReadStream(byte[] bytes) : Stream
    {
        private bool _hasRead;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_hasRead)
            {
                throw new IOException("Transient read failure.");
            }

            _hasRead = true;
            var bytesToCopy = Math.Min(count, bytes.Length);
            Array.Copy(bytes, 0, buffer, offset, bytesToCopy);
            return bytesToCopy;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_hasRead)
            {
                throw new IOException("Transient read failure.");
            }

            _hasRead = true;
            var bytesToCopy = Math.Min(buffer.Length, bytes.Length);
            bytes.AsMemory(0, bytesToCopy).CopyTo(buffer);
            return ValueTask.FromResult(bytesToCopy);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FailFirstWriteMemoryStream : MemoryStream
    {
        private bool _hasThrown;

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowOnce();
            base.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowOnce();
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowOnce();
            return base.WriteAsync(buffer, cancellationToken);
        }

        private void ThrowOnce()
        {
            if (_hasThrown)
            {
                return;
            }

            _hasThrown = true;
            throw new IOException("Transient destination write failure.");
        }
    }

    private sealed class PartialThenFailWriteMemoryStream(byte[] partialBytes) : MemoryStream
    {
        private bool _hasThrown;

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowOnceAfterPartialWrite();
            base.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowOnceAfterPartialWrite();
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowOnceAfterPartialWrite();
            return base.WriteAsync(buffer, cancellationToken);
        }

        private void ThrowOnceAfterPartialWrite()
        {
            if (_hasThrown)
            {
                return;
            }

            _hasThrown = true;
            base.Write(partialBytes, 0, partialBytes.Length);
            throw new IOException("Transient partial destination write failure.");
        }
    }

    private sealed class FileNotFoundWriteMemoryStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new FileNotFoundException("Destination disappeared.");
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new FileNotFoundException("Destination disappeared.");
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new FileNotFoundException("Destination disappeared.");
        }
    }

    private sealed class NonSeekableMemoryStream : MemoryStream
    {
        public override bool CanSeek => false;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin loc)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"picshare-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
