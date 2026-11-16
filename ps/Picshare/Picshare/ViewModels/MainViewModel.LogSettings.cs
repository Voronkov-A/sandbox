using CommunityToolkit.Mvvm.ComponentModel;
using Picshare.Services;

namespace Picshare.ViewModels;

public partial class MainViewModel
{
    public IReadOnlyList<string> DiagnosticLogLevelOptions => DiagnosticLogLevels.Choices;

    [ObservableProperty]
    private string _diagnosticLogLevel = "Debug";

    partial void OnDiagnosticLogLevelChanged(string value)
    {
        var normalized = DiagnosticLogLevels.Normalize(value);
        if (value != normalized) { DiagnosticLogLevel = normalized; return; }
        DiagnosticLogging.SetLevel(normalized);
        PersistLocalUserSettingsIfReady();
    }
}
