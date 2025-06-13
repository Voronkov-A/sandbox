namespace Yaprt.Miscellaneous.Geometry.Shapes;

public readonly record struct Circle : IShape
{
    public Circle(float radius)
    {
        Radius = radius;
    }

    public float Radius { get; }
}
