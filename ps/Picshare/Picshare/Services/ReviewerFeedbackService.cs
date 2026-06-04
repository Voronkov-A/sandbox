using System.Text.Json;
using Picshare.Models;

namespace Picshare.Services;

public sealed class ReviewerFeedbackService
{
    private const string FeedbackFileName = "feedback.json";
    private const string CommitFileName = "commit.json";
    private const string LocalFeedbackFileName = "feedback.json";
    private const string LocalCommitFileName = "commit.json";
    private const string LocalStateFileName = "sync-state.json";
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<ReviewerFeedbackLoadResult> LoadAsync(
        AlbumManifest manifest,
        GoogleOAuthTokenSet token,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var reviewerUserId = token.UserId ?? throw new InvalidOperationException("Google reviewer id is missing.");
        var reviewerIdentity = CreateGoogleReviewerIdentity(token);
        var localFolderPath = GetLocalFolderPath(manifest.AlbumId, reviewerUserId);
        Directory.CreateDirectory(localFolderPath);

        var localDatabase = await LoadLocalDatabaseAsync(localFolderPath, cancellationToken);
        var localState = await LoadLocalStateAsync(localFolderPath, cancellationToken);
        var client = new GoogleDriveRestClient(accessToken);
        var reviewerFolder = await EnsureReviewerFolderAsync(client, manifest.GoogleDrive.FeedbackFolderId, reviewerUserId, cancellationToken);
        localState.ReviewerFolderId = reviewerFolder.Id;

        var remoteFile = await client.FindChildByNameAsync(reviewerFolder.Id, FeedbackFileName, null, cancellationToken);
        ReviewerFeedbackDatabase database;
        var concurrentRemoteUpdate = false;

        if (remoteFile is null)
        {
            database = localDatabase ?? CreateEmptyDatabase(manifest.AlbumId, reviewerUserId);
            localState = await UploadDatabaseAsync(client, reviewerFolder.Id, localState, database, cancellationToken);
        }
        else
        {
            var remoteChanged = localState.RemoteModifiedTime is not null &&
                remoteFile.ModifiedTime is not null &&
                remoteFile.ModifiedTime != localState.RemoteModifiedTime;

            if (localDatabase is null || !localState.LocalDirty || remoteChanged)
            {
                concurrentRemoteUpdate = localState.LocalDirty && remoteChanged;
                database = await DownloadDatabaseAsync(client, remoteFile.Id, cancellationToken);
                localState.RemoteFileId = remoteFile.Id;
                localState.RemoteModifiedTime = remoteFile.ModifiedTime;
                localState.LocalDirty = false;
            }
            else
            {
                database = localDatabase;
                localState.RemoteFileId = remoteFile.Id;
                localState.RemoteModifiedTime = remoteFile.ModifiedTime;
                localState = await UploadDatabaseAsync(client, reviewerFolder.Id, localState, database, cancellationToken);
            }
        }

        await SaveLocalDatabaseAsync(localFolderPath, database, cancellationToken);

        var commitLoad = await LoadCommitAsync(
            client,
            reviewerFolder.Id,
            localFolderPath,
            localState,
            manifest.AlbumId,
            reviewerIdentity,
            cancellationToken);
        localState = commitLoad.State;

        await SaveLocalStateAsync(localFolderPath, localState, cancellationToken);

        return new ReviewerFeedbackLoadResult(
            new ReviewerFeedbackSession
            {
                AlbumId = manifest.AlbumId,
                ReviewerUserId = reviewerUserId,
                FeedbackFolderId = manifest.GoogleDrive.FeedbackFolderId,
                LocalFolderPath = localFolderPath,
                State = localState
            },
            database,
            commitLoad.Commit,
            concurrentRemoteUpdate || commitLoad.ConcurrentRemoteUpdate);
    }

    public async Task SaveLocalDecisionAsync(
        ReviewerFeedbackSession session,
        ReviewerFeedbackDatabase database,
        string photoId,
        string category,
        CancellationToken cancellationToken)
    {
        database.PhotoCategories[photoId] = category;
        database.UpdatedAt = DateTimeOffset.UtcNow;
        var state = session.State;
        state.LocalDirty = true;

        await SaveLocalDatabaseAsync(session.LocalFolderPath, database, cancellationToken);
        await SaveLocalStateAsync(session.LocalFolderPath, state, cancellationToken);
        session.State = state;
    }

    public async Task RemoveLocalDecisionAsync(
        ReviewerFeedbackSession session,
        ReviewerFeedbackDatabase database,
        string photoId,
        CancellationToken cancellationToken)
    {
        database.PhotoCategories.Remove(photoId);
        database.UpdatedAt = DateTimeOffset.UtcNow;
        var state = session.State;
        state.LocalDirty = true;

        await SaveLocalDatabaseAsync(session.LocalFolderPath, database, cancellationToken);
        await SaveLocalStateAsync(session.LocalFolderPath, state, cancellationToken);
        session.State = state;
    }

    public async Task<ReviewerFeedbackSyncResult> SyncAsync(
        ReviewerFeedbackSession session,
        ReviewerFeedbackDatabase localDatabase,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = new GoogleDriveRestClient(accessToken);
        var state = await LoadLocalStateAsync(session.LocalFolderPath, cancellationToken);
        var localDirtyBeforeSync = state.LocalDirty;
        var remoteFile = !string.IsNullOrWhiteSpace(state.RemoteFileId)
            ? await client.GetFileMetadataAsync(state.RemoteFileId, cancellationToken)
            : null;

        if (remoteFile is null)
        {
            var foundFile = await client.FindChildByNameAsync(session.State.ReviewerFolderId!, FeedbackFileName, null, cancellationToken);
            if (foundFile is not null)
            {
                remoteFile = new DriveFileInfo
                {
                    Id = foundFile.Id,
                    Name = foundFile.Name,
                    ModifiedTime = foundFile.ModifiedTime
                };
            }
        }

        if (remoteFile is not null &&
            state.RemoteModifiedTime is not null &&
            remoteFile.ModifiedTime is not null &&
            remoteFile.ModifiedTime != state.RemoteModifiedTime)
        {
            var remoteDatabase = await DownloadDatabaseAsync(client, remoteFile.Id, cancellationToken);
            state.RemoteFileId = remoteFile.Id;
            state.RemoteModifiedTime = remoteFile.ModifiedTime;
            state.LocalDirty = false;

            await SaveLocalDatabaseAsync(session.LocalFolderPath, remoteDatabase, cancellationToken);
            await SaveLocalStateAsync(session.LocalFolderPath, state, cancellationToken);
            var remoteWonLocalCommit = await LoadLocalCommitAsync(session.LocalFolderPath, cancellationToken);
            var remoteWonCommitSync = await SyncCommitAsync(client, session.State.ReviewerFolderId!, session.LocalFolderPath, state, remoteWonLocalCommit, cancellationToken);
            session.State = remoteWonCommitSync.State;
            return new ReviewerFeedbackSyncResult(
                remoteDatabase,
                remoteWonCommitSync.State,
                remoteWonCommitSync.Commit,
                RemoteWon: true,
                LocalDirtyBeforeSync: localDirtyBeforeSync || remoteWonCommitSync.LocalDirtyBeforeSync);
        }

        if (state.LocalDirty)
        {
            state = await UploadDatabaseAsync(client, session.State.ReviewerFolderId!, state, localDatabase, cancellationToken);
            await SaveLocalStateAsync(session.LocalFolderPath, state, cancellationToken);
        }

        var localCommit = await LoadLocalCommitAsync(session.LocalFolderPath, cancellationToken);
        var commitSync = await SyncCommitAsync(client, session.State.ReviewerFolderId!, session.LocalFolderPath, state, localCommit, cancellationToken);
        session.State = commitSync.State;

        return new ReviewerFeedbackSyncResult(
            localDatabase,
            commitSync.State,
            commitSync.Commit,
            RemoteWon: commitSync.RemoteWon,
            LocalDirtyBeforeSync: localDirtyBeforeSync || commitSync.LocalDirtyBeforeSync);
    }

    public async Task<ReviewerFeedbackCommitResult> CommitAsync(
        ReviewerFeedbackSession session,
        FeedbackReviewerIdentity reviewer,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = new GoogleDriveRestClient(accessToken);
        var state = await LoadLocalStateAsync(session.LocalFolderPath, cancellationToken);
        var localCommit = await LoadLocalCommitAsync(session.LocalFolderPath, cancellationToken) ??
            new ReviewerFeedbackCommit
            {
                AlbumId = session.AlbumId,
                Reviewer = reviewer,
                CommittedAt = DateTimeOffset.UtcNow
            };

        var commitSync = await SyncCommitAsync(client, session.State.ReviewerFolderId!, session.LocalFolderPath, state, localCommit, cancellationToken);
        if (commitSync.RemoteWon && commitSync.Commit is not null)
        {
            session.State = commitSync.State;
            return new ReviewerFeedbackCommitResult(commitSync.Commit, commitSync.State, RemoteWon: true);
        }

        state = commitSync.State;
        state.CommitLocalDirty = true;
        await SaveLocalCommitAsync(session.LocalFolderPath, localCommit, cancellationToken);
        state = await UploadCommitAsync(client, session.State.ReviewerFolderId!, state, localCommit, cancellationToken);
        await SaveLocalStateAsync(session.LocalFolderPath, state, cancellationToken);
        session.State = state;

        return new ReviewerFeedbackCommitResult(localCommit, state, RemoteWon: false);
    }

    public async Task<IReadOnlyList<ReviewerFeedbackFlowItem>> LoadCommittedReviewersAsync(
        string feedbackFolderId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = new GoogleDriveRestClient(accessToken);
        var folders = new List<DriveItemInfo>();
        string? pageToken = null;

        do
        {
            var page = await client.ListChildrenAsync(feedbackFolderId, pageToken, 100, cancellationToken);
            folders.AddRange(page.Files.Where(file => string.Equals(file.MimeType, FolderMimeType, StringComparison.Ordinal)));
            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        var reviewers = new List<ReviewerFeedbackFlowItem>();
        foreach (var folder in folders)
        {
            var commitFile = await client.FindChildByNameAsync(folder.Id, CommitFileName, null, cancellationToken);
            if (commitFile is null)
            {
                continue;
            }

            var commit = await DownloadCommitAsync(client, commitFile.Id, cancellationToken);
            reviewers.Add(new ReviewerFeedbackFlowItem(commit.Reviewer, commit.CommittedAt));
        }

        return reviewers
            .OrderBy(item => item.CommittedAt)
            .ThenBy(item => item.Reviewer.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ReviewerFeedbackDatabase CreateEmptyDatabase(string albumId, string reviewerUserId)
    {
        return new ReviewerFeedbackDatabase
        {
            AlbumId = albumId,
            ReviewerUserId = reviewerUserId
        };
    }

    private static async Task<DriveFileInfo> EnsureReviewerFolderAsync(
        GoogleDriveRestClient client,
        string feedbackFolderId,
        string reviewerUserId,
        CancellationToken cancellationToken)
    {
        var folder = await client.FindChildByNameAsync(feedbackFolderId, reviewerUserId, FolderMimeType, cancellationToken);
        return folder is null
            ? await client.CreateFolderAsync(reviewerUserId, feedbackFolderId, cancellationToken)
            : new DriveFileInfo
            {
                Id = folder.Id,
                Name = folder.Name,
                ModifiedTime = folder.ModifiedTime
            };
    }

    private static async Task<ReviewerFeedbackDatabase> DownloadDatabaseAsync(
        GoogleDriveRestClient client,
        string fileId,
        CancellationToken cancellationToken)
    {
        await using var stream = await client.DownloadFileAsync(fileId, cancellationToken);
        return await JsonSerializer.DeserializeAsync<ReviewerFeedbackDatabase>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Remote feedback database is empty or invalid.");
    }

    private static async Task<(ReviewerFeedbackCommit? Commit, ReviewerFeedbackLocalState State, bool ConcurrentRemoteUpdate)> LoadCommitAsync(
        GoogleDriveRestClient client,
        string reviewerFolderId,
        string localFolderPath,
        ReviewerFeedbackLocalState localState,
        string albumId,
        FeedbackReviewerIdentity reviewer,
        CancellationToken cancellationToken)
    {
        var localCommit = await LoadLocalCommitAsync(localFolderPath, cancellationToken);
        var remoteFile = await client.FindChildByNameAsync(reviewerFolderId, CommitFileName, null, cancellationToken);

        if (remoteFile is null)
        {
            if (localCommit is not null && localState.CommitLocalDirty)
            {
                localState = await UploadCommitAsync(client, reviewerFolderId, localState, localCommit, cancellationToken);
            }

            return (localCommit, localState, ConcurrentRemoteUpdate: false);
        }

        var remoteChanged = localState.CommitRemoteModifiedTime is null ||
            remoteFile.ModifiedTime is not null &&
            remoteFile.ModifiedTime != localState.CommitRemoteModifiedTime;

        if (localCommit is null || !localState.CommitLocalDirty || remoteChanged)
        {
            var concurrentRemoteUpdate = localState.CommitLocalDirty && remoteChanged;
            var remoteCommit = await DownloadCommitAsync(client, remoteFile.Id, cancellationToken);
            localState.CommitRemoteFileId = remoteFile.Id;
            localState.CommitRemoteModifiedTime = remoteFile.ModifiedTime;
            localState.CommitLocalDirty = false;
            await SaveLocalCommitAsync(localFolderPath, remoteCommit, cancellationToken);
            return (remoteCommit, localState, concurrentRemoteUpdate);
        }

        localState = await UploadCommitAsync(client, reviewerFolderId, localState, localCommit, cancellationToken);
        return (localCommit, localState, ConcurrentRemoteUpdate: false);
    }

    private static async Task<(ReviewerFeedbackCommit? Commit, ReviewerFeedbackLocalState State, bool RemoteWon, bool LocalDirtyBeforeSync)> SyncCommitAsync(
        GoogleDriveRestClient client,
        string reviewerFolderId,
        string localFolderPath,
        ReviewerFeedbackLocalState state,
        ReviewerFeedbackCommit? localCommit,
        CancellationToken cancellationToken)
    {
        var localDirtyBeforeSync = state.CommitLocalDirty;
        var remoteFile = !string.IsNullOrWhiteSpace(state.CommitRemoteFileId)
            ? await client.GetFileMetadataAsync(state.CommitRemoteFileId, cancellationToken)
            : null;

        if (remoteFile is null)
        {
            var foundFile = await client.FindChildByNameAsync(reviewerFolderId, CommitFileName, null, cancellationToken);
            if (foundFile is not null)
            {
                remoteFile = new DriveFileInfo
                {
                    Id = foundFile.Id,
                    Name = foundFile.Name,
                    ModifiedTime = foundFile.ModifiedTime
                };
            }
        }

        if (remoteFile is not null &&
            (state.CommitRemoteModifiedTime is null ||
             remoteFile.ModifiedTime is not null &&
             remoteFile.ModifiedTime != state.CommitRemoteModifiedTime))
        {
            var remoteCommit = await DownloadCommitAsync(client, remoteFile.Id, cancellationToken);
            state.CommitRemoteFileId = remoteFile.Id;
            state.CommitRemoteModifiedTime = remoteFile.ModifiedTime;
            state.CommitLocalDirty = false;
            await SaveLocalCommitAsync(localFolderPath, remoteCommit, cancellationToken);
            await SaveLocalStateAsync(localFolderPath, state, cancellationToken);
            return (remoteCommit, state, RemoteWon: true, localDirtyBeforeSync);
        }

        if (localCommit is not null && state.CommitLocalDirty)
        {
            state = await UploadCommitAsync(client, reviewerFolderId, state, localCommit, cancellationToken);
            await SaveLocalStateAsync(localFolderPath, state, cancellationToken);
        }

        return (localCommit, state, RemoteWon: false, localDirtyBeforeSync);
    }

    private static async Task<ReviewerFeedbackCommit> DownloadCommitAsync(
        GoogleDriveRestClient client,
        string fileId,
        CancellationToken cancellationToken)
    {
        await using var stream = await client.DownloadFileAsync(fileId, cancellationToken);
        return await JsonSerializer.DeserializeAsync<ReviewerFeedbackCommit>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Remote feedback commit is empty or invalid.");
    }

    private static async Task<ReviewerFeedbackLocalState> UploadDatabaseAsync(
        GoogleDriveRestClient client,
        string reviewerFolderId,
        ReviewerFeedbackLocalState state,
        ReviewerFeedbackDatabase database,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(database, JsonOptions));

        DriveFileInfo metadata;
        if (string.IsNullOrWhiteSpace(state.RemoteFileId))
        {
            metadata = await client.UploadFileAsync(FeedbackFileName, reviewerFolderId, stream, "application/json", cancellationToken);
        }
        else
        {
            await client.UpdateFileContentAsync(state.RemoteFileId, stream, "application/json", cancellationToken);
            metadata = await client.GetFileMetadataAsync(state.RemoteFileId, cancellationToken);
        }

        state.RemoteFileId = metadata.Id;
        state.RemoteModifiedTime = metadata.ModifiedTime;
        state.LocalDirty = false;
        return state;
    }

    private static async Task<ReviewerFeedbackLocalState> UploadCommitAsync(
        GoogleDriveRestClient client,
        string reviewerFolderId,
        ReviewerFeedbackLocalState state,
        ReviewerFeedbackCommit commit,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(commit, JsonOptions));

        DriveFileInfo metadata;
        if (string.IsNullOrWhiteSpace(state.CommitRemoteFileId))
        {
            metadata = await client.UploadFileAsync(CommitFileName, reviewerFolderId, stream, "application/json", cancellationToken);
        }
        else
        {
            await client.UpdateFileContentAsync(state.CommitRemoteFileId, stream, "application/json", cancellationToken);
            metadata = await client.GetFileMetadataAsync(state.CommitRemoteFileId, cancellationToken);
        }

        state.CommitRemoteFileId = metadata.Id;
        state.CommitRemoteModifiedTime = metadata.ModifiedTime;
        state.CommitLocalDirty = false;
        return state;
    }

    private static async Task<ReviewerFeedbackDatabase?> LoadLocalDatabaseAsync(string localFolderPath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(localFolderPath, LocalFeedbackFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ReviewerFeedbackDatabase>(stream, JsonOptions, cancellationToken);
    }

    private static async Task SaveLocalDatabaseAsync(
        string localFolderPath,
        ReviewerFeedbackDatabase database,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(localFolderPath);
        var path = Path.Combine(localFolderPath, LocalFeedbackFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, database, JsonOptions, cancellationToken);
    }

    private static async Task<ReviewerFeedbackCommit?> LoadLocalCommitAsync(string localFolderPath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(localFolderPath, LocalCommitFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ReviewerFeedbackCommit>(stream, JsonOptions, cancellationToken);
    }

    private static async Task SaveLocalCommitAsync(
        string localFolderPath,
        ReviewerFeedbackCommit commit,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(localFolderPath);
        var path = Path.Combine(localFolderPath, LocalCommitFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, commit, JsonOptions, cancellationToken);
    }

    private static async Task<ReviewerFeedbackLocalState> LoadLocalStateAsync(string localFolderPath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(localFolderPath, LocalStateFileName);
        if (!File.Exists(path))
        {
            return new ReviewerFeedbackLocalState();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ReviewerFeedbackLocalState>(stream, JsonOptions, cancellationToken)
            ?? new ReviewerFeedbackLocalState();
    }

    private static async Task SaveLocalStateAsync(
        string localFolderPath,
        ReviewerFeedbackLocalState state,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(localFolderPath);
        var path = Path.Combine(localFolderPath, LocalStateFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
    }

    private static string GetLocalFolderPath(string albumId, string reviewerUserId)
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = AppContext.BaseDirectory;
        }

        return Path.Combine(basePath, "Picshare", "feedback", Sanitize(albumId), Sanitize(reviewerUserId));
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }

    private static FeedbackReviewerIdentity CreateGoogleReviewerIdentity(GoogleOAuthTokenSet token)
    {
        return new FeedbackReviewerIdentity
        {
            BackendType = "google",
            UserId = token.UserId ?? throw new InvalidOperationException("Google reviewer id is missing."),
            DisplayName = token.DisplayName,
            Email = token.Email
        };
    }
}
