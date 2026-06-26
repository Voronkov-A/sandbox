using Picshare.Models;

namespace Picshare.ViewModels;

public sealed class WorkflowHistoryEntryViewModel
{
    public WorkflowHistoryEntryViewModel(WorkflowHistoryEntry entry)
    {
        Title = entry.Kind switch
        {
            "round-started" => "Round started",
            "feedback-collected" => "Feedback collected",
            "random-verdict" => "Random verdict",
            "finalization-started" => "Finalization started",
            _ => "Workflow updated"
        };
        Timestamp = entry.CreatedAt.ToLocalTime().ToString("g");
        Description = entry.Kind switch
        {
            "feedback-collected" => $"{entry.FeedbackCount} feedback(s), uncategorized {entry.UncategorizedBefore} -> {entry.UncategorizedAfter}",
            "random-verdict" => $"{entry.FeedbackCount} random nice, uncommitted {entry.UncategorizedBefore} -> {entry.UncategorizedAfter}",
            _ => ""
        };
        HasDescription = !string.IsNullOrWhiteSpace(Description);
    }

    public string Title { get; }

    public string Timestamp { get; }

    public string Description { get; }

    public bool HasDescription { get; }
}
