using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using Yaprt.Miscellaneous.Geometry;

namespace Yaprt.Domain.Physics;

internal sealed class CollisionTree
{
    private readonly List<IBoundedObject> _objects;
    private readonly Box2 _box;

    public CollisionTree(Vector2 size)
    {
        _objects = new List<IBoundedObject>();
        _box = new Box2(-size / 2, size / 2);
    }

    public Box2 Root => _box;

    public IEnumerable<IBoundedObject> Objects => _objects;

    public IEnumerable<IBoundedObject> GetIntersections(IBoundedObject obj)
    {
        return _objects.Where(x => x.Intersects(obj));
    }

    public void Add(IBoundedObject obj)
    {
        if (!_box.Contains(obj))
        {
            throw new InvalidOperationException($"Object {obj} is out of box {_box}.");
        }

        _objects.Add(obj);
    }
}
