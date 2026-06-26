using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Picshare.Services;

public sealed class GooglePeopleRestClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public GooglePeopleRestClient(string accessToken, HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<IReadOnlyList<GoogleContactSearchResult>> SearchContactsAsync(
        string query,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GoogleContactSearchResult>();
        }

        var url =
            "https://people.googleapis.com/v1/people:searchContacts" +
            $"?query={Uri.EscapeDataString(query.Trim())}" +
            $"&pageSize={Math.Clamp(pageSize, 1, 30)}" +
            "&readMask=names,emailAddresses";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var searchResponse = await JsonSerializer.DeserializeAsync<PeopleSearchResponse>(stream, JsonOptions, cancellationToken);
        if (searchResponse?.Results is null)
        {
            return Array.Empty<GoogleContactSearchResult>();
        }

        return searchResponse.Results
            .SelectMany(result =>
            {
                var displayName = result.Person?.Names?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name.DisplayName))?.DisplayName ?? "";
                return result.Person?.EmailAddresses?
                    .Where(email => !string.IsNullOrWhiteSpace(email.Value))
                    .Select(email => new GoogleContactSearchResult(displayName, email.Value!.Trim())) ??
                    Enumerable.Empty<GoogleContactSearchResult>();
            })
            .DistinctBy(result => result.EmailAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task WarmupContactSearchAsync(CancellationToken cancellationToken)
    {
        var url = "https://people.googleapis.com/v1/people:searchContacts?query=&pageSize=1&readMask=names,emailAddresses";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Google People API request failed ({(int)response.StatusCode}): {body}");
    }

    private sealed class PeopleSearchResponse
    {
        [JsonPropertyName("results")]
        public List<PersonSearchResult>? Results { get; set; }
    }

    private sealed class PersonSearchResult
    {
        [JsonPropertyName("person")]
        public Person? Person { get; set; }
    }

    private sealed class Person
    {
        [JsonPropertyName("names")]
        public List<PersonName>? Names { get; set; }

        [JsonPropertyName("emailAddresses")]
        public List<PersonEmailAddress>? EmailAddresses { get; set; }
    }

    private sealed class PersonName
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }

    private sealed class PersonEmailAddress
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }
}

public sealed record GoogleContactSearchResult(string DisplayName, string EmailAddress);
