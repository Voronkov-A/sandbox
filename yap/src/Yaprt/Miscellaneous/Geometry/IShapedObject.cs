namespace Yaprt.Miscellaneous.Geometry;

public interface IShapedObject
{
    Position Position { get; }

    IShape Shape { get; }
}
