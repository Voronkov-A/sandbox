using Avalonia.Skia;

namespace Picshare.Tests;

internal static class TestRenderingPlatform
{
    // Avalonia's service registry is global and initialization writes to a normal dictionary.
    // The CLR runs this initializer once, even when test classes start in parallel.
    static TestRenderingPlatform() => SkiaPlatform.Initialize();
    public static void EnsureInitialized() { }
}
