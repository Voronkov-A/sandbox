using System.Text.Json;
using Picshare.Models;

namespace Picshare.Services;

public sealed class ReviewerFeedbackService
{
    private const string FeedbackFileName = "feedback.json";
    private const string LocalFeedbackFileName = "feedback.json";
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
            concurrentRemoteUpdate);
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
            session.State = state;
            return new ReviewerFeedbackSyncResult(remoteDatabase, state, RemoteWon: true, localDirtyBeforeSync);
        }

        if (state.LocalDirty)
        {
            state = await UploadDatabaseAsync(client, session.State.ReviewerFolderId!, state, localDatabase, cancellationToken);
            await SaveLocalStateAsync(session.LocalFolderPath, state, cancellationToken);
            session.State = state;
        }

        return new ReviewerFeedbackSyncResult(localDatabase, state, RemoteWon: false, localDirtyBeforeSync);
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
}
