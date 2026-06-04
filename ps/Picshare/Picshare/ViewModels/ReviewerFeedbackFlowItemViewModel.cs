using Picshare.Models;

namespace Picshare.ViewModels;

public sealed class ReviewerFeedbackFlowItemViewModel
{
    public ReviewerFeedbackFlowItemViewModel(ReviewerFeedbackFlowItem item)
    {
        Name = item.Reviewer.DisplayLabel;
        Description = item.CommittedAt.ToLocalTime().ToString("g");
    }

    public string Name { get; }

    public string Description { get; }
}
