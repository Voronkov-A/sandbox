using Yap.Miscellaneous.Numerics;

namespace Yap.Domain;

internal sealed class Field(HexagonalPosition location)
{
    public HexagonalPosition Location { get; } = location;
}
