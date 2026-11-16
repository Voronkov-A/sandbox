using System.Text.Json;
using Picshare.Models;
using Picshare.Services;

namespace Picshare.Tests;

public sealed class AiDuplicateFeedbackMergerTests
{
    [Fact]
    public void MergesExistingGroups_PreservesUniqueBestAndUnrelatedGroups_WithoutMutatingInput()
    {
        var database = CreateDatabase();
        var before = JsonSerializer.Serialize(database);
        var updated = AiDuplicateFeedbackMerger.Merge(database, [["a", "b", "c"]], new HashSet<string>(["a", "b", "c"]));
        Assert.Equal(before, JsonSerializer.Serialize(database));
        Assert.Equal(2, updated.DuplicateGroups.Count);
        Assert.Equal("b", updated.DuplicateGroups.Single(group => group.PhotoIds.Contains("a")).BestPhotoId);
        Assert.Equal("untouched", updated.DuplicateGroups.Single(group => group.PhotoIds.Contains("x")).Id);
        Assert.All(new[] { "a", "b", "c" }, id => Assert.Equal("nice", updated.PhotoCategories[id]));
        Assert.Equal(90, updated.PhotoRotations["b"]);
        Assert.Equal(4, updated.PhotoScores["c"]);
        Assert.Equal("x", updated.BookmarkPhotoId);
    }

    [Fact]
    public void TwoExistingBestPhotos_AreLeftUnresolved_WhenTheirGroupsMerge()
    {
        var database = CreateDatabase();
        database.DuplicateGroups.Add(new() { PhotoIds = ["c", "d"], BestPhotoId = "c" });
        var updated = AiDuplicateFeedbackMerger.Merge(database, [["a", "b", "c", "d"]], new HashSet<string>(["a", "b", "c", "d"]));
        Assert.Empty(updated.DuplicateGroups.Single(group => group.PhotoIds.Contains("a")).BestPhotoId);
    }

    [Theory]
    [InlineData("split")]
    [InlineData("unknown")]
    [InlineData("frozen")]
    [InlineData("overlap")]
    public void InvalidMerges_LeaveTheOriginalFeedbackUntouched(string problem)
    {
        var database = CreateDatabase();
        if (problem == "frozen") database.FrozenPhotoIds.Add("b");
        var before = JsonSerializer.Serialize(database);
        IReadOnlyList<IReadOnlyList<string>> groups = problem switch
        {
            "split" => [["a", "c"]],
            "unknown" => [["a", "b", "unknown"]],
            "overlap" => [["a", "b", "c"], ["c", "d"]],
            _ => [["a", "b", "c"]]
        };
        Assert.Throws<InvalidDataException>(() => AiDuplicateFeedbackMerger.Merge(database, groups, new HashSet<string>(["a", "b", "c", "d"])));
        Assert.Equal(before, JsonSerializer.Serialize(database));
    }

    [Fact]
    public async Task CompleteMergedFeedback_CanBePersistedUsingTheExistingFeedbackFormat()
    {
        var root = Path.Combine(Path.GetTempPath(), "picshare-ai-feedback-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new ReviewerFeedbackService(root);
            var session = new ReviewerFeedbackSession
            {
                AlbumId = "album", ReviewerUserId = "reviewer", ReviewerStoreId = "reviewer",
                LocalFolderPath = root, State = new ReviewerFeedbackLocalState()
            };
            var updated = AiDuplicateFeedbackMerger.Merge(CreateDatabase(), [["a", "b", "c"]], new HashSet<string>(["a", "b", "c"]));
            await service.RestoreLocalDatabaseAsync(session, updated, default);
            var persisted = JsonSerializer.Deserialize<ReviewerFeedbackDatabase>(await File.ReadAllTextAsync(Path.Combine(root, "feedback.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.True(session.State.LocalDirty);
            Assert.Equal("b", persisted.DuplicateGroups.Single(group => group.PhotoIds.Contains("a")).BestPhotoId);
            Assert.Equal("nice", persisted.PhotoCategories["a"]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ReviewerFeedbackDatabase CreateDatabase() => new()
    {
        AlbumId = "album", ReviewerUserId = "reviewer", BookmarkPhotoId = "x",
        DuplicateGroups = [new() { PhotoIds = ["a", "b"], BestPhotoId = "b" }, new() { Id = "untouched", PhotoIds = ["x", "y"], BestPhotoId = "y" }],
        PhotoCategories = new() { ["b"] = "trash", ["c"] = "nice" },
        PhotoRotations = new() { ["b"] = 90 }, PhotoScores = new() { ["c"] = 4 }
    };
}
