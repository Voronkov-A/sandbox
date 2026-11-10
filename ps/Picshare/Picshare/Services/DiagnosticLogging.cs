using System.Diagnostics;
using System.Text;
using System.Runtime.CompilerServices;
using Avalonia.Logging;

namespace Picshare.Services;

public static class DiagnosticLogging
{
    private static bool _initialized;
    private static ITimer? _maintenanceTimer;
    private static ITimer? _flushTimer;
    // Avalonia calls its Trace level Verbose.
    public const LogEventLevel MinimumLevel = LogEventLevel.Verbose;
    public static DiagnosticLogService Current { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Picshare", "diagnostics"));

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Current.Maintain();
        _maintenanceTimer = TimeProvider.System.CreateTimer(_ => Current.Maintain(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        _flushTimer = TimeProvider.System.CreateTimer(_ => Current.Flush(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Console.SetOut(new DiagnosticTextWriter(Console.Out, "Debug", "Console"));
        Console.SetError(new DiagnosticTextWriter(Console.Error, "Error", "Console"));
        Trace.Listeners.Add(new DiagnosticTraceListener());
        Logger.Sink = new DiagnosticAvaloniaSink();
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Current.Write("Critical", "Runtime", "Unhandled exception", args.ExceptionObject as Exception);
            Current.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
            Current.Write("Error", "Runtime", "Unobserved task exception", args.Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _flushTimer?.Dispose();
            _maintenanceTimer?.Dispose();
            Current.Write("Information", "Lifecycle", "Application exiting");
            Current.Dispose();
        };
        Current.Write("Information", "Lifecycle", $"Application starting. OS={System.Runtime.InteropServices.RuntimeInformation.OSDescription}; runtime={Environment.Version}; version={typeof(DiagnosticLogging).Assembly.GetName().Version}");
    }

    public static HttpClient CreateHttpClient(DelegatingHandler? handler = null)
    {
        var diagnostics = new DiagnosticHttpMessageHandler(new HttpClientHandler());
        if (handler is null) return new HttpClient(diagnostics);
        handler.InnerHandler = diagnostics;
        return new HttpClient(handler);
    }

    public static void LogException(Exception exception, [CallerMemberName] string operation = "") =>
        Current.Write(exception is OperationCanceledException ? "Debug" : "Error", "Workflow", $"{operation} failed", exception);

    private sealed class DiagnosticTextWriter(TextWriter original, string level, string area) : TextWriter
    {
        private readonly StringBuilder _line = new();
        private readonly object _gate = new();
        public override Encoding Encoding => original.Encoding;
        public override void Write(char value)
        {
            lock (_gate)
            {
                original.Write(value);
                Append(value);
            }
        }
        public override void Write(string? value)
        {
            if (value is null) return;
            lock (_gate)
            {
                original.Write(value);
                foreach (var character in value) Append(character);
            }
        }
        public override void WriteLine(string? value)
        {
            lock (_gate)
            {
                original.WriteLine(value);
                if (value is not null)
                    foreach (var character in value) Append(character);
                Append('\n');
            }
        }
        public override void Flush()
        {
            lock (_gate) { original.Flush(); Emit(); }
        }
        private void Emit()
        {
            if (_line.Length == 0) return;
            Current.Write(level, area, _line.ToString());
            _line.Clear();
        }
        private void Append(char value)
        {
            if (value == '\n') Emit();
            else if (value != '\r') _line.Append(value);
            if (_line.Length >= 16 * 1024) Emit();
        }
    }

    private sealed class DiagnosticTraceListener : TraceListener
    {
        private readonly DiagnosticTextWriter _writer = new(TextWriter.Null, "Trace", "Trace");
        public override void Write(string? message) => _writer.Write(message);
        public override void WriteLine(string? message) => _writer.WriteLine(message);
        public override void Flush() => _writer.Flush();
    }

    private sealed class DiagnosticAvaloniaSink : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area) => level >= MinimumLevel;
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Current.Write(level == LogEventLevel.Verbose ? "Trace" : level.ToString(), $"Avalonia.{area}", messageTemplate);
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        {
            // Snapshot UI-owned objects here; formatting, redaction, JSON and disk I/O run on the log worker.
            var values = Array.ConvertAll(propertyValues, value => value?.ToString());
            Current.WriteTemplate(level == LogEventLevel.Verbose ? "Trace" : level.ToString(), $"Avalonia.{area}", messageTemplate, values);
        }
    }
}

public sealed class DiagnosticHttpMessageHandler(HttpMessageHandler innerHandler, DiagnosticLogService? logs = null) : DelegatingHandler(innerHandler)
{
    private readonly DiagnosticLogService _logs = logs ?? DiagnosticLogging.Current;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        // Never record query strings, request/response bodies, cookies, or authorization headers.
        var endpoint = request.RequestUri?.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        var started = Stopwatch.GetTimestamp();
        _logs.Write("Debug", "HTTP", $"{id} {request.Method} {endpoint} started");
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _logs.Write(response.IsSuccessStatusCode ? "Debug" : "Warning", "HTTP",
                $"{id} {request.Method} {endpoint} -> {(int)response.StatusCode}; {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}ms");
            return response;
        }
        catch (Exception ex)
        {
            _logs.Write(ex is OperationCanceledException ? "Debug" : "Error", "HTTP",
                $"{id} {request.Method} {endpoint} failed after {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}ms", ex);
            throw;
        }
    }
}
