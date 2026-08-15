using System.Net;
using Picshare.Services;

namespace Picshare.Tests;

public sealed class GoogleOAuthClientTests
{
    [Fact]
    public async Task RefreshAsync_RetriesTransientConnectionFailure()
    {
        var handler = new FailThenTokenHandler(failuresBeforeSuccess: 2);
        List<TimeSpan> retryDelays = [];
        var client = new GoogleOAuthClient(
            new HttpClient(handler),
            (delay, _) =>
            {
                retryDelays.Add(delay);
                return Task.CompletedTask;
            });

        var token = await client.RefreshAsync("client-id", "client-secret", "refresh-token", CancellationToken.None);

        Assert.Equal("access-token", token.AccessToken);
        Assert.Equal(3, handler.RequestCount);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)], retryDelays);
    }

    [Fact]
    public async Task RefreshAsync_RetriesTransientServerFailure()
    {
        var handler = new ServerFailureThenTokenHandler();
        List<TimeSpan> retryDelays = [];
        var client = new GoogleOAuthClient(
            new HttpClient(handler),
            (delay, _) =>
            {
                retryDelays.Add(delay);
                return Task.CompletedTask;
            });

        var token = await client.RefreshAsync("client-id", "client-secret", "refresh-token", CancellationToken.None);

        Assert.Equal("access-token", token.AccessToken);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal([TimeSpan.FromSeconds(5)], retryDelays);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotRetryPermanentOAuthFailure()
    {
        var handler = new PermanentOAuthFailureHandler();
        var client = new GoogleOAuthClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.RefreshAsync("client-id", "client-secret", "refresh-token", CancellationToken.None));

        Assert.Contains("Bad code", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class FailThenTokenHandler(int failuresBeforeSuccess) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestCount = Interlocked.Increment(ref _requestCount);
            if (requestCount <= failuresBeforeSuccess)
            {
                throw new HttpRequestException("Connection failure");
            }

            return Task.FromResult(CreateTokenResponse());
        }
    }

    private sealed class ServerFailureThenTokenHandler : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestCount = Interlocked.Increment(ref _requestCount);
            return Task.FromResult(requestCount == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("""{"error":"temporarily_unavailable"}""")
                }
                : CreateTokenResponse());
        }
    }

    private sealed class PermanentOAuthFailureHandler : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":"invalid_grant","error_description":"Bad code"}""")
            });
        }
    }

    private static HttpResponseMessage CreateTokenResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "access_token": "access-token",
                  "expires_in": 3600,
                  "scope": "openid email profile https://www.googleapis.com/auth/drive"
                }
                """)
        };
    }
}
