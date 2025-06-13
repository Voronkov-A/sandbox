using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using Yaprt.Domain.Physics;
using Yaprt.Miscellaneous.Geometry;

namespace Yaprt.Domain;

public sealed class World
{
    private readonly CollisionTree _collisionTree;
    private readonly List<Pawn> _pawns;

    public World(Vector2 size)
    {
        _pawns = new List<Pawn>();
        _collisionTree = new CollisionTree(size);
    }

    public IEnumerable<IBoundedObject> Objects => _collisionTree.Objects;

    public Box2 Bounds => _collisionTree.Root;

    public void AddObject(Pawn pawn)
    {
        var intersection = _collisionTree.GetIntersections(pawn).FirstOrDefault();

        if (intersection != null)
        {
            throw new InvalidOperationException($"Object {pawn} intersects object {intersection}.");
        }

        _collisionTree.Add(pawn);
        _pawns.Add(pawn);
    }

    public IEnumerable<IBoundedObject> GetVisibleObjects(Participant participant)
    {
        var objects = new HashSet<IBoundedObject>();

        foreach (var pawn in _pawns.Where(x => x.Participant == participant))
        {
            objects.Add(pawn);
        }

        return objects;
    }
}
