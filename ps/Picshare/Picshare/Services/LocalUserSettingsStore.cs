using System.Text.Json;

namespace Picshare.Services;

public sealed class LocalUserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public LocalUserSettingsStore(string? localStorageRootPath = null)
    {
        var rootPath = GetLocalStorageRootPath(localStorageRootPath);
        _settingsFilePath = Path.Combine(rootPath, "Picshare", "user-settings.json");
    }

    public LocalUserSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new LocalUserSettings();
        }

        try
        {
            using var stream = File.OpenRead(_settingsFilePath);
            return JsonSerializer.Deserialize<LocalUserSettings>(stream, JsonOptions) ?? new LocalUserSettings();
        }
        catch
        {
            return new LocalUserSettings();
        }
    }

    public void Save(LocalUserSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        using var stream = File.Create(_settingsFilePath);
        JsonSerializer.Serialize(stream, settings, JsonOptions);
    }

    private static string GetLocalStorageRootPath(string? configuredRootPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredRootPath))
        {
            return configuredRootPath;
        }

        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(basePath) ? AppContext.BaseDirectory : basePath;
    }
}

public sealed class LocalUserSettings
{
    public const int DefaultMaximumParallelism = 8;
    public const int DefaultPicturesPerRow = 4;
    public const int DefaultZoomPower = 10;
    public const string DefaultPhotoViewerAspectRatioMode = "original";

    public string AnonymousReviewerName { get; set; } = "";

    public int MaximumParallelism { get; set; } = DefaultMaximumParallelism;

    public int NumberOfPicturesPerRow { get; set; } = DefaultPicturesPerRow;

    public bool CacheThumbnails { get; set; } = true;

    public bool CacheOriginalImages { get; set; } = true;

    public bool? FixedHeader { get; set; }

    public bool? FixedTabs { get; set; }

    public bool? FixedActionPanel { get; set; }

    public int ZoomPower { get; set; } = DefaultZoomPower;

    public string PhotoViewerAspectRatioMode { get; set; } = DefaultPhotoViewerAspectRatioMode;

    public int AlbumFastThumbnailMemoryCacheSizeMb { get; set; } = 256;

    public int AlbumDetailedThumbnailMemoryCacheSizeMb { get; set; } = 512;

    public int AlbumOriginalImageMemoryCacheSizeMb { get; set; } = 256;

    public int AlbumFastThumbnailBitmapCacheSizeMb { get; set; } = 64;

    public int AlbumDetailedThumbnailBitmapCacheSizeMb { get; set; } = 128;

    public int AlbumOriginalImageBitmapCacheSizeMb { get; set; } = 64;

    public int AlbumFastThumbnailDiskCacheSizeMb { get; set; } = 512;

    public int AlbumDetailedThumbnailDiskCacheSizeMb { get; set; } = 2048;

    public int AlbumOriginalImageDiskCacheSizeMb { get; set; } = 2048;

    public string PictureDefaultDownloadDirectoryPath { get; set; } = "";

    public string UncategorizedDefaultDownloadDirectoryPath { get; set; } = "";

    public string NiceDefaultDownloadDirectoryPath { get; set; } = "";

    public string OkDefaultDownloadDirectoryPath { get; set; } = "";

    public string TrashDefaultDownloadDirectoryPath { get; set; } = "";
}
