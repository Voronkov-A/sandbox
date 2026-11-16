using System.Diagnostics;

namespace Picshare.Services;

/// <summary>Coalesces groups across batches without changing album feedback.</summary>
public sealed class AiDuplicateDetectionWorkflow(IAiDuplicateDetector detector, AiDuplicateOptions options, Random? random = null,
    AiDuplicateDiagnostics? diagnostics = null)
{
    public const int MaximumIterations = 10;
    private readonly Random _random = random ?? Random.Shared;

    public async Task<IReadOnlyList<IReadOnlyList<string>>> FindDuplicatesAsync(
        IReadOnlyList<AiDuplicateCandidate> candidates,
        Func<string, int, CancellationToken, Task<byte[]>> loadImage,
        string additionalPrompt,
        Action<AiDuplicateProgress>? progress,
        CancellationToken cancellationToken)
    {
        options.Validate();
        var allIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (candidate.PhotoIds.Count == 0 || candidate.PhotoIds.Any(id => string.IsNullOrWhiteSpace(id) || !allIds.Add(id)))
                throw new ArgumentException("AI candidates must contain distinct, nonempty photo IDs.", nameof(candidates));
        }

        var groups = candidates.Select((candidate, index) => new Group([candidate], -1, index, index)).ToList();
        diagnostics?.AnalysisStarted(allIds.Count, groups.Count, options);
        var completedIterations = 0;
        var stopReason = "no_comparisons";
        var outcome = "failed";
        try
        {
            for (var iteration = 1; iteration <= MaximumIterations && groups.Count > 1; iteration++)
            {
                var iterationStartedAt = Stopwatch.GetTimestamp();
                cancellationToken.ThrowIfCancellationRequested();
                var inputCount = groups.Count;
                var batches = iteration == 1
                    ? groups.Chunk(options.BatchSize).Select(batch => batch.ToList()).ToList()
                    : Rebatch(groups, preferAdjacentBoundaries: iteration == 2);
                diagnostics?.IterationStarted(iteration, inputCount, batches.Count);
                var next = new List<Group>();
                for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Invoke(new(iteration, batchIndex, batches.Count));
                    var batch = batches[batchIndex];
                    if (batch.Count == 1)
                    {
                        next.Add(batch[0] with { PreviousBatch = batchIndex });
                        diagnostics?.BatchCompleted(iteration, batchIndex + 1, 1, 0, 1);
                        progress?.Invoke(new(iteration, batchIndex + 1, batches.Count));
                        continue;
                    }

                    var photos = new List<AiPhoto>(batch.Count);
                    foreach (var group in batch)
                    {
                        var candidate = group.Candidates[_random.Next(group.Candidates.Count)];
                        var representative = candidate.PhotoIds.Contains(candidate.BestPhotoId, StringComparer.Ordinal)
                            ? candidate.BestPhotoId! : candidate.PhotoIds[_random.Next(candidate.PhotoIds.Count)];
                        photos.Add(new(representative, await loadImage(representative, options.MaxImageDimension, cancellationToken).ConfigureAwait(false)));
                    }

                    var matches = await detector.FindDuplicatesAsync(photos, additionalPrompt, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    var byId = photos.Select((photo, index) => (photo.Id, Group: batch[index])).ToDictionary(item => item.Id, item => item.Group, StringComparer.Ordinal);
                    var used = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var match in matches)
                    {
                        if (match.Count < 2 || match.Any(id => !byId.ContainsKey(id) || !used.Add(id)))
                            throw new InvalidDataException("AI returned invalid or overlapping duplicate groups.");
                        next.Add(new(match.SelectMany(id => byId[id].Candidates).ToList(), batchIndex,
                            match.Min(id => byId[id].FirstInput), match.Max(id => byId[id].LastInput)));
                    }
                    next.AddRange(photos.Where(photo => !used.Contains(photo.Id)).Select(photo => byId[photo.Id] with { PreviousBatch = batchIndex }));
                    diagnostics?.BatchCompleted(iteration, batchIndex + 1, photos.Count, matches.Count, photos.Count - used.Count);
                    progress?.Invoke(new(iteration, batchIndex + 1, batches.Count));
                }

                groups = next;
                completedIterations = iteration;
                // Count singletons too: every input remains represented in the next iteration.
                stopReason = batches.Count == 1 ? "single_batch" :
                    (double)(inputCount - groups.Count) / inputCount <= 0.10 ? "low_efficiency" :
                    iteration == MaximumIterations ? "maximum_iterations" : "continue";
                diagnostics?.IterationCompleted(iteration, inputCount, groups.Count, stopReason, Stopwatch.GetElapsedTime(iterationStartedAt).TotalMilliseconds);
                if (stopReason != "continue") break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            outcome = "completed";
            // Existing groups which were never merged need no changes (including their chosen best).
            return groups.Where(group => group.Candidates.Count > 1)
                .Select(group => (IReadOnlyList<string>)group.Candidates.SelectMany(candidate => candidate.PhotoIds).ToList()).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            stopReason = "cancelled";
            throw;
        }
        finally
        {
            diagnostics?.AnalysisFinished(outcome, completedIterations, groups.Count,
                groups.Count(group => group.Candidates.Count > 1), outcome == "failed" ? "error" : stopReason);
        }
    }

    private List<List<Group>> Rebatch(List<Group> groups, bool preferAdjacentBoundaries)
    {
        var shuffled = groups.ToArray();
        _random.Shuffle(shuffled);
        var count = (groups.Count + options.BatchSize - 1) / options.BatchSize;
        var batches = Enumerable.Range(0, count).Select(_ => new List<Group>()).ToList();
        var neighbors = new Dictionary<Group, HashSet<Group>>(ReferenceEqualityComparer.Instance);
        if (preferAdjacentBoundaries)
        {
            var previousBatches = groups.GroupBy(group => group.PreviousBatch).OrderBy(batch => batch.Key).ToList();
            var pairs = previousBatches.Zip(previousBatches.Skip(1), (left, right) =>
                (Left: left.MaxBy(group => group.LastInput)!, Right: right.MinBy(group => group.FirstInput)!)).ToArray();
            _random.Shuffle(pairs);
            var preferredOrder = new List<Group>();
            var queued = new HashSet<Group>(ReferenceEqualityComparer.Instance);
            foreach (var (left, right) in pairs)
            {
                AddNeighbor(left, right);
                AddNeighbor(right, left);
                if (queued.Add(left)) preferredOrder.Add(left);
                if (queued.Add(right)) preferredOrder.Add(right);
            }
            preferredOrder.AddRange(shuffled.Where(queued.Add));
            shuffled = preferredOrder.ToArray();

            void AddNeighbor(Group group, Group neighbor)
            {
                if (!neighbors.TryGetValue(group, out var adjacent))
                    neighbors[group] = adjacent = new HashSet<Group>(ReferenceEqualityComparer.Instance);
                adjacent.Add(neighbor);
            }
        }
        foreach (var group in shuffled)
        {
            // Minimize repeated companions, then favor a previous boundary neighbor, then
            // balance sizes. Capacity and repeated-companion preferences can override a pair.
            var choices = Enumerable.Range(0, count).Where(index => batches[index].Count < options.BatchSize).ToArray();
            _random.Shuffle(choices);
            var destination = choices.OrderBy(index => batches[index].Count(item => item.PreviousBatch == group.PreviousBatch))
                .ThenByDescending(index => neighbors.TryGetValue(group, out var adjacent) ? batches[index].Count(adjacent.Contains) : 0)
                .ThenBy(index => batches[index].Count).First();
            batches[destination].Add(group);
        }
        return batches;
    }

    private sealed record Group(IReadOnlyList<AiDuplicateCandidate> Candidates, int PreviousBatch, int FirstInput, int LastInput);
}
