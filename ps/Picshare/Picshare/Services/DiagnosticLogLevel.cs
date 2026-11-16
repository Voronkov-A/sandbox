namespace Picshare.Services;

public enum DiagnosticLogLevel { Debug, Trace }

public static class DiagnosticLogLevels
{
    public static IReadOnlyList<string> Choices { get; } = ["Debug", "Trace"];
    public static string Normalize(string? level) =>
        string.Equals(level, "Trace", StringComparison.OrdinalIgnoreCase) ? "Trace" : "Debug";
}
