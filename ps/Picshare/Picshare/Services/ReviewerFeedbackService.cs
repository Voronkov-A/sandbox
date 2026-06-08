using System.Text.Json;
using Picshare.Models;

namespace Picshare.Services;

public sealed class ReviewerFeedbackService
{
    private const string FeedbackFileName = "feedback.json";
    private const string StatusFileName = "status.json";
    private const string SharedFeedbackFileName = "shared-feedback.json";
    private const string SharedFeedbackVersionFileName = "shared-feedback-version.json";
    private const string LocalFeedbackFileName = "feedback.json";
    private const string LocalStatusFileName = "status.json";
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

        var databaseLoad = await LoadDatabaseAsync(
            client,
            reviewerFolder.Id,
            localFolderPath,
            localState,
            localDatabase,
            manifest.AlbumId,
            reviewerUserId,
            cancellationToken);

        localState = databaseLoad.State;

        var statusLoad = await LoadStatusAsync(
            client,
            reviewerFolder.Id,
            localFolderPath,
            localState,
            manifest.AlbumId,
            reviewerIdentity,
            cancellationToken);

        localState = statusLoad.State;

        var sharedApply = await TryApplySharedFeedbackAsync(
            client,
            manifest.GoogleDrive.FeedbackFolderId,
            reviewerFolder.Id,
            localFolderPath,
            localState,
            databaseLoad.Database,
            manifest.AlbumId,
            reviewerUserId,
            cancellationToken);
        localState = sharedApply.State;

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
            sharedApply.Database,
            statusLoad.Status,
            databaseLoad.ConcurrentRemoteUpdate || statusLoad.ConcurrentRemoteUpdate);
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
        var status = await LoadLocalStatusAsync(session.LocalFolderPath, cancellationToken);
        var statusSync = await SyncStatusAsync(
            client,
            session.State.ReviewerFolderId!,
            session.LocalFolderPath,
            state,
            status,
            cancellationToken);

        state = statusSync.State;
        var sharedApply = await TryApplySharedFeedbackAsync(
            client,
            session.FeedbackFolderId,
            session.State.ReviewerFolderId!,
            session.LocalFolderPath,
            state,
            localDatabase,
            session.AlbumId,
            session.ReviewerUserId,
            cancellationToken);

        if (sharedApply.Applied)
        {
            session.State = sharedApply.State;
            return new ReviewerFeedbackSyncResult(
                sharedApply.Database,
                sharedApply.State,
                statusSync.Status,
                RemoteWon: true,
                LocalDirtyBeforeSync: localDirtyBeforeSync || statusSync.LocalDirtyBeforeSync);
        }

        var databaseSync = await SyncDatabaseAsync(
            client,
            session.State.ReviewerFolderId!,
            session.LocalFolderPath,
            sharedApply.State,
            localDatabase,
            cancellationToken);

        session.State = databaseSync.State;

        return new ReviewerFeedbackSyncResult(
            databaseSync.Database,
            databaseSync.State,
            statusSync.Status,
            databaseSync.RemoteWon || statusSync.RemoteWon,
            localDirtyBeforeSync || statusSync.LocalDirtyBeforeSync);
    }

    public async Task<ReviewerFeedbackStatusResult> CommitAsync(
        ReviewerFeedbackSession session,
        FeedbackReviewerIdentity reviewer,
        string accessToken,
        CancellationToken cancellationToken)
    {
        return await SetStatusAsync(
            session,
            reviewer,
            accessToken,
            ReviewerFeedbackStatusKind.Committed,
            cancellationToken);
    }

    public async Task<ReviewerFeedbackStatusResult> PassAsync(
        ReviewerFeedbackSession session,
        FeedbackReviewerIdentity reviewer,
        string accessToken,
        CancellationToken cancellationToken)
    {
        return await SetStatusAsync(
            session,
            reviewer,
            accessToken,
            ReviewerFeedbackStatusKind.Passed,
            cancellationToken);
    }

    public async Task<ReviewerFeedbackFlowSnapshot> LoadFeedbackFlowAsync(
        string feedbackFolderId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = new GoogleDriveRestClient(accessToken);
        var folders = await ListReviewerFoldersAsync(client, feedbackFolderId, cancellationToken);

        var committed = new List<ReviewerFeedbackFlowItem>();
        var passed = new List<ReviewerFeedbackFlowItem>();
        var inProgress = new List<ReviewerFeedbackFlowItem>();
        foreach (var folder in folders)
        {
            var statusFile = await client.FindChildByNameAsync(folder.Id, StatusFileName, null, cancellationToken);
            if (statusFile is null)
            {
                continue;
            }

            var status = await DownloadStatusAsync(client, statusFile.Id, cancellationToken);
            var item = new ReviewerFeedbackFlowItem(status.Reviewer, status.UpdatedAt);
            switch (status.Status)
            {
                case ReviewerFeedbackStatusKind.Committed:
                    committed.Add(item);
                    break;
                case ReviewerFeedbackStatusKind.Passed:
                    passed.Add(item);
                    break;
                default:
                    inProgress.Add(item);
                    break;
            }
        }

        return new ReviewerFeedbackFlowSnapshot(
            SortFlowItems(committed),
            SortFlowItems(passed),
            SortFlowItems(inProgress));
    }

    public async Task<ReviewerFeedbackCollectResult> CollectFeedbackAsync(
        AlbumManifest manifest,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = new GoogleDriveRestClient(accessToken);
        var folders = await ListReviewerFoldersAsync(client, manifest.GoogleDrive.FeedbackFolderId, cancellationToken);
        var committedDatabases = new List<ReviewerFeedbackDatabase>();

        foreach (var folder in folders)
        {
            var statusFile = await client.FindChildByNameAsync(folder.Id, StatusFileName, null, cancellationToken);
            if (statusFile is null)
            {
                continue;
            }

            var status = await DownloadStatusAsync(client, statusFile.Id, cancellationToken);
            if (status.Status != ReviewerFeedbackStatusKind.Committed)
            {
                continue;
            }

            var feedbackFile = await client.FindChildByNameAsync(folder.Id, FeedbackFileName, null, cancellationToken);
            if (feedbackFile is not null)
            {
                committedDatabases.Add(await DownloadDatabaseAsync(client, feedbackFile.Id, cancellationToken));
            }
        }

        if (committedDatabases.Count == 0)
        {
            throw new InvalidOperationException("There are no committed feedbacks to collect.");
        }

        var mergedCategories = new Dictionary<string, string>(StringComparer.Ordinal);
        var photoScores = new Dictionary<string, int>(StringComparer.Ordinal);
        var frozenPhotoIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var photo in manifest.Photos)
        {
            var niceScore = committedDatabases.Count(database =>
                database.PhotoCategories.TryGetValue(photo.Id, out var category) &&
                string.Equals(category, "nice", StringComparison.Ordinal));
            photoScores[photo.Id] = niceScore;

            if (niceScore > 0)
            {
                mergedCategories[photo.Id] = "nice";
                continue;
            }

            var allTrash = committedDatabases.All(database =>
                database.PhotoCategories.TryGetValue(photo.Id, out var category) &&
                string.Equals(category, "trash", StringComparison.Ordinal));

            mergedCategories[photo.Id] = allTrash ? "trash" : "ok";
            frozenPhotoIds.Add(photo.Id);
        }

        var nicePhotoIds = manifest.Photos
            .Select(photo => photo.Id)
            .Where(photoId => string.Equals(mergedCategories[photoId], "nice", StringComparison.Ordinal))
            .ToList();

        if (nicePhotoIds.Count > manifest.TargetNicePhotoCount)
        {
            if (manifest.TargetNicePhotoCount <= 0)
            {
                foreach (var photoId in nicePhotoIds)
                {
                    mergedCategories[photoId] = "ok";
                    frozenPhotoIds.Add(photoId);
                }
            }
            else
            {
                var boundaryScore = nicePhotoIds
                    .Select(photoId => photoScores[photoId])
                    .OrderByDescending(score => score)
                    .Skip(manifest.TargetNicePhotoCount - 1)
                    .First();

                foreach (var photoId in nicePhotoIds)
                {
                    var score = photoScores[photoId];
                    if (score > boundaryScore)
                    {
                        frozenPhotoIds.Add(photoId);
                    }
                    else if (score < boundaryScore)
                    {
                        mergedCategories[photoId] = "ok";
                        frozenPhotoIds.Add(photoId);
                    }
                }
            }
        }

        var databaseVersion = Guid.NewGuid().ToString("N");
        var sharedDatabase = new SharedFeedbackDatabase
        {
            AlbumId = manifest.AlbumId,
            UpdatedAt = DateTimeOffset.UtcNow,
            HasCollectedFeedback = true,
            PhotoCategories = mergedCategories,
            PhotoScores = photoScores,
            FrozenPhotoIds = frozenPhotoIds
        };

        var sharedVersion = new SharedFeedbackVersion
        {
            AlbumId = manifest.AlbumId,
            DatabaseVersion = databaseVersion,
            UpdatedAt = sharedDatabase.UpdatedAt
        };

        await UploadSharedDatabaseAsync(client, manifest.GoogleDrive.FeedbackFolderId, sharedDatabase, cancellationToken);
        await UploadSharedVersionAsync(client, manifest.GoogleDrive.FeedbackFolderId, sharedVersion, cancellationToken);

        var unfrozenCount = manifest.Photos.Count(photo => !frozenPhotoIds.Contains(photo.Id));
        return new ReviewerFeedbackCollectResult(committedDatabases.Count, mergedCategories.Count, unfrozenCount, databaseVersion);
    }

    public async Task<int> StartNextRoundAsync(
        AlbumManifest manifest,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = new GoogleDriveRestClient(accessToken);
        var folders = await ListReviewerFoldersAsync(client, manifest.GoogleDrive.FeedbackFolderId, cancellationToken);
        var updated = 0;

        foreach (var folder in folders)
        {
            var statusFile = await client.FindChildByNameAsync(folder.Id, StatusFileName, null, cancellationToken);
            if (statusFile is null)
            {
                continue;
            }

            var status = await DownloadStatusAsync(client, statusFile.Id, cancellationToken);
            status.Status = ReviewerFeedbackStatusKind.InProgress;
            status.UpdatedAt = DateTimeOffset.UtcNow;
            await UploadOrUpdateJsonFileAsync(client, folder.Id, StatusFileName, status, cancellationToken);
            updated++;
        }

        var sharedDatabaseFile = await client.FindChildByNameAsync(manifest.GoogleDrive.FeedbackFolderId, SharedFeedbackFileName, null, cancellationToken);
        if (sharedDatabaseFile is not null)
        {
            var sharedDatabase = await DownloadSharedDatabaseAsync(client, sharedDatabaseFile.Id, cancellationToken);
            foreach (var photo in manifest.Photos)
            {
                if (!sharedDatabase.FrozenPhotoIds.Contains(photo.Id) &&
                    sharedDatabase.PhotoCategories.TryGetValue(photo.Id, out var category) &&
                    string.Equals(category, "nice", StringComparison.Ordinal))
                {
                    sharedDatabase.PhotoCategories.Remove(photo.Id);
                }
            }

            sharedDatabase.HasCollectedFeedback = false;
            sharedDatabase.UpdatedAt = DateTimeOffset.UtcNow;
            var sharedVersion = new SharedFeedbackVersion
            {
                AlbumId = manifest.AlbumId,
                DatabaseVersion = Guid.NewGuid().ToString("N"),
                UpdatedAt = sharedDatabase.UpdatedAt
            };

            await UploadSharedDatabaseAsync(client, manifest.GoogleDrive.FeedbackFolderId, sharedDatabase, cancellationToken);
            await UploadSharedVersionAsync(client, manifest.GoogleDrive.FeedbackFolderId, sharedVersion, cancellationToken);
        }

        return updated;
    }

    private static async Task<(
        ReviewerFeedbackDatabase Database,
        ReviewerFeedbackLocalState State,
        bool ConcurrentRemoteUpdate)> LoadDatabaseAsync(
        GoogleDriveRestClient client,
        string reviewerFolderId,
        string localFolderPath,
        ReviewerFeedbackLocalState localState,
        ReviewerFeedbackDatabase? localDatabase,
        string albumId,
        string reviewerUserId,
        CancellationToken cancellationToken)
    {
        var remoteFile = await client.FindChildByNameAsync(reviewerFolderId, FeedbackFileName, null, cancellationToken);
        ReviewerFeedbackDatabase database;
        var concurrentRemoteUpdate = false;

        if (remoteFile is null)
        {
            database = localDatabase ?? CreateEmptyDatabase(albumId, reviewerUserId);
            localState = await UploadDatabaseAsync(client, reviewerFolderId, localState, database, cancellationToken);
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
                localState = await UploadDatabaseAsync(client, reviewerFolderId, localState, database, cancellationToken);
            }
        }

        await SaveLocalDatabaseAsync(localFolderPath, database, cancellationToken);
        return (database, localState, concurrentRemoteUpdate);
    }

    private static async Task<IReadOnlyList<DriveItemInfo>> ListReviewerFoldersAsync(
        GoogleDriveRestClient client,
        string feedbackFolderId,
        CancellationToken cancellationToken)
    {
        var folders = new List<DriveItemInfo>();
        string? pageToken = null;

        do
        {
            var page = await client.ListChildrenAsync(feedbackFolderId, pageToken, 100, cancellationToken);
            folders.AddRange(page.Files.Where(file => string.Equals(file.MimeType, FolderMimeType, StringComparison.Ordinal)));
            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return folders;
    }

    private static async Task<(
        ReviewerFeedbackDatabase Database,
        ReviewerFeedbackLocalState State,
        bool RemoteWon)> SyncDatabaseAsync(
        GoogleDriveRestClient client,
        string reviewerFolderId,
        string localFolderPath,
        ReviewerFeedbackLocalState state,
        ReviewerFeedbackDatabase localDatabase,
        CancellationToken cancellationToken)
    {
        var remoteFile = !string.IsNullOrWhiteSpace(state.RemoteFileId)
            ? await client.GetFileMetadataAsync(state.RemoteFileId, cancellationToken)
            : null;

        if (remoteFile is null)
        {
            var foundFile = await client.FindChildByNameAsync(reviewerFolderId, FeedbackFileName, null, cancellationToken);
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

            await SaveLocalDatabaseAsync(localFolderPath, remoteDatabase, cancellationToken);
            await SaveLocalStateAsync(localFolderPath, state, cancellationToken);
            return (remoteDatabase, state, RemoteWon: true);
        }

        if (state.LocalDirty)
        {
            state = await UploadDatabaseAsync(client, reviewerFolderId, state, localDatabase, cancellationToken);
            await SaveLocalStateAsync(localFolderPath, state, cancellationToken);
        }

        return (localDatabase, state, RemoteWon: false);
    }

    private static async Task<(
        ReviewerFeedbackStatus Status,
        ReviewerFeedbackLocalState State,
        bool ConcurrentRemoteUpdate)> LoadStatusAsync(
        GoogleDriveRestClient client,
        string reviewerFolderId,
        string localFolderPath,
        ReviewerFeedbackLocalState localState,
        string albumId,
        FeedbackReviewerIdentity reviewer,
        CancellationToken cancellationToken)
    {
        var localStatus = await LoadLocalStatusAsync(localFolderPath, cancellationToken);
        var remoteFile = await client.FindChildByNameAsync(reviewerFolderId, StatusFileName, null, cancellationToken);
        ReviewerFeedbackStatus status;
        var concurrentRemoteUpdate = false;

        if (remoteFile is null)
        {
            status = localStatus ?? CreateStatus(albumId, reviewer, ReviewerFeedbackStatusKind.InProgress);
        }
        else
        {
            var remoteChanged = localState.StatusRemoteModifiedTime is null ||
                remoteFile.ModifiedTime is not null &&
                remoteFile.ModifiedTime != localState.StatusRemoteModifiedTime;

            if (localStatus is null || !localState.StatusLocalDirty || remoteChanged)
            {
                concurrentRemoteUpdate = localState.StatusLocalDirty && remoteChanged;
                status = await DownloadStatusAsync(client, remoteFile.Id, cancellationToken);
                localState.StatusRemoteFileId = remoteFile.Id;
                localState.StatusRemoteModifiedTime = remoteFile.ModifiedTime;
                localState.StatusLocalDirty = false;
            }
            else
            {
                status = localStatus;
                localState.StatusRemoteFileId = remoteFile.Id;
                localState.StatusRemoteModifiedTime = remoteFile.ModifiedTime;
            }
        }

        if (status.Status == ReviewerFeedbackStatusKind.InProgress)
        {
            status.UpdatedAt = DateTimeOffset.UtcNow;
            localState.StatusLocalDirty = true;
        }

        await SaveLocalStatusAsync(localFolderPath, status, cancellationToken);
        if (localState.StatusLocalDirty)
        {
            localState = await UploadStatusAsync(client, reviewerFolderId, localState, status, cancellationToken);
        }

        return (status, localState, concurrentRemoteUpdate);
    }

    private static async Task<(
        ReviewerFeedbackDatabase Database,
        ReviewerFeedbackLocalState State,
        bool Applied)> TryApplySharedFeedbackAsync(
        GoogleDriveRestClient client,
        string feedbackFolderId,
        string reviewerFolderId,
        string localFolderPath,
        ReviewerFeedbackLocalState state,
        ReviewerFeedbackDatabase currentDatabase,
        string albumId,
        string reviewerUserId,
        CancellationToken cancellationToken)
    {
        var versionFile = await client.FindChildByNameAsync(feedbackFolderId, SharedFeedbackVersionFileName, null, cancellationToken);
        if (versionFile is null)
        {
            return (currentDatabase, state, Applied: false);
        }

        var sharedVersion = await DownloadSharedVersionAsync(client, versionFile.Id, cancellationToken);
        if (string.Equals(state.SharedCategoriesVersion, sharedVersion.DatabaseVersion, StringComparison.Ordinal))
        {
            return (currentDatabase, state, Applied: false);
        }

        var databaseFile = await client.FindChildByNameAsync(feedbackFolderId, SharedFeedbackFileName, null, cancellationToken);
        if (databaseFile is null)
        {
            return (currentDatabase, state, Applied: false);
        }

        var sharedDatabase = await DownloadSharedDatabaseAsync(client, databaseFile.Id, cancellationToken);
        var reviewerDatabase = new ReviewerFeedbackDatabase
        {
            AlbumId = albumId,
            ReviewerUserId = reviewerUserId,
            UpdatedAt = sharedDatabase.UpdatedAt,
            HasCollectedFeedback = sharedDatabase.HasCollectedFeedback,
            PhotoCategories = new Dictionary<string, string>(sharedDatabase.PhotoCategories, StringComparer.Ordinal),
            PhotoScores = new Dictionary<string, int>(sharedDatabase.PhotoScores, StringComparer.Ordinal),
            FrozenPhotoIds = new HashSet<string>(sharedDatabase.FrozenPhotoIds, StringComparer.Ordinal)
        };

        await SaveLocalDatabaseAsync(localFolderPath, reviewerDatabase, cancellationToken);
        state = await UploadDatabaseAsync(client, reviewerFolderId, state, reviewerDatabase, cancellationToken);
        state.SharedCategoriesVersion = sharedVersion.DatabaseVersion;
        state.LocalDirty = false;
        await SaveLocalStateAsync(localFolderPath, state, cancellationToken);

        return (reviewerDatabase, state, Applied: true);
    }

    private static async Task<(
        ReviewerFeedbackStatus? Status,
        ReviewerFeedbackLocalState State,
        bool RemoteWon,
        bool LocalDirtyBeforeSync)> SyncStatusAsync(
        GoogleDriveRestClient client,
        string reviewerFolderId,
        string localFolderPath,
        ReviewerFeedbackLocalState state,
        ReviewerFeedbackStatus? localStatus,
        CancellationToken cancellationToken)
    {
        var localDirtyBeforeSync = state.StatusLocalDirty;
        var remoteFile = !string.IsNullOrWhiteSpace(state.StatusRemoteFileId)
            ? await client.GetFileMetadataAsync(state.StatusRemoteFileId, cancellationToken)
            : null;

        if (remoteFile is null)
        {
            var foundFile = await client.FindChildByNameAsync(reviewerFolderId, StatusFileName, null, cancellationToken);
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
            (state.StatusRemoteModifiedTime is null ||
             remoteFile.ModifiedTime is not null &&
             remoteFile.ModifiedTime != state.StatusRemoteModifiedTime))
        {
            var remoteStatus = await DownloadStatusAsync(client, remoteFile.Id, cancellationToken);
            state.StatusRemoteFileId = remoteFile.Id;
            state.StatusRemoteModifiedTime = remoteFile.ModifiedTime;
            state.StatusLocalDirty = false;
            await SaveLocalStatusAsync(localFolderPath, remoteStatus, cancellationToken);
            await SaveLocalStateAsync(localFolderPath, state, cancellationToken);
            return (remoteStatus, state, RemoteWon: true, localDirtyBeforeSync);
        }

        if (localStatus is not null && state.StatusLocalDirty)
        {
            state = await UploadStatusAsync(client, reviewerFolderId, state, localStatus, cancellationToken);
            await SaveLocalStateAsync(localFolderPath, state, cancellationToken);
        }

        return (localStatus, state, RemoteWon: false, localDirtyBeforeSync);
    }

    private static async Task<ReviewerFeedbackStatusResult> SetStatusAsync(
        ReviewerFeedbackSession session,
        FeedbackReviewerIdentity reviewer,
        string accessToken,
        ReviewerFeedbackStatusKind statusKind,
        CancellationToken cancellationToken)
    {
        var client = new GoogleDriveRestClient(accessToken);
        var state = await LoadLocalStateAsync(session.LocalFolderPath, cancellationToken);
        var localStatus = await LoadLocalStatusAsync(session.LocalFolderPath, cancellationToken) ??
            CreateStatus(session.AlbumId, reviewer, ReviewerFeedbackStatusKind.InProgress);

        var sync = await SyncStatusAsync(
            client,
            session.State.ReviewerFolderId!,
            session.LocalFolderPath,
            state,
            localStatus,
            cancellationToken);

        if (sync.RemoteWon && sync.Status is { Status: ReviewerFeedbackStatusKind.Committed or ReviewerFeedbackStatusKind.Passed })
        {
            session.State = sync.State;
            return new ReviewerFeedbackStatusResult(sync.Status, sync.State, RemoteWon: true);
        }

        var status = sync.Status ?? localStatus;
        status.Status = statusKind;
        status.UpdatedAt = DateTimeOffset.UtcNow;
        state = sync.State;
        state.StatusLocalDirty = true;
        await SaveLocalStatusAsync(session.LocalFolderPath, status, cancellationToken);
        state = await UploadStatusAsync(client, session.State.ReviewerFolderId!, state, status, cancellationToken);
        await SaveLocalStateAsync(session.LocalFolderPath, state, cancellationToken);
        session.State = state;

        return new ReviewerFeedbackStatusResult(status, state, RemoteWon: false);
    }

    private static ReviewerFeedbackStatus CreateStatus(
        string albumId,
        FeedbackReviewerIdentity reviewer,
        ReviewerFeedbackStatusKind status)
    {
        var now = DateTimeOffset.UtcNow;
        return new ReviewerFeedbackStatus
        {
            AlbumId = albumId,
            Reviewer = reviewer,
            OpenedAt = now,
            UpdatedAt = now,
            Status = status
        };
    }

    private static IReadOnlyList<ReviewerFeedbackFlowItem> SortFlowItems(IEnumerable<ReviewerFeedbackFlowItem> items)
    {
        return items
            .OrderBy(item => item.UpdatedAt)
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

    private static async Task<ReviewerFeedbackStatus> DownloadStatusAsync(
        GoogleDriveRestClient client,
        string fileId,
        CancellationToken cancellationToken)
    {
        await using var stream = await client.DownloadFileAsync(fileId, cancellationToken);
        return await JsonSerializer.DeserializeAsync<ReviewerFeedbackStatus>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Remote reviewer status is empty or invalid.");
    }

    private static async Task<SharedFeedbackDatabase> DownloadSharedDatabaseAsync(
        GoogleDriveRestClient client,
        string fileId,
        CancellationToken cancellationToken)
    {
        await using var stream = await client.DownloadFileAsync(fileId, cancellationToken);
        return await JsonSerializer.DeserializeAsync<SharedFeedbackDatabase>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Remote shared feedback database is empty or invalid.");
    }

    private static async Task<SharedFeedbackVersion> DownloadSharedVersionAsync(
        GoogleDriveRestClient client,
        string fileId,
        CancellationToken cancellationToken)
    {
        await using var stream = await client.DownloadFileAsync(fileId, cancellationToken);
        return await JsonSerializer.DeserializeAsync<SharedFeedbackVersion>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Remote shared feedback version is empty or invalid.");
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

    private static async Task<ReviewerFeedbackLocalState> UploadStatusAsync(
        GoogleDriveRestClient client,
        string reviewerFolderId,
        ReviewerFeedbackLocalState state,
        ReviewerFeedbackStatus status,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(status, JsonOptions));

        DriveFileInfo metadata;
        if (string.IsNullOrWhiteSpace(state.StatusRemoteFileId))
        {
            metadata = await client.UploadFileAsync(StatusFileName, reviewerFolderId, stream, "application/json", cancellationToken);
        }
        else
        {
            await client.UpdateFileContentAsync(state.StatusRemoteFileId, stream, "application/json", cancellationToken);
            metadata = await client.GetFileMetadataAsync(state.StatusRemoteFileId, cancellationToken);
        }

        state.StatusRemoteFileId = metadata.Id;
        state.StatusRemoteModifiedTime = metadata.ModifiedTime;
        state.StatusLocalDirty = false;
        return state;
    }

    private static async Task UploadSharedDatabaseAsync(
        GoogleDriveRestClient client,
        string feedbackFolderId,
        SharedFeedbackDatabase database,
        CancellationToken cancellationToken)
    {
        await UploadOrUpdateJsonFileAsync(client, feedbackFolderId, SharedFeedbackFileName, database, cancellationToken);
    }

    private static async Task UploadSharedVersionAsync(
        GoogleDriveRestClient client,
        string feedbackFolderId,
        SharedFeedbackVersion version,
        CancellationToken cancellationToken)
    {
        await UploadOrUpdateJsonFileAsync(client, feedbackFolderId, SharedFeedbackVersionFileName, version, cancellationToken);
    }

    private static async Task UploadOrUpdateJsonFileAsync<T>(
        GoogleDriveRestClient client,
        string parentFolderId,
        string fileName,
        T payload,
        CancellationToken cancellationToken)
    {
        var file = await client.FindChildByNameAsync(parentFolderId, fileName, null, cancellationToken);
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        if (file is null)
        {
            await client.UploadFileAsync(fileName, parentFolderId, stream, "application/json", cancellationToken);
        }
        else
        {
            await client.UpdateFileContentAsync(file.Id, stream, "application/json", cancellationToken);
        }
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

    private static async Task<ReviewerFeedbackStatus?> LoadLocalStatusAsync(string localFolderPath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(localFolderPath, LocalStatusFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ReviewerFeedbackStatus>(stream, JsonOptions, cancellationToken);
    }

    private static async Task SaveLocalStatusAsync(
        string localFolderPath,
        ReviewerFeedbackStatus status,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(localFolderPath);
        var path = Path.Combine(localFolderPath, LocalStatusFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, status, JsonOptions, cancellationToken);
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
