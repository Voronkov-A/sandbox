using System.Text.Json;
using Picshare.Models;

namespace Picshare.Services;

public sealed class PersistentAlbumCreationService
{
    private const int PendingCreationSaveBatchSize = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _pendingCreationFolderPath;

    public PersistentAlbumCreationService(string? localStorageRootPath = null)
    {
        _pendingCreationFolderPath = Path.Combine(GetLocalStorageRootPath(localStorageRootPath), "Picshare", "pending-creations");
    }

    public async Task<PendingAlbumCreation?> LoadPendingCreationAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_pendingCreationFolderPath))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(_pendingCreationFolderPath, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                return await JsonSerializer.DeserializeAsync<PendingAlbumCreation>(stream, JsonOptions, cancellationToken);
            }
            catch
            {
            }
        }

        return null;
    }

    public async Task<PendingAlbumCreation> CreatePendingCreationAsync(
        string albumTypeId,
        string title,
        int targetNicePhotoCount,
        string? parentDriveFolderId,
        string? parentFolderPath,
        FeedbackReviewerIdentity author,
        IReadOnlyList<PhotoUploadSource> photos,
        CancellationToken cancellationToken)
    {
        if (photos.Count == 0)
        {
            throw new InvalidOperationException("Add at least one photo to the album.");
        }

        if (photos.Count > 99999)
        {
            throw new InvalidOperationException("An album can contain at most 99,999 photos.");
        }

        var pending = new PendingAlbumCreation
        {
            AlbumId = Guid.NewGuid().ToString("N"),
            AlbumTypeId = albumTypeId,
            Title = title,
            TargetNicePhotoCount = targetNicePhotoCount,
            ParentDriveFolderId = parentDriveFolderId,
            ParentFolderPath = parentFolderPath,
            Author = author,
            Photos = photos.Select((photo, index) =>
            {
                if (string.IsNullOrWhiteSpace(photo.LocalPath))
                {
                    throw new InvalidOperationException("All selected photos must be local files for resumable album creation.");
                }

                return new PendingAlbumCreationPhoto
                {
                    PhotoId = Guid.NewGuid().ToString("N"),
                    FileName = photo.FileName,
                    StoredFileName = AlbumPublishingWorkflow.CreateStoredPhotoFileName(title, index + 1, photo.FileName),
                    SortKey = photo.SortKey,
                    LocalPath = photo.LocalPath
                };
            }).ToList()
        };

        await SavePendingCreationAsync(pending, cancellationToken);
        return pending;
    }

    public async Task<AlbumCreationResumeResult> ResumeAsync(
        PendingAlbumCreation pending,
        Func<CancellationToken, Task<string>> getGoogleAccessTokenAsync,
        IProgress<AlbumCreationProgress>? progress,
        int maximumParallelism,
        CancellationToken cancellationToken)
    {
        var lastProgress = new AlbumCreationProgress("Preparing album", 0, pending.Photos.Count + 3);
        void Report(AlbumCreationProgress value)
        {
            lastProgress = value;
            progress?.Report(value);
        }

        void ReportRetryWarning(string warning)
        {
            progress?.Report(lastProgress with { Warning = warning });
        }

        var trackedProgress = new Progress<AlbumCreationProgress>(Report);
        Report(lastProgress);
        AlbumCreationResumeResult result = string.Equals(pending.AlbumTypeId, "local-file-system", StringComparison.Ordinal)
            ? await ResumeLocalAsync(pending, trackedProgress, maximumParallelism, cancellationToken)
            : await TransientRetryPolicy.ExecuteAsync(
                token => ResumeGoogleAsync(pending, getGoogleAccessTokenAsync, trackedProgress, maximumParallelism, token),
                ReportRetryWarning,
                cancellationToken);

        if (result.Manifest.GoogleDrive is not null)
        {
            await TransientRetryPolicy.ExecuteAsync(
                token => EnsureInitialWorkflowHistoryAsync(result.Manifest, getGoogleAccessTokenAsync, token),
                ReportRetryWarning,
                cancellationToken);
        }
        else
        {
            await EnsureInitialWorkflowHistoryAsync(result.Manifest, getGoogleAccessTokenAsync, cancellationToken);
        }

        DeletePendingCreation(pending.AlbumId);
        return result;
    }

    public void ForgetPendingCreation(string albumId)
    {
        DeletePendingCreation(albumId);
    }

    public async Task<AlbumDestinationInspection> InspectDestinationAsync(
        string albumTypeId,
        string title,
        string? parentDriveFolderId,
        string? parentFolderPath,
        Func<CancellationToken, Task<string>> getGoogleAccessTokenAsync,
        CancellationToken cancellationToken)
    {
        if (string.Equals(albumTypeId, "local-file-system", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(parentFolderPath))
            {
                throw new InvalidOperationException("Missing local album destination.");
            }

            var albumFolderPath = CreateAlbumFolderPath(parentFolderPath, title);
            return new AlbumDestinationInspection(
                albumFolderPath,
                File.Exists(albumFolderPath) ||
                Directory.Exists(albumFolderPath));
        }

        var client = new GoogleDriveRestClient(await getGoogleAccessTokenAsync(cancellationToken));
        var albumFolder = await FindGoogleFolderByNameAsync(client, parentDriveFolderId, title, cancellationToken);
        if (albumFolder is null)
        {
            return new AlbumDestinationInspection(title, false);
        }

        return new AlbumDestinationInspection(albumFolder.Name, true);
    }

    public async Task ClearDestinationAsync(
        string albumTypeId,
        string title,
        string? parentDriveFolderId,
        string? parentFolderPath,
        Func<CancellationToken, Task<string>> getGoogleAccessTokenAsync,
        int maximumParallelism,
        CancellationToken cancellationToken)
    {
        if (string.Equals(albumTypeId, "local-file-system", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(parentFolderPath))
            {
                throw new InvalidOperationException("Missing local album destination.");
            }

            var albumFolderPath = CreateAlbumFolderPath(parentFolderPath, title);
            if (File.Exists(albumFolderPath))
            {
                File.Delete(albumFolderPath);
                return;
            }

            if (Directory.Exists(albumFolderPath))
            {
                Directory.Delete(albumFolderPath, recursive: true);
            }

            return;
        }

        var client = new GoogleDriveRestClient(await getGoogleAccessTokenAsync(cancellationToken));
        var albumFolder = await FindGoogleFolderByNameAsync(client, parentDriveFolderId, title, cancellationToken);
        if (albumFolder is null)
        {
            return;
        }

        await client.DeleteFileAsync(albumFolder.Id, cancellationToken);
    }

    public AlbumManifest? CreateDeletionManifest(PendingAlbumCreation pending)
    {
        if (string.Equals(pending.AlbumTypeId, "local-file-system", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(pending.LocalAlbumFolderPath))
        {
            return AlbumPublishingWorkflow.CreateManifest(
                pending.AlbumId,
                pending.Title,
                pending.TargetNicePhotoCount,
                "local-file-system",
                "local-file-system",
                pending.Author,
                null,
                new LocalFileSystemAlbumDetails
                {
                    RootPath = pending.LocalAlbumFolderPath,
                    PhotosFolderPath = pending.LocalPhotosFolderPath ?? Path.Combine(pending.LocalAlbumFolderPath, "photos"),
                    FeedbackFolderPath = pending.LocalFeedbackFolderPath ?? Path.Combine(pending.LocalAlbumFolderPath, "feedback"),
                    ManifestFilePath = pending.LocalManifestFilePath ?? Path.Combine(pending.LocalAlbumFolderPath, "album.json")
                },
                pending.Photos.Where(photo => photo.Reference is not null).Select(photo => photo.Reference!).ToList());
        }

        if (!string.IsNullOrWhiteSpace(pending.GoogleAlbumFolderId) &&
            !string.IsNullOrWhiteSpace(pending.GooglePhotosFolderId) &&
            !string.IsNullOrWhiteSpace(pending.GoogleFeedbackFolderId))
        {
            return AlbumPublishingWorkflow.CreateManifest(
                pending.AlbumId,
                pending.Title,
                pending.TargetNicePhotoCount,
                "local-photos-to-google-drive",
                "google-drive-folder",
                pending.Author,
                new GoogleDriveAlbumDetails
                {
                    AlbumFolderId = pending.GoogleAlbumFolderId,
                    PhotosFolderId = pending.GooglePhotosFolderId,
                    FeedbackFolderId = pending.GoogleFeedbackFolderId,
                    ManifestFileId = pending.GoogleManifestFileId ?? "",
                    AlbumFolderUrl = pending.GoogleAlbumFolderUrl ?? $"https://drive.google.com/drive/folders/{pending.GoogleAlbumFolderId}"
                },
                null,
                pending.Photos.Where(photo => photo.Reference is not null).Select(photo => photo.Reference!).ToList());
        }

        return null;
    }

    private async Task<AlbumCreationResumeResult> ResumeLocalAsync(
        PendingAlbumCreation pending,
        IProgress<AlbumCreationProgress>? progress,
        int maximumParallelism,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pending.ParentFolderPath))
        {
            throw new InvalidOperationException("Missing local album destination.");
        }

        pending.LocalAlbumFolderPath ??= CreateAlbumFolderPath(pending.ParentFolderPath, pending.Title);
        pending.LocalPhotosFolderPath ??= Path.Combine(pending.LocalAlbumFolderPath, "photos");
        pending.LocalFeedbackFolderPath ??= Path.Combine(pending.LocalAlbumFolderPath, "feedback");
        pending.LocalManifestFilePath ??= Path.Combine(pending.LocalAlbumFolderPath, "album.json");
        Directory.CreateDirectory(pending.LocalPhotosFolderPath);
        Directory.CreateDirectory(pending.LocalFeedbackFolderPath);
        await SavePendingCreationAsync(pending, cancellationToken);

        await StorePhotosAsync(
            pending,
            async (photo, contentType, contentStream, thumbnailStream, token) =>
            {
                var storedPath = Path.Combine(pending.LocalPhotosFolderPath, photo.StoredFileName);
                var thumbnailPath = Path.Combine(pending.LocalPhotosFolderPath, AlbumPublishingWorkflow.CreateThumbnailFileName(photo.PhotoId));

                if (!File.Exists(storedPath))
                {
                    await CopyToNewFileAtomicallyAsync(contentStream, storedPath, token);
                }

                if (!File.Exists(thumbnailPath))
                {
                    await CopyToNewFileAtomicallyAsync(thumbnailStream, thumbnailPath, token);
                }

                photo.Reference = new PhotoReference
                {
                    Id = photo.PhotoId,
                    FileName = photo.StoredFileName,
                    ContentType = contentType,
                    BackendType = "local-file",
                    DownloadUrl = new Uri(storedPath).AbsoluteUri,
                    ThumbnailDownloadUrl = new Uri(thumbnailPath).AbsoluteUri,
                    ThumbnailContentType = PhotoThumbnailGenerator.ContentType
                };
            },
            progress,
            maximumParallelism,
            cancellationToken);

        var manifest = AlbumPublishingWorkflow.CreateManifest(
            pending.AlbumId,
            pending.Title,
            pending.TargetNicePhotoCount,
            "local-file-system",
            "local-file-system",
            pending.Author,
            null,
            new LocalFileSystemAlbumDetails
            {
                RootPath = pending.LocalAlbumFolderPath,
                PhotosFolderPath = pending.LocalPhotosFolderPath,
                FeedbackFolderPath = pending.LocalFeedbackFolderPath,
                ManifestFilePath = pending.LocalManifestFilePath
            },
            pending.Photos.Select(photo => photo.Reference!).ToList());

        progress?.Report(new AlbumCreationProgress("Writing manifest", pending.Photos.Count + 1, pending.Photos.Count + 3));
        await using (var stream = File.Create(pending.LocalManifestFilePath))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
        }

        return new AlbumCreationResumeResult(
            manifest,
            pending.LocalAlbumFolderPath,
            AlbumLinkParser.CreateLocalPicshareLink(pending.LocalManifestFilePath));
    }

    private async Task<AlbumCreationResumeResult> ResumeGoogleAsync(
        PendingAlbumCreation pending,
        Func<CancellationToken, Task<string>> getGoogleAccessTokenAsync,
        IProgress<AlbumCreationProgress>? progress,
        int maximumParallelism,
        CancellationToken cancellationToken)
    {
        var client = new GoogleDriveRestClient(await getGoogleAccessTokenAsync(cancellationToken));
        if (string.IsNullOrWhiteSpace(pending.GoogleAlbumFolderId))
        {
            var albumFolderName = Sanitize(string.IsNullOrWhiteSpace(pending.Title) ? "Picshare album" : pending.Title.Trim());
            var albumFolder = await FindGoogleFolderByNameAsync(client, pending.ParentDriveFolderId, pending.Title, cancellationToken) ??
                await client.CreateFolderAsync(albumFolderName, pending.ParentDriveFolderId, cancellationToken);
            pending.GoogleAlbumFolderId = albumFolder.Id;
            pending.GoogleAlbumFolderUrl = albumFolder.WebViewLink ?? $"https://drive.google.com/drive/folders/{albumFolder.Id}";
            await SavePendingCreationAsync(pending, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(pending.GooglePhotosFolderId))
        {
            pending.GooglePhotosFolderId = (await FindGoogleFolderByNameAsync(client, pending.GoogleAlbumFolderId, "photos", cancellationToken) ??
                await client.CreateFolderAsync("photos", pending.GoogleAlbumFolderId, cancellationToken)).Id;
            await SavePendingCreationAsync(pending, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(pending.GoogleFeedbackFolderId))
        {
            pending.GoogleFeedbackFolderId = (await FindGoogleFolderByNameAsync(client, pending.GoogleAlbumFolderId, "feedback", cancellationToken) ??
                await client.CreateFolderAsync("feedback", pending.GoogleAlbumFolderId, cancellationToken)).Id;
            await SavePendingCreationAsync(pending, cancellationToken);
        }

        if (!pending.GoogleShared)
        {
            await client.ShareWithAnyoneAsync(pending.GoogleAlbumFolderId, "writer", cancellationToken);
            pending.GoogleShared = true;
            await SavePendingCreationAsync(pending, cancellationToken);
        }

        await StorePhotosAsync(
            pending,
            async (photo, contentType, contentStream, thumbnailStream, token) =>
            {
                DriveFileInfo driveFile;
                if (string.IsNullOrWhiteSpace(photo.DriveFileId))
                {
                    driveFile = await FindGoogleFileByNameAsync(client, pending.GooglePhotosFolderId!, photo.StoredFileName, contentType, token) ??
                        await client.UploadFileAsync(photo.StoredFileName, pending.GooglePhotosFolderId!, contentStream, contentType, token);
                    photo.DriveFileId = driveFile.Id;
                }

                DriveFileInfo thumbnailFile;
                if (string.IsNullOrWhiteSpace(photo.ThumbnailDriveFileId))
                {
                    var thumbnailFileName = AlbumPublishingWorkflow.CreateThumbnailFileName(photo.PhotoId);
                    thumbnailFile = await FindGoogleFileByNameAsync(client, pending.GooglePhotosFolderId!, thumbnailFileName, PhotoThumbnailGenerator.ContentType, token) ??
                        await client.UploadFileAsync(thumbnailFileName, pending.GooglePhotosFolderId!, thumbnailStream, PhotoThumbnailGenerator.ContentType, token);
                    photo.ThumbnailDriveFileId = thumbnailFile.Id;
                }

                photo.Reference = new PhotoReference
                {
                    Id = photo.PhotoId,
                    FileName = photo.StoredFileName,
                    ContentType = contentType,
                    BackendType = "google-drive-file",
                    DriveFileId = photo.DriveFileId,
                    DownloadUrl = GoogleDriveRestClient.CreatePublicDownloadUrl(photo.DriveFileId!),
                    ThumbnailDriveFileId = photo.ThumbnailDriveFileId,
                    ThumbnailDownloadUrl = GoogleDriveRestClient.CreatePublicDownloadUrl(photo.ThumbnailDriveFileId!),
                    ThumbnailContentType = PhotoThumbnailGenerator.ContentType
                };
            },
            progress,
            maximumParallelism,
            cancellationToken);

        var manifestWithoutId = AlbumPublishingWorkflow.CreateManifest(
            pending.AlbumId,
            pending.Title,
            pending.TargetNicePhotoCount,
            "local-photos-to-google-drive",
            "google-drive-folder",
            pending.Author,
            new GoogleDriveAlbumDetails
            {
                AlbumFolderId = pending.GoogleAlbumFolderId!,
                PhotosFolderId = pending.GooglePhotosFolderId!,
                FeedbackFolderId = pending.GoogleFeedbackFolderId!,
                ManifestFileId = pending.GoogleManifestFileId ?? "",
                AlbumFolderUrl = pending.GoogleAlbumFolderUrl!
            },
            null,
            pending.Photos.Select(photo => photo.Reference!).ToList());

        if (string.IsNullOrWhiteSpace(pending.GoogleManifestFileId))
        {
            await using var placeholderManifestStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifestWithoutId, JsonOptions));
            var manifestFile = await client.UploadFileAsync("album.json", pending.GoogleAlbumFolderId!, placeholderManifestStream, "application/json", cancellationToken);
            pending.GoogleManifestFileId = manifestFile.Id;
            await SavePendingCreationAsync(pending, cancellationToken);
        }

        var manifest = manifestWithoutId with
        {
            GoogleDrive = manifestWithoutId.GoogleDrive! with { ManifestFileId = pending.GoogleManifestFileId! }
        };
        progress?.Report(new AlbumCreationProgress("Writing manifest", pending.Photos.Count + 1, pending.Photos.Count + 3));
        await using var finalManifestStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
        await client.UpdateFileContentAsync(pending.GoogleManifestFileId!, finalManifestStream, "application/json", cancellationToken);

        return new AlbumCreationResumeResult(
            manifest,
            pending.GoogleAlbumFolderUrl!,
            AlbumLinkParser.CreatePicshareLink(pending.GoogleManifestFileId!, pending.GoogleAlbumFolderId!));
    }

    private async Task StorePhotosAsync(
        PendingAlbumCreation pending,
        Func<PendingAlbumCreationPhoto, string, Stream, Stream, CancellationToken, Task> storeAsync,
        IProgress<AlbumCreationProgress>? progress,
        int maximumParallelism,
        CancellationToken cancellationToken)
    {
        var completed = 0;
        var pendingChangesSinceSave = 0;
        var saveLock = new SemaphoreSlim(1, 1);
        await Parallel.ForEachAsync(
            pending.Photos.Select((photo, index) => (Photo: photo, Index: index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, maximumParallelism),
                CancellationToken = cancellationToken
            },
            async (entry, token) =>
            {
                var photo = entry.Photo;
                if (photo.Reference is not null)
                {
                    var skipped = Interlocked.Increment(ref completed);
                    progress?.Report(new AlbumCreationProgress($"Skipping {photo.StoredFileName}", skipped, pending.Photos.Count + 3));
                    return;
                }

                progress?.Report(new AlbumCreationProgress($"Uploading {photo.StoredFileName}", Volatile.Read(ref completed), pending.Photos.Count + 3));
                var contentType = GetContentType(photo.FileName);
                await using var thumbnailSourceStream = File.OpenRead(photo.LocalPath);
                await using var thumbnailStream = PhotoThumbnailGenerator.CreateJpegThumbnail(thumbnailSourceStream);
                await using var contentStream = File.OpenRead(photo.LocalPath);
                await storeAsync(photo, contentType, contentStream, thumbnailStream, token);
                if (Interlocked.Increment(ref pendingChangesSinceSave) >= PendingCreationSaveBatchSize)
                {
                    await saveLock.WaitAsync(token);
                    try
                    {
                        if (Interlocked.Exchange(ref pendingChangesSinceSave, 0) > 0)
                        {
                            await SavePendingCreationAsync(pending, token);
                        }
                    }
                    finally
                    {
                        saveLock.Release();
                    }
                }

                var uploaded = Interlocked.Increment(ref completed);
                progress?.Report(new AlbumCreationProgress($"Uploaded {photo.StoredFileName}", uploaded, pending.Photos.Count + 3));
            });

        await SavePendingCreationAsync(pending, saveLock, cancellationToken);
    }

    private async Task SavePendingCreationAsync(
        PendingAlbumCreation pending,
        SemaphoreSlim saveLock,
        CancellationToken cancellationToken)
    {
        await saveLock.WaitAsync(cancellationToken);
        try
        {
            await SavePendingCreationAsync(pending, cancellationToken);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private static async Task CopyToNewFileAtomicallyAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath) ??
            throw new InvalidOperationException("Missing destination directory.");
        var tempPath = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var destination = File.Create(tempPath))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            if (!File.Exists(destinationPath))
            {
                File.Move(tempPath, destinationPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static async Task<DriveFileInfo?> FindGoogleFileByNameAsync(
        GoogleDriveRestClient client,
        string parentFolderId,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        var item = await client.FindChildByNameAsync(parentFolderId, fileName, null, cancellationToken);
        return item is null
            ? null
            : await client.GetFileMetadataAsync(item.Id, cancellationToken);
    }

    private static async Task EnsureInitialWorkflowHistoryAsync(
        AlbumManifest manifest,
        Func<CancellationToken, Task<string>> getGoogleAccessTokenAsync,
        CancellationToken cancellationToken)
    {
        IReviewerFeedbackBackend backend;
        if (manifest.LocalFileSystem is not null)
        {
            backend = new LocalFileSystemReviewerFeedbackBackend(manifest.LocalFileSystem.FeedbackFolderPath);
        }
        else if (manifest.GoogleDrive is not null)
        {
            backend = new GoogleDriveReviewerFeedbackBackend(
                manifest.GoogleDrive.FeedbackFolderId,
                await getGoogleAccessTokenAsync(cancellationToken));
        }
        else
        {
            return;
        }

        await new ReviewerFeedbackService().EnsureInitialWorkflowHistoryAsync(
            manifest,
            backend,
            cancellationToken);
    }

    private static async Task<DriveFileInfo?> FindGoogleFolderByNameAsync(
        GoogleDriveRestClient client,
        string? parentFolderId,
        string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parentFolderId))
        {
            parentFolderId = "root";
        }

        var item = await client.FindChildByNameAsync(
            parentFolderId,
            Sanitize(string.IsNullOrWhiteSpace(name) ? "Picshare album" : name.Trim()),
            "application/vnd.google-apps.folder",
            cancellationToken);
        return item is null
            ? null
            : await client.GetFileMetadataAsync(item.Id, cancellationToken);
    }

    private async Task SavePendingCreationAsync(PendingAlbumCreation pending, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_pendingCreationFolderPath);
        await using var stream = File.Create(GetPendingCreationPath(pending.AlbumId));
        await JsonSerializer.SerializeAsync(stream, pending, JsonOptions, cancellationToken);
    }

    private void DeletePendingCreation(string albumId)
    {
        var path = GetPendingCreationPath(albumId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetPendingCreationPath(string albumId)
    {
        return Path.Combine(_pendingCreationFolderPath, $"{Sanitize(albumId)}.json");
    }

    private static string CreateAlbumFolderPath(string parentFolderPath, string title)
    {
        var name = Sanitize(string.IsNullOrWhiteSpace(title) ? "Picshare album" : title.Trim());
        return Path.Combine(parentFolderPath, name);
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
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

public sealed class PendingAlbumCreation
{
    public required string AlbumId { get; set; }
    public required string AlbumTypeId { get; set; }
    public required string Title { get; set; }
    public required int TargetNicePhotoCount { get; set; }
    public string? ParentDriveFolderId { get; set; }
    public string? ParentFolderPath { get; set; }
    public required FeedbackReviewerIdentity Author { get; set; }
    public List<PendingAlbumCreationPhoto> Photos { get; set; } = new();
    public string? LocalAlbumFolderPath { get; set; }
    public string? LocalPhotosFolderPath { get; set; }
    public string? LocalFeedbackFolderPath { get; set; }
    public string? LocalManifestFilePath { get; set; }
    public string? GoogleAlbumFolderId { get; set; }
    public string? GoogleAlbumFolderUrl { get; set; }
    public string? GooglePhotosFolderId { get; set; }
    public string? GoogleFeedbackFolderId { get; set; }
    public string? GoogleManifestFileId { get; set; }
    public bool GoogleShared { get; set; }
}

public sealed class PendingAlbumCreationPhoto
{
    public required string PhotoId { get; set; }
    public required string FileName { get; set; }
    public required string StoredFileName { get; set; }
    public required string SortKey { get; set; }
    public required string LocalPath { get; set; }
    public string? DriveFileId { get; set; }
    public string? ThumbnailDriveFileId { get; set; }
    public PhotoReference? Reference { get; set; }
}

public sealed record AlbumCreationProgress(string Message, int Value, int Maximum, string Warning = "");

public sealed record AlbumCreationResumeResult(AlbumManifest Manifest, string AlbumLocation, string PicshareLink);

public sealed record AlbumDestinationInspection(string DisplayName, bool HasItems);
