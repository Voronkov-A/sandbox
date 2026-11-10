using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Picshare.Services;
using Picshare.ViewModels;

namespace Picshare.Views;

internal static class MobileDownloadHelper
{
    public static async Task SaveAsync(Control owner, MainViewModel viewModel, string fileName, bool zip,
        Func<IStorageFile, Task> saveAsync)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
            if (storage?.CanSave != true) throw new InvalidOperationException("The system save picker is unavailable.");
            var extension = Path.GetExtension(fileName);
            var mimeType = extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream"
            };
            using var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = zip ? "Save photos as ZIP" : "Save photo",
                SuggestedFileName = fileName,
                DefaultExtension = extension.TrimStart('.'),
                FileTypeChoices = zip ? StoragePickerHelper.ZipFileTypeChoices
                    : [new FilePickerFileType("Image") { Patterns = [$"*{extension}"], MimeTypes = [mimeType] }],
                ShowOverwritePrompt = true
            });
            if (file is null) { viewModel.Status = "Download cancelled."; return; }
            // Native providers can return content:// URLs. OpenWriteAsync owns that access;
            // converting the document to a local filesystem path would silently lose it.
            await saveAsync(file);
        }
        catch (Exception ex)
        {
            DiagnosticLogging.LogException(ex);
            viewModel.Status = $"Could not save download: {ex.Message}";
            if (viewModel.IsPhotoViewerVisible) viewModel.PhotoViewerStatus = viewModel.Status;
        }
    }
}
