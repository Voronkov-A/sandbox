using OpenTK.Mathematics;
using System;
using Yaprt.Miscellaneous.Geometry.BoundingVolumes;

namespace Yaprt.Miscellaneous.Geometry;

public static class Box2Extensions
{
    public static bool Contains(this in Box2 self, IBoundedObject other)
    {
        switch (other.Bounds)
        {
            case BoundingCircle:
                {
                    var otherCenter = other.Position.Translation;
                    var otherScale = other.Position.Scale;

                    if (otherScale.X != otherScale.Y)
                    {
                        throw new InvalidOperationException($"Scale {otherScale} is not supported.");
                    }

                    return self.Min.X <= otherCenter.X - otherScale.X
                        && self.Min.Y <= otherCenter.Y - otherScale.Y
                        && self.Max.X >= otherCenter.X + otherScale.X
                        && self.Max.Y >= otherCenter.Y + otherScale.Y;
                }
            default:
                {
                    throw new InvalidOperationException($"{other.Bounds.GetType()} is not supported.");
                }
        }
    }

    private static bool Contains(
        in Vector2 sectorCenter,
        float squareOfSectorRadius,
        Angle cosOfHalfOfSectorAngle,
        in Vector2 normalizedSectorForwardDirection,
        in Vector2 otherPoint)
    {
        var v = otherPoint - sectorCenter;
        return v.LengthSquared <= squareOfSectorRadius
            && Vector2.Dot(v, normalizedSectorForwardDirection) >= cosOfHalfOfSectorAngle.Radians;
    }

    private readonly struct SectorHelper
    {
        private readonly Vector2 _sectorCenter;
        private readonly float _sectorRadius;
        private readonly float _squareOfSectorRadius;
        private readonly Angle _halfOfSectorAngle;
        private readonly float _cosOfHalfOfSectorAngle;
        private readonly Vector2 _normalizedSectorForwardDirection;

        public SectorHelper(in BoundingSector sector, in Matrix4 sectorModelMatrix)
        {
            _sectorCenter = sectorModelMatrix.ExtractTranslation().Xy;

            var scale = sectorModelMatrix.ExtractScale();

            if (scale.X != scale.Y)
            {
                throw new InvalidOperationException($"Scale {scale} is not supported.");
            }

            _sectorRadius = sector.Radius * scale.X;
            _squareOfSectorRadius = _sectorRadius * _sectorRadius;
            _halfOfSectorAngle = sector.Angle / 2;
            _cosOfHalfOfSectorAngle = MathF.Cos(_halfOfSectorAngle.Radians);
            _normalizedSectorForwardDirection = new Vector2(sectorModelMatrix[0, 0], sectorModelMatrix[1, 0])
                .Normalized();
        }

        public bool Contains(in Vector2 otherPoint)
        {
            var v = otherPoint - _sectorCenter;
            return v.LengthSquared <= _squareOfSectorRadius
                && Vector2.Dot(v.Normalized(), _normalizedSectorForwardDirection) >= _cosOfHalfOfSectorAngle;
        }

        public bool AnyRadiusBoundaryIntersectsBoxBoundaries(in Box2 box)
        {
            var leftBoundaryTail
                = _sectorCenter + Rotate(_normalizedSectorForwardDirection, -_halfOfSectorAngle) * _sectorRadius;
            var rightBoundaryTail
                = _sectorCenter + Rotate(_normalizedSectorForwardDirection, _halfOfSectorAngle) * _sectorRadius;

            return SegmentsIntersect(_sectorCenter, leftBoundaryTail, box.Min, new Vector2(box.Max.X, box.Min.Y))
                || SegmentsIntersect(_sectorCenter, leftBoundaryTail, new Vector2(box.Max.X, box.Min.Y), box.Max)
                || SegmentsIntersect(_sectorCenter, leftBoundaryTail, box.Max, new Vector2(box.Min.X, box.Max.Y))
                || SegmentsIntersect(_sectorCenter, leftBoundaryTail, new Vector2(box.Min.X, box.Max.Y), box.Min)
                || SegmentsIntersect(_sectorCenter, rightBoundaryTail, box.Min, new Vector2(box.Max.X, box.Min.Y))
                || SegmentsIntersect(_sectorCenter, rightBoundaryTail, new Vector2(box.Max.X, box.Min.Y), box.Max)
                || SegmentsIntersect(_sectorCenter, rightBoundaryTail, box.Max, new Vector2(box.Min.X, box.Max.Y))
                || SegmentsIntersect(_sectorCenter, rightBoundaryTail, new Vector2(box.Min.X, box.Max.Y), box.Min);
        }


        private static Vector2 Rotate(in Vector2 vector, Angle angle)
        {
            var cos = MathF.Cos(angle.Radians);
            var sin = MathF.Sin(angle.Radians);
            return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
        }

        private static int GetOrientation(in Vector2 p, in Vector2 q, in Vector2 r)
        {
            var val = (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);
            return val switch
            {
                0 => 0,
                > 0 => 1,
                < 0 => 2,
                _ => throw new InvalidOperationException()
            };
        }

        private static bool IsOnSegment(in Vector2 p, in Vector2 q, in Vector2 r)
        {
            return Math.Min(p.X, r.X) <= q.X
                && q.X <= Math.Max(p.X, r.X)
                && Math.Min(p.Y, r.Y) <= q.Y
                && q.Y <= Math.Max(p.Y, r.Y);
        }

        private static bool SegmentsIntersect(in Vector2 p1, in Vector2 q1, in Vector2 p2, in Vector2 q2)
        {
            var o1 = GetOrientation(p1, q1, p2);
            var o2 = GetOrientation(p1, q1, q2);
            var o3 = GetOrientation(p2, q2, p1);
            var o4 = GetOrientation(p2, q2, q1);

            return o1 != o2 && o3 != o4
                || o1 == 0 && IsOnSegment(p1, p2, q1)
                || o2 == 0 && IsOnSegment(p1, q2, q1)
                || o3 == 0 && IsOnSegment(p2, p1, q2)
                || o4 == 0 && IsOnSegment(p2, q1, q2);
        }
    }

    public static bool Intersects(this in Box2 self, IBoundedObject other)
    {
        return Intersects(self, other.Bounds, other.Position);
    }

    public static bool Intersects(this in Box2 self, IBoundingVolume otherBounds, Position otherPosition)
    {
        switch (otherBounds)
        {
            case BoundingSector otherSector:
                {
                    var otherCenter = otherPosition.Translation;

                    if (self.Min.X - otherSector.Radius > otherCenter.X
                        || self.Min.Y - otherSector.Radius > otherCenter.Y
                        || self.Max.X + otherSector.Radius < otherCenter.X
                        || self.Max.Y + otherSector.Radius < otherCenter.Y)
                    {
                        return false;
                    }

                    if (self.Min.X <= otherCenter.X
                        && self.Min.Y <= otherCenter.Y
                        && self.Max.X >= otherCenter.X
                        && self.Max.Y >= otherCenter.Y)
                    {
                        return true;
                    }

                    var otherSectorHelper = new SectorHelper(otherSector, otherPosition.ModelMatrix);

                    if (otherSectorHelper.Contains(self.Min)
                        || otherSectorHelper.Contains(new Vector2(self.Min.X, self.Max.Y))
                        || otherSectorHelper.Contains(new Vector2(self.Max.X, self.Min.Y))
                        || otherSectorHelper.Contains(self.Max)
                        || otherSectorHelper.AnyRadiusBoundaryIntersectsBoxBoundaries(self))
                    {
                        return true;
                    }

                    return false;
                }
            default:
                {
                    throw new InvalidOperationException($"{otherBounds.GetType()} is not supported.");
                }
        }
    }
}
