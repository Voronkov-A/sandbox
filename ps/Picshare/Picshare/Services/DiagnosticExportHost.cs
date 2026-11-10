namespace Picshare.Services;

public interface IDiagnosticShareHost
{
    string ExportDirectory { get; }
    /// <summary>Opens the platform share sheet. A return does not imply delivery to a recipient.</summary>
    Task ShareAsync(string archivePath);
}

public static class DiagnosticExportHost
{
    // Desktop uses Avalonia's save picker; mobile hosts register their native share sheet here.
    public static IDiagnosticShareHost? Current { get; set; }
    public static string ExportDirectory => Current?.ExportDirectory ?? Path.Combine(Path.GetTempPath(), "Picshare", "diagnostic-exports");
}
