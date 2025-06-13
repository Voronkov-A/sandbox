using OpenTK.Mathematics;

namespace Yaprt.Miscellaneous.Geometry;

public readonly record struct Sector
{
    public Sector(Vector3 center, float radius, Angle minAngle, Angle maxAngle)
    {
        Center = center;
        Radius = radius;
        MinAngle = minAngle;
        MaxAngle = maxAngle;
    }

    public Vector3 Center { get; }

    public float Radius { get; }

    public Angle MinAngle { get; }

    public Angle MaxAngle { get; }
}
