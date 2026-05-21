using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Picshare.Models;

namespace Picshare.ViewModels;

public sealed partial class AlbumPhotoSourceViewModel : ObservableObject
{
    private const int ThumbnailWidth = 96;
    private static readonly SemaphoreSlim ThumbnailLoadGate = new(3);

    public AlbumPhotoSourceViewModel(PhotoUploadSource source)
    {
        Source = source;
    }

    public PhotoUploadSource Source { get; }

    public string FileName => Source.FileName;

    public string SortKey => Source.SortKey;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _hasThumbnailError;

    public async Task LoadThumbnailAsync()
    {
        if (Thumbnail is not null || HasThumbnailError)
        {
            return;
        }

        await ThumbnailLoadGate.WaitAsync();
        try
        {
            await using var stream = await Source.OpenReadAsync();
            var thumbnail = Bitmap.DecodeToWidth(stream, ThumbnailWidth);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Thumbnail = thumbnail;
                HasThumbnailError = false;
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() => HasThumbnailError = true);
        }
        finally
        {
            ThumbnailLoadGate.Release();
        }
    }
}
