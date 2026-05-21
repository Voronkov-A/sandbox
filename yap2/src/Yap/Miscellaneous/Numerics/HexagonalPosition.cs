using System;

namespace Yap.Miscellaneous.Numerics;

public readonly record struct HexagonalPosition
{
    public HexagonalPosition(Vector2i oddRowOffsetCoordinates)
    {
        OddRowOffsetCoordinates = oddRowOffsetCoordinates;
    }

    public HexagonalPosition(int oddRowOffsetCoordinatesX, int oddRowOffsetCoordinatesY)
        : this(new Vector2i(oddRowOffsetCoordinatesX, oddRowOffsetCoordinatesY))
    {
    }

    public Vector2i OddRowOffsetCoordinates { get; }

    public Vector2i AxialCoordinates => new Vector2i(
        OddRowOffsetCoordinates.X - (OddRowOffsetCoordinates.Y - (OddRowOffsetCoordinates.Y & 1)) / 2,
        OddRowOffsetCoordinates.Y);

    public int CalculateDistance(HexagonalPosition other)
    {
        var diff = new Vector2i(
            AxialCoordinates.X - other.AxialCoordinates.X,
            AxialCoordinates.Y - other.AxialCoordinates.Y);
        var axialDistance = (Math.Abs(diff.X) + Math.Abs(diff.X + diff.Y) + Math.Abs(diff.Y)) / 2;
        return axialDistance;
    }

    public HexagonalPosition AddAxial(Vector2i axialVector)
    {
        return FromAxialCoordinates(AxialCoordinates + axialVector);
    }

    public static HexagonalPosition FromAxialCoordinates(Vector2i axialCoordinates)
    {
        return new HexagonalPosition(
            axialCoordinates.X + (axialCoordinates.Y - (axialCoordinates.Y & 1)) / 2,
            axialCoordinates.Y);
    }
}
