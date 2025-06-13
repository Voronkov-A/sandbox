namespace Yaprt.Miscellaneous.Geometry.BoundingVolumes;

public readonly record struct BoundingCircle : IBoundingVolume
{
    public BoundingCircle(float radius)
    {
        Radius = radius;
    }

    public float Radius { get; }
}
