using Picshare.Services;

namespace Picshare.Tests;

public sealed class AlbumScrollLoadingPolicyTests
{
    [Fact]
    public void FastScroll_DefersImmediatelyAndResumesOnlyAfterQuietPeriod()
    {
        var policy = new AlbumScrollLoadingPolicy();
        Assert.False(policy.IsLoadingDeferred);
        policy.RecordScroll(120, TimeSpan.Zero);
        Assert.True(policy.IsLoadingDeferred);
        policy.RecordScroll(120, TimeSpan.FromMilliseconds(100));
        policy.ResumeIfIdle(TimeSpan.FromMilliseconds(279));
        Assert.True(policy.IsLoadingDeferred);
        policy.ResumeIfIdle(TimeSpan.FromMilliseconds(280));
        Assert.False(policy.IsLoadingDeferred);
        Assert.False(policy.HasActivity);
    }

    [Fact]
    public void SlowScrolling_ResumesBeforeScrollingStopsAndLargeJumpPausesAgain()
    {
        var policy = new AlbumScrollLoadingPolicy();
        policy.RecordScroll(2, TimeSpan.Zero);
        policy.RecordScroll(2, TimeSpan.FromMilliseconds(50));
        policy.RecordScroll(2, TimeSpan.FromMilliseconds(100));
        Assert.False(policy.IsLoadingDeferred);
        Assert.True(policy.HasActivity);
        policy.RecordScroll(100, TimeSpan.FromMilliseconds(116));
        Assert.True(policy.IsLoadingDeferred);
    }

    [Fact]
    public void SpeedHysteresis_AvoidsRapidSwitchingBetweenLoadingAndDeferral()
    {
        var policy = new AlbumScrollLoadingPolicy();
        policy.RecordScroll(10, TimeSpan.Zero);
        policy.RecordScroll(8, TimeSpan.FromMilliseconds(100)); // 180 DIP/s keeps deferral.
        Assert.True(policy.IsLoadingDeferred);
        policy.RecordScroll(5, TimeSpan.FromMilliseconds(200));
        Assert.False(policy.IsLoadingDeferred);
        policy.RecordScroll(18, TimeSpan.FromMilliseconds(300)); // 180 DIP/s keeps loading.
        Assert.False(policy.IsLoadingDeferred);
        policy.RecordScroll(15, TimeSpan.FromMilliseconds(350));
        policy.RecordScroll(15, TimeSpan.FromMilliseconds(400));
        Assert.True(policy.IsLoadingDeferred);
    }

    [Fact]
    public void ResetAndInvalidDeltas_DoNotLeaveLoadingSuspended()
    {
        var policy = new AlbumScrollLoadingPolicy();
        foreach (var distance in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
            policy.RecordScroll(distance, TimeSpan.Zero);
        Assert.False(policy.HasActivity);
        Assert.False(policy.IsLoadingDeferred);
        policy.RecordScroll(100, TimeSpan.Zero);
        policy.Reset();
        Assert.False(policy.IsLoadingDeferred);
        Assert.False(policy.HasActivity);
    }
}
