namespace Picshare.ViewModels;

public sealed record PhotoViewerActionOption(string Id, string Name)
{
    public static IReadOnlyList<PhotoViewerActionOption> All { get; } =
    [
        new("previous", "Previous photo"), new("next", "Next photo"),
        new("nice", "Move to nice"), new("okay", "Move to okay"),
        new("rotate-left", "Rotate left"), new("rotate-right", "Rotate right")
    ];

    public static PhotoViewerActionOption Find(string? id, string fallback) =>
        All.FirstOrDefault(option => option.Id == id) ?? All.First(option => option.Id == fallback);

    public override string ToString() => Name;
}
