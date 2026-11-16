using System.Runtime.CompilerServices;
using Picshare.Services;

namespace Picshare.Tests;

internal static class TestAiNetworkPolicy
{
    // Runs before any tests, including tests that accidentally create the application's real AI client.
    // Never lift this restriction or depend on whether a settings file contains a placeholder key.
    [ModuleInitializer]
    internal static void DisableLiveAiRequests() => AppContext.SetSwitch(AiHttpClientFactory.DisableLiveRequestsSwitch, true);
}
