using System.Collections.Generic;
using System.Linq;
using Yap.Miscellaneous.Numerics;

namespace Yap.Domain;

internal sealed class Map
{
    private readonly Vector2i _size;
    private readonly Field[] _fields;
    //private readonly Dictionary<HexagonalPosition, Pawn> _pawns;
    private readonly List<Pawn> _pawns;

    public Map(Vector2i size, IEnumerable<Field> fields)
    {
        _size = size;
        _fields = fields.ToArray();
        _pawns = new List<Pawn>();
    }

    public IReadOnlyList<Field> Fields => _fields;

    public IReadOnlyCollection<Pawn> Pawns => _pawns;

    //public int CurrentPlayerIndex { get; private set; }
    /*
    public void AddPawn(Pawn pawn, HexagonalPosition location)
    {
        if (_pawns.ContainsKey(position))
        {
            throw new InvalidOperationException($"Field {location} is already occupied.");
        }

        var field = GetField(position);

        if (field.Pawn != null)
        {
            throw new InvalidOperationException($"Field {position} is already occupied.");
        }

        field.Pawn = pawn;
    }
    */
    /*public void MovePawn(Pawn pawn, HexagonalPosition to)
    {
        var fromField = _fields.FirstOrDefault(x => x.Pawn == pawn);

        if (fromField != null)
        {
            fromField.Pawn = null;
        }

        var field = GetField(to);

        if (field.Pawn != null)
        {
            throw new InvalidOperationException($"Field {to} is already occupied.");
        }

        field.Pawn = pawn;

        CurrentPlayerIndex = (CurrentPlayerIndex + 1) % _playerCount;
    }*/

    public Field GetField(HexagonalPosition position)
    {
        var normalized = Normalize(position);
        return _fields[normalized.OddRowOffsetCoordinates.Y * _size.Y + normalized.OddRowOffsetCoordinates.X];
    }

    private HexagonalPosition Normalize(HexagonalPosition position)
    {
        var normalizedX = position.OddRowOffsetCoordinates.X % _size.X;

        if (normalizedX < 0)
        {
            normalizedX += _size.X;
        }

        var normalizedY = position.OddRowOffsetCoordinates.Y % _size.Y;

        if (normalizedY < 0)
        {
            normalizedY += _size.Y;
        }

        return new HexagonalPosition(normalizedX, normalizedY);
    }
}
