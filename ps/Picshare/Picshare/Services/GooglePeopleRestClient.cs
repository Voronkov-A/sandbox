using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Picshare.Services;

public sealed class GooglePeopleRestClient
{
    public const string OtherContactsReadOnlyScope = "https://www.googleapis.com/auth/contacts.other.readonly";
    private const string OtherContactsSearchEndpoint = "https://people.googleapis.com/v1/otherContacts:search";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly DiagnosticLogService _logs;

    public GooglePeopleRestClient(string accessToken, HttpClient? httpClient = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null, DiagnosticLogService? logs = null)
    {
        _httpClient = httpClient ?? DiagnosticLogging.CreateHttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _delayAsync = delayAsync ?? Task.Delay;
        _logs = logs ?? DiagnosticLogging.Current;
    }

    public async Task<IReadOnlyList<GoogleContactSearchResult>> SearchOtherContactsAsync(
        string query,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GoogleContactSearchResult>();
        }

        var url =
            OtherContactsSearchEndpoint +
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
            _logs.Write("Debug", "Contacts", "Other Contacts search returned no results");
            return Array.Empty<GoogleContactSearchResult>();
        }

        var results = searchResponse.Results
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
        _logs.Write("Debug", "Contacts", $"Other Contacts search parsed; people={searchResponse.Results.Count}; uniqueEmailResults={results.Count}");
        return results;
    }

    public async Task WarmupOtherContactSearchAsync(CancellationToken cancellationToken)
    {
        var url = OtherContactsSearchEndpoint + "?query=&pageSize=1&readMask=names,emailAddresses";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        // Other Contacts uses a lazy cache; Google's guide waits five seconds after warmup.
        // https://developers.google.com/people/v1/other-contacts#search_the_users_other_contacts
        _logs.Write("Debug", "Contacts", "Other Contacts cache warmup accepted; waiting 5 seconds");
        await _delayAsync(TimeSpan.FromSeconds(5), cancellationToken);
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
