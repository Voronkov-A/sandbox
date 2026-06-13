using CommunityToolkit.Mvvm.ComponentModel;

namespace Picshare.ViewModels;

public sealed partial class AlbumDownloadCategoryViewModel : ObservableObject
{
    public AlbumDownloadCategoryViewModel(
        string categoryKey,
        string categoryName,
        int photoCount,
        string defaultDestinationDirectoryPath,
        string defaultModeId)
    {
        CategoryKey = categoryKey;
        CategoryName = categoryName;
        PhotoCount = photoCount;
        DestinationDirectoryPath = defaultDestinationDirectoryPath;
        SelectedMode = ModeOptions.FirstOrDefault(mode => mode.Id == defaultModeId) ?? ModeOptions[0];
    }

    public string CategoryKey { get; }

    public string CategoryName { get; }

    public int PhotoCount { get; }

    public string Header => $"{CategoryName} ({PhotoCount})";

    public IReadOnlyList<AlbumDownloadModeViewModel> ModeOptions { get; } =
    [
        new("include", "Include"),
        new("archive", "Include as archive"),
        new("exclude", "Exclude")
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDestinationDirectoryEnabled))]
    private AlbumDownloadModeViewModel _selectedMode = null!;

    [ObservableProperty]
    private string _destinationDirectoryPath = "";

    public bool IsDestinationDirectoryEnabled => SelectedMode.Id != "exclude";
}

public sealed record AlbumDownloadModeViewModel(string Id, string Name);
