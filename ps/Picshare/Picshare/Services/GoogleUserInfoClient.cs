using System.Text.Json;
using System.Text.Json.Serialization;

namespace Picshare.Services;

public sealed class GoogleUserInfoClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public GoogleUserInfoClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<GoogleUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Google user info request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var userInfo = await JsonSerializer.DeserializeAsync<GoogleUserInfoResponse>(stream, JsonOptions, cancellationToken);
        if (userInfo is null || string.IsNullOrWhiteSpace(userInfo.Sub))
        {
            throw new InvalidOperationException("Google did not return a user id.");
        }

        return new GoogleUserInfo(userInfo.Sub, userInfo.Email, userInfo.Name);
    }
}

public sealed record GoogleUserInfo(string UserId, string? Email, string? DisplayName);

internal sealed record GoogleUserInfoResponse
{
    [JsonPropertyName("sub")]
    public string Sub { get; init; } = "";

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
