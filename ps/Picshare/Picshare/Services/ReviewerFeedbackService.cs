using System.Text.Json;
using Picshare.Models;

namespace Picshare.Services;

public sealed class ReviewerFeedbackService
{
    private const string LocalFeedbackFileName = "feedback.json";
    private const string LocalStatusFileName = "status.json";
    private const string LocalStateFileName = "sync-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _localStorageRootPath;

    public ReviewerFeedbackService(string? localStorageRootPath = null)
    {
        _localStorageRootPath = GetLocalStorageRootPath(localStorageRootPath);
    }

    public async Task<ReviewerFeedbackLoadResult> LoadAsync(
        AlbumManifest manifest,
        FeedbackReviewerIdentity reviewer,
        IReviewerFeedbackBackend backend,
        CancellationToken cancellationToken)
    {
        var localFolderPath = GetLocalFolderPath(manifest.AlbumId, reviewer.BackendType, reviewer.UserId);
        Directory.CreateDirectory(localFolderPath);

        var localDatabase = await LoadLocalDatabaseAsync(localFolderPath, cancellationToken);
        var localState = await LoadLocalStateAsync(localFolderPath, cancellationToken);
        var reviewerStore = await backend.EnsureReviewerStoreAsync(reviewer.UserId, cancellationToken);
        localState.ReviewerStoreId = reviewerStore.Id;

        var databaseLoad = await LoadDatabaseAsync(
            backend,
            reviewerStore.Id,
            localFolderPath,
            localState,
            localDatabase,
            manifest.AlbumId,
            reviewer.UserId,
            cancellationToken);
        localState = databaseLoad.State;

        var statusLoad = await LoadStatusAsync(
            backend,
            reviewerStore.Id,
            localFolderPath,
            localState,
            manifest.AlbumId,
            reviewer,
            cancellationToken);
        localState = statusLoad.State;

        var sharedApply = await TryApplySharedFeedbackAsync(
            backend,
            reviewerStore.Id,
            localFolderPath,
            localState,
            databaseLoad.Database,
            manifest.AlbumId,
            reviewer.UserId,
            cancellationToken);
        localState = sharedApply.State;

        await SaveLocalStateAsync(localFolderPath, localState, cancellationToken);

        return new ReviewerFeedbackLoadResult(
            new ReviewerFeedbackSession
            {
                AlbumId = manifest.AlbumId,
                ReviewerUserId = reviewer.UserId,
                ReviewerStoreId = reviewerStore.Id,
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

    public async Task SaveLocalPhotoRotationAsync(
        ReviewerFeedbackSession session,
        ReviewerFeedbackDatabase database,
        string photoId,
        int rotationDegrees,
        CancellationToken cancellationToken)
    {
        var normalizedRotation = NormalizeRotationDegrees(rotationDegrees);
        if (normalizedRotation == 0)
        {
            database.PhotoRotations.Remove(photoId);
        }
        else
        {
            database.PhotoRotations[photoId] = normalizedRotation;
        }

        database.UpdatedAt = DateTimeOffset.UtcNow;
        var state = session.State;
        state.LocalDirty = true;

        await SaveLocalDatabaseAsync(session.LocalFolderPath, database, cancellationToken);
        await SaveLocalStateAsync(session.LocalFolderPath, state, cancellationToken);
        session.State = state;
    }

    public async Task SaveLocalDuplicateGroupAsync(
        ReviewerFeedbackSession session,
        ReviewerFeedbackDatabase database,
        IReadOnlyList<string> photoIds,
        CancellationToken cancellationToken)
    {
        var normalizedPhotoIds = photoIds
            .Where(photoId => !string.IsNullOrWhiteSpace(photoId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalizedPhotoIds.Count < 2)
        {
            return;
        }

        var normalizedSet = normalizedPhotoIds.ToHashSet(StringComparer.Ordinal);
        database.DuplicateGroups.RemoveAll(group => group.PhotoIds.Any(normalizedSet.Contains));
        database.DuplicateGroups.Add(new DuplicatePhotoGroup
        {
            Id = Guid.NewGuid().ToString("N"),
            PhotoIds = normalizedPhotoIds
        });

        database.UpdatedAt = DateTimeOffset.UtcNow;
        var state = session.State;
        state.LocalDirty = true;

        await SaveLocalDatabaseAsync(session.LocalFolderPath, database, cancellationToken);
        await SaveLocalStateAsync(session.LocalFolderPath, state, cancellationToken);
        session.State = state;
    }

    public async Task RemoveLocalPhotoFromDuplicateGroupAsync(
        ReviewerFeedbackSession session,
        ReviewerFeedbackDatabase database,
        string photoId,
        CancellationToken cancellationToken)
    {
        foreach (var group in database.DuplicateGroups)
        {
            group.PhotoIds.RemoveAll(id => string.Equals(id, photoId, StringComparison.Ordinal));
            if (string.Equals(group.BestPhotoId, photoId, StringComparison.Ordinal))
            {
                group.BestPhotoId = "";
            }
        }

        database.DuplicateGroups.RemoveAll(group => group.PhotoIds.Distinct(StringComparer.Ordinal).Count() < 2);
        database.UpdatedAt = DateTimeOffset.UtcNow;
        var state = session.State;
        state.LocalDirty = true;

        await SaveLocalDatabaseAsync(session.LocalFolderPath, database, cancellationToken);
        await SaveLocalStateAsync(session.LocalFolderPath, state, cancellationToken);
        session.State = state;
    }

    public async Task SetLocalDuplicateGroupBestPhotoAsync(
        ReviewerFeedbackSession session,
        ReviewerFeedbackDatabase database,
        string groupId,
        string photoId,
        bool isBest,
        CancellationToken cancellationToken)
    {
        var group = database.DuplicateGroups.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, groupId, StringComparison.Ordinal));
        if (group is null || !group.PhotoIds.Contains(photoId, StringComparer.Ordinal))
        {
            return;
        }

        group.BestPhotoId = isBest ? photoId : "";
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
        IReviewerFeedbackBackend backend,
        CancellationToken cancellationToken)
    {
        var state = await LoadLocalStateAsync(session.LocalFolderPath, cancellationToken);
        var localDirtyBeforeSync = state.LocalDirty;
        var status = await LoadLocalStatusAsync(session.LocalFolderPath, cancellationToken);
        var statusSync = await SyncStatusAsync(
            backend,
            session.ReviewerStoreId,
            session.LocalFolderPath,
            state,
            status,
            cancellationToken);

        state = statusSync.State;
        var sharedApply = await TryApplySharedFeedbackAsync(
            backend,
            session.ReviewerStoreId,
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
            backend,
            session.ReviewerStoreId,
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
        IReviewerFeedbackBackend backend,
        CancellationToken cancellationToken)
    {
        return await SetStatusAsync(session, reviewer, backend, ReviewerFeedbackStatusKind.Committed, cancellationToken);
    }

    public async Task<ReviewerFeedbackStatusResult> PassAsync(
        ReviewerFeedbackSession session,
        FeedbackReviewerIdentity reviewer,
        IReviewerFeedbackBackend backend,
        CancellationToken cancellationToken)
    {
        return await SetStatusAsync(session, reviewer, backend, ReviewerFeedbackStatusKind.Passed, cancellationToken);
    }

    public async Task<ReviewerFeedbackStatusResult> LeaveAsync(
        ReviewerFeedbackSession session,
        FeedbackReviewerIdentity reviewer,
        IReviewerFeedbackBackend backend,
        CancellationToken cancellationToken)
    {
        return await SetStatusAsync(session, reviewer, backend, ReviewerFeedbackStatusKind.Left, cancellationToken);
    }

    public async Task<ReviewerFeedbackFlowSnapshot> LoadFeedbackFlowAsync(
        IReviewerFeedbackBackend backend,
        CancellationToken cancellationToken)
    {
        var stores = await backend.ListReviewerStoresAsync(cancellationToken);
        var committed = new List<ReviewerFeedbackFlowItem>();
        var passed = new List<ReviewerFeedbackFlowItem>();
        var left = new List<ReviewerFeedbackFlowItem>();
        var inProgress = new List<ReviewerFeedbackFlowItem>();

        foreach (var store in stores)
        {
            var statusFile = await backend.LoadReviewerStatusAsync(store.Id, cancellationToken);
            if (statusFile is null)
            {
                continue;
            }

            var status = statusFile.Value;
            var item = new ReviewerFeedbackFlowItem(status.Reviewer, status.UpdatedAt);
            switch (status.Status)
            {
                case ReviewerFeedbackStatusKind.Committed:
                    committed.Add(item);
                    break;
                case ReviewerFeedbackStatusKind.Passed:
                    passed.Add(item);
                    break;
                case ReviewerFeedbackStatusKind.Left:
                    left.Add(item);
                    break;
                default:
                    inProgress.Add(item);
                    break;
            }
        }

        return new ReviewerFeedbackFlowSnapshot(
            SortFlowItems(committed),
            SortFlowItems(passed),
            SortFlowItems(left),
            SortFlowItems(inProgress));
    }

    public async Task<ReviewerFeedbackCollectResult> CollectFeedbackAsync(
        AlbumManifest manifest,
        IReviewerFeedbackBackend backend,
        CancellationToken cancellationToken)
    {
        var stores = await backend.ListReviewerStoresAsync(cancellationToken);
        var committedDatabases = new List<ReviewerFeedbackDatabase>();
        var terminalReviewerCount = 0;

        foreach (var store in stores)
        {
            var statusFile = await backend.LoadReviewerStatusAsync(store.Id, cancellationToken);
            if (statusFile is null)
            {
                continue;
            }

            if (statusFile.Value.Status is ReviewerFeedbackStatusKind.Committed or ReviewerFeedbackStatusKind.Passed)
            {
                terminalReviewerCount++;
            }

            if (statusFile.Value.Status != ReviewerFeedbackStatusKind.Committed)
            {
                continue;
            }

            var feedbackFile = await backend.LoadReviewerFeedbackAsync(store.Id, cancellationToken);
            if (feedbackFile is not null)
            {
                committedDatabases.Add(feedbackFile.Value);
            }
        }

        if (committedDatabases.Count == 0)
        {
            throw new InvalidOperationException("There are no committed feedbacks to collect.");
        }

        var previousSharedDatabase = (await backend.LoadSharedFeedbackAsync(cancellationToken))?.Value;
        var shouldAddRoundScores = previousSharedDatabase?.HasCollectedFeedback != true;
        var previousCategories = previousSharedDatabase?.PhotoCategories ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var previousScores = previousSharedDatabase?.PhotoScores ?? new Dictionary<string, int>(StringComparer.Ordinal);
        var previousFrozenPhotoIds = previousSharedDatabase?.FrozenPhotoIds ?? new HashSet<string>(StringComparer.Ordinal);

        var mergedCategories = new Dictionary<string, string>(StringComparer.Ordinal);
        var photoScores = new Dictionary<string, int>(StringComparer.Ordinal);
        var frozenPhotoIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var photo in manifest.Photos)
        {
            var previousScore = previousScores.TryGetValue(photo.Id, out var storedScore)
                ? storedScore
                : 0;
            var wasFrozen = previousFrozenPhotoIds.Contains(photo.Id);
            var previousCategory = previousCategories.TryGetValue(photo.Id, out var storedCategory)
                ? storedCategory
                : "";

            if (wasFrozen && !string.IsNullOrWhiteSpace(previousCategory))
            {
                var frozenNiceScore = previousScore;
                if (shouldAddRoundScores && string.Equals(previousCategory, "nice", StringComparison.Ordinal))
                {
                    frozenNiceScore += terminalReviewerCount;
                }

                mergedCategories[photo.Id] = previousCategory;
                photoScores[photo.Id] = frozenNiceScore;
                frozenPhotoIds.Add(photo.Id);
                continue;
            }

            var currentRoundNiceScore = committedDatabases.Count(database =>
                database.PhotoCategories.TryGetValue(photo.Id, out var category) &&
                string.Equals(category, "nice", StringComparison.Ordinal));
            var niceScore = previousScore + (shouldAddRoundScores ? currentRoundNiceScore : 0);
            photoScores[photo.Id] = niceScore;

            if (currentRoundNiceScore > 0)
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

        ResolveDuplicateGroups(
            committedDatabases,
            manifest.Photos.Select(photo => photo.Id).ToList(),
            mergedCategories,
            photoScores,
            frozenPhotoIds);

        var nicePhotoIds = manifest.Photos
            .Select(photo => photo.Id)
            .Where(photoId => string.Equals(mergedCategories[photoId], "nice", StringComparison.Ordinal))
            .ToList();

        if (nicePhotoIds.Count <= manifest.TargetNicePhotoCount)
        {
            foreach (var photoId in nicePhotoIds)
            {
                frozenPhotoIds.Add(photoId);
            }
        }
        else
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
                    if (previousFrozenPhotoIds.Contains(photoId))
                    {
                        continue;
                    }

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
            IsFinalized = previousSharedDatabase?.IsFinalized == true,
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

        await backend.SaveSharedFeedbackAsync(sharedDatabase, cancellationToken);
        await backend.SaveSharedFeedbackVersionAsync(sharedVersion, cancellationToken);

        var unfrozenCount = manifest.Photos.Count(photo => !frozenPhotoIds.Contains(photo.Id));
        return new ReviewerFeedbackCollectResult(committedDatabases.Count, mergedCategories.Count, unfrozenCount, databaseVersion);
    }

    public async Task<int> StartNextRoundAsync(
        AlbumManifest manifest,
        IReviewerFeedbackBackend backend,
        CancellationToken cancellationToken)
    {
        var stores = await backend.ListReviewerStoresAsync(cancellationToken);
        var updated = 0;

        foreach (var store in stores)
        {
            var statusFile = await backend.LoadReviewerStatusAsync(store.Id, cancellationToken);
            if (statusFile is null)
            {
                continue;
            }

            var status = statusFile.Value;
            status.Status = ReviewerFeedbackStatusKind.InProgress;
            status.UpdatedAt = DateTimeOffset.UtcNow;
            await backend.SaveReviewerStatusAsync(store.Id, status, cancellationToken);
            updated++;
        }

        var sharedDatabaseFile = await backend.LoadSharedFeedbackAsync(cancellationToken);
        if (sharedDatabaseFile is not null)
        {
            var sharedDatabase = sharedDatabaseFile.Value;
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
            sharedDatabase.IsFinalized = false;
            sharedDatabase.UpdatedAt = DateTimeOffset.UtcNow;
            var sharedVersion = new SharedFeedbackVersion
            {
                AlbumId = manifest.AlbumId,
                DatabaseVersion = Guid.NewGuid().ToString("N"),
                UpdatedAt = sharedDatabase.UpdatedAt
            };

            await backend.SaveSharedFeedbackAsync(sharedDatabase, cancellationToken);
            await backend.SaveSharedFeedbackVersionAsync(sharedVersion, cancellationToken);
        }

        return updated;
    }

    public async Task<ReviewerFeedbackFinalizeResult> FinalizeAsync(
        AlbumManifest manifest,
        IReviewerFeedbackBackend backend,
        CancellationToken cancellationToken)
    {
        var stores = await backend.ListReviewerStoresAsync(cancellationToken);
        var updatedReviewers = 0;

        foreach (var store in stores)
        {
            var statusFile = await backend.LoadReviewerStatusAsync(store.Id, cancellationToken);
            if (statusFile is null)
            {
                continue;
            }

            var status = statusFile.Value;
            if (status.Status != ReviewerFeedbackStatusKind.Left)
            {
                status.Status = ReviewerFeedbackStatusKind.InProgress;
                status.UpdatedAt = DateTimeOffset.UtcNow;
                await backend.SaveReviewerStatusAsync(store.Id, status, cancellationToken);
                updatedReviewers++;
            }
        }

        var previousSharedDatabase = (await backend.LoadSharedFeedbackAsync(cancellationToken))?.Value;
        var categories = previousSharedDatabase is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(previousSharedDatabase.PhotoCategories, StringComparer.Ordinal);
        var scores = previousSharedDatabase is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(previousSharedDatabase.PhotoScores, StringComparer.Ordinal);
        var frozenPhotoIds = new HashSet<string>(manifest.Photos.Select(photo => photo.Id), StringComparer.Ordinal);

        foreach (var photo in manifest.Photos)
        {
            if (!categories.ContainsKey(photo.Id))
            {
                categories[photo.Id] = "ok";
            }

            if (!scores.ContainsKey(photo.Id))
            {
                scores[photo.Id] = 0;
            }
        }

        var databaseVersion = Guid.NewGuid().ToString("N");
        var sharedDatabase = new SharedFeedbackDatabase
        {
            AlbumId = manifest.AlbumId,
            UpdatedAt = DateTimeOffset.UtcNow,
            HasCollectedFeedback = true,
            IsFinalized = true,
            PhotoCategories = categories,
            PhotoScores = scores,
            FrozenPhotoIds = frozenPhotoIds
        };

        var sharedVersion = new SharedFeedbackVersion
        {
            AlbumId = manifest.AlbumId,
            DatabaseVersion = databaseVersion,
            UpdatedAt = sharedDatabase.UpdatedAt
        };

        await backend.SaveSharedFeedbackAsync(sharedDatabase, cancellationToken);
        await backend.SaveSharedFeedbackVersionAsync(sharedVersion, cancellationToken);

        return new ReviewerFeedbackFinalizeResult(updatedReviewers, manifest.Photos.Count, databaseVersion);
    }

    public Task DeleteLocalAlbumStateAsync(string albumId)
    {
        var path = Path.Combine(_localStorageRootPath, "Picshare", "feedback", Sanitize(albumId));
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static async Task<(
        ReviewerFeedbackDatabase Database,
        ReviewerFeedbackLocalState State,
        bool ConcurrentRemoteUpdate)> LoadDatabaseAsync(
        IReviewerFeedbackBackend backend,
        string reviewerStoreId,
        string localFolderPath,
        ReviewerFeedbackLocalState localState,
        ReviewerFeedbackDatabase? localDatabase,
        string albumId,
        string reviewerUserId,
        CancellationToken cancellationToken)
    {
        var remoteFile = await backend.LoadReviewerFeedbackAsync(reviewerStoreId, cancellationToken);
        ReviewerFeedbackDatabase database;
        var concurrentRemoteUpdate = false;

        if (remoteFile is null)
        {
            database = localDatabase ?? CreateEmptyDatabase(albumId, reviewerUserId);
            var saved = await backend.SaveReviewerFeedbackAsync(reviewerStoreId, database, cancellationToken);
            localState.RemoteRevision = saved.Revision;
            localState.LocalDirty = false;
        }
        else
        {
            var remoteChanged = HasRemoteChanged(localState.RemoteRevision, remoteFile.Revision);
            if (localDatabase is null || !localState.LocalDirty || remoteChanged)
            {
                concurrentRemoteUpdate = localState.LocalDirty && remoteChanged;
                database = remoteFile.Value;
                localState.RemoteRevision = remoteFile.Revision;
                localState.LocalDirty = false;
            }
            else
            {
                database = localDatabase;
                var saved = await backend.SaveReviewerFeedbackAsync(reviewerStoreId, database, cancellationToken);
                localState.RemoteRevision = saved.Revision;
                localState.LocalDirty = false;
            }
        }

        await SaveLocalDatabaseAsync(localFolderPath, database, cancellationToken);
        return (database, localState, concurrentRemoteUpdate);
    }

    private static async Task<(
        ReviewerFeedbackStatus Status,
        ReviewerFeedbackLocalState State,
        bool ConcurrentRemoteUpdate)> LoadStatusAsync(
        IReviewerFeedbackBackend backend,
        string reviewerStoreId,
        string localFolderPath,
        ReviewerFeedbackLocalState localState,
        string albumId,
        FeedbackReviewerIdentity reviewer,
        CancellationToken cancellationToken)
    {
        var localStatus = await LoadLocalStatusAsync(localFolderPath, cancellationToken);
        var remoteFile = await backend.LoadReviewerStatusAsync(reviewerStoreId, cancellationToken);
        ReviewerFeedbackStatus status;
        var concurrentRemoteUpdate = false;

        if (remoteFile is null)
        {
            status = localStatus ?? CreateStatus(albumId, reviewer, ReviewerFeedbackStatusKind.InProgress);
        }
        else
        {
            var remoteChanged = localState.StatusRemoteRevision is null ||
                HasRemoteChanged(localState.StatusRemoteRevision, remoteFile.Revision);

            if (localStatus is null || !localState.StatusLocalDirty || remoteChanged)
            {
                concurrentRemoteUpdate = localState.StatusLocalDirty && remoteChanged;
                status = remoteFile.Value;
                localState.StatusRemoteRevision = remoteFile.Revision;
                localState.StatusLocalDirty = false;
            }
            else
            {
                status = localStatus;
                localState.StatusRemoteRevision = remoteFile.Revision;
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
            var saved = await backend.SaveReviewerStatusAsync(reviewerStoreId, status, cancellationToken);
            localState.StatusRemoteRevision = saved.Revision;
            localState.StatusLocalDirty = false;
        }

        return (status, localState, concurrentRemoteUpdate);
    }

    private static async Task<(
        ReviewerFeedbackDatabase Database,
        ReviewerFeedbackLocalState State,
        bool Applied)> TryApplySharedFeedbackAsync(
        IReviewerFeedbackBackend backend,
        string reviewerStoreId,
        string localFolderPath,
        ReviewerFeedbackLocalState state,
        ReviewerFeedbackDatabase currentDatabase,
        string albumId,
        string reviewerUserId,
        CancellationToken cancellationToken)
    {
        var versionFile = await backend.LoadSharedFeedbackVersionAsync(cancellationToken);
        if (versionFile is null ||
            string.Equals(state.SharedCategoriesVersion, versionFile.Value.DatabaseVersion, StringComparison.Ordinal))
        {
            return (currentDatabase, state, Applied: false);
        }

        var databaseFile = await backend.LoadSharedFeedbackAsync(cancellationToken);
        if (databaseFile is null)
        {
            return (currentDatabase, state, Applied: false);
        }

        var sharedDatabase = databaseFile.Value;
        var reviewerDatabase = new ReviewerFeedbackDatabase
        {
            AlbumId = albumId,
            ReviewerUserId = reviewerUserId,
            UpdatedAt = sharedDatabase.UpdatedAt,
            HasCollectedFeedback = sharedDatabase.HasCollectedFeedback,
            IsFinalized = sharedDatabase.IsFinalized,
            PhotoCategories = new Dictionary<string, string>(sharedDatabase.PhotoCategories, StringComparer.Ordinal),
            PhotoScores = new Dictionary<string, int>(sharedDatabase.PhotoScores, StringComparer.Ordinal),
            FrozenPhotoIds = new HashSet<string>(sharedDatabase.FrozenPhotoIds, StringComparer.Ordinal),
            PhotoRotations = new Dictionary<string, int>(currentDatabase.PhotoRotations, StringComparer.Ordinal)
        };

        await SaveLocalDatabaseAsync(localFolderPath, reviewerDatabase, cancellationToken);
        var saved = await backend.SaveReviewerFeedbackAsync(reviewerStoreId, reviewerDatabase, cancellationToken);
        state.SharedCategoriesVersion = versionFile.Value.DatabaseVersion;
        state.RemoteRevision = saved.Revision;
        state.LocalDirty = false;
        await SaveLocalStateAsync(localFolderPath, state, cancellationToken);

        return (reviewerDatabase, state, Applied: true);
    }

    private static async Task<(
        ReviewerFeedbackStatus? Status,
        ReviewerFeedbackLocalState State,
        bool RemoteWon,
        bool LocalDirtyBeforeSync)> SyncStatusAsync(
        IReviewerFeedbackBackend backend,
        string reviewerStoreId,
        string localFolderPath,
        ReviewerFeedbackLocalState state,
        ReviewerFeedbackStatus? localStatus,
        CancellationToken cancellationToken)
    {
        var localDirtyBeforeSync = state.StatusLocalDirty;
        var remoteFile = await backend.LoadReviewerStatusAsync(reviewerStoreId, cancellationToken);
        if (remoteFile is not null && HasRemoteChanged(state.StatusRemoteRevision, remoteFile.Revision))
        {
            state.StatusRemoteRevision = remoteFile.Revision;
            state.StatusLocalDirty = false;
            await SaveLocalStatusAsync(localFolderPath, remoteFile.Value, cancellationToken);
            await SaveLocalStateAsync(localFolderPath, state, cancellationToken);
            return (remoteFile.Value, state, RemoteWon: true, localDirtyBeforeSync);
        }

        if (localStatus is not null && state.StatusLocalDirty)
        {
            var saved = await backend.SaveReviewerStatusAsync(reviewerStoreId, localStatus, cancellationToken);
            state.StatusRemoteRevision = saved.Revision;
            state.StatusLocalDirty = false;
            await SaveLocalStateAsync(localFolderPath, state, cancellationToken);
        }

        return (localStatus, state, RemoteWon: false, localDirtyBeforeSync);
    }

    private static async Task<(
        ReviewerFeedbackDatabase Database,
        ReviewerFeedbackLocalState State,
        bool RemoteWon)> SyncDatabaseAsync(
        IReviewerFeedbackBackend backend,
        string reviewerStoreId,
        string localFolderPath,
        ReviewerFeedbackLocalState state,
        ReviewerFeedbackDatabase localDatabase,
        CancellationToken cancellationToken)
    {
        var remoteFile = await backend.LoadReviewerFeedbackAsync(reviewerStoreId, cancellationToken);
        if (remoteFile is not null && HasRemoteChanged(state.RemoteRevision, remoteFile.Revision))
        {
            state.RemoteRevision = remoteFile.Revision;
            state.LocalDirty = false;
            await SaveLocalDatabaseAsync(localFolderPath, remoteFile.Value, cancellationToken);
            await SaveLocalStateAsync(localFolderPath, state, cancellationToken);
            return (remoteFile.Value, state, RemoteWon: true);
        }

        if (state.LocalDirty)
        {
            var saved = await backend.SaveReviewerFeedbackAsync(reviewerStoreId, localDatabase, cancellationToken);
            state.RemoteRevision = saved.Revision;
            state.LocalDirty = false;
            await SaveLocalStateAsync(localFolderPath, state, cancellationToken);
        }

        return (localDatabase, state, RemoteWon: false);
    }

    private static async Task<ReviewerFeedbackStatusResult> SetStatusAsync(
        ReviewerFeedbackSession session,
        FeedbackReviewerIdentity reviewer,
        IReviewerFeedbackBackend backend,
        ReviewerFeedbackStatusKind statusKind,
        CancellationToken cancellationToken)
    {
        var state = await LoadLocalStateAsync(session.LocalFolderPath, cancellationToken);
        var localStatus = await LoadLocalStatusAsync(session.LocalFolderPath, cancellationToken) ??
            CreateStatus(session.AlbumId, reviewer, ReviewerFeedbackStatusKind.InProgress);

        var sync = await SyncStatusAsync(
            backend,
            session.ReviewerStoreId,
            session.LocalFolderPath,
            state,
            localStatus,
            cancellationToken);

        if (sync.RemoteWon && sync.Status is { Status: ReviewerFeedbackStatusKind.Committed or ReviewerFeedbackStatusKind.Passed or ReviewerFeedbackStatusKind.Left })
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
        var saved = await backend.SaveReviewerStatusAsync(session.ReviewerStoreId, status, cancellationToken);
        state.StatusRemoteRevision = saved.Revision;
        state.StatusLocalDirty = false;
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

    private static void ResolveDuplicateGroups(
        IReadOnlyList<ReviewerFeedbackDatabase> committedDatabases,
        IReadOnlyList<string> manifestPhotoIds,
        Dictionary<string, string> mergedCategories,
        Dictionary<string, int> photoScores,
        HashSet<string> frozenPhotoIds)
    {
        var manifestPhotoIdSet = manifestPhotoIds.ToHashSet(StringComparer.Ordinal);
        var reviewerGroups = committedDatabases
            .SelectMany((database, reviewerIndex) => database.DuplicateGroups
                .Select(group => new
                {
                    ReviewerIndex = reviewerIndex,
                    BestPhotoId = group.BestPhotoId,
                    PhotoIds = group.PhotoIds
                        .Where(manifestPhotoIdSet.Contains)
                        .Distinct(StringComparer.Ordinal)
                        .ToHashSet(StringComparer.Ordinal)
                })
                .Where(group => group.PhotoIds.Count > 1))
            .ToList();
        if (reviewerGroups.Count == 0)
        {
            return;
        }

        var survivedGroups = new List<HashSet<string>>();
        if (committedDatabases.Count == 1)
        {
            survivedGroups.AddRange(reviewerGroups.Select(group => group.PhotoIds));
        }
        else
        {
            for (var left = 0; left < reviewerGroups.Count; left++)
            {
                for (var right = left + 1; right < reviewerGroups.Count; right++)
                {
                    if (reviewerGroups[left].ReviewerIndex == reviewerGroups[right].ReviewerIndex)
                    {
                        continue;
                    }

                    var intersection = reviewerGroups[left].PhotoIds.Intersect(reviewerGroups[right].PhotoIds, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
                    if (intersection.Count > 1 && !survivedGroups.Any(group => group.SetEquals(intersection)))
                    {
                        survivedGroups.Add(intersection);
                    }
                }
            }
        }

        survivedGroups = survivedGroups
            .Where(group => !survivedGroups.Any(other => !ReferenceEquals(group, other) && other.Count > group.Count && group.IsSubsetOf(other)))
            .ToList();

        foreach (var group in survivedGroups)
        {
            var category = GetHighestPriorityCategory(group.Select(photoId => mergedCategories.TryGetValue(photoId, out var value) ? value : ""));
            if (string.Equals(category, "trash", StringComparison.Ordinal))
            {
                foreach (var photoId in group)
                {
                    mergedCategories[photoId] = "trash";
                    frozenPhotoIds.Add(photoId);
                }

                continue;
            }

            if (!string.Equals(category, "nice", StringComparison.Ordinal) &&
                !string.Equals(category, "ok", StringComparison.Ordinal))
            {
                continue;
            }

            var bestVotes = group.ToDictionary(
                photoId => photoId,
                photoId => reviewerGroups.Count(reviewerGroup =>
                    reviewerGroup.PhotoIds.IsSupersetOf(group) &&
                    string.Equals(reviewerGroup.BestPhotoId, photoId, StringComparison.Ordinal)),
                StringComparer.Ordinal);
            var winner = group
                .OrderByDescending(photoId => bestVotes.TryGetValue(photoId, out var votes) ? votes : 0)
                .ThenByDescending(photoId => photoScores.TryGetValue(photoId, out var score) ? score : 0)
                .ThenBy(photoId => StableTieBreaker(photoId))
                .First();

            foreach (var photoId in group)
            {
                mergedCategories[photoId] = string.Equals(photoId, winner, StringComparison.Ordinal)
                    ? category
                    : "trash";
                frozenPhotoIds.Add(photoId);
            }
        }
    }

    private static string GetHighestPriorityCategory(IEnumerable<string> categories)
    {
        var categoryList = categories.ToList();
        if (categoryList.Any(category => string.Equals(category, "nice", StringComparison.Ordinal)))
        {
            return "nice";
        }

        if (categoryList.Any(category => string.Equals(category, "ok", StringComparison.Ordinal)))
        {
            return "ok";
        }

        if (categoryList.Any(category => string.Equals(category, "trash", StringComparison.Ordinal)))
        {
            return "trash";
        }

        return "";
    }

    private static int StableTieBreaker(string value)
    {
        return StringComparer.Ordinal.GetHashCode(value);
    }

    private static ReviewerFeedbackDatabase CreateEmptyDatabase(string albumId, string reviewerUserId)
    {
        return new ReviewerFeedbackDatabase
        {
            AlbumId = albumId,
            ReviewerUserId = reviewerUserId
        };
    }

    private static bool HasRemoteChanged(string? knownRevision, string? remoteRevision)
    {
        return knownRevision is null ||
            remoteRevision is not null &&
            !string.Equals(remoteRevision, knownRevision, StringComparison.Ordinal);
    }

    private static int NormalizeRotationDegrees(int rotationDegrees)
    {
        var normalized = rotationDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
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

    private string GetLocalFolderPath(string albumId, string reviewerBackendType, string reviewerUserId)
    {
        return Path.Combine(
            _localStorageRootPath,
            "Picshare",
            "feedback",
            Sanitize(albumId),
            Sanitize(reviewerBackendType),
            Sanitize(reviewerUserId));
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
