using Avalonia.Platform.Storage;
using Picshare.Models;

namespace Picshare.Services;

public sealed class LocalPhotoScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".bmp"
    };

    public async Task<IReadOnlyList<PhotoUploadSource>> FindPhotosAsync(
        IReadOnlyList<IStorageFolder> pickedFolders,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (pickedFolders.Count == 0)
        {
            throw new InvalidOperationException("Choose at least one photo folder.");
        }

        var photos = new Dictionary<string, PhotoUploadSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in pickedFolders)
        {
            await AddPickedFolderPhotosAsync(photos, folder, date, cancellationToken);
        }

        return photos.Values
            .OrderBy(photo => photo.SortKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task AddPickedFolderPhotosAsync(
        Dictionary<string, PhotoUploadSource> photos,
        IStorageFolder folder,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await foreach (var item in folder.GetItemsAsync().WithCancellation(cancellationToken))
        {
            if (item is IStorageFolder childFolder)
            {
                await AddPickedFolderPhotosAsync(photos, childFolder, date, cancellationToken);
                continue;
            }

            if (item is not IStorageFile file || !SupportedExtensions.Contains(Path.GetExtension(file.Name)))
            {
                continue;
            }

            var localPath = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath) && !IsFileFromDate(localPath, date))
            {
                continue;
            }

            var key = !string.IsNullOrWhiteSpace(localPath) ? Path.GetFullPath(localPath) : file.Path.AbsoluteUri;
            photos.TryAdd(
                key,
                new PhotoUploadSource(
                    file.Name,
                    key,
                    async () => await file.OpenReadAsync()));
        }
    }

    private static bool IsFileFromDate(string path, DateOnly date)
    {
        var file = new FileInfo(path);
        var created = DateOnly.FromDateTime(file.CreationTime);
        var modified = DateOnly.FromDateTime(file.LastWriteTime);

        return created == date || modified == date;
    }
}
