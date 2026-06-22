using System.Text.Json;

namespace Picshare.Services;

public sealed class AlbumOpenHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _historyFilePath;

    public AlbumOpenHistoryStore(string? localStorageRootPath = null)
    {
        var rootPath = GetLocalStorageRootPath(localStorageRootPath);
        _historyFilePath = Path.Combine(rootPath, "Picshare", "album-open-history.json");
    }

    public AlbumOpenHistory Load()
    {
        if (!File.Exists(_historyFilePath))
        {
            return new AlbumOpenHistory();
        }

        try
        {
            using var stream = File.OpenRead(_historyFilePath);
            return JsonSerializer.Deserialize<AlbumOpenHistory>(stream, JsonOptions) ?? new AlbumOpenHistory();
        }
        catch
        {
            return new AlbumOpenHistory();
        }
    }

    public void Save(AlbumOpenHistory history)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_historyFilePath)!);
        using var stream = File.Create(_historyFilePath);
        JsonSerializer.Serialize(stream, history, JsonOptions);
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

public sealed class AlbumOpenHistory
{
    public string LastOpenAlbumLink { get; set; } = "";

    public List<RecentAlbumSettings> RecentAlbums { get; set; } = new();
}

public sealed class RecentAlbumSettings
{
    public string Title { get; set; } = "";

    public string Link { get; set; } = "";

    public string Location { get; set; } = "";

    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
}
