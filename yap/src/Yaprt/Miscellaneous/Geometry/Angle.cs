using System;

namespace Yaprt.Miscellaneous.Geometry;

public readonly record struct Angle
{
    public static readonly Angle Zero = new();

    private Angle(float radians)
    {
        Radians = radians;
    }

    public float Radians { get; }

    public static Angle FromRadians(float radians)
    {
        return new Angle(radians);
    }

    public static Angle FromDegrees(float degrees)
    {
        return new Angle(degrees / 180 * MathF.PI);
    }

    public static Angle operator /(Angle left, float right)
    {
        return new Angle(left.Radians / right);
    }

    public static Angle operator -(Angle operand)
    {
        return new Angle(-operand.Radians);
    }
}
