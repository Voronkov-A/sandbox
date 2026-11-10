using System.Net;
using System.Net.Http.Headers;
using Picshare.Services;

namespace Picshare.Tests;

public sealed class GooglePeopleRestClientTests
{
    [Theory]
    [InlineData(10, 10)]
    [InlineData(100, 30)]
    [InlineData(0, 1)]
    public async Task OtherContactsSearch_UsesCorrectEndpointEscapesQueryAndParsesUniqueEmails(int pageSize, int expectedPageSize)
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("https://people.googleapis.com/v1/otherContacts:search", request.RequestUri!.GetLeftPart(UriPartial.Path));
            Assert.Contains("query=alice%2Btest%40example.test%20%26%20name", request.RequestUri.Query);
            Assert.Contains($"pageSize={expectedPageSize}", request.RequestUri.Query);
            Assert.Contains("readMask=names,emailAddresses", request.RequestUri.Query);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "access-token"), request.Headers.Authorization);
            return Json("""
                {"results":[
                  {"person":{"names":[{"displayName":"Alice"}],"emailAddresses":[{"value":" alice@example.test "},{"value":"ALICE@example.test"},{"value":""}]}},
                  {"person":{"names":[{"displayName":"No email"}]}},
                  {"person":{"emailAddresses":[{"value":"another@example.test"}]}},
                  {}
                ]}
                """);
        }));
        var results = await new GooglePeopleRestClient("access-token", http)
            .SearchOtherContactsAsync(" alice+test@example.test & name ", pageSize, CancellationToken.None);
        Assert.Equal(2, results.Count);
        Assert.Equal(new GoogleContactSearchResult("Alice", "alice@example.test"), results[0]);
        Assert.Equal(new GoogleContactSearchResult("", "another@example.test"), results[1]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"results\":null}")]
    [InlineData("{\"results\":[]}")]
    public async Task EmptyOtherContactsResponse_ReturnsEmptyWithoutCallingSavedContacts(string json)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            Assert.EndsWith("/otherContacts:search", request.RequestUri!.AbsolutePath);
            return Json(json);
        }));
        Assert.Empty(await new GooglePeopleRestClient("token", http).SearchOtherContactsAsync("ali", 10, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task BlankQuery_DoesNotMakeRequests()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Unexpected HTTP request")));
        Assert.Empty(await new GooglePeopleRestClient("token", http).SearchOtherContactsAsync(" ", 10, CancellationToken.None));
    }

    [Fact]
    public async Task Warmup_UsesOtherContactsAndWaitsForCacheBeforeSearch()
    {
        var calls = 0;
        var waited = false;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            Assert.EndsWith("/otherContacts:search", request.RequestUri!.AbsolutePath);
            if (calls == 1) Assert.Contains("query=&", request.RequestUri.Query);
            else Assert.True(waited);
            return Json("{}");
        }));
        var client = new GooglePeopleRestClient("token", http, (delay, token) =>
        {
            Assert.Equal(1, calls);
            Assert.Equal(TimeSpan.FromSeconds(5), delay);
            waited = true;
            return Task.CompletedTask;
        });
        await client.WarmupOtherContactSearchAsync(CancellationToken.None);
        await client.SearchOtherContactsAsync("ali", 10, CancellationToken.None);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Warmup_CacheDelayHonorsCancellation()
    {
        using var http = new HttpClient(new Handler(_ => Json("{}")));
        using var cancellation = new CancellationTokenSource();
        var client = new GooglePeopleRestClient("token", http, (delay, token) =>
        {
            cancellation.Cancel();
            return Task.Delay(delay, token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.WarmupOtherContactSearchAsync(cancellation.Token));
    }

    [Fact]
    public async Task MissingPermission_IsReportedAsErrorInsteadOfEmptyResults()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"error":{"status":"PERMISSION_DENIED"}}""")
        }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GooglePeopleRestClient("token", http).SearchOtherContactsAsync("ali", 10, CancellationToken.None));
        Assert.Contains("403", error.Message);
        Assert.Contains("PERMISSION_DENIED", error.Message);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
