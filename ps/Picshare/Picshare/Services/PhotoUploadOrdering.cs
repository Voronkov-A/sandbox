using System.Diagnostics;
using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Xmp;
using Picshare.Models;

namespace Picshare.Services;

/// <summary>Determines upload numbering without changing the author's preview collection.</summary>
public static class PhotoUploadOrdering
{
    public static Task<IReadOnlyList<PhotoUploadSource>> SortAsync(
        IReadOnlyList<PhotoUploadSource> photos, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var started = Stopwatch.GetTimestamp();
        var entries = new Entry[photos.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, photos.Count), new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken
        }, async (index, token) =>
        {
            var photo = photos[index];
            DateTimeOffset? timestamp = null;
            var source = "name";
            try
            {
                await using var stream = await photo.OpenReadAsync();
                token.ThrowIfCancellationRequested();
                timestamp = ReadCaptureTime(stream, photo.FileName);
                if (timestamp.HasValue) source = "metadata";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageProcessingException
                or MetadataException or ArgumentException or NotSupportedException)
            {
                // Unreadable or malformed metadata must not prevent the timestamp/name fallback.
            }
            token.ThrowIfCancellationRequested();
            if (timestamp is null && !string.IsNullOrWhiteSpace(photo.LocalPath))
            {
                try
                {
                    var file = new FileInfo(photo.LocalPath);
                    if (file.Exists && file.LastWriteTimeUtc.Year > 1601)
                    {
                        timestamp = new DateTimeOffset(file.LastWriteTimeUtc);
                        source = "modified";
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
            entries[index] = new Entry(photo, timestamp, source, index);
        });
        cancellationToken.ThrowIfCancellationRequested();
        var ordered = entries.OrderBy(entry => entry.Timestamp is null)
            .ThenBy(entry => entry.Timestamp)
            .ThenBy(entry => entry.Photo.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Photo.FileName, StringComparer.Ordinal)
            .ThenBy(entry => entry.Photo.SortKey, StringComparer.Ordinal)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Photo).ToArray();
        DiagnosticLogging.Current.Write("Information", "AlbumUpload",
            $"Upload order calculated: photos={photos.Count}; metadata={entries.Count(e => e.Source == "metadata")}; " +
            $"modified={entries.Count(e => e.Source == "modified")}; filename={entries.Count(e => e.Source == "name")}; " +
            $"elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}");
        return (IReadOnlyList<PhotoUploadSource>)ordered;
    }, cancellationToken);

    private static DateTimeOffset? ReadCaptureTime(Stream stream, string fileName)
    {
        var directories = ImageMetadataReader.ReadMetadata(stream, fileName);
        foreach (var directory in directories.OfType<ExifSubIfdDirectory>())
        {
            // DateTime (IFD0) describes editing; prefer the original/digitization capture dates.
            foreach (var (tag, offsetTag, subsecondTag) in new[]
            {
                (ExifDirectoryBase.TagDateTimeOriginal, 0x9011, 0x9291),
                (ExifDirectoryBase.TagDateTimeDigitized, 0x9012, 0x9292)
            })
            {
                if (!directory.TryGetDateTime(tag, out var date) || date.Year <= 1) continue;
                var fraction = directory.GetString(subsecondTag)?.Trim().TrimEnd('\0');
                if (!string.IsNullOrEmpty(fraction) && fraction.All(char.IsAsciiDigit) &&
                    long.TryParse(fraction.PadRight(7, '0')[..7], out var ticks))
                    date = date.AddTicks(ticks);
                var offset = directory.GetString(offsetTag)?.Trim().TrimEnd('\0');
                if (DateTimeOffset.TryParseExact(date.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + offset,
                    "yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var zoned))
                    return zoned;
                // Cameras commonly omit a timezone. Interpret their wall clock in the local zone,
                // consistently with filesystem modification times shown on this device.
                return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(date));
            }
        }
        foreach (var directory in directories.OfType<XmpDirectory>())
        {
            var original = directory.XmpMeta?.GetPropertyString("http://ns.adobe.com/exif/1.0/", "DateTimeOriginal");
            var created = directory.XmpMeta?.GetPropertyString("http://ns.adobe.com/xap/1.0/", "CreateDate");
            foreach (var value in new[] { original, created })
                if (!string.IsNullOrWhiteSpace(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var timestamp)) return timestamp;
        }
        return null;
    }

    private sealed record Entry(PhotoUploadSource Photo, DateTimeOffset? Timestamp, string Source, int Index);
}
