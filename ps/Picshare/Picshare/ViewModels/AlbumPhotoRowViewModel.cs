namespace Picshare.ViewModels;

public sealed class AlbumPhotoRowViewModel
{
    public AlbumPhotoRowViewModel(string groupHeader, IReadOnlyList<AlbumPhotoViewModel> photos)
    {
        GroupHeader = groupHeader;
        Photos = photos;
    }

    public string GroupHeader { get; }

    public IReadOnlyList<AlbumPhotoViewModel> Photos { get; }
}
