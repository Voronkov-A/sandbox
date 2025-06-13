using OpenTK.Mathematics;

namespace Yaprt.Miscellaneous.Geometry.Shapes;

public readonly record struct Rectangle : IShape
{
    public Rectangle(Vector2 size)
    {
        Size = size;
    }

    public Vector2 Size { get; }
}
