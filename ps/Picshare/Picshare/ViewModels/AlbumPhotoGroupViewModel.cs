using System.Collections.ObjectModel;

namespace Picshare.ViewModels;

public sealed class AlbumPhotoGroupViewModel
{
    public AlbumPhotoGroupViewModel(string header, IEnumerable<AlbumPhotoViewModel> photos, int photosPerRow)
    {
        Header = header;
        foreach (var row in photos.Chunk(photosPerRow))
        {
            Rows.Add(new AlbumPhotoRowViewModel(header, row));
        }
    }

    public string Header { get; }

    public ObservableCollection<AlbumPhotoRowViewModel> Rows { get; } = new();
}
