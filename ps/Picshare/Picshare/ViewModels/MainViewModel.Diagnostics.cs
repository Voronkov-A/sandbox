using System.ComponentModel;
using Picshare.Services;

namespace Picshare.ViewModels;

public partial class MainViewModel
{
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Record workflow outcomes without logging every observable UI value (or account details).
        var message = e.PropertyName switch
        {
            nameof(Status) => Status,
            nameof(AlbumWorkflowStatus) => AlbumWorkflowStatus,
            nameof(PhotoViewerStatus) => PhotoViewerStatus,
            nameof(FlowStatus) => FlowStatus,
            nameof(CommitFeedbackStatus) => CommitFeedbackStatus,
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(message))
            DiagnosticLogging.Current.Write("Information", "Workflow", $"{e.PropertyName}: {message}");
    }
}
