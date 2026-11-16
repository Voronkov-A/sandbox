using System.Reflection;
using Picshare.Services;
using Picshare.ViewModels;

namespace Picshare.Tests;

public sealed class AiNetworkIsolationTests
{
    [Fact]
    public void TestProcess_DisablesLiveAiBeforeAnyTestRuns()
    {
        Assert.True(AppContext.TryGetSwitch(AiHttpClientFactory.DisableLiveRequestsSwitch, out var disabled));
        Assert.True(disabled);
    }

    [Fact]
    public async Task ApplicationAiClient_BlocksRequestsWithANonPlaceholderKey()
    {
        // Accessing this static field does not construct a view model or load user settings.
        var client = (HttpClient)typeof(MainViewModel).GetField("AiHttpClient", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        // Use an invalid destination even for this guard regression test; it must never
        // address a real provider if a future change accidentally breaks the guard.
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://must-not-resolve.invalid/v1/responses");
        request.Headers.Authorization = new("Bearer", "dummy-non-placeholder-key");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(request));
        Assert.Contains("Live AI requests are disabled", error.Message);
    }

    [Fact]
    public async Task GuardedClient_BlocksEveryDestination_ForFutureProvidersToo()
    {
        using var client = AiHttpClientFactory.Create();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("https://must-not-resolve.invalid/"));
        Assert.Contains("Live AI requests are disabled", error.Message);
    }

    [Fact]
    public void GuardedClient_AlsoBlocksSynchronousRequests()
    {
        using var client = AiHttpClientFactory.Create();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://must-not-resolve.invalid/");
        var error = Assert.Throws<InvalidOperationException>(() => client.Send(request));
        Assert.Contains("Live AI requests are disabled", error.Message);
    }
}
