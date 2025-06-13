using Yap.Miscellaneous.Numerics;

namespace Yap.Domain;

public readonly record struct Position
{
    public Position(HexagonalPosition location, Angle rotation)
    {
        Location = location;
        Rotation = rotation;
    }

    public HexagonalPosition Location { get; }

    public Angle Rotation { get; }
}
