using Yaprt.Domain.Visibility;
using Yaprt.Miscellaneous.Geometry;
using Yaprt.Miscellaneous.Geometry.BoundingVolumes;

namespace Yaprt.Domain;

public sealed class Pawn : IBoundedObject, IVisibleObject
{
    public Pawn(Participant participant, Position position, BoundingCircle bounds, BoundingSector visionField)
    {
        Participant = participant;
        Bounds = bounds;
        VisionField = visionField;
        Position = position;
    }

    public Participant Participant { get; }

    public IBoundingVolume Bounds { get; }

    public BoundingSector VisionField { get; }

    public Position Position { get; }

    public bool IsTransparent => true;

    public ObjectVisibilityMode VisibilityMode => ObjectVisibilityMode.VisibleWithinFieldOfView;
}
