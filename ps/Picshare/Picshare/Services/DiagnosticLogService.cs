using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Picshare.Services;

/// <summary>Bounded, persistent application diagnostics. Each JSON line is one timestamped event.</summary>
public sealed class DiagnosticLogService : IDisposable
{
    public const int RetentionMonths = 1;
    public const long MaximumStorageBytes = 64 * 1024 * 1024;
    private const long MaximumFileBytes = 2 * 1024 * 1024;
    private readonly object _gate = new();
    // Producers never take the disk/rotation lock. Bound queued records even if storage stalls.
    private const int MaximumPendingRecords = 8192;
    private const long MaximumPendingCharacters = 4 * 1024 * 1024;
    private readonly object _queueGate = new();
    private readonly Channel<LogWork> _queue = Channel.CreateUnbounded<LogWork>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });
    private readonly Task _worker;
    private int _pendingRecords;
    private long _pendingCharacters;
    private long _droppedRecords;
    private readonly string _directory;
    private readonly TimeProvider _time;
    private string? _currentFile;
    private DateOnly _currentDay;
    private StreamWriter? _writer;
    private long _currentBytes;
    private bool _disposed;
    private static readonly Regex UrlQuery = new(@"(https?://[^\s?""<>]+)\?[^\s""<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SecretField = new(@"((?:access_token|refresh_token|id_token|client_secret|code_verifier|authorization|password|\bcode)\s*[""']?\s*[:=]\s*[""']?)([^\s""'&,}]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Bearer = new(@"\bBearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GoogleSecret = new(@"(?:ya29\.|GOCSPX-|1//)[A-Za-z0-9._~+/-]+", RegexOptions.Compiled);

    public DiagnosticLogService(string directory, TimeProvider? timeProvider = null)
    {
        _directory = directory;
        _time = timeProvider ?? TimeProvider.System;
        _worker = Task.Run(ProcessQueueAsync);
    }

    public static string Redact(string message)
    {
        message = UrlQuery.Replace(message, "$1?[redacted]");
        message = Bearer.Replace(message, "Bearer [redacted]");
        message = SecretField.Replace(message, "$1[redacted]");
        return GoogleSecret.Replace(message, "[redacted]");
    }

    public void Write(string level, string area, string message, Exception? exception = null)
    {
        Enqueue(level, area, message, exception, null);
    }

    internal void WriteTemplate(string level, string area, string template, string?[] values)
        => Enqueue(level, area, template, null, values);

    private void Enqueue(string level, string area, string message, Exception? exception, string?[]? values)
    {
        lock (_queueGate)
        {
            if (_disposed) return;
            var entry = new PendingEntry(_time.GetUtcNow(), level, area,
                Environment.CurrentManagedThreadId, message.Length > 64 * 1024 ? message[..(64 * 1024)] + " [truncated]" : message,
                exception, values);
            if (Volatile.Read(ref _pendingRecords) >= MaximumPendingRecords ||
                Volatile.Read(ref _pendingCharacters) + entry.QueuedCharacters > MaximumPendingCharacters)
            {
                Interlocked.Increment(ref _droppedRecords);
                return;
            }
            Interlocked.Increment(ref _pendingRecords);
            Interlocked.Add(ref _pendingCharacters, entry.QueuedCharacters);
            _queue.Writer.TryWrite(new LogWork(entry, null, null));
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var work in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (work.Entry is { } entry)
                {
                    Interlocked.Decrement(ref _pendingRecords);
                    Interlocked.Add(ref _pendingCharacters, -entry.QueuedCharacters);
                    Persist(entry);
                }
                else work.Action?.Invoke();
                var dropped = Interlocked.Exchange(ref _droppedRecords, 0);
                if (dropped > 0)
                    Persist(new PendingEntry(_time.GetUtcNow(), "Warning", "Diagnostics", Environment.CurrentManagedThreadId,
                        $"Skipped {dropped} diagnostic events because the background log queue was full.", null, null));
                work.Completion?.TrySetResult();
            }
            catch (Exception ex)
            {
                // A failed record must not stop the worker or recursively log through Console/Trace.
                work.Completion?.TrySetException(ex);
            }
        }
        try { lock (_gate) CloseWriter(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Persist(PendingEntry pending)
    {
        try
        {
            lock (_gate)
            {
                var now = pending.Timestamp;
                var message = pending.Values is null ? pending.Message : FormatTemplate(pending.Message, pending.Values);
                var detail = pending.Exception is null ? message : $"{message}\n{pending.Exception}";
                // Bound individual records as well as the store, including very large exception messages.
                if (detail.Length > 64 * 1024)
                    detail = detail[..(64 * 1024)] + " [truncated]";
                var entry = new DiagnosticLogEntry(now, pending.Level, pending.Area, pending.ThreadId, Redact(detail));
                var line = JsonSerializer.Serialize(entry) + "\n";
                var bytes = Encoding.UTF8.GetByteCount(line);
                var day = DateOnly.FromDateTime(now.UtcDateTime);
                if (_writer is null || _currentDay != day || _currentBytes + bytes > MaximumFileBytes)
                {
                    CloseWriter();
                    Directory.CreateDirectory(_directory);
                    _currentFile = Path.Combine(_directory, $"log-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
                    _currentDay = day;
                    MaintainCore(now);
                    _writer = new StreamWriter(new FileStream(_currentFile, FileMode.CreateNew, FileAccess.Write,
                        FileShare.Read | FileShare.Delete, 64 * 1024), new UTF8Encoding(false), 64 * 1024);
                    _currentBytes = 0;
                }
                _writer.Write(line);
                _currentBytes += bytes;
                if (pending.Level is "Warning" or "Error" or "Critical" or "Fatal") _writer.Flush();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Do not log this through Console/Trace: those streams also feed this service.
        }
    }

    public void Flush()
    {
        try { RunInQueue(() => { lock (_gate) _writer?.Flush(); }); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        lock (_queueGate)
        {
            _disposed = true;
            _queue.Writer.TryComplete();
        }
        _worker.GetAwaiter().GetResult();
    }

    private void RunInQueue(Action action)
    {
        Task completion;
        lock (_queueGate)
        {
            if (_disposed) completion = _worker;
            else
            {
                var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _queue.Writer.TryWrite(new LogWork(null, action, source));
                completion = source.Task;
            }
        }
        completion.GetAwaiter().GetResult();
    }

    private static string FormatTemplate(string template, string?[] values)
    {
        var result = new StringBuilder(template.Length);
        var start = 0;
        var index = 0;
        while (template.IndexOf('{', start) is var open && open >= 0 &&
               template.IndexOf('}', open + 1) is var close && close > open + 1)
        {
            result.Append(template, start, open - start);
            result.Append(index < values.Length ? values[index++] ?? "null" : "?");
            start = close + 1;
        }
        return result.Append(template, start, template.Length - start).ToString();
    }

    private sealed record PendingEntry(DateTimeOffset Timestamp, string Level, string Area, int ThreadId,
        string Message, Exception? Exception, string?[]? Values)
    {
        public long QueuedCharacters { get; } = Message.Length + (Values?.Sum(value => (long)(value?.Length ?? 0)) ?? 0);
    }
    private sealed record LogWork(PendingEntry? Entry, Action? Action, TaskCompletionSource? Completion);

    private void CloseWriter()
    {
        var writer = _writer;
        _writer = null;
        writer?.Dispose();
    }

    /// <summary>Rotates idle/previous-session logs, compresses closed chunks, and enforces retention.</summary>
    public void Maintain()
    {
        try
        {
            RunInQueue(() => { lock (_gate) MaintainCore(_time.GetUtcNow()); });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Diagnostics maintenance must not interrupt the app or recursively log failures.
        }
    }

    private void MaintainCore(DateTimeOffset now)
    {
        if (!Directory.Exists(_directory)) return;
        _writer?.Flush();
        if (_currentDay != DateOnly.FromDateTime(now.UtcDateTime))
        {
            CloseWriter();
            _currentFile = null;
        }
        foreach (var temporary in Directory.EnumerateFiles(_directory, "log-*.jsonl.gz.tmp"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(temporary) < now.UtcDateTime.AddDays(-1)) File.Delete(temporary);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Prune(now);
        foreach (var source in Directory.EnumerateFiles(_directory, "log-*.jsonl"))
        {
            if (source == _currentFile) continue;
            var archive = source + ".gz";
            var temporary = archive + ".tmp";
            try
            {
                var modified = File.GetLastWriteTimeUtc(source);
                using (var input = File.OpenRead(source))
                using (var output = File.Create(temporary))
                using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
                    input.CopyTo(gzip);
                File.Move(temporary, archive, overwrite: true);
                // Compression must not reset the retention clock for old records.
                File.SetLastWriteTimeUtc(archive, modified);
                File.Delete(source);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        Prune(now);
    }

    public Task<string> CreateArchiveAsync(TimeSpan period, string exportDirectory, CancellationToken cancellationToken = default)
        => CreateArchiveAsync(period, exportDirectory, _time.GetUtcNow(), cancellationToken);

    public Task<string> CreateArchiveAsync(TimeSpan period, string exportDirectory, DateTimeOffset until, CancellationToken cancellationToken = default)
    {
        if (period <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(period));
        var since = period.Ticks > until.UtcTicks ? DateTimeOffset.MinValue : until - period;
        return Task.Run(() => CreateArchive(since, until, exportDirectory, cancellationToken), cancellationToken);
    }

    private string CreateArchive(DateTimeOffset since, DateTimeOffset until, string exportDirectory, CancellationToken cancellationToken)
    {
        // A barrier includes every accepted record queued before export, even with a slow disk.
        Flush();
        Directory.CreateDirectory(exportDirectory);
        // Keep mobile attachments alive after handing them to another app. Remove only old exports.
        foreach (var old in Directory.EnumerateFiles(exportDirectory, "picshare-logs-*.zip"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(old) < _time.GetUtcNow().UtcDateTime.AddDays(-1))
                    File.Delete(old);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var archivePath = Path.Combine(exportDirectory, $"picshare-logs-{until:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        try
        {
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            using var logWriter = new StreamWriter(archive.CreateEntry("logs.jsonl", CompressionLevel.Optimal).Open(), Encoding.UTF8);
            var count = 0;
            DateTimeOffset? earliest = null;
            // Read bounded snapshots of each chunk so appending cannot extend an export indefinitely.
            (string Path, long Length)[] sources;
            lock (_gate)
            {
                MaintainCore(_time.GetUtcNow());
                sources = Directory.Exists(_directory)
                    ? Directory.GetFiles(_directory, "log-*").Where(IsLogFile)
                        .Where(path => !path.EndsWith(".gz", StringComparison.Ordinal) || !File.Exists(path[..^3]))
                        .Order(StringComparer.Ordinal)
                        .Select(path => (path, new FileInfo(path).Length)).ToArray()
                    : [];
            }
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] bytes;
                var compressed = source.Path.EndsWith(".gz", StringComparison.Ordinal);
                try
                {
                    lock (_gate)
                    {
                        // A snapshot's active chunk may have rotated while the ZIP was being written.
                        var path = source.Path;
                        var length = source.Length;
                        if (!File.Exists(path) && !compressed && File.Exists(path + ".gz"))
                        {
                            path += ".gz";
                            compressed = true;
                            length = new FileInfo(path).Length;
                        }
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        bytes = new byte[checked((int)length)];
                        stream.ReadExactly(bytes);
                    }
                }
                catch (FileNotFoundException) { continue; }
                using var buffer = new MemoryStream(bytes);
                using var content = compressed ? (Stream)new GZipStream(buffer, CompressionMode.Decompress) : buffer;
                using var reader = new StreamReader(content, Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DiagnosticLogEntry? entry;
                    try { entry = JsonSerializer.Deserialize<DiagnosticLogEntry>(line); }
                    catch (JsonException) { continue; } // Tolerate an incomplete record after a crash.
                    if (entry is null) continue;
                    earliest = earliest is null || entry.Timestamp < earliest ? entry.Timestamp : earliest;
                    if (entry.Timestamp < since || entry.Timestamp > until || entry.Timestamp < _time.GetUtcNow().AddMonths(-RetentionMonths)) continue;
                    logWriter.WriteLine(line);
                    count++;
                }
            }
            logWriter.Dispose();
            using var metadata = new StreamWriter(archive.CreateEntry("diagnostics.json").Open());
            metadata.Write(JsonSerializer.Serialize(new
            {
                Application = "Picshare",
                Version = typeof(DiagnosticLogService).Assembly.GetName().Version?.ToString(),
                OS = RuntimeInformation.OSDescription,
                Framework = RuntimeInformation.FrameworkDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                RequestedFromUtc = since,
                RequestedUntilUtc = until,
                EarliestRetainedEntryUtc = earliest,
                ExportedEntryCount = count,
                RetentionMonths,
                MaximumStorageBytes,
                Note = "Contains available recorded events in the requested UTC interval. Logging begins when the app runs; retention and size limits may shorten coverage. No settings files, tokens, photos, or feedback documents are attached."
            }, new JsonSerializerOptions { WriteIndented = true }));
            return archivePath;
        }
        catch
        {
            try { File.Delete(archivePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        if (!Directory.Exists(_directory)) return;
        var files = new DirectoryInfo(_directory).GetFiles("log-*").Where(file => IsLogFile(file.FullName)).OrderBy(file => file.LastWriteTimeUtc).ToArray();
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (file.FullName == _currentFile) continue;
            // Chunks rotate at UTC midnight. Drop the entire boundary day's chunk so no
            // entries older than one calendar month remain in a partially expired file.
            if (file.LastWriteTimeUtc.Date >= now.UtcDateTime.AddMonths(-RetentionMonths) && total <= MaximumStorageBytes - MaximumFileBytes) continue;
            try { file.Delete(); total -= file.Length; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool IsLogFile(string path) => path.EndsWith(".jsonl", StringComparison.Ordinal) || path.EndsWith(".jsonl.gz", StringComparison.Ordinal);
}

public sealed record DiagnosticLogEntry(DateTimeOffset Timestamp, string Level, string Area, int ThreadId, string Message);
