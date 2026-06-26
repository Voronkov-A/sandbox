using Picshare.Models;
using System.Text.Json;

namespace Picshare.Services;

public sealed class AlbumDeletionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ReviewerFeedbackService _reviewerFeedbackService;
    private readonly ImageCacheService _imageCacheService;
    private readonly string _pendingDeletionFolderPath;

    public AlbumDeletionService(
        ReviewerFeedbackService reviewerFeedbackService,
        ImageCacheService imageCacheService,
        string? localStorageRootPath = null)
    {
        _reviewerFeedbackService = reviewerFeedbackService;
        _imageCacheService = imageCacheService;
        _pendingDeletionFolderPath = Path.Combine(GetLocalStorageRootPath(localStorageRootPath), "Picshare", "pending-deletions");
    }

    public async Task RequestDeletionAsync(
        AlbumManifest manifest,
        FeedbackReviewerIdentity requestedBy,
        IReviewerFeedbackBackend feedbackBackend,
        Func<CancellationToken, Task<string>> getGoogleAccessTokenAsync,
        CancellationToken cancellationToken,
        IProgress<AlbumDeletionProgress>? progress = null,
        int maximumParallelism = LocalUserSettings.DefaultMaximumParallelism)
    {
        var lastProgress = new AlbumDeletionProgress("Saving deletion request", 0, 4);
        void Report(AlbumDeletionProgress value)
        {
            lastProgress = value;
            progress?.Report(value);
        }

        void ReportRetryWarning(string warning)
        {
            progress?.Report(lastProgress with { Warning = warning });
        }

        Report(lastProgress);
        await SavePendingDeletionAsync(manifest, requestedBy, cancellationToken);

        Report(new AlbumDeletionProgress("Writing deletion marker", 1, 4));
        var existingMarker = await TransientRetryPolicy.ExecuteAsync(
            feedbackBackend.LoadAlbumDeletionMarkerAsync,
            ReportRetryWarning,
            cancellationToken);
        if (existingMarker is null)
        {
            var marker = new AlbumDeletionMarker
            {
                AlbumId = manifest.AlbumId,
                RequestedAt = DateTimeOffset.UtcNow,
                RequestedBy = requestedBy
            };
            await TransientRetryPolicy.ExecuteAsync(
                token => feedbackBackend.SaveAlbumDeletionMarkerAsync(marker, token),
                ReportRetryWarning,
                cancellationToken);
        }

        Report(new AlbumDeletionProgress("Deleting album storage", 2, 4));
        await TransientRetryPolicy.ExecuteAsync(
            token => DeleteRemoteStorageAsync(manifest, getGoogleAccessTokenAsync, maximumParallelism, token),
            ReportRetryWarning,
            cancellationToken);
        Report(new AlbumDeletionProgress("Deleting local state", 3, 4));
        await DeleteLocalStateAsync(manifest.AlbumId);
        DeletePendingDeletion(manifest.AlbumId);
        Report(new AlbumDeletionProgress("Album deleted", 4, 4));
    }

    public async Task<IReadOnlyList<PendingAlbumDeletion>> LoadPendingDeletionsAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_pendingDeletionFolderPath))
        {
            return Array.Empty<PendingAlbumDeletion>();
        }

        var deletions = new List<PendingAlbumDeletion>();
        foreach (var file in Directory.EnumerateFiles(_pendingDeletionFolderPath, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var deletion = await JsonSerializer.DeserializeAsync<PendingAlbumDeletion>(stream, JsonOptions, cancellationToken);
                if (deletion is not null)
                {
                    deletions.Add(deletion);
                }
            }
            catch
            {
            }
        }

        return deletions;
    }

    public async Task DeleteLocalStateAsync(string albumId)
    {
        await _imageCacheService.ClearAlbumAsync(albumId);
        await _reviewerFeedbackService.DeleteLocalAlbumStateAsync(albumId);
    }

    public void ForgetPendingDeletion(string albumId)
    {
        DeletePendingDeletion(albumId);
    }

    private async Task SavePendingDeletionAsync(
        AlbumManifest manifest,
        FeedbackReviewerIdentity requestedBy,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_pendingDeletionFolderPath);
        var path = GetPendingDeletionPath(manifest.AlbumId);
        var payload = new PendingAlbumDeletion
        {
            Manifest = manifest,
            RequestedBy = requestedBy,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken);
    }

    private void DeletePendingDeletion(string albumId)
    {
        var path = GetPendingDeletionPath(albumId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetPendingDeletionPath(string albumId)
    {
        return Path.Combine(_pendingDeletionFolderPath, $"{Sanitize(albumId)}.json");
    }

    private static async Task DeleteRemoteStorageAsync(
        AlbumManifest manifest,
        Func<CancellationToken, Task<string>> getGoogleAccessTokenAsync,
        int maximumParallelism,
        CancellationToken cancellationToken)
    {
        if (manifest.GoogleDrive is not null)
        {
            var accessToken = await getGoogleAccessTokenAsync(cancellationToken);
            var client = new GoogleDriveRestClient(accessToken);
            await client.DeleteFileAsync(manifest.GoogleDrive.AlbumFolderId, cancellationToken);
            return;
        }

        if (manifest.LocalFileSystem is not null)
        {
            var rootPath = manifest.LocalFileSystem.RootPath;
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }

            return;
        }

        throw new InvalidOperationException("The album storage backend is not supported.");
    }

    private static string GetLocalStorageRootPath(string? configuredRootPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredRootPath))
        {
            return configuredRootPath;
        }

        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(basePath) ? AppContext.BaseDirectory : basePath;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }
}

public sealed record PendingAlbumDeletion
{
    public required AlbumManifest Manifest { get; init; }

    public required FeedbackReviewerIdentity RequestedBy { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record AlbumDeletionProgress(string Message, int Value, int Maximum, string Warning = "");
