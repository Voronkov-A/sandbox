using System;
using Yap.Miscellaneous.Numerics;

namespace Yap.Domain;

internal sealed class MapGenerator(Random random)
{
    private readonly Random _random = random;

    public Map Generate(Vector2i size, int playerCount)
    {
        var fields = new Field[size.X * size.Y];

        for (var i = 0; i < fields.Length; ++i)
        {
            fields[i] = new Field(new HexagonalPosition(new Vector2i(i % size.X, i / size.X)));
        }

        return new Map(size, fields);
    }
}