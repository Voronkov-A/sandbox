namespace Picshare.Views;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

internal static class StoragePickerHelper
{
    public static readonly IReadOnlyList<FilePickerFileType> ZipFileTypeChoices =
    [
        new("Zip archive")
        {
            Patterns = ["*.zip"],
            MimeTypes = ["application/zip"]
        }
    ];

    public static async Task<IReadOnlyList<IStorageFolder>> OpenSingleFolderPickerAsync(
        TopLevel topLevel,
        string title,
        string? suggestedStartPath)
    {
        IStorageFolder? suggestedStartLocation = null;
        if (!string.IsNullOrWhiteSpace(suggestedStartPath))
        {
            try
            {
                suggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedStartPath);
            }
            catch
            {
                suggestedStartLocation = null;
            }
        }

        return await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStartLocation
        });
    }

    public static async Task<IStorageFile?> OpenSaveFilePickerAsync(
        TopLevel topLevel,
        string title,
        string? suggestedStartPath,
        string suggestedFileName,
        IReadOnlyList<FilePickerFileType>? fileTypeChoices)
    {
        IStorageFolder? suggestedStartLocation = null;
        var startPath = GetPickerStartPath(suggestedStartPath);
        if (!string.IsNullOrWhiteSpace(startPath))
        {
            try
            {
                suggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(startPath);
            }
            catch
            {
                suggestedStartLocation = null;
            }
        }

        var extension = Path.GetExtension(suggestedFileName);
        return await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedStartLocation = suggestedStartLocation,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = string.IsNullOrWhiteSpace(extension) ? null : extension,
            FileTypeChoices = fileTypeChoices,
            ShowOverwritePrompt = true
        });
    }

    public static string GetPickerStartPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase) ||
            (!Directory.Exists(path) && !string.IsNullOrWhiteSpace(Path.GetFileName(path)))
                ? Path.GetDirectoryName(path) ?? ""
                : path;
    }
}
