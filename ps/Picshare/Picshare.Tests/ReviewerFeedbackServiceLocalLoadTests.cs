using System.Text.Json;
using Picshare.Models;
using Picshare.Services;

namespace Picshare.Tests;

public sealed class ReviewerFeedbackServiceLocalLoadTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [Fact]
    public async Task TryLoadLocalAsync_ReturnsLocalStateWhenComplete()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var service = new ReviewerFeedbackService(tempRoot);
            var manifest = CreateManifest();
            var reviewer = CreateReviewer();
            var localFolder = GetLocalFeedbackFolder(tempRoot, manifest.AlbumId, reviewer.BackendType, reviewer.UserId);
            Directory.CreateDirectory(localFolder);

            var database = new ReviewerFeedbackDatabase
            {
                AlbumId = manifest.AlbumId,
                ReviewerUserId = reviewer.UserId
            };
            database.PhotoCategories["photo-1"] = "nice";
            var status = new ReviewerFeedbackStatus
            {
                AlbumId = manifest.AlbumId,
                Reviewer = reviewer,
                Status = ReviewerFeedbackStatusKind.InProgress
            };
            var state = new ReviewerFeedbackLocalState
            {
                ReviewerStoreId = "store-1",
                RemoteRevision = "database-revision-1",
                StatusRemoteRevision = "status-revision-1"
            };

            await SaveJsonAsync(Path.Combine(localFolder, "feedback.json"), database);
            await SaveJsonAsync(Path.Combine(localFolder, "status.json"), status);
            await SaveJsonAsync(Path.Combine(localFolder, "sync-state.json"), state);

            var result = await service.TryLoadLocalAsync(manifest, reviewer, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("store-1", result.Session.ReviewerStoreId);
            Assert.Equal("nice", result.Database.PhotoCategories["photo-1"]);
            Assert.Equal(ReviewerFeedbackStatusKind.InProgress, result.Status.Status);
            Assert.False(result.ConcurrentRemoteUpdate);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task TryLoadLocalAsync_ReturnsNullWhenReviewerStoreIdIsMissing()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var service = new ReviewerFeedbackService(tempRoot);
            var manifest = CreateManifest();
            var reviewer = CreateReviewer();
            var localFolder = GetLocalFeedbackFolder(tempRoot, manifest.AlbumId, reviewer.BackendType, reviewer.UserId);
            Directory.CreateDirectory(localFolder);

            await SaveJsonAsync(
                Path.Combine(localFolder, "feedback.json"),
                new ReviewerFeedbackDatabase
                {
                    AlbumId = manifest.AlbumId,
                    ReviewerUserId = reviewer.UserId
                });
            await SaveJsonAsync(
                Path.Combine(localFolder, "status.json"),
                new ReviewerFeedbackStatus
                {
                    AlbumId = manifest.AlbumId,
                    Reviewer = reviewer
                });
            await SaveJsonAsync(Path.Combine(localFolder, "sync-state.json"), new ReviewerFeedbackLocalState());

            var result = await service.TryLoadLocalAsync(manifest, reviewer, CancellationToken.None);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task SaveJsonAsync<T>(string path, T value)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
    }

    private static AlbumManifest CreateManifest()
    {
        return new AlbumManifest
        {
            AlbumId = "album-1",
            Title = "Album",
            CreatedAt = DateTimeOffset.UtcNow,
            TargetNicePhotoCount = 1,
            PhotoBackendType = "local",
            DatabaseBackendType = "local",
            Author = CreateReviewer(),
            LocalFileSystem = new LocalFileSystemAlbumDetails
            {
                RootPath = "root",
                PhotosFolderPath = "photos",
                FeedbackFolderPath = "feedback",
                ManifestFilePath = "album.json"
            },
            Photos = Array.Empty<PhotoReference>()
        };
    }

    private static FeedbackReviewerIdentity CreateReviewer()
    {
        return new FeedbackReviewerIdentity
        {
            BackendType = "local",
            UserId = "reviewer-1",
            DisplayName = "Reviewer"
        };
    }

    private static string GetLocalFeedbackFolder(string tempRoot, string albumId, string backendType, string userId)
    {
        return Path.Combine(tempRoot, "Picshare", "feedback", albumId, backendType, userId);
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "Picshare.Tests", Guid.NewGuid().ToString("N"));
    }
}
