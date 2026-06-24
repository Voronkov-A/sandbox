using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Picshare.Services;

namespace Picshare.ViewModels;

public partial class AlbumPhotoViewModel : ObservableObject
{
    private const int ThumbnailPixelSize = 64;
    private const int DisplayPixelWidth = 220;
    private const int DisplayPixelHeight = 150;
    private static readonly SemaphoreSlim DisplayImageLoadGate = new(1, 1);

    public AlbumPhotoViewModel(
        string albumId,
        string photoId,
        string fileName,
        string downloadUrl,
        string thumbnailDownloadUrl)
    {
        AlbumId = albumId;
        PhotoId = photoId;
        FileName = fileName;
        DownloadUrl = downloadUrl;
        ThumbnailDownloadUrl = thumbnailDownloadUrl;
    }

    public string AlbumId { get; }

    public string PhotoId { get; }

    public string FileName { get; }

    public string DownloadUrl { get; }

    public string ThumbnailDownloadUrl { get; }

    public string FileExtension
    {
        get
        {
            var extension = Path.GetExtension(FileName);
            return string.IsNullOrWhiteSpace(extension) ? ".img" : extension;
        }
    }

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private string _status = "Loading";

    [ObservableProperty]
    private bool _isFullImageLoaded;

    [ObservableProperty]
    private bool _isSelectedForViewing;

    [ObservableProperty]
    private string _category = "";

    [ObservableProperty]
    private bool _isFrozen;

    [ObservableProperty]
    private int _rotationDegrees;

    [ObservableProperty]
    private bool _isSelectedForBulk;

    [ObservableProperty]
    private bool _isDuplicateGroupMain;

    [ObservableProperty]
    private string _duplicateGroupId = "";

    [ObservableProperty]
    private int _duplicateGroupCount;

    [ObservableProperty]
    private AlbumPhotoViewModel? _duplicateStackPhoto;

    [ObservableProperty]
    private bool _isBestInDuplicateGroup;

    [ObservableProperty]
    private bool _isImageLoading;

    public bool HasDuplicateGroup => IsDuplicateGroupMain && DuplicateGroupCount > 1;

    public string DuplicateGroupCountText => DuplicateGroupCount > 1 ? DuplicateGroupCount.ToString() : "";

    public bool IsBestMarkerVisible => IsBestInDuplicateGroup;

    public IBrush CardBorderBrush => IsSelectedForViewing
        ? Brushes.DeepSkyBlue
        : IsSelectedForBulk
            ? Brushes.ForestGreen
        : new SolidColorBrush(Color.Parse("#D6D8D1"));

    public Thickness CardBorderThickness => IsSelectedForViewing
        ? new Thickness(3)
        : IsSelectedForBulk
            ? new Thickness(3)
        : new Thickness(1);

    public string SelectionGlyph => IsSelectedForBulk ? "On" : "+";

    public IBrush SelectionBackground => IsSelectedForBulk ? Brushes.ForestGreen : Brushes.White;

    public IBrush SelectionForeground => IsSelectedForBulk ? Brushes.White : Brushes.Black;

    private CancellationTokenSource? _loadCancellation;

    public async Task StartViewportLoadAsync(ImageCacheService imageCache, HttpClient httpClient)
    {
        if (IsImageLoading)
        {
            return;
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;

        try
        {
            IsImageLoading = true;

            if (Image is null)
            {
                await LoadThumbnailAsync(imageCache, httpClient, cancellation.Token);
            }

            await LoadDisplayImageAsync(imageCache, httpClient, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation.Dispose();
                _loadCancellation = null;
                IsImageLoading = false;
            }
        }
    }

    public void StopViewportLoad()
    {
        _loadCancellation?.Cancel();
        IsImageLoading = false;
        ReleaseCachedImage();
    }

    private async Task LoadThumbnailAsync(ImageCacheService imageCache, HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            Status = "Loading thumbnail";
            Image = await imageCache.LoadDisplayBitmapAsync(
                AlbumId,
                $"{PhotoId}-thumbnail.jpg",
                ThumbnailDownloadUrl,
                httpClient,
                ThumbnailPixelSize,
                ThumbnailPixelSize,
                cancellationToken);

            Status = "Loading full image";
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                Status = ex.Message;
            }
        }
    }

    private async Task LoadDisplayImageAsync(ImageCacheService imageCache, HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (IsFullImageLoaded)
        {
            return;
        }

        try
        {
            Status = "Loading full image";
            await DisplayImageLoadGate.WaitAsync(cancellationToken);
            try
            {
                if (IsFullImageLoaded)
                {
                    return;
                }

                Image = await imageCache.LoadDisplayBitmapAsync(
                    AlbumId,
                    $"{PhotoId}-full{FileExtension}",
                    DownloadUrl,
                    httpClient,
                    DisplayPixelWidth,
                    DisplayPixelHeight,
                    cancellationToken);

                IsFullImageLoaded = true;
                Status = "Full image loaded";
            }
            finally
            {
                DisplayImageLoadGate.Release();
            }
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                Status = ex.Message;
            }
        }
    }

    public void ReleaseCachedImage()
    {
        Image = null;
        IsFullImageLoaded = false;
        Status = "Loading";
    }

    partial void OnImageChanging(Bitmap? value)
    {
        if (Image is not null && !ReferenceEquals(Image, value))
        {
            Image.Dispose();
        }
    }

    partial void OnIsSelectedForViewingChanged(bool value)
    {
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(CardBorderThickness));
    }

    partial void OnIsSelectedForBulkChanged(bool value)
    {
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(CardBorderThickness));
        OnPropertyChanged(nameof(SelectionGlyph));
        OnPropertyChanged(nameof(SelectionBackground));
        OnPropertyChanged(nameof(SelectionForeground));
    }

    partial void OnIsDuplicateGroupMainChanged(bool value)
    {
        OnPropertyChanged(nameof(HasDuplicateGroup));
    }

    partial void OnDuplicateGroupCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasDuplicateGroup));
        OnPropertyChanged(nameof(DuplicateGroupCountText));
    }

    partial void OnIsBestInDuplicateGroupChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBestMarkerVisible));
    }
}
