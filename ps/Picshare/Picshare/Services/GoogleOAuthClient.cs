using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Picshare.Services;

public sealed class GoogleOAuthClient
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] Scopes =
    {
        "https://www.googleapis.com/auth/drive"
    };

    private readonly HttpClient _httpClient;

    public GoogleOAuthClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<GoogleOAuthTokenSet> SignInWithLoopbackAsync(
        string clientId,
        string? clientSecret,
        Func<GoogleOAuthBrowserLaunch, Task> launchBrowserAsync,
        CancellationToken cancellationToken)
    {
        using var listener = CreateLoopbackListener();
        var redirectUri = listener.Prefixes.Single().TrimEnd('/');
        var state = CreateUrlSafeRandomString(32);
        var codeVerifier = CreateUrlSafeRandomString(64);
        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var authorizationUrl = BuildAuthorizationUrl(clientId, redirectUri, state, codeChallenge);

        listener.Start();
        await launchBrowserAsync(new GoogleOAuthBrowserLaunch(authorizationUrl, redirectUri));

        var context = await WaitForCallbackAsync(listener, cancellationToken);
        var query = ParseQuery(context.Request.Url?.Query ?? "");

        if (query.TryGetValue("error", out var error))
        {
            var description = query.TryGetValue("error_description", out var errorDescription)
                ? errorDescription
                : error;
            await WriteBrowserResponseAsync(context.Response, false, description, cancellationToken);
            listener.Stop();
            throw new InvalidOperationException($"Google sign-in failed: {description}");
        }

        if (!query.TryGetValue("state", out var returnedState) || !string.Equals(returnedState, state, StringComparison.Ordinal))
        {
            await WriteBrowserResponseAsync(context.Response, false, "The authorization response state did not match.", cancellationToken);
            listener.Stop();
            throw new InvalidOperationException("Google sign-in failed because the authorization response state did not match.");
        }

        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            await WriteBrowserResponseAsync(context.Response, false, "Google did not return an authorization code.", cancellationToken);
            listener.Stop();
            throw new InvalidOperationException("Google did not return an authorization code.");
        }

        try
        {
            var tokenSet = await ExchangeCodeAsync(clientId, clientSecret, code, codeVerifier, redirectUri, cancellationToken);
            await WriteBrowserResponseAsync(context.Response, true, null, cancellationToken);
            listener.Stop();
            return tokenSet;
        }
        catch (Exception ex)
        {
            await WriteBrowserResponseAsync(context.Response, false, ex.Message, cancellationToken);
            listener.Stop();
            throw;
        }
    }

    public async Task<GoogleOAuthTokenSet> RefreshAsync(
        string clientId,
        string? clientSecret,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };
        AddClientSecret(form, clientSecret);

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(TokenEndpoint, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadTokenSetAsync(response, cancellationToken, refreshToken);
    }

    private async Task<GoogleOAuthTokenSet> ExchangeCodeAsync(
        string clientId,
        string? clientSecret,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        };
        AddClientSecret(form, clientSecret);

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(TokenEndpoint, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadTokenSetAsync(response, cancellationToken);
    }

    private static HttpListener CreateLoopbackListener()
    {
        for (var attempts = 0; attempts < 20; attempts++)
        {
            var port = RandomNumberGenerator.GetInt32(49152, 65535);
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                listener.Start();
                listener.Stop();
                return listener;
            }
            catch (HttpListenerException)
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException("Could not reserve a local redirect port for Google sign-in.");
    }

    private static async Task<HttpListenerContext> WaitForCallbackAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        var callbackTask = listener.GetContextAsync();
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var completedTask = await Task.WhenAny(callbackTask, cancellationTask);

        if (completedTask == cancellationTask)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return await callbackTask;
    }

    private static Uri BuildAuthorizationUrl(
        string clientId,
        string redirectUri,
        string state,
        string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', Scopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["include_granted_scopes"] = "true"
        };

        return new Uri($"{AuthorizationEndpoint}?{BuildQuery(query)}");
    }

    private static async Task WriteBrowserResponseAsync(
        HttpListenerResponse response,
        bool isSuccess,
        string? detail,
        CancellationToken cancellationToken)
    {
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";

        var title = isSuccess ? "Picshare sign-in complete" : "Picshare sign-in failed";
        var message = isSuccess
            ? "Google Drive is connected. You can close this tab and return to Picshare."
            : "Google sign-in did not complete. Return to Picshare for details.";
        var detailHtml = string.IsNullOrWhiteSpace(detail)
            ? ""
            : $"<pre style=\"white-space: pre-wrap; background: #f3f3f3; padding: 12px; border-radius: 6px;\">{WebUtility.HtmlEncode(detail)}</pre>";
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{WebUtility.HtmlEncode(title)}}</title>
            </head>
            <body style="font-family: system-ui, sans-serif; margin: 40px; line-height: 1.4;">
              <h1>{{WebUtility.HtmlEncode(title)}}</h1>
              <p>{{WebUtility.HtmlEncode(message)}}</p>
              {{detailHtml}}
            </body>
            </html>
            """;

        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    private static async Task<GoogleOAuthTokenSet> ReadTokenSetAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        string? existingRefreshToken = null)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var token = await JsonSerializer.DeserializeAsync<GoogleOAuthTokenResponse>(stream, JsonOptions, cancellationToken);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("Google did not return an access token.");
        }

        return new GoogleOAuthTokenSet(
            token.AccessToken,
            token.RefreshToken ?? existingRefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn),
            token.Scope ?? "");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        GoogleOAuthError? error = null;
        try
        {
            error = JsonSerializer.Deserialize<GoogleOAuthError>(body, JsonOptions);
        }
        catch (JsonException)
        {
        }

        var message = error?.ErrorDescription ?? error?.Error ?? body;
        if (string.Equals(error?.Error, "invalid_client", StringComparison.Ordinal))
        {
            message += " If this is a Google Desktop OAuth client, add its client secret to google.oauthClientSecret in Picshare settings.";
        }

        throw new InvalidOperationException($"Google OAuth request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {message}");
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
                parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')),
                StringComparer.Ordinal);
    }

    private static string BuildQuery(Dictionary<string, string> values)
    {
        return string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static string CreateUrlSafeRandomString(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void AddClientSecret(Dictionary<string, string> form, string? clientSecret)
    {
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            form["client_secret"] = clientSecret.Trim();
        }
    }
}

public sealed record GoogleOAuthBrowserLaunch(Uri AuthorizationUrl, string RedirectUri);

public sealed record GoogleOAuthTokenSet(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string Scope);

internal sealed record GoogleOAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}

internal sealed record GoogleOAuthError
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}
