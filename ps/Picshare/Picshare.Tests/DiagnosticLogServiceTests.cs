using System.IO.Compression;
using System.Text.Json;
using Picshare.Services;

namespace Picshare.Tests;

public sealed class DiagnosticLogServiceTests
{
    [Fact]
    public async Task SlowPersistence_DoesNotBlockProducersAndPreservesEventTimeAndThread()
    {
        using var temp = new TempDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var logs = new DiagnosticLogService(temp.Logs, time);
        using var blocker = new BlockingException();
        var first = Task.Run(() => logs.Write("Error", "Test", "slow exception formatting", blocker));
        Assert.True(blocker.Entered.Wait(TimeSpan.FromSeconds(5)));
        var eventTime = time.Now;
        var producerThread = 0;
        try
        {
            await Task.Run(() =>
            {
                producerThread = Environment.CurrentManagedThreadId;
                for (var i = 0; i < 100; i++) logs.Write("Trace", "Test", $"queued {i}");
            }).WaitAsync(TimeSpan.FromSeconds(5));
            time.Now = time.Now.AddMinutes(1);
        }
        finally { blocker.Release.Set(); }
        await first;
        using var archive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromHours(1), temp.Exports));
        var entries = ReadEntries(archive).Where(entry => entry.Message.StartsWith("queued ")).ToList();
        Assert.Equal(100, entries.Count);
        Assert.All(entries, entry => { Assert.Equal(eventTime, entry.Timestamp); Assert.Equal(producerThread, entry.ThreadId); });
    }

    [Fact]
    public async Task SlowPersistence_BoundsBacklogAndReportsOverflowInsteadOfBlocking()
    {
        using var temp = new TempDirectory();
        using var logs = new DiagnosticLogService(temp.Logs);
        using var blocker = new BlockingException();
        var first = Task.Run(() => logs.Write("Error", "Test", "slow record", blocker));
        Assert.True(blocker.Entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            await Task.Run(() =>
            {
                // Hit the byte bound, rather than allowing huge messages to exhaust mobile memory.
                var message = new string('x', 64 * 1024);
                for (var i = 0; i < 1000; i++) logs.Write("Trace", "Test", message);
            }).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { blocker.Release.Set(); }
        await first;
        logs.Flush();
        using var archive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromHours(1), temp.Exports));
        var entries = ReadEntries(archive);
        Assert.InRange(entries.Count, 2, 70);
        Assert.True(entries.Any(entry => entry.Area == "Diagnostics" && entry.Level == "Warning" && entry.Message.Contains("queue was full")),
            "The archive must explain the events skipped while storage was blocked.");
    }

    private sealed class BlockingException : Exception, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public override string ToString()
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(15));
            return "slow formatting complete";
        }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }

    [Fact]
    public async Task DeferredAvaloniaFormatting_PreservesPlaceholdersAndRedactsValues()
    {
        using var temp = new TempDirectory();
        using var logs = new DiagnosticLogService(temp.Logs);
        var method = typeof(DiagnosticLogService).GetMethod("WriteTemplate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        method.Invoke(logs, ["Trace", "Avalonia.Test", "Offset={Offset}; Source={Source}; Endpoint={Endpoint}",
            new string?[] { "12,34", null, "https://example.test/image?access_token=secret-value" }]);
        using var archive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromHours(1), temp.Exports));
        var entry = Assert.Single(ReadEntries(archive));
        Assert.Equal("Trace", entry.Level);
        Assert.Equal("Offset=12,34; Source=null; Endpoint=https://example.test/image?[redacted]", entry.Message);
    }

    [Fact]
    public void BackgroundWrites_UseOneOpenFileAndFlushAndDisposeDrainAcceptedRecords()
    {
        using var temp = new TempDirectory();
        using var logs = new DiagnosticLogService(temp.Logs);
        for (var index = 0; index < 10; index++) logs.Write("Trace", "Test", $"event {index}");
        logs.Flush();
        var file = Assert.Single(Directory.GetFiles(temp.Logs));
        Assert.Throws<IOException>(() => { using var exclusive = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
        Assert.Equal(10, ReadActiveLog(file).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        logs.Write("Error", "Test", "immediate error");
        logs.Dispose();
        using var reopened = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var reader = new StreamReader(reopened);
        Assert.Equal(11, reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Archive_FiltersIndividualEventsAcrossSessionsAndKeepsBoundaryEvents()
    {
        using var temp = new TempDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var start = time.GetUtcNow();
        using var first = new DiagnosticLogService(temp.Logs, time);
        first.Write("Debug", "Test", "outside period");
        time.Now = start.AddMinutes(1);
        first.Write("Debug", "Test", "lower boundary");
        first.Dispose();
        time.Now = start.AddMinutes(2);
        using var second = new DiagnosticLogService(temp.Logs, time);
        second.Write("Error", "Test", "upper boundary", new InvalidOperationException("exception detail"));
        time.Now = start.AddMinutes(3);
        second.Write("Debug", "Test", "future event");
        time.Now = start.AddMinutes(2);

        var path = await second.CreateArchiveAsync(TimeSpan.FromMinutes(1), temp.Exports);
        using var archive = ZipFile.OpenRead(path);
        var entries = ReadEntries(archive);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.Message == "lower boundary");
        Assert.Contains(entries, entry => entry.Message.Contains("InvalidOperationException: exception detail"));
        using var metadata = JsonDocument.Parse(ReadEntry(archive, "diagnostics.json"));
        Assert.Equal(2, metadata.RootElement.GetProperty("ExportedEntryCount").GetInt32());
        Assert.Equal(start.AddMinutes(1), metadata.RootElement.GetProperty("RequestedFromUtc").GetDateTimeOffset());
        Assert.Equal(start.AddMinutes(2), metadata.RootElement.GetProperty("RequestedUntilUtc").GetDateTimeOffset());
        Assert.Equal(start, metadata.RootElement.GetProperty("EarliestRetainedEntryUtc").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public async Task Archive_PeriodEndsAtClickEvenWhenSavePickerTakesTime(int periodSeconds)
    {
        using var temp = new TempDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var logs = new DiagnosticLogService(temp.Logs, time);
        var click = time.GetUtcNow();
        logs.Write("Debug", "Test", "at click");
        time.Now = click.AddMinutes(2);
        logs.Write("Debug", "Test", "after save picker");

        using var archive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromSeconds(periodSeconds), temp.Exports, click));
        Assert.Equal("at click", Assert.Single(ReadEntries(archive)).Message);
    }

    [Theory]
    [InlineData("Authorization: Bearer top-secret-token")]
    [InlineData("{\"access_token\":\"top-secret-token\",\"refresh_token\":\"top-secret-token\"}")]
    [InlineData("client_secret=top-secret-token&code=top-secret-token")]
    [InlineData("code_verifier: top-secret-token")]
    [InlineData("GET https://example.test/resource?key=top-secret-token&code=top-secret-token")]
    [InlineData("token ya29.top-secret-token")]
    [InlineData("secret GOCSPX-top-secret-token")]
    public async Task Credentials_AreRedactedBeforePersistenceAndExport(string message)
    {
        using var temp = new TempDirectory();
        using var logs = new DiagnosticLogService(temp.Logs);
        logs.Write("Error", "Test", message, new Exception(message));
        logs.Flush();
        var stored = ReadActiveLog(Directory.GetFiles(temp.Logs).Single());
        Assert.DoesNotContain("top-secret-token", stored);
        using var archive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromHours(1), temp.Exports));
        Assert.DoesNotContain("top-secret-token", ReadEntry(archive, "logs.jsonl"));
    }

    [Fact]
    public async Task Archive_WithNoLogsStillContainsMetadataAndValidEmptyLog()
    {
        using var temp = new TempDirectory();
        using var logs = new DiagnosticLogService(temp.Logs);
        using var archive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromDays(1), temp.Exports));
        Assert.Empty(ReadEntries(archive));
        using var metadata = JsonDocument.Parse(ReadEntry(archive, "diagnostics.json"));
        Assert.Equal(0, metadata.RootElement.GetProperty("ExportedEntryCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, metadata.RootElement.GetProperty("EarliestRetainedEntryUtc").ValueKind);
    }

    [Fact]
    public async Task Archive_ToleratesDamagedRecordsAndPrunesExpiredLogsAndExports()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Logs);
        Directory.CreateDirectory(temp.Exports);
        var expired = Path.Combine(temp.Logs, "log-expired.jsonl");
        File.WriteAllText(expired, "expired");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-31));
        File.WriteAllText(Path.Combine(temp.Logs, "log-damaged.jsonl"), "{partial\n");
        var oldExport = Path.Combine(temp.Exports, "picshare-logs-old.zip");
        var recentExport = Path.Combine(temp.Exports, "picshare-logs-recent.zip");
        File.WriteAllText(oldExport, "old");
        File.WriteAllText(recentExport, "recent");
        File.SetLastWriteTimeUtc(oldExport, DateTime.UtcNow.AddDays(-2));
        using var logs = new DiagnosticLogService(temp.Logs);
        logs.Write("Debug", "Test", "valid");

        using var archive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromDays(30), temp.Exports));
        Assert.Equal("valid", Assert.Single(ReadEntries(archive)).Message);
        Assert.False(File.Exists(expired));
        Assert.False(File.Exists(oldExport));
        Assert.True(File.Exists(recentExport));
    }

    [Fact]
    public async Task ConcurrentWritersAndExport_ProduceCompleteJsonRecords()
    {
        using var temp = new TempDirectory();
        using var logs = new DiagnosticLogService(temp.Logs);
        logs.Write("Debug", "Test", "initial");
        var writers = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            for (var index = 0; index < 100; index++) logs.Write("Debug", "Test", $"{worker}:{index}");
        })).ToArray();
        var export = logs.CreateArchiveAsync(TimeSpan.FromHours(1), temp.Exports);
        await Task.WhenAll(writers);
        using (var concurrentArchive = ZipFile.OpenRead(await export))
            Assert.NotEmpty(ReadEntries(concurrentArchive));
        using var finalArchive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromHours(1), temp.Exports));
        Assert.Equal(401, ReadEntries(finalArchive).Count);
    }

    [Fact]
    public async Task DailyRotation_CompressesClosedChunkAndExportsBothCompressedAndActiveLogs()
    {
        using var temp = new TempDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var logs = new DiagnosticLogService(temp.Logs, time);
        logs.Write("Debug", "Test", "previous day");
        time.Now = time.Now.AddDays(1);
        logs.Write("Debug", "Test", "current day");
        logs.Flush();
        Assert.Single(Directory.GetFiles(temp.Logs, "*.jsonl.gz"));
        Assert.Single(Directory.GetFiles(temp.Logs, "*.jsonl"));
        using var archive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromDays(2), temp.Exports));
        Assert.Equal(2, ReadEntries(archive).Count);
    }

    [Fact]
    public async Task RestartMaintenance_CompressesPreviousSessionWithoutResettingItsTimestamp()
    {
        using var temp = new TempDirectory();
        using var first = new DiagnosticLogService(temp.Logs);
        first.Write("Debug", "Test", "previous session");
        first.Dispose();
        var raw = Directory.GetFiles(temp.Logs).Single();
        var modified = File.GetLastWriteTimeUtc(raw);
        using var restarted = new DiagnosticLogService(temp.Logs);
        restarted.Maintain();
        Assert.False(File.Exists(raw));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(raw + ".gz"));
        using var archive = ZipFile.OpenRead(await restarted.CreateArchiveAsync(TimeSpan.FromHours(1), temp.Exports));
        Assert.Equal("previous session", Assert.Single(ReadEntries(archive)).Message);
    }

    [Fact]
    public void IdleMaintenance_DeletesRawAndCompressedLogsOlderThanOneCalendarMonth()
    {
        using var temp = new TempDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        Directory.CreateDirectory(temp.Logs);
        var cutoff = time.Now.AddMonths(-1).UtcDateTime;
        var expiredRaw = Path.Combine(temp.Logs, "log-old.jsonl");
        var expiredArchive = Path.Combine(temp.Logs, "log-old-archive.jsonl.gz");
        var recentArchive = Path.Combine(temp.Logs, "log-recent.jsonl.gz");
        foreach (var file in new[] { expiredRaw, expiredArchive, recentArchive }) File.WriteAllText(file, "test");
        File.SetLastWriteTimeUtc(expiredRaw, cutoff.AddDays(-1));
        File.SetLastWriteTimeUtc(expiredArchive, cutoff.AddDays(-1));
        using var logs = new DiagnosticLogService(temp.Logs, time);
        logs.Maintain();
        Assert.False(File.Exists(expiredRaw));
        Assert.False(File.Exists(expiredArchive));
        Assert.True(File.Exists(recentArchive));
    }

    [Fact]
    public void IdleMaintenance_RotatesAndExpiresThePreviouslyActiveFile()
    {
        using var temp = new TempDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var logs = new DiagnosticLogService(temp.Logs, time);
        logs.Write("Debug", "Test", "old active file");
        time.Now = time.Now.AddMonths(1).AddDays(1);
        logs.Maintain();
        Assert.Empty(Directory.GetFiles(temp.Logs));
    }

    [Fact]
    public async Task StorageCap_PrunesOldChunksAndRotationPreservesRecentRecords()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Logs);
        var oversized = Path.Combine(temp.Logs, "log-large.jsonl");
        using (var file = File.Create(oversized)) file.SetLength(DiagnosticLogService.MaximumStorageBytes);
        using var logs = new DiagnosticLogService(temp.Logs);
        var detail = new string('x', 64 * 1024);
        for (var index = 0; index < 40; index++) logs.Write("Debug", "Test", detail);
        logs.Flush();
        Assert.False(File.Exists(oversized));
        Assert.True(Directory.GetFiles(temp.Logs).Length >= 2);
        Assert.True(new DirectoryInfo(temp.Logs).GetFiles().Sum(file => file.Length) <= DiagnosticLogService.MaximumStorageBytes);
        using var archive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromHours(1), temp.Exports));
        Assert.Equal(40, ReadEntries(archive).Count);
    }

    [Fact]
    public async Task HttpLogging_IncludesStatusAndTimingWithoutHeadersBodiesOrUrlCredentials()
    {
        using var temp = new TempDirectory();
        using var logs = new DiagnosticLogService(temp.Logs);
        using var client = new HttpClient(new DiagnosticHttpMessageHandler(new StubHandler(), logs));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://user:uri-secret@example.test/path?code=query-secret");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "header-secret");
        request.Content = new StringContent("request-secret");
        using var response = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.BadGateway, response.StatusCode);
        using var archive = ZipFile.OpenRead(await logs.CreateArchiveAsync(TimeSpan.FromHours(1), temp.Exports));
        var text = ReadEntry(archive, "logs.jsonl");
        Assert.Contains("https://example.test/path", text);
        Assert.Contains("502", text);
        Assert.Contains("ms", text);
        Assert.DoesNotContain("secret", text);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway) { Content = new StringContent("response-secret") });
    }

    [Fact]
    public async Task InvalidOrCancelledExport_DoesNotLeavePartialArchives()
    {
        using var temp = new TempDirectory();
        using var logs = new DiagnosticLogService(temp.Logs);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => logs.CreateArchiveAsync(TimeSpan.Zero, temp.Exports));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => logs.CreateArchiveAsync(TimeSpan.FromHours(1), temp.Exports, cancellation.Token));
        Assert.False(Directory.Exists(temp.Exports));
    }

    private static string ReadActiveLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static List<DiagnosticLogEntry> ReadEntries(ZipArchive archive) => ReadEntry(archive, "logs.jsonl")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => JsonSerializer.Deserialize<DiagnosticLogEntry>(line)!).ToList();

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TempDirectory : IDisposable
    {
        private string Root { get; } = Path.Combine(Path.GetTempPath(), "picshare-diagnostics-tests", Guid.NewGuid().ToString("N"));
        public string Logs => Path.Combine(Root, "logs");
        public string Exports => Path.Combine(Root, "exports");
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
