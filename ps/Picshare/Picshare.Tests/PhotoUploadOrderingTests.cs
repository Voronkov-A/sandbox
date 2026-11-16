using System.Text;
using Picshare.Models;
using Picshare.Services;
using SkiaSharp;

namespace Picshare.Tests;

public sealed class PhotoUploadOrderingTests
{
    [Fact]
    public async Task UploadOrder_UsesCaptureTimeBeforeModificationTimeAndDoesNotChangePreview()
    {
        using var temp = new TempDirectory();
        var early = temp.Photo("z-early.jpg", "2020:01:01 12:00:00", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var middle = temp.Photo("b-middle.jpg", null, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var late = temp.Photo("a-late.jpg", "2022:01:01 12:00:00", new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var preview = new[] { late, middle, early };
        var sorted = await PhotoUploadOrdering.SortAsync(preview, CancellationToken.None);
        Assert.Equal([early, middle, late], sorted);
        Assert.Equal([late, middle, early], preview);
    }

    [Fact]
    public async Task CaptureTime_HonorsExifTimezoneOffsets()
    {
        using var temp = new TempDirectory();
        var later = temp.Photo("a.jpg", "2020:01:01 12:00:00", DateTime.UtcNow, "+01:00");
        var earlier = temp.Photo("z.jpg", "2020:01:01 12:00:00", DateTime.UtcNow, "+03:00");
        Assert.Equal([earlier, later], await PhotoUploadOrdering.SortAsync([later, earlier], CancellationToken.None));
    }

    [Fact]
    public async Task MissingOrInvalidMetadata_FallsBackToModificationTimeThenFilename()
    {
        using var temp = new TempDirectory();
        var date = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var corrupt = temp.Photo("b.jpg", "not a date", date);
        var ordinary = temp.Photo("a.jpg", null, date);
        var missingZ = Source("z.jpg", () => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        var missingC = Source("c.jpg", () => throw new IOException("Cannot read metadata"));
        Assert.Equal([ordinary, corrupt, missingC, missingZ],
            await PhotoUploadOrdering.SortAsync([missingZ, corrupt, missingC, ordinary], CancellationToken.None));
    }

    [Fact]
    public async Task Sorting_ClosesMetadataStreamsAndHonorsCancellation()
    {
        var stream = new MemoryStream([1, 2, 3]);
        await PhotoUploadOrdering.SortAsync([Source("photo.jpg", () => Task.FromResult<Stream>(stream))], CancellationToken.None);
        Assert.False(stream.CanRead);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PhotoUploadOrdering.SortAsync(
            [Source("photo.jpg", () => throw new InvalidOperationException("Must not open"))], cancellation.Token));
    }

    [Fact]
    public async Task ResumableCreation_PersistsChronologicalNumberingAndKeepsItWhenFilesChange()
    {
        using var temp = new TempDirectory();
        var later = temp.Photo("later.jpg", "2022:01:01 12:00:00", DateTime.UtcNow);
        var earlier = temp.Photo("earlier.jpg", "2020:01:01 12:00:00", DateTime.UtcNow);
        var service = new PersistentAlbumCreationService(temp.Path);
        var pending = await service.CreatePendingCreationAsync("local-file-system", "Ordered", 1, null,
            System.IO.Path.Combine(temp.Path, "albums"), GoogleDriveAlbumShareSettings.Public,
            new FeedbackReviewerIdentity { BackendType = "local-file-system", UserId = "test" },
            [later, earlier], CancellationToken.None);
        Assert.Equal(["earlier.jpg", "later.jpg"], pending.Photos.Select(photo => photo.FileName));
        Assert.Equal(["Ordered_00001.jpg", "Ordered_00002.jpg"], pending.Photos.Select(photo => photo.StoredFileName));
        var reloaded = await service.LoadPendingCreationAsync(CancellationToken.None);
        Assert.NotNull(reloaded);
        File.WriteAllBytes(earlier.LocalPath!, JpegWithExif("2024:01:01 12:00:00", null));
        var result = await service.ResumeAsync(reloaded, _ => throw new InvalidOperationException(), null, 2, CancellationToken.None);
        Assert.Equal(["Ordered_00001.jpg", "Ordered_00002.jpg"], result.Manifest.Photos.Select(photo => photo.FileName));
        Assert.Equal(File.ReadAllBytes(earlier.LocalPath!),
            File.ReadAllBytes(new Uri(result.Manifest.Photos[0].DownloadUrl).LocalPath));
    }

    [Fact]
    public async Task SharedPublishing_SortsBeforeAssigningFileNumbersEvenWithParallelStorage()
    {
        using var temp = new TempDirectory();
        var later = temp.Photo("later.jpg", "2022:01:01 12:00:00", DateTime.UtcNow);
        var earlier = temp.Photo("earlier.jpg", "2020:01:01 12:00:00", DateTime.UtcNow);
        var stored = new System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>();
        var references = await AlbumPublishingWorkflow.PublishPhotosAsync([later, earlier], "Ordered", 2,
            async (photo, token) =>
            {
                using var data = new MemoryStream();
                await photo.ContentStream.CopyToAsync(data, token);
                stored[photo.FileName] = data.ToArray();
                return new StoredAlbumPhoto("local-file", photo.FileName, photo.PhotoId);
            }, CancellationToken.None);
        Assert.Equal(["Ordered_00001.jpg", "Ordered_00002.jpg"], references.Select(photo => photo.FileName));
        Assert.Equal(File.ReadAllBytes(earlier.LocalPath!), stored["Ordered_00001.jpg"]);
    }

    private static PhotoUploadSource Source(string name, Func<Task<Stream>> open) => new(name, name, open);

    [Fact]
    public async Task MissingOrMalformedMetadata_UsesModificationTimeThenNamesWithStableTies()
    {
        using var temp = new TempDirectory();
        var invalid = temp.Photo("dated.jpg", "not a date", DateTime.UtcNow);
        var first = Source("a.jpg", () => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        var second = Source("b.jpg", () => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        var tied = first with { SortKey = "z-other-folder" };
        var missing = new PhotoUploadSource("c.jpg", "missing", () => throw new FileNotFoundException(),
            System.IO.Path.Combine(temp.Path, "absent.jpg"));
        var result = await PhotoUploadOrdering.SortAsync([second, tied, missing, invalid, first], CancellationToken.None);
        Assert.Equal([invalid, first, tied, second, missing], result);
    }

    // Real JPEG pixels plus an EXIF APP1 segment: TIFF IFD0 points to DateTimeOriginal
    // and, optionally, OffsetTimeOriginal in the Exif SubIFD.
    private static byte[] JpegWithExif(string? capture, string? offset)
    {
        using var bitmap = new SKBitmap(1, 1);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        var jpeg = encoded.ToArray();
        if (capture is null) return jpeg;
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("Exif\0\0II"));
            writer.Write((ushort)42);
            writer.Write((uint)8);
            writer.Write((ushort)1);
            writer.Write((ushort)0x8769); writer.Write((ushort)4); writer.Write((uint)1); writer.Write((uint)26);
            writer.Write((uint)0);
            var tagCount = offset is null ? 1 : 2;
            var dateBytes = Encoding.ASCII.GetBytes(capture + '\0');
            var dateOffset = (uint)(26 + 2 + tagCount * 12 + 4);
            writer.Write((ushort)tagCount);
            writer.Write((ushort)0x9003); writer.Write((ushort)2); writer.Write((uint)dateBytes.Length); writer.Write(dateOffset);
            if (offset is not null)
            {
                writer.Write((ushort)0x9011); writer.Write((ushort)2); writer.Write((uint)7); writer.Write(dateOffset + (uint)dateBytes.Length);
            }
            writer.Write((uint)0);
            writer.Write(dateBytes);
            if (offset is not null) writer.Write(Encoding.ASCII.GetBytes(offset + '\0'));
        }
        var segment = payload.ToArray();
        var length = segment.Length + 2;
        return [.. jpeg.AsSpan(0, 2), 0xff, 0xe1, (byte)(length >> 8), (byte)length, .. segment, .. jpeg.AsSpan(2)];
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "picshare-ordering-tests", Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public PhotoUploadSource Photo(string name, string? capture, DateTime modified, string? offset = null)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, JpegWithExif(capture, offset));
            File.SetLastWriteTimeUtc(path, modified);
            return new(name, name, () => Task.FromResult<Stream>(File.OpenRead(path)), path);
        }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
