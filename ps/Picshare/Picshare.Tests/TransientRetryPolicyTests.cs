using System.Net;
using Picshare.Services;

namespace Picshare.Tests;

public sealed class TransientRetryPolicyTests
{
    [Fact]
    public void IsTransient_TreatsHttpNotFoundAsPermanent()
    {
        using var cancellation = new CancellationTokenSource();
        var exception = new HttpRequestException(
            "Not found.",
            inner: null,
            statusCode: HttpStatusCode.NotFound);

        Assert.False(TransientRetryPolicy.IsTransient(exception, cancellation.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void IsTransient_TreatsRetryableHttpStatusesAsTransient(HttpStatusCode statusCode)
    {
        using var cancellation = new CancellationTokenSource();
        var exception = new HttpRequestException(
            "Temporary failure.",
            inner: null,
            statusCode: statusCode);

        Assert.True(TransientRetryPolicy.IsTransient(exception, cancellation.Token));
    }

    [Fact]
    public void IsTransient_TreatsHttpRequestWithoutStatusAsTransient()
    {
        using var cancellation = new CancellationTokenSource();
        var exception = new HttpRequestException("Network failure.");

        Assert.True(TransientRetryPolicy.IsTransient(exception, cancellation.Token));
    }
}
