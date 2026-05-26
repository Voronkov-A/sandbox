using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Picshare.Services;

namespace Picshare.ViewModels;

public partial class AlbumPhotoViewModel : ObservableObject
{
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

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private string _status = "Loading";

    [ObservableProperty]
    private bool _isFullImageLoaded;

    private bool _isFullImageLoading;

    public async Task LoadThumbnailAsync(ImageCacheService imageCache, HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            Status = "Loading thumbnail";
            var imagePath = await imageCache.GetOrDownloadAsync(
                AlbumId,
                $"{PhotoId}-thumbnail.jpg",
                ThumbnailDownloadUrl,
                httpClient,
                cancellationToken);

            await using var stream = File.OpenRead(imagePath);
            Image = new Bitmap(stream);
            Status = IsFullImageLoaded ? "Full image loaded" : "Click to load full image";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    public async Task LoadFullImageAsync(ImageCacheService imageCache, HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (IsFullImageLoaded || _isFullImageLoading)
        {
            return;
        }

        try
        {
            _isFullImageLoading = true;
            Status = "Loading full image";
            var imagePath = await imageCache.GetOrDownloadAsync(
                AlbumId,
                $"{PhotoId}-full{GetFileExtension(FileName)}",
                DownloadUrl,
                httpClient,
                cancellationToken);

            await using var stream = File.OpenRead(imagePath);
            Image = new Bitmap(stream);
            IsFullImageLoaded = true;
            Status = "Full image loaded";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            _isFullImageLoading = false;
        }
    }

    public void ReleaseCachedImage()
    {
        Image = null;
        IsFullImageLoaded = false;
        Status = "Cache cleared";
    }

    partial void OnImageChanging(Bitmap? value)
    {
        if (Image is not null && !ReferenceEquals(Image, value))
        {
            Image.Dispose();
        }
    }

    private static string GetFileExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.IsNullOrWhiteSpace(extension) ? ".img" : extension;
    }
}
