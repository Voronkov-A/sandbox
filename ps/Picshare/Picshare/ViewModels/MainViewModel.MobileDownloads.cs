using Avalonia.Platform.Storage;
using Picshare.Services;

namespace Picshare.ViewModels;

public partial class MainViewModel
{
    public bool IsMobilePlatform => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
    public bool IsDesktopPlatform => !IsMobilePlatform;
    public string AlbumArchiveFileName => GetSafeArchiveFileName($"{CurrentAlbumTitle}.zip");

    public Task SaveMobileCurrentPhotoAsync(IStorageFile destination)
    {
        IReadOnlyList<MobileDownloadItem> items = _selectedViewedPhoto is { } photo
            ? [CreateMobileDownloadItem(photo, GetSafeDownloadFileName(photo))] : [];
        return SaveMobileDownloadAsync(destination, items, asArchive: false);
    }

    public Task SaveMobileSelectedPhotosAsync(IStorageFile destination) => SaveMobileDownloadAsync(destination,
        GetSelectedPhotos().Select(photo => CreateMobileDownloadItem(photo, GetSafeDownloadFileName(photo))).ToList(), asArchive: true);

    public Task SaveMobileAlbumAsync(IStorageFile destination)
    {
        var items = AlbumDownloadCategories.Where(category => category.SelectedMode.Id != "exclude")
            .SelectMany(category => GetPhotosForCategory(category.CategoryKey)
                .Select(photo => CreateMobileDownloadItem(photo, $"{category.CategoryName}/{GetSafeDownloadFileName(photo)}"))).ToList();
        return SaveMobileDownloadAsync(destination, items, asArchive: true);
    }

    private MobileDownloadItem CreateMobileDownloadItem(AlbumPhotoViewModel photo, string entryName) =>
        new(entryName, (destination, token) => _imageCache.CopyOriginalToAsync(photo.AlbumId,
            GetFullPhotoCacheFileName(photo), photo.DownloadUrl, _imageHttpClient, destination, token));

    private async Task SaveMobileDownloadAsync(IStorageFile destination, IReadOnlyList<MobileDownloadItem> items, bool asArchive)
    {
        if (items.Count == 0) { Status = "Select at least one photo to download."; return; }
        if (_albumDownloadCancellation is not null) { Status = "A download is already in progress."; return; }
        using var cancellation = new CancellationTokenSource();
        _albumDownloadCancellation = cancellation;
        IsAlbumDownloadDialogVisible = false;
        IsAlbumDownloadProgressVisible = true;
        AlbumDownloadProgressValue = 0;
        AlbumDownloadProgressMaximum = items.Count;
        AlbumDownloadProgressMessage = "Preparing download...";
        AlbumDownloadProgressWarning = "";
        try
        {
            await using var operation = await BeginLongRunningOperationAsync(LongRunningOperationKind.Download,
                "Picshare download", AlbumDownloadProgressMessage, 0, items.Count, cancellation.Token);
            await MobilePhotoDownloadService.SaveAsync(items, asArchive, destination.OpenWriteAsync,
                (completed, name) => { AlbumDownloadProgressValue = completed; AlbumDownloadProgressMessage = $"Saving {name}"; },
                warning => AlbumDownloadProgressWarning = warning, cancellation.Token);
            Status = $"Saved {destination.Name}.";
            if (IsPhotoViewerVisible) PhotoViewerStatus = Status;
        }
        catch (OperationCanceledException) { Status = "Download cancelled."; }
        catch (Exception ex)
        {
            DiagnosticLogging.LogException(ex);
            Status = $"Could not save download: {ex.Message}";
            if (IsPhotoViewerVisible) PhotoViewerStatus = Status;
        }
        finally
        {
            IsAlbumDownloadProgressVisible = false;
            AlbumDownloadProgressWarning = "";
            ClearLongRunningOperation(LongRunningOperationKind.Download);
            if (ReferenceEquals(_albumDownloadCancellation, cancellation)) _albumDownloadCancellation = null;
        }
    }
}
