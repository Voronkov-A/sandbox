using System.Diagnostics;
using Picshare.Services;

namespace Picshare.Views;

public partial class MainView
{
    private long _lastScrollSummaryAt;

    // No per-card probes, UI heartbeat, or forced diagnostic rendering loop. One small
    // summary on return to full cards (at most every five seconds) verifies pool behavior.
    private void ReportAlbumScrollRendering()
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastScrollSummaryAt != 0 && Stopwatch.GetElapsedTime(_lastScrollSummaryAt, now).TotalSeconds < 5) return;
        _lastScrollSummaryAt = now;
        long rows = 0, bindings = 0, heavy = 0;
        foreach (var list in _albumPhotoLists.OfType<AlbumPhotoListBox>())
        {
            rows += list.LightweightRowsCreated;
            bindings += list.LightweightRowBindings;
            heavy += list.HeavyRowsCreated;
        }
        DiagnosticLogging.Current.Write("Debug", "Performance.Scroll",
            $"Full cards requested; pooledRowsCreated={rows}; rowBindings={bindings}; fullRowsCreated={heavy}; " +
            $"loadedCards={_albumPhotoCards.Count}; managedHeapBytes={GC.GetGCMemoryInfo().HeapSizeBytes}; gc0={GC.CollectionCount(0)}; gc2={GC.CollectionCount(2)}");
    }
}
