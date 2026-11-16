using Picshare.Services;

namespace Picshare.Tests;

public sealed class AiDuplicateDetectionWorkflowTests
{
    [Fact]
    public async Task OneBatch_IsProcessedExactlyOnce_AndExistingGroupsUseTheirBest()
    {
        var detector = new Detector(photos => [[photos[0].Id, photos[1].Id]]);
        var loaded = new List<string>();
        var result = await Run(detector, [new(["a", "b"], "b"), new(["c"]), new(["d"])],
            load: (id, size, token) => { Assert.Equal(512, size); loaded.Add(id); return Task.FromResult(Array.Empty<byte>()); });
        Assert.Single(detector.Calls);
        Assert.Equal(new[] { "b", "c", "d" }, loaded);
        Assert.Equal(new[] { "a", "b", "c" }, Assert.Single(result).Order());
    }

    [Fact]
    public async Task NoMatches_StopAfterFirstIteration_AndDoNotRewriteExistingGroups()
    {
        var detector = new Detector(_ => []);
        var result = await Run(detector, Candidates(105));
        Assert.Equal(new[] { 50, 50, 5 }, detector.Calls.Select(call => call.Count));
        Assert.Empty(result);
        var existing = new Detector(_ => []);
        Assert.Empty(await Run(existing, [new(["a", "b"], "b"), new(["c", "d"])]));
    }

    [Fact]
    public async Task FirstIteration_PreservesInputOrderAcrossBatchBoundaries()
    {
        var candidates = new[] { "z", "b", "y", "a", "x", "c", "w", "d", "v" }
            .Select(id => new AiDuplicateCandidate([id])).ToList();
        var detector = new Detector(_ => []);
        await Run(detector, candidates, batchSize: 4);
        Assert.Equal(new[] { "z", "b", "y", "a" }, detector.Calls[0].Select(photo => photo.Id));
        Assert.Equal(new[] { "x", "c", "w", "d" }, detector.Calls[1].Select(photo => photo.Id));
        // The final singleton requires no LLM request.
        Assert.Equal(2, detector.Calls.Count);
    }

    [Fact]
    public async Task SecondIteration_PrefersAdjacentBoundaries_EvenWhenAiReturnsGroupsOutOfOrder()
    {
        var calls = 0;
        var detector = new Detector(photos => ++calls <= 3
            ? [[photos[2].Id, photos[3].Id], [photos[0].Id, photos[1].Id]] : []);
        var result = await Run(detector, Candidates(12), batchSize: 4);
        var secondPass = detector.Calls.Skip(3).ToList();
        Assert.Equal(2, secondPass.Count);
        var batchesByOriginalPair = secondPass.SelectMany((batch, index) =>
            batch.Select(photo => (Pair: int.Parse(photo.Id) / 2, Batch: index))).ToDictionary(item => item.Pair, item => item.Batch);
        Assert.Equal(batchesByOriginalPair[1], batchesByOriginalPair[2]); // End of batch 0, start of batch 1.
        Assert.Equal(batchesByOriginalPair[3], batchesByOriginalPair[4]); // End of batch 1, start of batch 2.
        Assert.NotEqual(batchesByOriginalPair[2], batchesByOriginalPair[3]); // Earlier companions still spread out.
        Assert.All(secondPass, batch => Assert.InRange(batch.Count, 2, 4));
        Assert.Equal(Enumerable.Range(0, 12).Select(index => index.ToString()).Order(), result.SelectMany(group => group).Order());
    }

    [Fact]
    public async Task BoundaryPreferences_AreSoft_WhenTheyFormAChainLargerThanABatch()
    {
        var calls = 0;
        var detector = new Detector(photos => ++calls <= 10 ? [[photos[0].Id, photos[1].Id]] : []);
        var result = await Run(detector, Candidates(20), batchSize: 2);
        var secondPass = detector.Calls.Skip(10).ToList();
        Assert.Equal(5, secondPass.Count);
        Assert.All(secondPass, batch => Assert.Equal(2, batch.Count));
        Assert.Contains(secondPass, batch => Math.Abs(int.Parse(batch[0].Id) / 2 - int.Parse(batch[1].Id) / 2) == 1);
        Assert.Equal(10, secondPass.SelectMany(batch => batch).Select(photo => int.Parse(photo.Id) / 2).Distinct().Count());
        Assert.Equal(20, result.SelectMany(group => group).Distinct().Count());
    }

    [Fact]
    public async Task EfficiencyOfExactlyTenPercent_StopsAfterTheIteration()
    {
        var detector = new Detector(photos => [[photos[0].Id, photos[1].Id]]);
        var result = await Run(detector, Candidates(20), batchSize: 10);
        Assert.Equal(2, detector.Calls.Count);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task EfficientIteration_RebatchesUnmatchedPhotos_AndMergesGroupsTransitively()
    {
        var detector = new Detector(photos => [[photos[0].Id, photos[1].Id]]);
        var result = await Run(detector, Candidates(8), batchSize: 4);
        Assert.Equal(new[] { 4, 4, 3, 3, 4 }, detector.Calls.Select(call => call.Count));
        var allMembers = result.SelectMany(group => group).ToList();
        Assert.Equal(allMembers.Count, allMembers.Distinct().Count());
        Assert.Contains(result, group => group.Count > 2);
        // The second pass still contains every candidate not merged in the first pass.
        var secondPass = detector.Calls.Skip(2).Take(2).SelectMany(call => call).Select(photo => photo.Id).ToHashSet();
        Assert.Subset(secondPass, new HashSet<string> { "2", "3", "6", "7" });
    }

    [Fact]
    public async Task Rebatching_PrefersSeparatingPreviousCompanions()
    {
        var detector = new Detector(photos => photos.Chunk(2).Where(pair => pair.Length == 2)
            .Select(pair => (IReadOnlyList<string>)pair.Select(photo => photo.Id).ToArray()).ToList());
        await Run(detector, Candidates(32), batchSize: 4);
        var firstPassById = detector.Calls.Take(8).SelectMany((call, batch) => call.Select(photo => (photo.Id, batch)))
            .ToDictionary(item => item.Id, item => item.batch);
        foreach (var call in detector.Calls.Skip(8).Take(4))
            Assert.Equal(call.Count, call.Select(photo => firstPassById[photo.Id]).Distinct().Count());
    }

    [Fact]
    public async Task TenIterations_AreTheHardLimit()
    {
        var progress = new List<AiDuplicateProgress>();
        var detector = new Detector(photos => photos.Chunk(5).Where(chunk => chunk.Length >= 2)
            .Select(chunk => (IReadOnlyList<string>)new[] { chunk[0].Id, chunk[1].Id }).ToList());
        await Run(detector, Candidates(1200), progress: progress.Add);
        Assert.Equal(10, progress.Max(item => item.Iteration));
        Assert.True(progress.Last().BatchCount > 1);
    }

    [Fact]
    public async Task CancellationAfterAResponse_DoesNotReturnPartialResults()
    {
        using var cancellation = new CancellationTokenSource();
        var detector = new Detector(photos => { cancellation.Cancel(); return [[photos[0].Id, photos[1].Id]]; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(detector, Candidates(10), token: cancellation.Token));
        Assert.Single(detector.Calls);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("overlap")]
    [InlineData("singleton")]
    public async Task InvalidProviderGroups_AbortTheWorkflow(string invalid)
    {
        var detector = new Detector(photos => invalid switch
        {
            "unknown" => [[photos[0].Id, "unknown"]],
            "overlap" => [[photos[0].Id, photos[1].Id], [photos[1].Id, photos[2].Id]],
            _ => [[photos[0].Id]]
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(detector, Candidates(5)));
    }

    [Fact]
    public async Task FailureInLaterBatch_DoesNotReturnEarlierGroups()
    {
        var calls = 0;
        var detector = new Detector(photos => ++calls == 2 ? throw new HttpRequestException("failed") : [[photos[0].Id, photos[1].Id]]);
        await Assert.ThrowsAsync<HttpRequestException>(() => Run(detector, Candidates(8), batchSize: 4));
    }

    [Fact]
    public async Task AdditionalPrompt_AndConfiguredImageSize_ReachTheServices()
    {
        var detector = new Detector(_ => []);
        var workflow = new AiDuplicateDetectionWorkflow(detector, new(3, 256));
        await workflow.FindDuplicatesAsync(Candidates(4), (id, size, token) =>
        {
            Assert.Equal(256, size);
            return Task.FromResult(Array.Empty<byte>());
        }, "Keep different poses separate", null, default);
        Assert.Equal("Keep different poses separate", detector.Prompt);
    }

    private static IReadOnlyList<AiDuplicateCandidate> Candidates(int count) =>
        Enumerable.Range(0, count).Select(index => new AiDuplicateCandidate([index.ToString()])).ToList();

    private static Task<IReadOnlyList<IReadOnlyList<string>>> Run(Detector detector,
        IReadOnlyList<AiDuplicateCandidate> candidates, int batchSize = 50,
        Func<string, int, CancellationToken, Task<byte[]>>? load = null,
        Action<AiDuplicateProgress>? progress = null, CancellationToken token = default) =>
        new AiDuplicateDetectionWorkflow(detector, new(batchSize), new Random(42)).FindDuplicatesAsync(candidates,
            load ?? ((id, size, cancellation) => Task.FromResult(Array.Empty<byte>())), "", progress, token);

    private sealed class Detector(Func<IReadOnlyList<AiPhoto>, IReadOnlyList<IReadOnlyList<string>>> analyze) : IAiDuplicateDetector
    {
        public List<IReadOnlyList<AiPhoto>> Calls { get; } = [];
        public string Prompt { get; private set; } = "";

        public Task<IReadOnlyList<IReadOnlyList<string>>> FindDuplicatesAsync(IReadOnlyList<AiPhoto> photos, string additionalPrompt, CancellationToken cancellationToken)
        {
            Calls.Add(photos);
            Prompt = additionalPrompt;
            return Task.FromResult(analyze(photos));
        }
    }
}
