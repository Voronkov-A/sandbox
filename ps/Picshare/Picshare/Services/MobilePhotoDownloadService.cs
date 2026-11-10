using System.IO.Compression;

namespace Picshare.Services;

public sealed record MobileDownloadItem(string EntryName, Func<Stream, CancellationToken, Task> CopyToAsync);

/// <summary>Stages downloads before writing a native document stream, which need not be seekable.</summary>
public static class MobilePhotoDownloadService
{
    public static async Task SaveAsync(IReadOnlyList<MobileDownloadItem> items, bool asArchive,
        Func<Task<Stream>> openDestinationAsync, Action<int, string>? reportProgress,
        Action<string>? reportWarning, CancellationToken cancellationToken)
    {
        if (items.Count == 0) throw new InvalidOperationException("Select at least one photo to download.");
        if (!asArchive && items.Count != 1) throw new InvalidOperationException("Multiple photos must be saved as a ZIP file.");
        var directory = Path.Combine(Path.GetTempPath(), "Picshare", "downloads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var payloadPath = Path.Combine(directory, "download");
        var itemPath = Path.Combine(directory, "photo");
        try
        {
            if (asArchive)
            {
                using var archive = ZipFile.Open(payloadPath, ZipArchiveMode.Create);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < items.Count; index++)
                {
                    var item = items[index];
                    reportProgress?.Invoke(index, item.EntryName);
                    await using var buffer = new FileStream(itemPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                    await PrepareItemAsync(item, buffer, reportWarning, cancellationToken);
                    var entryName = GetEntryName(item.EntryName, names);
                    var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
                    await using var output = entry.Open();
                    await buffer.CopyToAsync(output, cancellationToken);
                    reportProgress?.Invoke(index + 1, item.EntryName);
                }
            }
            else
            {
                reportProgress?.Invoke(0, items[0].EntryName);
                await using var buffer = new FileStream(payloadPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                await PrepareItemAsync(items[0], buffer, reportWarning, cancellationToken);
                reportProgress?.Invoke(1, items[0].EntryName);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await using var source = File.OpenRead(payloadPath);
            await using var destination = await openDestinationAsync();
            if (destination.CanSeek) { destination.Position = 0; destination.SetLength(0); }
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }
        finally
        {
            // Only remove files inside this operation's private temporary directory.
            try { File.Delete(itemPath); File.Delete(payloadPath); Directory.Delete(directory); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task PrepareItemAsync(MobileDownloadItem item, Stream buffer,
        Action<string>? reportWarning, CancellationToken cancellationToken)
    {
        await TransientRetryPolicy.ExecuteAsync(async token =>
        {
            buffer.SetLength(0);
            buffer.Position = 0;
            await item.CopyToAsync(buffer, token);
        }, reportWarning, cancellationToken);
        buffer.Position = 0;
    }

    private static string GetEntryName(string requestedName, HashSet<string> names)
    {
        var parts = requestedName.Replace('\\', '/').Split('/');
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.Contains(':')))
            throw new InvalidOperationException("Invalid photo archive entry name.");
        var name = string.Join('/', parts);
        var candidate = name;
        var extension = Path.GetExtension(name);
        var stem = name[..(name.Length - extension.Length)];
        for (var suffix = 2; !names.Add(candidate); suffix++) candidate = $"{stem} ({suffix}){extension}";
        return candidate;
    }
}
