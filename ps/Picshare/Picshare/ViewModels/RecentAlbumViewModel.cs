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
    }

    public string Title { get; }

    public string Link { get; }

    public string Location { get; }

    public DateTimeOffset OpenedAt { get; }

    public string DisplayText => string.IsNullOrWhiteSpace(Location)
        ? Title
        : $"{Title} - {Location}";
}
