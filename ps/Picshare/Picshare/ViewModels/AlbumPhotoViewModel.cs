using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Picshare.ViewModels;

public partial class AlbumPhotoViewModel : ObservableObject
{
    public AlbumPhotoViewModel(string fileName, string downloadUrl)
    {
        FileName = fileName;
        DownloadUrl = downloadUrl;
    }

    public string FileName { get; }

    public string DownloadUrl { get; }

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private string _status = "Loading";

    public async Task LoadAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await httpClient.GetStreamAsync(DownloadUrl, cancellationToken);
            Image = new Bitmap(stream);
            Status = "";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }
}
