namespace Yap.Miscellaneous.Numerics;

public readonly record struct Angle
{
    private readonly float _degrees;

    private Angle(float degrees)
    {
        _degrees = degrees;
    }

    public static Angle FromDegrees(float degrees)
    {
        return new Angle(degrees);
    }
}
