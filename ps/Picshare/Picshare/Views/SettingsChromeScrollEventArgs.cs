namespace Picshare.Views;

public sealed class SettingsChromeScrollEventArgs(double delta, double currentOffset, double maximumOffset) : EventArgs
{
    public double Delta { get; } = delta;

    public double CurrentOffset { get; } = currentOffset;

    public double MaximumOffset { get; } = maximumOffset;
}
