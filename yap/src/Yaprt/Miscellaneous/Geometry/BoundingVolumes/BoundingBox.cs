namespace Yaprt.Miscellaneous.Geometry.BoundingVolumes;

public readonly record struct BoundingBox : IBoundingVolume
{
    public BoundingBox(float width, float height)
    {
        Width = width;
        Height = height;
    }

    public float Width { get; }

    public float Height { get; }
}
