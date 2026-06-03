namespace Picshare.Models;

public sealed class ReviewerFeedbackDatabase
{
    public int Version { get; set; } = 1;

    public required string AlbumId { get; set; }

    public required string ReviewerUserId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Dictionary<string, string> PhotoCategories { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ReviewerFeedbackLocalState
{
    public string? ReviewerFolderId { get; set; }

    public string? RemoteFileId { get; set; }

    public DateTimeOffset? RemoteModifiedTime { get; set; }

    public bool LocalDirty { get; set; }
}

public sealed record ReviewerFeedbackLoadResult(
    ReviewerFeedbackSession Session,
    ReviewerFeedbackDatabase Database,
    bool ConcurrentRemoteUpdate);

public sealed record ReviewerFeedbackSyncResult(
    ReviewerFeedbackDatabase Database,
    ReviewerFeedbackLocalState State,
    bool RemoteWon,
    bool LocalDirtyBeforeSync);

public sealed class ReviewerFeedbackSession
{
    public required string AlbumId { get; init; }

    public required string ReviewerUserId { get; init; }

    public required string FeedbackFolderId { get; init; }

    public required string LocalFolderPath { get; init; }

    public required ReviewerFeedbackLocalState State { get; set; }
}
