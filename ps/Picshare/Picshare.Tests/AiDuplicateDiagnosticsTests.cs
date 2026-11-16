using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Picshare.Services;

namespace Picshare.Tests;

public sealed class AiDuplicateDiagnosticsTests
{
    private static readonly object Usage = new
    {
        input_tokens = 100, output_tokens = 10, total_tokens = 110,
        input_tokens_details = new { cached_tokens = 20, cache_write_tokens = 30 },
        output_tokens_details = new { reasoning_tokens = 3 }
    };

    [Fact]
    public async Task DebugExport_ContainsIterationsRequestUsageAndRunTotals_WithoutPrivateContent()
    {
        using var temp = new TempLogs();
        using var logs = new DiagnosticLogService(temp.Logs);
        var diagnostics = new AiDuplicateDiagnostics(logs);
        using var handler = new Handler((_, _) => Task.FromResult(Response(Usage)));
        using var client = new HttpClient(handler);
        var detector = new OpenAiDuplicateDetector(client, "secret-api-key", diagnostics: diagnostics);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            await Run(detector, diagnostics, 8, 4);
        }
        finally { CultureInfo.CurrentCulture = culture; }

        var entries = await ReadEntries(logs, temp);
        Assert.All(entries, entry => Assert.Equal("AI.Duplicates", entry.Area));
        Assert.All(entries, entry => Assert.Equal("Debug", entry.Level));
        var messages = entries.Select(entry => entry.Message).ToList();
        Assert.Single(messages.Select(message => message.Split(';')[0]).Distinct());
        Assert.Contains(messages, message => message.Contains("Analysis started; originalPhotos=8; inputImages=8; batchSize=4; maxImageDimension=512"));
        var iterations = messages.Where(message => message.Contains("Iteration completed;")).ToList();
        Assert.Equal(3, iterations.Count);
        Assert.Contains("iteration=1; inputImages=8; outputGroups=6; mergedInputs=2; efficiency=0.2500", iterations[0]);
        Assert.Contains("requests=2; usageComplete=True; knownInputTokens=200; knownOutputTokens=20; knownTotalTokens=220", iterations[0]);
        Assert.Contains("stopReason=single_batch", iterations[2]);
        var usage = messages.Where(message => message.Contains("Request usage;")).ToList();
        Assert.Equal(5, usage.Count);
        Assert.All(usage, message => Assert.Contains("inputTokens=100; outputTokens=10; totalTokens=110; cachedInputTokens=20; cacheWriteInputTokens=30; reasoningTokens=3", message));
        var summary = Assert.Single(messages, message => message.Contains("Analysis finished;"));
        Assert.Contains("outcome=completed; iterations=3;", summary);
        Assert.Contains("requests=5; usageReports=5; usageComplete=True", summary);
        Assert.Contains("knownInputTokens=500; knownOutputTokens=50; knownTotalTokens=550", summary);
        Assert.Contains("knownCachedInputTokens=100; knownCacheWriteInputTokens=150; knownReasoningTokens=15", summary);
        Assert.DoesNotContain("secret-", string.Join('\n', messages));
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("secret-image")), string.Join('\n', messages));
    }

    [Theory]
    [InlineData("incomplete", "output_text", "{\"groups\":[]}")]
    [InlineData("failed", "output_text", "{\"groups\":[]}")]
    [InlineData("completed", "refusal", "secret-refusal")]
    [InlineData("completed", "output_text", "secret-invalid-output")]
    [InlineData(null, "output_text", "{\"groups\":[]}")]
    public async Task UnusableResponses_StillLogTheirReportedUsage(string? status, string type, string output)
    {
        using var temp = new TempLogs();
        using var logs = new DiagnosticLogService(temp.Logs);
        var diagnostics = new AiDuplicateDiagnostics(logs);
        using var handler = new Handler((_, _) => Task.FromResult(Response(Usage, status, type, output)));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(new OpenAiDuplicateDetector(client, "dummy-key", diagnostics: diagnostics), diagnostics, 2, 50));
        var messages = (await ReadEntries(logs, temp)).Select(entry => entry.Message).ToList();
        Assert.Contains("totalTokens=110", Assert.Single(messages, message => message.Contains("Request usage;")));
        var summary = Assert.Single(messages, message => message.Contains("Analysis finished;"));
        Assert.Contains("outcome=failed", summary);
        Assert.Contains("knownTotalTokens=110", summary);
        Assert.Contains("usageReports=1; usageComplete=True", summary);
        Assert.DoesNotContain("secret-", string.Join('\n', messages));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrInvalidUsage_IsExplicitlyUnknown_AndDoesNotPreventValidGrouping(bool invalid)
    {
        using var temp = new TempLogs();
        using var logs = new DiagnosticLogService(temp.Logs);
        var diagnostics = new AiDuplicateDiagnostics(logs);
        object? usage = invalid ? new { input_tokens = -1, output_tokens = "secret-invalid-usage", total_tokens = "secret-invalid-usage" } : null;
        using var handler = new Handler((_, _) => Task.FromResult(Response(usage)));
        using var client = new HttpClient(handler);
        Assert.Single(await Run(new OpenAiDuplicateDetector(client, "dummy-key", diagnostics: diagnostics), diagnostics, 2, 50));
        var messages = (await ReadEntries(logs, temp)).Select(entry => entry.Message).ToList();
        Assert.Contains(messages, message => message.Contains("inputTokens=unknown; outputTokens=unknown; totalTokens=unknown"));
        Assert.Contains(messages, message => message.Contains("requests=1; usageReports=0; usageComplete=False"));
        Assert.DoesNotContain("secret-", string.Join('\n', messages));
    }

    [Fact]
    public async Task HttpErrors_LogUnavailableUsage_WithoutProviderErrorBodies()
    {
        using var temp = new TempLogs();
        using var logs = new DiagnosticLogService(temp.Logs);
        var diagnostics = new AiDuplicateDiagnostics(logs);
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Content = new StringContent("secret-provider-error") }));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => Run(new OpenAiDuplicateDetector(client, "dummy-key", diagnostics: diagnostics), diagnostics, 2, 50));
        var messages = (await ReadEntries(logs, temp)).Select(entry => entry.Message).ToList();
        Assert.Contains(messages, message => message.Contains("status=unavailable; inputTokens=unknown"));
        Assert.Contains(messages, message => message.Contains("outcome=failed"));
        Assert.DoesNotContain("secret-", string.Join('\n', messages));
    }

    [Fact]
    public async Task CancellationDuringLaterBatch_PreservesUsageFromTheEarlierBatch()
    {
        using var temp = new TempLogs();
        using var logs = new DiagnosticLogService(temp.Logs);
        var diagnostics = new AiDuplicateDiagnostics(logs);
        using var cancellation = new CancellationTokenSource();
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var handler = new Handler(async (_, token) =>
        {
            if (++requests == 1) return Response(Usage);
            secondStarted.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable");
        });
        using var client = new HttpClient(handler);
        var run = Run(new OpenAiDuplicateDetector(client, "dummy-key", diagnostics: diagnostics), diagnostics, 4, 2, cancellation.Token);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var messages = (await ReadEntries(logs, temp)).Select(entry => entry.Message).ToList();
        var summary = Assert.Single(messages, message => message.Contains("Analysis finished;"));
        Assert.Contains("outcome=cancelled", summary);
        Assert.Contains("stopReason=cancelled; requests=2; usageReports=1; usageComplete=False", summary);
        Assert.Contains("knownTotalTokens=110", summary);
    }

    [Theory]
    [InlineData(20, 10, "low_efficiency")]
    [InlineData(1200, 50, "maximum_iterations")]
    public async Task IterationSummary_LogsWhyTheWorkflowStopped(int count, int batchSize, string stopReason)
    {
        using var temp = new TempLogs();
        using var logs = new DiagnosticLogService(temp.Logs);
        var diagnostics = new AiDuplicateDiagnostics(logs);
        await Run(new Detector(count == 20), diagnostics, count, batchSize);
        var messages = (await ReadEntries(logs, temp)).Select(entry => entry.Message).ToList();
        Assert.Contains(messages, message => message.Contains("Iteration completed;") && message.Contains($"stopReason={stopReason}"));
        Assert.Contains(messages, message => message.Contains("Analysis finished;") && message.Contains($"stopReason={stopReason}"));
    }

    private static Task<IReadOnlyList<IReadOnlyList<string>>> Run(IAiDuplicateDetector detector, AiDuplicateDiagnostics diagnostics,
        int count, int batchSize, CancellationToken token = default) =>
        new AiDuplicateDetectionWorkflow(detector, new(batchSize), new Random(42), diagnostics).FindDuplicatesAsync(
            Enumerable.Range(0, count).Select(index => new AiDuplicateCandidate([$"secret-photo-{index}"])).ToList(),
            (_, _, _) => Task.FromResult(Encoding.UTF8.GetBytes("secret-image")), "secret-prompt", null, token);

    private static HttpResponseMessage Response(object? usage, string? status = "completed", string type = "output_text", string output = "{\"groups\":[[0,1]]}") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            status, usage,
            output = new[] { new { type = "message", content = new[] { new { type, text = output } } } }
        }), Encoding.UTF8, "application/json")
    };

    private static async Task<List<DiagnosticLogEntry>> ReadEntries(DiagnosticLogService logs, TempLogs temp)
    {
        using var archive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromHours(1), temp.Exports));
        using var reader = new StreamReader(archive.GetEntry("logs.jsonl")!.Open());
        return (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<DiagnosticLogEntry>(line)!).ToList();
    }

    private sealed class Detector(bool onePair) : IAiDuplicateDetector
    {
        public Task<IReadOnlyList<IReadOnlyList<string>>> FindDuplicatesAsync(IReadOnlyList<AiPhoto> photos, string additionalPrompt, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IReadOnlyList<string>>>(onePair ? [[photos[0].Id, photos[1].Id]] : photos.Chunk(5)
                .Where(chunk => chunk.Length >= 2).Select(chunk => (IReadOnlyList<string>)new[] { chunk[0].Id, chunk[1].Id }).ToList());
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class TempLogs : IDisposable
    {
        private string Root { get; } = Path.Combine(Path.GetTempPath(), "picshare-ai-diagnostics-tests", Guid.NewGuid().ToString("N"));
        public string Logs => Path.Combine(Root, "logs");
        public string Exports => Path.Combine(Root, "exports");
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
