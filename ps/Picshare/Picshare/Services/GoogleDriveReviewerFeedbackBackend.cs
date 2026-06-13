using System.Text.Json;
using Picshare.Models;

namespace Picshare.Services;

public sealed class GoogleDriveReviewerFeedbackBackend : IReviewerFeedbackBackend
{
    private const string FeedbackFileName = "feedback.json";
    private const string StatusFileName = "status.json";
    private const string SharedFeedbackFileName = "shared-feedback.json";
    private const string SharedFeedbackVersionFileName = "shared-feedback-version.json";
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly GoogleDriveRestClient _client;
    private readonly string _feedbackFolderId;

    public GoogleDriveReviewerFeedbackBackend(string feedbackFolderId, string accessToken)
    {
        _feedbackFolderId = feedbackFolderId;
        _client = new GoogleDriveRestClient(accessToken);
    }

    public async Task<ReviewerFeedbackStoreRef> EnsureReviewerStoreAsync(
        string reviewerUserId,
        CancellationToken cancellationToken)
    {
        var folder = await _client.FindChildByNameAsync(_feedbackFolderId, reviewerUserId, FolderMimeType, cancellationToken);
        var file = folder is null
            ? await _client.CreateFolderAsync(reviewerUserId, _feedbackFolderId, cancellationToken)
            : new DriveFileInfo
            {
                Id = folder.Id,
                Name = folder.Name,
                ModifiedTime = folder.ModifiedTime
            };

        return ToStoreRef(file);
    }

    public async Task<IReadOnlyList<ReviewerFeedbackStoreRef>> ListReviewerStoresAsync(CancellationToken cancellationToken)
    {
        var folders = new List<ReviewerFeedbackStoreRef>();
        string? pageToken = null;

        do
        {
            var page = await _client.ListChildrenAsync(_feedbackFolderId, pageToken, 100, cancellationToken);
            folders.AddRange(page.Files
                .Where(file => string.Equals(file.MimeType, FolderMimeType, StringComparison.Ordinal))
                .Select(file => new ReviewerFeedbackStoreRef(file.Id, file.Name, CreateRevision(file.ModifiedTime, null))));
            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return folders;
    }

    public async Task<StoredDocument<ReviewerFeedbackDatabase>?> LoadReviewerFeedbackAsync(
        string reviewerStoreId,
        CancellationToken cancellationToken)
    {
        return await LoadJsonAsync<ReviewerFeedbackDatabase>(reviewerStoreId, FeedbackFileName, cancellationToken);
    }

    public async Task<StoredDocument<ReviewerFeedbackDatabase>> SaveReviewerFeedbackAsync(
        string reviewerStoreId,
        ReviewerFeedbackDatabase database,
        CancellationToken cancellationToken)
    {
        return await SaveJsonAsync(reviewerStoreId, FeedbackFileName, database, cancellationToken);
    }

    public async Task<StoredDocument<ReviewerFeedbackStatus>?> LoadReviewerStatusAsync(
        string reviewerStoreId,
        CancellationToken cancellationToken)
    {
        return await LoadJsonAsync<ReviewerFeedbackStatus>(reviewerStoreId, StatusFileName, cancellationToken);
    }

    public async Task<StoredDocument<ReviewerFeedbackStatus>> SaveReviewerStatusAsync(
        string reviewerStoreId,
        ReviewerFeedbackStatus status,
        CancellationToken cancellationToken)
    {
        return await SaveJsonAsync(reviewerStoreId, StatusFileName, status, cancellationToken);
    }

    public async Task<StoredDocument<SharedFeedbackDatabase>?> LoadSharedFeedbackAsync(CancellationToken cancellationToken)
    {
        return await LoadJsonAsync<SharedFeedbackDatabase>(_feedbackFolderId, SharedFeedbackFileName, cancellationToken);
    }

    public async Task<StoredDocument<SharedFeedbackDatabase>> SaveSharedFeedbackAsync(
        SharedFeedbackDatabase database,
        CancellationToken cancellationToken)
    {
        return await SaveJsonAsync(_feedbackFolderId, SharedFeedbackFileName, database, cancellationToken);
    }

    public async Task<StoredDocument<SharedFeedbackVersion>?> LoadSharedFeedbackVersionAsync(CancellationToken cancellationToken)
    {
        return await LoadJsonAsync<SharedFeedbackVersion>(_feedbackFolderId, SharedFeedbackVersionFileName, cancellationToken);
    }

    public async Task<StoredDocument<SharedFeedbackVersion>> SaveSharedFeedbackVersionAsync(
        SharedFeedbackVersion version,
        CancellationToken cancellationToken)
    {
        return await SaveJsonAsync(_feedbackFolderId, SharedFeedbackVersionFileName, version, cancellationToken);
    }

    private async Task<StoredDocument<T>?> LoadJsonAsync<T>(
        string parentFolderId,
        string fileName,
        CancellationToken cancellationToken)
    {
        var file = await _client.FindChildByNameAsync(parentFolderId, fileName, null, cancellationToken);
        if (file is null)
        {
            return null;
        }

        await using var stream = await _client.DownloadFileAsync(file.Id, cancellationToken);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"{fileName} is empty or invalid.");
        return new StoredDocument<T>(value, CreateRevision(file.ModifiedTime, file.Id));
    }

    private async Task<StoredDocument<T>> SaveJsonAsync<T>(
        string parentFolderId,
        string fileName,
        T payload,
        CancellationToken cancellationToken)
    {
        var file = await _client.FindChildByNameAsync(parentFolderId, fileName, null, cancellationToken);
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        DriveFileInfo metadata;
        if (file is null)
        {
            metadata = await _client.UploadFileAsync(fileName, parentFolderId, stream, "application/json", cancellationToken);
        }
        else
        {
            await _client.UpdateFileContentAsync(file.Id, stream, "application/json", cancellationToken);
            metadata = await _client.GetFileMetadataAsync(file.Id, cancellationToken);
        }

        return new StoredDocument<T>(payload, CreateRevision(metadata.ModifiedTime, metadata.Id));
    }

    private static ReviewerFeedbackStoreRef ToStoreRef(DriveFileInfo file)
    {
        return new ReviewerFeedbackStoreRef(file.Id, file.Name, CreateRevision(file.ModifiedTime, file.Id));
    }

    private static string? CreateRevision(DateTimeOffset? modifiedTime, string? id)
    {
        if (modifiedTime is null)
        {
            return id;
        }

        return $"{modifiedTime.Value.UtcTicks}:{id}";
    }
}
