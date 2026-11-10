using Picshare.Models;
using Picshare.Services;
using SkiaSharp;

namespace Picshare.Tests;

public sealed class PhotoThumbnailGeneratorTests
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public void ReadSizeThenGenerateThumbnail_LeavesThePhotoFileOpenForRewindAndReuse()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "photo.png");
        File.WriteAllBytes(path, PngBytes);
        using var source = File.OpenRead(path);
        Assert.Equal((1, 1), PhotoThumbnailGenerator.GetImageSize(source));
        source.Position = 0; // Matches the failing StorePhotosAsync code path.
        using (var thumbnail = PhotoThumbnailGenerator.CreateJpegThumbnail(source))
        {
            Assert.Equal(0, thumbnail.Position);
            using var image = SKBitmap.Decode(thumbnail);
            Assert.NotNull(image);
            Assert.Equal(PhotoThumbnailGenerator.ThumbnailSize, image.Width);
            Assert.Equal(PhotoThumbnailGenerator.ThumbnailSize, image.Height);
        }
        source.Position = 0;
        using var copy = new MemoryStream();
        source.CopyTo(copy);
        Assert.Equal(PngBytes, copy.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidImage_DoesNotDisposeTheCallersStream(bool readSize)
    {
        using var source = new MemoryStream([1, 2, 3, 4]);
        if (readSize)
            Assert.Throws<InvalidOperationException>(() => PhotoThumbnailGenerator.GetImageSize(source));
        else
            Assert.Throws<InvalidOperationException>(() => PhotoThumbnailGenerator.CreateJpegThumbnail(source));
        Assert.True(source.CanRead);
        source.Position = 0;
        Assert.Equal(1, source.ReadByte());
    }

    [Fact]
    public async Task ResumableAlbumCreation_StoresPhotosAndThumbnailsThroughTheSharedUploadPreparationPath()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.png");
        await File.WriteAllBytesAsync(sourcePath, PngBytes);
        var service = new PersistentAlbumCreationService(temp.Path);
        var sources = Enumerable.Range(1, 3).Select(index => new PhotoUploadSource(
            $"photo-{index}.png", $"photo-{index}", () => Task.FromResult<Stream>(File.OpenRead(sourcePath)), sourcePath)).ToArray();
        var pending = await service.CreatePendingCreationAsync("local-file-system", "Test album", 1,
            null, Path.Combine(temp.Path, "albums"), GoogleDriveAlbumShareSettings.Public,
            new FeedbackReviewerIdentity { BackendType = "local-file-system", UserId = "test-author" },
            sources, CancellationToken.None);

        var result = await service.ResumeAsync(pending,
            _ => throw new InvalidOperationException("Local creation must not request Google access"),
            progress: null, maximumParallelism: 3, CancellationToken.None);
        Assert.Equal(3, result.Manifest.Photos.Count);
        Assert.True(File.Exists(result.Manifest.LocalFileSystem!.ManifestFilePath));
        foreach (var photo in result.Manifest.Photos)
        {
            Assert.Equal(1, photo.Width);
            Assert.Equal(1, photo.Height);
            Assert.Equal(PngBytes, await File.ReadAllBytesAsync(new Uri(photo.DownloadUrl).LocalPath));
            using var thumbnail = SKBitmap.Decode(new Uri(photo.ThumbnailDownloadUrl).LocalPath);
            Assert.NotNull(thumbnail);
            Assert.Equal(PhotoThumbnailGenerator.ThumbnailSize, thumbnail.Width);
        }
        Assert.Null(await service.LoadPendingCreationAsync(CancellationToken.None));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "picshare-thumbnail-tests", Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
