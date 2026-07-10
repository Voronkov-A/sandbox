using System.Text.Json;
using System.Text.Json.Serialization;

namespace Picshare.Services;

public sealed class PicshareSettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PicshareSettingsProvider()
    {
        SettingsFilePaths = GetSettingsFilePaths();
        Settings = LoadSettings(SettingsFilePaths);
    }

    public IReadOnlyList<string> SettingsFilePaths { get; }

    public PicshareSettings Settings { get; }

    public string? GoogleOAuthClientId => FirstNonWhiteSpace(Settings.Google?.OAuthClientId, Settings.GoogleOAuthClientId);

    public string? GoogleOAuthClientSecret => FirstNonWhiteSpace(Settings.Google?.OAuthClientSecret, Settings.GoogleOAuthClientSecret);

    public string? LocalStorageRootPath => FirstNonWhiteSpace(Settings.LocalStorage?.RootPath, Settings.LocalStorageRootPath);

    public DefaultUserSettings DefaultSettings => Settings.DefaultSettings ?? new DefaultUserSettings();

    public string MissingGoogleOAuthClientIdMessage =>
        $"Google OAuth client id is not configured. Add it to {string.Join(" or ", SettingsFilePaths)}, or package picshare.settings.json with the application.";

    private static IReadOnlyList<string> GetSettingsFilePaths()
    {
        var paths = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "picshare.settings.json")
        };

        AddSpecialFolderPath(paths, Environment.SpecialFolder.ApplicationData);
        AddSpecialFolderPath(paths, Environment.SpecialFolder.LocalApplicationData);

        return paths;
    }

    private static void AddSpecialFolderPath(List<string> paths, Environment.SpecialFolder specialFolder)
    {
        var folder = Environment.GetFolderPath(specialFolder);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            paths.Add(Path.Combine(folder, "Picshare", "settings.json"));
        }
    }

    private static PicshareSettings LoadSettings(IReadOnlyList<string> paths)
    {
        var settings = LoadEmbeddedSettings() ?? new PicshareSettings();

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            using var stream = File.OpenRead(path);
            settings = MergeSettings(settings, JsonSerializer.Deserialize<PicshareSettings>(stream, JsonOptions));
        }

        return settings;
    }

    private static PicshareSettings? LoadEmbeddedSettings()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            var resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("picshare.settings.json", StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                continue;
            }

            return JsonSerializer.Deserialize<PicshareSettings>(stream, JsonOptions);
        }

        return null;
    }

    private static PicshareSettings MergeSettings(PicshareSettings current, PicshareSettings? next)
    {
        var oauthClientId = FirstNonWhiteSpace(
            next?.Google?.OAuthClientId,
            next?.GoogleOAuthClientId,
            current.Google?.OAuthClientId,
            current.GoogleOAuthClientId);
        var oauthClientSecret = FirstNonWhiteSpace(
            next?.Google?.OAuthClientSecret,
            next?.GoogleOAuthClientSecret,
            current.Google?.OAuthClientSecret,
            current.GoogleOAuthClientSecret);
        var localStorageRootPath = FirstNonWhiteSpace(
            next?.LocalStorage?.RootPath,
            next?.LocalStorageRootPath,
            current.LocalStorage?.RootPath,
            current.LocalStorageRootPath);
        var defaultSettings = MergeDefaultSettings(current.DefaultSettings, next?.DefaultSettings);

        return new PicshareSettings
        {
            Google = oauthClientId is null && oauthClientSecret is null
                ? null
                : new GoogleSettings
                {
                    OAuthClientId = oauthClientId,
                    OAuthClientSecret = oauthClientSecret
                },
            LocalStorage = localStorageRootPath is null
                ? null
                : new LocalStorageSettings
                {
                    RootPath = localStorageRootPath
                },
            DefaultSettings = defaultSettings
        };
    }

    private static DefaultUserSettings? MergeDefaultSettings(DefaultUserSettings? current, DefaultUserSettings? next)
    {
        var fixedHeader = next?.FixedHeader ?? current?.FixedHeader;
        var fixedTabs = next?.FixedTabs ?? current?.FixedTabs;
        var fixedActionPanel = next?.FixedActionPanel ?? current?.FixedActionPanel;
        return fixedHeader is null && fixedTabs is null && fixedActionPanel is null
            ? null
            : new DefaultUserSettings
            {
                FixedHeader = fixedHeader,
                FixedTabs = fixedTabs,
                FixedActionPanel = fixedActionPanel
            };
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

public sealed record PicshareSettings
{
    public GoogleSettings? Google { get; init; }

    public LocalStorageSettings? LocalStorage { get; init; }

    public DefaultUserSettings? DefaultSettings { get; init; }

    public string? GoogleOAuthClientId { get; init; }

    public string? GoogleOAuthClientSecret { get; init; }

    [JsonPropertyName("localStorage.rootPath")]
    public string? LocalStorageRootPath { get; init; }
}

public sealed record GoogleSettings
{
    public string? OAuthClientId { get; init; }

    public string? OAuthClientSecret { get; init; }
}

public sealed record LocalStorageSettings
{
    public string? RootPath { get; init; }
}

public sealed record DefaultUserSettings
{
    public bool? FixedHeader { get; init; }

    public bool? FixedTabs { get; init; }

    public bool? FixedActionPanel { get; init; }
}
