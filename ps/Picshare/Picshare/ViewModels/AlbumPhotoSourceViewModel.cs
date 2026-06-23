using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Media;
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

    [ObservableProperty]
    private bool _isSelected;

    public IBrush SelectionBackground => IsSelected ? Brushes.White : Brushes.Transparent;

    public IBrush SelectionForeground => IsSelected ? Brushes.White : Brushes.Transparent;

    public IBrush CardBorderBrush => IsSelected
        ? new SolidColorBrush(Color.Parse("#27343B"))
        : new SolidColorBrush(Color.Parse("#D6D8D1"));

    public Thickness CardBorderThickness => IsSelected ? new Thickness(3) : new Thickness(1);

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

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectionBackground));
        OnPropertyChanged(nameof(SelectionForeground));
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(CardBorderThickness));
    }
}

public sealed class AlbumPhotoSourceRowViewModel
{
    public AlbumPhotoSourceRowViewModel(IEnumerable<AlbumPhotoSourceViewModel> photos)
    {
        Photos = photos.ToList();
    }

    public IReadOnlyList<AlbumPhotoSourceViewModel> Photos { get; }
}
