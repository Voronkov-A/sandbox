namespace Yaprt.Miscellaneous.Geometry.BoundingVolumes;

public readonly record struct BoundingSector : IBoundingVolume
{
    public BoundingSector(float radius, Angle angle)
    {
        Radius = radius;
        Angle = angle;
    }

    public float Radius { get; }

    public Angle Angle { get; }
}
