using System.Diagnostics;
using System.Text;

namespace Picshare.Services;

public enum AlbumListMetric
{
    RowsBuilt, CardsBuilt, RowsRebound, CardsRebound, CardsLoaded, CardsUnloaded,
    RowMeasure, RowArrange, CardMeasure, CardArrange,
    ScrollHandler, ChromeUpdate, ViewportUpdate, StickyHeader, ViewportQueueDelay,
    BitmapAssignment, BitmapSuppressed
}

/// <summary>Allocation-free counters on hot paths; formatting happens only at reporting time.</summary>
public sealed class AlbumListPerformanceCounters
{
    public static AlbumListPerformanceCounters Shared { get; } = new();
    private readonly long[] _counts = new long[Enum.GetValues<AlbumListMetric>().Length];
    private readonly long[] _ticks = new long[Enum.GetValues<AlbumListMetric>().Length];
    private readonly long[] _maxTicks = new long[Enum.GetValues<AlbumListMetric>().Length];

    public void Count(AlbumListMetric metric) => Interlocked.Increment(ref _counts[(int)metric]);
    public Timing Measure(AlbumListMetric metric) => new(this, metric);

    public void Record(AlbumListMetric metric, long ticks)
    {
        var index = (int)metric;
        Interlocked.Increment(ref _counts[index]);
        Interlocked.Add(ref _ticks[index], ticks);
        var maximum = Volatile.Read(ref _maxTicks[index]);
        while (ticks > maximum)
        {
            var previous = Interlocked.CompareExchange(ref _maxTicks[index], ticks, maximum);
            if (previous == maximum) break;
            maximum = previous;
        }
    }

    public Snapshot Capture()
    {
        var values = new MetricValue[_counts.Length];
        for (var index = 0; index < values.Length; index++)
            values[index] = new(Volatile.Read(ref _counts[index]), Volatile.Read(ref _ticks[index]),
                Interlocked.Exchange(ref _maxTicks[index], 0));
        return new(values);
    }

    public sealed record Snapshot(MetricValue[] Values)
    {
        public string DescribeChanges(Snapshot previous)
        {
            var text = new StringBuilder();
            for (var index = 0; index < Values.Length; index++)
            {
                var value = Values[index];
                var count = value.Count - previous.Values[index].Count;
                var ticks = value.Ticks - previous.Values[index].Ticks;
                text.Append($"; {(AlbumListMetric)index}={count}");
                if (ticks > 0)
                    text.Append(FormattableString.Invariant($"/{ticks * 1000d / Stopwatch.Frequency:F2}ms/max={value.MaxTicks * 1000d / Stopwatch.Frequency:F2}ms"));
            }
            return text.ToString();
        }
    }

    public readonly record struct MetricValue(long Count, long Ticks, long MaxTicks);

    public readonly struct Timing : IDisposable
    {
        private readonly AlbumListPerformanceCounters _owner;
        private readonly AlbumListMetric _metric;
        private readonly long _started;
        public Timing(AlbumListPerformanceCounters owner, AlbumListMetric metric)
        {
            _owner = owner;
            _metric = metric;
            _started = Stopwatch.GetTimestamp();
        }
        public void Dispose() => _owner.Record(_metric, Stopwatch.GetTimestamp() - _started);
    }
}
