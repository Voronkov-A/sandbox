namespace Picshare.ViewModels;

public sealed class AlbumTypeOptionViewModel
{
    public AlbumTypeOptionViewModel(string id, string title, string description)
    {
        Id = id;
        Title = title;
        Description = description;
    }

    public string Id { get; }

    public string Title { get; }

    public string Description { get; }
}
