namespace Picshare.Models;

public sealed class ReviewerFeedbackDatabase
{
    public int Version { get; set; } = 1;

    public required string AlbumId { get; set; }

    public required string ReviewerUserId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool HasCollectedFeedback { get; set; }

    public bool IsFinalized { get; set; }

    public Dictionary<string, string> PhotoCategories { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> PhotoScores { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> FrozenPhotoIds { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ReviewerFeedbackLocalState
{
    public string? ReviewerStoreId { get; set; }

    public string? ReviewerFolderId { get; set; }

    public string? RemoteFileId { get; set; }

    public DateTimeOffset? RemoteModifiedTime { get; set; }

    public string? RemoteRevision { get; set; }

    public bool LocalDirty { get; set; }

    public string? StatusRemoteFileId { get; set; }

    public DateTimeOffset? StatusRemoteModifiedTime { get; set; }

    public string? StatusRemoteRevision { get; set; }

    public bool StatusLocalDirty { get; set; }

    public string? SharedCategoriesVersion { get; set; }
}

public enum ReviewerFeedbackStatusKind
{
    InProgress,
    Committed,
    Passed,
    Left
}

public sealed class ReviewerFeedbackStatus
{
    public int Version { get; set; } = 1;

    public required string AlbumId { get; set; }

    public required FeedbackReviewerIdentity Reviewer { get; set; }

    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ReviewerFeedbackStatusKind Status { get; set; } = ReviewerFeedbackStatusKind.InProgress;
}

public sealed record ReviewerFeedbackLoadResult(
    ReviewerFeedbackSession Session,
    ReviewerFeedbackDatabase Database,
    ReviewerFeedbackStatus Status,
    bool ConcurrentRemoteUpdate);

public sealed record ReviewerFeedbackSyncResult(
    ReviewerFeedbackDatabase Database,
    ReviewerFeedbackLocalState State,
    ReviewerFeedbackStatus? Status,
    bool RemoteWon,
    bool LocalDirtyBeforeSync);

public sealed record ReviewerFeedbackStatusResult(
    ReviewerFeedbackStatus Status,
    ReviewerFeedbackLocalState State,
    bool RemoteWon);

public sealed class SharedFeedbackDatabase
{
    public int Version { get; set; } = 1;

    public required string AlbumId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool HasCollectedFeedback { get; set; }

    public bool IsFinalized { get; set; }

    public Dictionary<string, string> PhotoCategories { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> PhotoScores { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> FrozenPhotoIds { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SharedFeedbackVersion
{
    public int Version { get; set; } = 1;

    public required string AlbumId { get; set; }

    public required string DatabaseVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ReviewerFeedbackCollectResult(
    int ReviewerCount,
    int PhotoCount,
    int UnfrozenPhotoCount,
    string DatabaseVersion);

public sealed record ReviewerFeedbackFinalizeResult(
    int ReviewerCount,
    int PhotoCount,
    string DatabaseVersion);

public sealed record ReviewerFeedbackFlowItem(
    FeedbackReviewerIdentity Reviewer,
    DateTimeOffset UpdatedAt);

public sealed record ReviewerFeedbackFlowSnapshot(
    IReadOnlyList<ReviewerFeedbackFlowItem> Committed,
    IReadOnlyList<ReviewerFeedbackFlowItem> Passed,
    IReadOnlyList<ReviewerFeedbackFlowItem> Left,
    IReadOnlyList<ReviewerFeedbackFlowItem> InProgress);

public sealed class ReviewerFeedbackSession
{
    public required string AlbumId { get; init; }

    public required string ReviewerUserId { get; init; }

    public required string ReviewerStoreId { get; init; }

    public required string LocalFolderPath { get; init; }

    public required ReviewerFeedbackLocalState State { get; set; }
}
