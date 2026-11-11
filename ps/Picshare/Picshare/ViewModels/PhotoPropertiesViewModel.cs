using CommunityToolkit.Mvvm.ComponentModel;
using Picshare.Services;

namespace Picshare.ViewModels;

public partial class PhotoPropertiesViewModel : ObservableObject
{
    public PhotoPropertiesViewModel(string fileName, int width, int height)
    {
        FileName = fileName;
        DimensionsText = width > 0 && height > 0 ? FormatDimensions(width, height) : "Loading...";
    }

    public string FileName { get; }

    [ObservableProperty]
    private string _fileSizeText = "Loading...";

    [ObservableProperty]
    private string _dimensionsText = "Loading...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusVisible))]
    private string _statusText = "Loading file properties...";

    public bool IsStatusVisible => !string.IsNullOrWhiteSpace(StatusText);

    public void Apply(PhotoFileProperties properties)
    {
        FileSizeText = FormatFileSize(properties.SizeBytes);
        DimensionsText = properties.Width > 0 && properties.Height > 0
            ? FormatDimensions(properties.Width, properties.Height) : "Unavailable";
        StatusText = properties.SizeBytes is null || properties.Width <= 0 || properties.Height <= 0
            ? "Some properties are unavailable." : "";
    }

    public void ShowError()
    {
        FileSizeText = "Unavailable";
        if (DimensionsText == "Loading...") DimensionsText = "Unavailable";
        StatusText = "File properties could not be loaded. Check that the photo is available and try again.";
    }

    private static string FormatDimensions(int width, int height) => $"{width} × {height} pixels";

    private static string FormatFileSize(long? bytes)
    {
        if (bytes is null or < 0) return "Unavailable";
        if (bytes < 1024) return $"{bytes:N0} bytes";
        string[] units = ["KiB", "MiB", "GiB", "TiB"];
        var size = (double)bytes.Value / 1024;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]} ({bytes:N0} bytes)";
    }
}
