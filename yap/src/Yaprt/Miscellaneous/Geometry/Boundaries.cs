using System;
using Yaprt.Miscellaneous.Geometry.Shapes;

namespace Yaprt.Miscellaneous.Geometry;

public sealed class Boundaries
{
    public Boundaries(IShape shape, Position position)
    {
        Shape = shape;
        Position = position;
    }

    public IShape Shape { get; }

    public Position Position { get; }

    public override string ToString()
    {
        return $"({Shape}, {Position})";
    }

    public bool Contains(Boundaries other)
    {
        switch (Shape, other.Shape)
        {
            case (Rectangle rectangle, Circle otherCircle):
                {
                    if (Position.Rotation == Angle.Zero)
                    {
                        var translation = Position.Translation;
                        var otherTranslation = other.Position.Translation;
                        return translation.X - rectangle.Size.X / 2 >= otherTranslation.X - otherCircle.Radius
                            && translation.X + rectangle.Size.X / 2 <= otherTranslation.X + otherCircle.Radius
                            && translation.Y - rectangle.Size.Y / 2 >= otherTranslation.Y - otherCircle.Radius
                            && translation.Y + rectangle.Size.Y / 2 <= otherTranslation.Y + otherCircle.Radius;
                    }

                    throw new InvalidOperationException("Rotated rectangle is not supported.");
                }
            default:
                {
                    throw new InvalidOperationException(
                        $"({Shape.GetType()}, {other.Shape.GetType()}) is not supported.");
                }
        }
    }
}
