namespace Ml.CartPole;

internal readonly struct CartPoleStats
{
    public CartPoleStats(int stepCount)
    {
        StepCount = stepCount;
    }

    public int StepCount { get; }
}
