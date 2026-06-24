namespace Picshare.ViewModels;

public sealed class AlbumPhotoGroupHeaderViewModel
{
    public AlbumPhotoGroupHeaderViewModel(string header)
    {
        Header = header;
    }

    public string Header { get; }
}
