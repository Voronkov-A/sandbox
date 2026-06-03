using System.Text.Json;

namespace Picshare.Services;

public sealed class GoogleOAuthTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public GoogleOAuthTokenSet? Load()
    {
        var path = GetTokenFilePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var token = JsonSerializer.Deserialize<PersistedGoogleOAuthToken>(stream, JsonOptions);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return null;
            }

            return new GoogleOAuthTokenSet(
                token.AccessToken,
                token.RefreshToken,
                token.ExpiresAt,
                token.Scope ?? "",
                token.UserId,
                token.Email,
                token.DisplayName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save(GoogleOAuthTokenSet token)
    {
        var path = GetTokenFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var persistedToken = new PersistedGoogleOAuthToken
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            ExpiresAt = token.ExpiresAt,
            Scope = token.Scope,
            UserId = token.UserId,
            Email = token.Email,
            DisplayName = token.DisplayName
        };

        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, persistedToken, JsonOptions);
    }

    public void Clear()
    {
        var path = GetTokenFilePath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string GetTokenFilePath()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = AppContext.BaseDirectory;
        }

        return Path.Combine(folder, "Picshare", "google-oauth-token.json");
    }
}

internal sealed record PersistedGoogleOAuthToken
{
    public string AccessToken { get; init; } = "";

    public string? RefreshToken { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public string? Scope { get; init; }

    public string? UserId { get; init; }

    public string? Email { get; init; }

    public string? DisplayName { get; init; }
}
