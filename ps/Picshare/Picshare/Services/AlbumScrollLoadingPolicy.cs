namespace Picshare.Services;

/// <summary>Tracks scroll activity using monotonic times supplied by the view.</summary>
public sealed class AlbumScrollLoadingPolicy
{
    public static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan SpeedWindow = TimeSpan.FromMilliseconds(100);
    private TimeSpan? _lastScrollAt;
    private TimeSpan _sampleStart;
    private double _sampleDistance;

    public bool IsLoadingDeferred { get; private set; }
    public bool HasActivity => _lastScrollAt.HasValue;

    public void RecordScroll(double distance, TimeSpan now)
    {
        if (!double.IsFinite(distance) || distance <= 0) return;
        if (_lastScrollAt is null || now - _lastScrollAt.Value >= IdleDelay)
        {
            _sampleStart = now;
            _sampleDistance = 0;
            IsLoadingDeferred = true;
        }
        _lastScrollAt = now;
        _sampleDistance += distance;
        // Wheel steps and large jumps suspend immediately, even after a slow interval.
        if (distance >= 24) IsLoadingDeferred = true;
        var elapsed = now - _sampleStart;
        if (elapsed < SpeedWindow) return;
        var speed = _sampleDistance / elapsed.TotalSeconds;
        if (speed <= 120) IsLoadingDeferred = false;
        else if (speed >= 240) IsLoadingDeferred = true;
        _sampleStart = now;
        _sampleDistance = 0;
    }

    public void ResumeIfIdle(TimeSpan now)
    {
        if (_lastScrollAt is { } last && now - last >= IdleDelay) Reset();
    }

    public void Reset()
    {
        _lastScrollAt = null;
        _sampleDistance = 0;
        IsLoadingDeferred = false;
    }
}
