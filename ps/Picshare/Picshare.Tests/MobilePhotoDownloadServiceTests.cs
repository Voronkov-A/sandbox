using System.IO.Compression;
using System.Text;
using Picshare.Services;
using Picshare.ViewModels;

namespace Picshare.Tests;

public sealed class MobilePhotoDownloadServiceTests
{
    [Fact]
    public void DesktopCategoryDownload_RetainsArchiveModeAndDestinationSwitching()
    {
        var directory = Path.Combine(Path.GetTempPath(), "picshare-download-destination");
        var category = new AlbumDownloadCategoryViewModel("nice", "Nice", 2, directory, "nice.zip", "archive");
        Assert.Equal(new[] { "include", "archive", "exclude" }, category.ModeOptions.Select(mode => mode.Id));
        Assert.Equal(Path.Combine(directory, "nice.zip"), category.DestinationDirectoryPath);
        category.SelectedMode = category.ModeOptions.Single(mode => mode.Id == "include");
        Assert.Equal(directory, category.DestinationDirectoryPath);
        category.SelectedMode = category.ModeOptions.Single(mode => mode.Id == "archive");
        Assert.Equal(Path.Combine(directory, "nice.zip"), category.DestinationDirectoryPath);
        Assert.True(category.UsesLocalDownloadPaths);
    }

    [Fact]
    public async Task SinglePhoto_WritesToNonSeekableDocumentStreamWithoutLocalPath()
    {
        var output = new DocumentStream();
        await MobilePhotoDownloadService.SaveAsync([Item("photo.jpg", "original image")], false,
            () => Task.FromResult<Stream>(output), null, null, CancellationToken.None);
        Assert.Equal("original image", Encoding.UTF8.GetString(output.ToArray()));
        Assert.True(output.WasDisposed);
    }

    [Fact]
    public async Task AlbumZip_PreservesCategoryFoldersAndResolvesDuplicateNames()
    {
        var output = new DocumentStream();
        var progress = new List<int>();
        await MobilePhotoDownloadService.SaveAsync(
            [Item("Nice/photo.jpg", "one"), Item("Nice/PHOTO.jpg", "two"), Item("Okay/another.jpg", "three")], true,
            () => Task.FromResult<Stream>(output), (count, _) => progress.Add(count), null, CancellationToken.None);
        using var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        Assert.Equal(3, archive.Entries.Count);
        Assert.Equal("one", Read(archive, "Nice/photo.jpg"));
        Assert.Equal("two", Read(archive, "Nice/PHOTO (2).jpg"));
        Assert.Equal("three", Read(archive, "Okay/another.jpg"));
        Assert.Equal(3, progress.Last());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkRetry_DiscardsPartialBytesBeforeOpeningNativeDestination(bool zip)
    {
        var attempts = 0;
        var output = new DocumentStream();
        var item = new MobileDownloadItem("photo.jpg", async (buffer, token) =>
        {
            if (++attempts == 1)
            {
                await buffer.WriteAsync("partial junk"u8.ToArray(), token);
                throw new HttpRequestException("Transient connection failure");
            }
            await buffer.WriteAsync("complete"u8.ToArray(), token);
        });
        await MobilePhotoDownloadService.SaveAsync([item], zip, () =>
        {
            Assert.Equal(2, attempts);
            return Task.FromResult<Stream>(output);
        }, null, null, CancellationToken.None);
        if (zip)
        {
            using var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
            Assert.Equal("complete", Read(archive, "photo.jpg"));
        }
        else Assert.Equal("complete", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Theory]
    [InlineData("../outside.jpg")]
    [InlineData("/absolute.jpg")]
    [InlineData("Nice/../../outside.jpg")]
    public async Task InvalidArchiveName_DoesNotWriteNativeDestination(string name)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => MobilePhotoDownloadService.SaveAsync([Item(name, "data")], true,
            () => throw new Exception("Destination must not be opened"), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task CancelledDownload_DoesNotOpenNativeDestination()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MobilePhotoDownloadService.SaveAsync([Item("photo.jpg", "data")], false,
            () => throw new Exception("Destination must not be opened"), null, null, cancellation.Token));
    }

    private static MobileDownloadItem Item(string name, string content) => new(name,
        (stream, token) => stream.WriteAsync(Encoding.UTF8.GetBytes(content), token).AsTask());

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private sealed class DocumentStream : MemoryStream
    {
        public bool WasDisposed { get; private set; }
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
