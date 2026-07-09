using Picshare.Services;

namespace Picshare.ViewModels;

public sealed class RecentAlbumViewModel
{
    public RecentAlbumViewModel(RecentAlbumSettings settings)
    {
        Title = string.IsNullOrWhiteSpace(settings.Title) ? "Untitled album" : settings.Title;
        Link = settings.Link;
        Location = settings.Location;
        OpenedAt = settings.OpenedAt;
        Description = string.IsNullOrWhiteSpace(settings.Location) ? settings.Link : settings.Location;
    }

    public string Title { get; }

    public string Link { get; }

    public string Location { get; }

    public string Description { get; }

    public DateTimeOffset OpenedAt { get; }

    public string DisplayText => string.IsNullOrWhiteSpace(Location)
        ? Title
        : $"{Title} - {Location}";
}
