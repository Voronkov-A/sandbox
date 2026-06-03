namespace Picshare.ViewModels;

public sealed class AlbumPhotoRowViewModel
{
    public AlbumPhotoRowViewModel(IReadOnlyList<AlbumPhotoViewModel> photos)
    {
        Photos = photos;
    }

    public IReadOnlyList<AlbumPhotoViewModel> Photos { get; }
}
