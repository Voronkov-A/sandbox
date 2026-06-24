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

    public bool HasThumbnailPlaceholder => Thumbnail is null || HasThumbnailError;

    [ObservableProperty]
    private bool _isSelected;

    public IBrush SelectionBackground => IsSelected ? Brushes.ForestGreen : Brushes.White;

    public IBrush SelectionForeground => IsSelected ? Brushes.White : Brushes.Black;

    public string SelectionGlyph => IsSelected ? "On" : "+";

    public IBrush CardBorderBrush => IsSelected
        ? Brushes.ForestGreen
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
                OnPropertyChanged(nameof(HasThumbnailPlaceholder));
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HasThumbnailError = true;
                OnPropertyChanged(nameof(HasThumbnailPlaceholder));
            });
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
        OnPropertyChanged(nameof(SelectionGlyph));
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(CardBorderThickness));
    }

    partial void OnThumbnailChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasThumbnailPlaceholder));
    }

    partial void OnHasThumbnailErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(HasThumbnailPlaceholder));
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
