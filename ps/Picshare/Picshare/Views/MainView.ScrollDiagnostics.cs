using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Picshare.Services;
using Picshare.ViewModels;

namespace Picshare.Views;

public partial class MainView
{
    private Timer? _scrollDiagnosticsTimer;
    private int _scrollDiagnosticsGeneration;
    private int _scrollDiagnosticsPulsePending;
    private long _scrollDiagnosticsReportedAt;
    private long _scrollDiagnosticsAllocatedBytes;
    private TimeSpan _scrollDiagnosticsGcPause;
    private int[] _scrollDiagnosticsCollections = new int[3];
    private AlbumListPerformanceCounters.Snapshot? _scrollDiagnosticsSnapshot;
    private double _scrollDiagnosticsMaxUiDelayMs;
    private int _scrollDiagnosticsUiStalls;
    private long _scrollDiagnosticsLastPulseAt;
    private double _scrollDiagnosticsMaxPulseGapMs;
    private long _scrollDiagnosticsPulseQueuedAt;
    private long _scrollDiagnosticsLastAnimationAt;
    private double _scrollDiagnosticsMaxAnimationGapMs;
    private int _scrollDiagnosticsAnimationCallbacks;
    private long _scrollDiagnosticsLastActivityAt;

    private void AlbumScrollDiagnostics_Wheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Length > 0) ObserveScrollDiagnosticInput(e);
    }

    private void AlbumScrollDiagnostics_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type is PointerType.Touch or PointerType.Pen) ObserveScrollDiagnosticInput(e);
    }

    private void ObserveScrollDiagnosticInput(PointerEventArgs e)
    {
        if (DataContext is not MainViewModel model || model.IsPhotoViewerVisible || model.IsSettingsPanelVisible ||
            e.Source is not Control source || GetActiveAlbumPhotoListBox() is not { } list) return;
        if (ReferenceEquals(source, list) || source.GetVisualAncestors().Contains(list)) StartScrollDiagnostics();
    }

    private void StartScrollDiagnostics()
    {
        _scrollDiagnosticsLastActivityAt = Stopwatch.GetTimestamp();
        if (_scrollDiagnosticsTimer is not null) return;
        _scrollDiagnosticsReportedAt = Stopwatch.GetTimestamp();
        _scrollDiagnosticsLastPulseAt = _scrollDiagnosticsReportedAt;
        Volatile.Write(ref _scrollDiagnosticsPulseQueuedAt, 0);
        _scrollDiagnosticsAllocatedBytes = GC.GetTotalAllocatedBytes(false);
        _scrollDiagnosticsGcPause = GC.GetTotalPauseDuration();
        for (var index = 0; index < 3; index++) _scrollDiagnosticsCollections[index] = GC.CollectionCount(index);
        _scrollDiagnosticsSnapshot = AlbumListPerformanceCounters.Shared.Capture();
        _scrollDiagnosticsMaxUiDelayMs = 0;
        _scrollDiagnosticsUiStalls = 0;
        _scrollDiagnosticsMaxPulseGapMs = 0;
        _scrollDiagnosticsLastAnimationAt = _scrollDiagnosticsReportedAt;
        _scrollDiagnosticsMaxAnimationGapMs = 0;
        _scrollDiagnosticsAnimationCallbacks = 0;
        var snapshot = _scrollDiagnosticsSnapshot;
        DiagnosticLogging.Current.Write("Debug", "Performance.Scroll",
            $"Scroll started; totalRowsBuilt={snapshot.Values[(int)AlbumListMetric.RowsBuilt].Count}; totalCardsBuilt={snapshot.Values[(int)AlbumListMetric.CardsBuilt].Count}; " +
            $"loadingDeferred={_albumScrollLoadingWasPaused}; " +
            "timings=count/totalMs/maxMs; row timings include card timings (do not sum); allocation/GC statistics are process-wide; UI pulse interval=100ms, stall threshold=32ms; pulse gaps are not frame timings");
        var generation = ++_scrollDiagnosticsGeneration;
        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            Action<TimeSpan>? onFrame = null;
            onFrame = _ =>
            {
                if (generation != _scrollDiagnosticsGeneration) return;
                var now = Stopwatch.GetTimestamp();
                _scrollDiagnosticsMaxAnimationGapMs = Math.Max(_scrollDiagnosticsMaxAnimationGapMs,
                    Stopwatch.GetElapsedTime(_scrollDiagnosticsLastAnimationAt, now).TotalMilliseconds);
                _scrollDiagnosticsLastAnimationAt = now;
                _scrollDiagnosticsAnimationCallbacks++;
                topLevel.RequestAnimationFrame(onFrame!);
            };
            topLevel.RequestAnimationFrame(onFrame);
        }
        // A background timer can observe a blocked dispatcher. Keep at most one pulse queued.
        _scrollDiagnosticsTimer = new Timer(_ =>
        {
            if (generation != Volatile.Read(ref _scrollDiagnosticsGeneration) ||
                Interlocked.CompareExchange(ref _scrollDiagnosticsPulsePending, generation, 0) != 0) return;
            if (generation != Volatile.Read(ref _scrollDiagnosticsGeneration))
            {
                Interlocked.CompareExchange(ref _scrollDiagnosticsPulsePending, 0, generation);
                return;
            }
            var queued = Stopwatch.GetTimestamp();
            Volatile.Write(ref _scrollDiagnosticsPulseQueuedAt, queued);
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.CompareExchange(ref _scrollDiagnosticsPulsePending, 0, generation);
                if (generation != _scrollDiagnosticsGeneration) return;
                var now = Stopwatch.GetTimestamp();
                var delay = Stopwatch.GetElapsedTime(queued, now).TotalMilliseconds;
                var gap = Stopwatch.GetElapsedTime(_scrollDiagnosticsLastPulseAt, now).TotalMilliseconds;
                _scrollDiagnosticsLastPulseAt = now;
                _scrollDiagnosticsMaxPulseGapMs = Math.Max(_scrollDiagnosticsMaxPulseGapMs, gap);
                _scrollDiagnosticsMaxUiDelayMs = Math.Max(_scrollDiagnosticsMaxUiDelayMs, delay);
                if (delay >= 32) _scrollDiagnosticsUiStalls++;
                if (Stopwatch.GetElapsedTime(_scrollDiagnosticsReportedAt, now).TotalSeconds >= 1)
                    ReportScrollDiagnostics("sample");
                // A wheel event at the list edge may never produce ScrollChanged/its idle
                // timer. End that input-only session as well, without another UI timer.
                if (Stopwatch.GetElapsedTime(_scrollDiagnosticsLastActivityAt, now).TotalMilliseconds >= 500)
                    StopScrollDiagnostics();
            }, DispatcherPriority.Normal);
        }, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    }

    private void StopScrollDiagnostics()
    {
        if (_scrollDiagnosticsTimer is null) return;
        _scrollDiagnosticsTimer.Dispose();
        _scrollDiagnosticsTimer = null;
        Interlocked.Increment(ref _scrollDiagnosticsGeneration);
        if (Volatile.Read(ref _scrollDiagnosticsPulsePending) != 0 && Volatile.Read(ref _scrollDiagnosticsPulseQueuedAt) is var queued && queued > 0)
        {
            var delay = Stopwatch.GetElapsedTime(queued).TotalMilliseconds;
            _scrollDiagnosticsMaxUiDelayMs = Math.Max(_scrollDiagnosticsMaxUiDelayMs, delay);
            if (delay >= 32) _scrollDiagnosticsUiStalls++;
        }
        Interlocked.Exchange(ref _scrollDiagnosticsPulsePending, 0);
        ReportScrollDiagnostics("stopped");
        _scrollDiagnosticsSnapshot = null;
    }

    private void ReportScrollDiagnostics(string phase)
    {
        if (_scrollDiagnosticsSnapshot is null) return;
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_scrollDiagnosticsReportedAt, now);
        var snapshot = AlbumListPerformanceCounters.Shared.Capture();
        var allocated = GC.GetTotalAllocatedBytes(false);
        var pause = GC.GetTotalPauseDuration();
        var collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        var activeList = GetActiveAlbumPhotoListBox();
        DiagnosticLogging.Current.Write("Debug", "Performance.Scroll", FormattableString.Invariant(
            $"Scroll {phase}; intervalMs={elapsed.TotalMilliseconds:F0}; uiDelayMaxMs={_scrollDiagnosticsMaxUiDelayMs:F1}; uiStalls={_scrollDiagnosticsUiStalls}; pulseGapMaxMs={_scrollDiagnosticsMaxPulseGapMs:F1}; allocatedBytes={allocated - _scrollDiagnosticsAllocatedBytes}; gc0={collections[0] - _scrollDiagnosticsCollections[0]}; gc1={collections[1] - _scrollDiagnosticsCollections[1]}; gc2={collections[2] - _scrollDiagnosticsCollections[2]}; gcPauseMs={(pause - _scrollDiagnosticsGcPause).TotalMilliseconds:F2}; heapBytes={GC.GetGCMemoryInfo().HeapSizeBytes}; loadedCards={_albumPhotoCards.Count}; realizedRows={activeList?.GetRealizedContainers().Count() ?? 0}; totalRows={activeList?.ItemCount ?? 0}; photos={(DataContext as MainViewModel)?.Photos.Count ?? 0}; loadingDeferred={_albumScrollLoadingWasPaused}") +
            FormattableString.Invariant($"; animationCallbacks={_scrollDiagnosticsAnimationCallbacks}; animationCallbackGapMaxMs={_scrollDiagnosticsMaxAnimationGapMs:F1}") +
            snapshot.DescribeChanges(_scrollDiagnosticsSnapshot));
        _scrollDiagnosticsSnapshot = snapshot;
        _scrollDiagnosticsReportedAt = now;
        _scrollDiagnosticsAllocatedBytes = allocated;
        _scrollDiagnosticsGcPause = pause;
        _scrollDiagnosticsCollections = collections;
        _scrollDiagnosticsMaxUiDelayMs = 0;
        _scrollDiagnosticsUiStalls = 0;
        _scrollDiagnosticsMaxPulseGapMs = 0;
        _scrollDiagnosticsMaxAnimationGapMs = 0;
        _scrollDiagnosticsAnimationCallbacks = 0;
    }
}
