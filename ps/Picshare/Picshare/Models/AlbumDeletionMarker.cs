namespace Picshare.Models;

public sealed record AlbumDeletionMarker
{
    public required string AlbumId { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }

    public required FeedbackReviewerIdentity RequestedBy { get; init; }
}
