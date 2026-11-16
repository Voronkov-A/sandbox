using System.Diagnostics;
using System.Globalization;

namespace Picshare.Services;

/// <summary>One analysis's counts and provider-reported usage, without photo or prompt content.</summary>
public sealed class AiDuplicateDiagnostics(DiagnosticLogService logs)
{
    private readonly string _operationId = Guid.NewGuid().ToString("N");
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private readonly long[] _knownTokens = new long[6];
    private int _requests;
    private int _usageReports;
    private long _iterationInputTokens;
    private long _iterationOutputTokens;
    private long _iterationTotalTokens;
    private int _iterationRequests;
    private int _iterationUsageReports;

    public void AnalysisStarted(int originalPhotos, int inputImages, AiDuplicateOptions options) => Write(
        $"Analysis started; originalPhotos={originalPhotos}; inputImages={inputImages}; batchSize={options.BatchSize}; maxImageDimension={options.MaxImageDimension}");

    public void IterationStarted(int iteration, int inputImages, int batches)
    {
        _iterationInputTokens = _knownTokens[0];
        _iterationOutputTokens = _knownTokens[1];
        _iterationTotalTokens = _knownTokens[2];
        _iterationRequests = _requests;
        _iterationUsageReports = _usageReports;
        Write($"Iteration started; iteration={iteration}; inputImages={inputImages}; batches={batches}");
    }

    public void BatchCompleted(int iteration, int batch, int inputImages, int duplicateGroups, int unmatchedInputs) => Write(
        $"Batch completed; iteration={iteration}; batch={batch}; inputImages={inputImages}; duplicateGroups={duplicateGroups}; unmatchedInputs={unmatchedInputs}; outputGroups={duplicateGroups + unmatchedInputs}");

    public void IterationCompleted(int iteration, int inputImages, int outputGroups, string stopReason, double elapsedMs) => Write(
        FormattableString.Invariant($"Iteration completed; iteration={iteration}; inputImages={inputImages}; outputGroups={outputGroups}; mergedInputs={inputImages - outputGroups}; efficiency={(double)(inputImages - outputGroups) / inputImages:F4}; requests={_requests - _iterationRequests}; usageComplete={_requests - _iterationRequests == _usageReports - _iterationUsageReports}; knownInputTokens={_knownTokens[0] - _iterationInputTokens}; knownOutputTokens={_knownTokens[1] - _iterationOutputTokens}; knownTotalTokens={_knownTokens[2] - _iterationTotalTokens}; elapsedMs={elapsedMs:F0}; stopReason={stopReason}"));

    public int RequestStarted(int inputImages, string model)
    {
        var request = ++_requests;
        Write($"Request started; request={request}; provider=OpenAI; model={model}; inputImages={inputImages}");
        return request;
    }

    public void RequestCompleted(int request, string status, AiTokenUsage usage, double elapsedMs)
    {
        long?[] values = [usage.Input, usage.Output, usage.Total, usage.CachedInput, usage.CacheWriteInput, usage.Reasoning];
        for (var index = 0; index < values.Length; index++) _knownTokens[index] += values[index] ?? 0;
        if (usage.Input.HasValue && usage.Output.HasValue && usage.Total.HasValue) _usageReports++;
        Write(FormattableString.Invariant($"Request usage; request={request}; status={status}; inputTokens={Number(usage.Input)}; outputTokens={Number(usage.Output)}; totalTokens={Number(usage.Total)}; cachedInputTokens={Number(usage.CachedInput)}; cacheWriteInputTokens={Number(usage.CacheWriteInput)}; reasoningTokens={Number(usage.Reasoning)}; elapsedMs={elapsedMs:F0}"));
    }

    public void AnalysisFinished(string outcome, int iterations, int outputGroups, int newDuplicateGroups, string stopReason) => Write(
        FormattableString.Invariant($"Analysis finished; outcome={outcome}; iterations={iterations}; outputGroups={outputGroups}; newDuplicateGroups={newDuplicateGroups}; stopReason={stopReason}; requests={_requests}; usageReports={_usageReports}; usageComplete={_requests == _usageReports}; knownInputTokens={_knownTokens[0]}; knownOutputTokens={_knownTokens[1]}; knownTotalTokens={_knownTokens[2]}; knownCachedInputTokens={_knownTokens[3]}; knownCacheWriteInputTokens={_knownTokens[4]}; knownReasoningTokens={_knownTokens[5]}; elapsedMs={Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds:F0}"));

    private void Write(string message) => logs.Write("Debug", "AI.Duplicates", $"operation={_operationId}; {message}");
    private static string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
}

public sealed record AiTokenUsage(long? Input = null, long? Output = null, long? Total = null,
    long? CachedInput = null, long? CacheWriteInput = null, long? Reasoning = null);
