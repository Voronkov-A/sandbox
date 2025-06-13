using System;

namespace Yap.Domain;

internal sealed class Pawn
{
    private World? _world;

    public Pawn(int playerIndex, Position position)
    {
        PlayerIndex = playerIndex;
        Position = position;
    }

    public int PlayerIndex { get; }

    public Position Position { get; }

    public World World
    {
        get => _world ?? throw new InvalidOperationException("World is not set.");
        internal set => _world = value;
    }
    /*
    public void Move(HexagonalPosition to)
    {
        if (World.Map.)

            if (World.Map)
    }*/
}
