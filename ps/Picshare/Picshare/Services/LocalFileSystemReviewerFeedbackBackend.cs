using System.Text.Json;
using Picshare.Models;

namespace Picshare.Services;

public sealed class LocalFileSystemReviewerFeedbackBackend : IReviewerFeedbackBackend
{
    private const string FeedbackFileName = "feedback.json";
    private const string StatusFileName = "status.json";
    private const string SharedFeedbackFileName = "shared-feedback.json";
    private const string SharedFeedbackVersionFileName = "shared-feedback-version.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _feedbackFolderPath;

    public LocalFileSystemReviewerFeedbackBackend(string feedbackFolderPath)
    {
        _feedbackFolderPath = feedbackFolderPath;
        Directory.CreateDirectory(_feedbackFolderPath);
    }

    public Task<ReviewerFeedbackStoreRef> EnsureReviewerStoreAsync(
        string reviewerUserId,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_feedbackFolderPath, SanitizePathSegment(reviewerUserId));
        Directory.CreateDirectory(path);
        return Task.FromResult(CreateStoreRef(path));
    }

    public Task<IReadOnlyList<ReviewerFeedbackStoreRef>> ListReviewerStoresAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ReviewerFeedbackStoreRef> stores = Directory.Exists(_feedbackFolderPath)
            ? Directory.EnumerateDirectories(_feedbackFolderPath)
                .Select(CreateStoreRef)
                .OrderBy(store => store.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : Array.Empty<ReviewerFeedbackStoreRef>();

        return Task.FromResult(stores);
    }

    public async Task<StoredDocument<ReviewerFeedbackDatabase>?> LoadReviewerFeedbackAsync(
        string reviewerStoreId,
        CancellationToken cancellationToken)
    {
        return await LoadJsonAsync<ReviewerFeedbackDatabase>(Path.Combine(reviewerStoreId, FeedbackFileName), cancellationToken);
    }

    public async Task<StoredDocument<ReviewerFeedbackDatabase>> SaveReviewerFeedbackAsync(
        string reviewerStoreId,
        ReviewerFeedbackDatabase database,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(reviewerStoreId);
        return await SaveJsonAsync(Path.Combine(reviewerStoreId, FeedbackFileName), database, cancellationToken);
    }

    public async Task<StoredDocument<ReviewerFeedbackStatus>?> LoadReviewerStatusAsync(
        string reviewerStoreId,
        CancellationToken cancellationToken)
    {
        return await LoadJsonAsync<ReviewerFeedbackStatus>(Path.Combine(reviewerStoreId, StatusFileName), cancellationToken);
    }

    public async Task<StoredDocument<ReviewerFeedbackStatus>> SaveReviewerStatusAsync(
        string reviewerStoreId,
        ReviewerFeedbackStatus status,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(reviewerStoreId);
        return await SaveJsonAsync(Path.Combine(reviewerStoreId, StatusFileName), status, cancellationToken);
    }

    public async Task<StoredDocument<SharedFeedbackDatabase>?> LoadSharedFeedbackAsync(CancellationToken cancellationToken)
    {
        return await LoadJsonAsync<SharedFeedbackDatabase>(Path.Combine(_feedbackFolderPath, SharedFeedbackFileName), cancellationToken);
    }

    public async Task<StoredDocument<SharedFeedbackDatabase>> SaveSharedFeedbackAsync(
        SharedFeedbackDatabase database,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_feedbackFolderPath);
        return await SaveJsonAsync(Path.Combine(_feedbackFolderPath, SharedFeedbackFileName), database, cancellationToken);
    }

    public async Task<StoredDocument<SharedFeedbackVersion>?> LoadSharedFeedbackVersionAsync(CancellationToken cancellationToken)
    {
        return await LoadJsonAsync<SharedFeedbackVersion>(Path.Combine(_feedbackFolderPath, SharedFeedbackVersionFileName), cancellationToken);
    }

    public async Task<StoredDocument<SharedFeedbackVersion>> SaveSharedFeedbackVersionAsync(
        SharedFeedbackVersion version,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_feedbackFolderPath);
        return await SaveJsonAsync(Path.Combine(_feedbackFolderPath, SharedFeedbackVersionFileName), version, cancellationToken);
    }

    private static async Task<StoredDocument<T>?> LoadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"{Path.GetFileName(path)} is empty or invalid.");
        return new StoredDocument<T>(value, CreateFileRevision(path));
    }

    private static async Task<StoredDocument<T>> SaveJsonAsync<T>(
        string path,
        T payload,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? "", $".{Guid.NewGuid():N}.tmp");
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
        return new StoredDocument<T>(payload, CreateFileRevision(path));
    }

    private static ReviewerFeedbackStoreRef CreateStoreRef(string path)
    {
        return new ReviewerFeedbackStoreRef(path, Path.GetFileName(path), CreateDirectoryRevision(path));
    }

    private static string? CreateFileRevision(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        return $"{info.LastWriteTimeUtc.Ticks}:{info.Length}";
    }

    private static string? CreateDirectoryRevision(string path)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }

        var info = new DirectoryInfo(path);
        return info.LastWriteTimeUtc.Ticks.ToString();
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }
}
