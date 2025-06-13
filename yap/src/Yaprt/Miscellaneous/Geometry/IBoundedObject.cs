namespace Yaprt.Miscellaneous.Geometry;

public interface IBoundedObject
{
    Position Position { get; }

    IBoundingVolume Bounds { get; }
}
