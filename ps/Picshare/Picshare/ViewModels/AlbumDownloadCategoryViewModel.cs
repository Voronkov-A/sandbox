using CommunityToolkit.Mvvm.ComponentModel;

namespace Picshare.ViewModels;

public sealed partial class AlbumDownloadCategoryViewModel : ObservableObject
{
    public AlbumDownloadCategoryViewModel(
        string categoryKey,
        string categoryName,
        int photoCount,
        string defaultDestinationDirectoryPath,
        string defaultArchiveFileName,
        string defaultModeId)
    {
        CategoryKey = categoryKey;
        CategoryName = categoryName;
        PhotoCount = photoCount;
        DefaultDestinationDirectoryPath = defaultDestinationDirectoryPath;
        DefaultArchiveFileName = defaultArchiveFileName;
        SelectedMode = ModeOptions.FirstOrDefault(mode => mode.Id == defaultModeId) ?? ModeOptions[0];
        DestinationDirectoryPath = SelectedMode.Id == "archive"
            ? Path.Combine(defaultDestinationDirectoryPath, defaultArchiveFileName)
            : defaultDestinationDirectoryPath;
    }

    public string CategoryKey { get; }

    public string CategoryName { get; }

    public int PhotoCount { get; }

    public string Header => $"{CategoryName} ({PhotoCount})";

    private string DefaultDestinationDirectoryPath { get; }

    private string DefaultArchiveFileName { get; }

    public IReadOnlyList<AlbumDownloadModeViewModel> ModeOptions { get; } =
    [
        new("include", "Include"),
        new("archive", "Include as archive"),
        new("exclude", "Exclude")
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDestinationDirectoryEnabled))]
    [NotifyPropertyChangedFor(nameof(IsArchiveMode))]
    [NotifyPropertyChangedFor(nameof(DestinationPathWatermark))]
    [NotifyPropertyChangedFor(nameof(DestinationSelectButtonText))]
    private AlbumDownloadModeViewModel _selectedMode = null!;

    [ObservableProperty]
    private string _destinationDirectoryPath = "";

    public bool IsDestinationDirectoryEnabled => SelectedMode.Id != "exclude";

    public bool IsArchiveMode => SelectedMode.Id == "archive";

    public string DestinationPathWatermark => IsArchiveMode ? "Destination archive file" : "Destination directory";

    public string DestinationSelectButtonText => IsArchiveMode ? "Save as" : "Select";

    partial void OnSelectedModeChanged(AlbumDownloadModeViewModel value)
    {
        if (value.Id == "archive")
        {
            if (string.IsNullOrWhiteSpace(DestinationDirectoryPath) ||
                !string.Equals(Path.GetExtension(DestinationDirectoryPath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                DestinationDirectoryPath = Path.Combine(
                    string.IsNullOrWhiteSpace(DestinationDirectoryPath)
                        ? DefaultDestinationDirectoryPath
                        : DestinationDirectoryPath,
                    DefaultArchiveFileName);
            }
        }
        else if (!string.IsNullOrWhiteSpace(DestinationDirectoryPath) &&
            string.Equals(Path.GetExtension(DestinationDirectoryPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            DestinationDirectoryPath = Path.GetDirectoryName(DestinationDirectoryPath) ?? DefaultDestinationDirectoryPath;
        }
    }
}

public sealed record AlbumDownloadModeViewModel(string Id, string Name);
