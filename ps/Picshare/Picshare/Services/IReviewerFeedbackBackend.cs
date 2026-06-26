using Picshare.Models;

namespace Picshare.Services;

public interface IReviewerFeedbackBackend
{
    Task<ReviewerFeedbackStoreRef> EnsureReviewerStoreAsync(string reviewerUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewerFeedbackStoreRef>> ListReviewerStoresAsync(CancellationToken cancellationToken);

    Task<StoredDocument<ReviewerFeedbackDatabase>?> LoadReviewerFeedbackAsync(
        string reviewerStoreId,
        CancellationToken cancellationToken);

    Task<StoredDocument<ReviewerFeedbackDatabase>> SaveReviewerFeedbackAsync(
        string reviewerStoreId,
        ReviewerFeedbackDatabase database,
        CancellationToken cancellationToken);

    Task<StoredDocument<ReviewerFeedbackStatus>?> LoadReviewerStatusAsync(
        string reviewerStoreId,
        CancellationToken cancellationToken);

    Task<StoredDocument<ReviewerFeedbackStatus>> SaveReviewerStatusAsync(
        string reviewerStoreId,
        ReviewerFeedbackStatus status,
        CancellationToken cancellationToken);

    Task<StoredDocument<SharedFeedbackDatabase>?> LoadSharedFeedbackAsync(CancellationToken cancellationToken);

    Task<StoredDocument<SharedFeedbackDatabase>> SaveSharedFeedbackAsync(
        SharedFeedbackDatabase database,
        CancellationToken cancellationToken);

    Task<StoredDocument<SharedFeedbackVersion>?> LoadSharedFeedbackVersionAsync(CancellationToken cancellationToken);

    Task<StoredDocument<SharedFeedbackVersion>> SaveSharedFeedbackVersionAsync(
        SharedFeedbackVersion version,
        CancellationToken cancellationToken);

    Task<StoredDocument<WorkflowHistoryDatabase>?> LoadWorkflowHistoryAsync(CancellationToken cancellationToken);

    Task<StoredDocument<WorkflowHistoryDatabase>> SaveWorkflowHistoryAsync(
        WorkflowHistoryDatabase database,
        CancellationToken cancellationToken);

    Task<StoredDocument<AlbumDeletionMarker>?> LoadAlbumDeletionMarkerAsync(CancellationToken cancellationToken);

    Task<StoredDocument<AlbumDeletionMarker>> SaveAlbumDeletionMarkerAsync(
        AlbumDeletionMarker marker,
        CancellationToken cancellationToken);
}

public sealed record ReviewerFeedbackStoreRef(
    string Id,
    string Name,
    string? Revision);

public sealed record StoredDocument<T>(
    T Value,
    string? Revision);
