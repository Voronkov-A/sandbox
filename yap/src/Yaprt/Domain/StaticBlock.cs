using Yaprt.Domain.Visibility;
using Yaprt.Miscellaneous.Geometry;
using Yaprt.Miscellaneous.Geometry.BoundingVolumes;

namespace Yaprt.Domain;

public sealed class StaticBlock : IBoundedObject, IVisibleObject
{
    public StaticBlock(Position position, BoundingBox bounds)
    {
        Bounds = bounds;
        Position = position;
    }

    public IBoundingVolume Bounds { get; }

    public Position Position { get; }

    public bool IsTransparent => false;

    public ObjectVisibilityMode VisibilityMode => ObjectVisibilityMode.AlwaysVisible;
}
