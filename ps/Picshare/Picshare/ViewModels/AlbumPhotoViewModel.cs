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

    public IBrush CardBorderBrush => IsSelectedForViewing
        ? Brushes.DeepSkyBlue
        : new SolidColorBrush(Color.Parse("#D6D8D1"));

    public Thickness CardBorderThickness => IsSelectedForViewing
        ? new Thickness(3)
        : new Thickness(1);

    private CancellationTokenSource? _loadCancellation;
    private bool _isLoading;

    public async Task StartViewportLoadAsync(ImageCacheService imageCache, HttpClient httpClient)
    {
        if (_isLoading)
        {
            return;
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;

        try
        {
            _isLoading = true;

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
                _isLoading = false;
            }
        }
    }

    public void StopViewportLoad()
    {
        _loadCancellation?.Cancel();
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
}
