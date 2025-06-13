using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using Yaprt.Domain;
using Yaprt.Miscellaneous.Geometry;
using Yaprt.Miscellaneous.Geometry.BoundingVolumes;

namespace Yaprt.Views.Controls;

internal class MapView : Control
{
    private readonly DispatcherTimer _mainTimer;

    public MapView()
    {
        _mainTimer = new DispatcherTimer()
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _mainTimer.Tick += (s, e) =>
        {
            if (DataContext is not World world)
            {
                return;
            }

            try
            {
                //world.Map.NextTick();
                InvalidateVisual();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Environment.Exit(1);
            }
        };
        _mainTimer.Start();
    }

    private static Vector2 Rotate(in Vector2 vector, Angle angle)
    {
        var cos = MathF.Cos(angle.Radians);
        var sin = MathF.Sin(angle.Radians);
        return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (DataContext is not World world)
        {
            return;
        }

        var boxes = new List<Box2>();

        var a = 10f;

        for (var y = world.Bounds.Min.Y; y <= world.Bounds.Max.Y; y += a)
        {
            for (var x = world.Bounds.Min.X; x <= world.Bounds.Max.X; x += a)
            {
                var box = new Box2(x, y, x + a, y + a);

                var p = world.Objects.OfType<Pawn>().First();
                var intersects = box.Intersects(p.VisionField, p.Position);

                var color = intersects
                    ? new Color(255, 0, 128, 0)
                    : new Color(255, 0, 255, 0);

                var brush = new SolidColorBrush(color);
                var pen = new Pen(brush);

                var polygone = new PolylineGeometry(
                    new Avalonia.Point[]
                    {
                        new(x - world.Bounds.Min.X, y - world.Bounds.Min.Y),
                        new(x - world.Bounds.Min.X + a, y - world.Bounds.Min.Y),
                        new(x - world.Bounds.Min.X + a, y - world.Bounds.Min.Y + a),
                        new(x - world.Bounds.Min.X, y - world.Bounds.Min.Y + a)
                    },
                    isFilled: false);
                context.DrawGeometry(brush, pen, polygone);
            }
        }

        foreach (var obj in world.Objects)
        {
            if (obj is Pawn pawn)
            {
                var color = new Color(255, 255, 0, 0);

                var brush = new SolidColorBrush(color);
                var pen = new Pen(brush);

                var center = pawn.Position.Translation;
                var scale = pawn.Position.Scale;
                context.DrawEllipse(
                    brush,
                    pen,
                    new Avalonia.Point(center.X - world.Bounds.Min.X, center.Y - world.Bounds.Min.Y),
                    ((BoundingCircle)pawn.Bounds).Radius * scale.X,
                    ((BoundingCircle)pawn.Bounds).Radius * scale.Y);

                var sector = new Avalonia.Controls.Shapes.Sector
                {
                    //Bounds = new
                };



                var vf = pawn.VisionField;
                var vfCenter = pawn.Position.Translation;

                var normalizedSectorForwardDirection = new Vector2(
                        pawn.Position.ModelMatrix[0, 0],
                        pawn.Position.ModelMatrix[1, 0])
                    .Normalized();
                var leftBoundaryTail = Rotate(normalizedSectorForwardDirection, -vf.Angle / 2) * vf.Radius * scale.X;
                var rightBoundaryTail = Rotate(normalizedSectorForwardDirection, vf.Angle / 2) * vf.Radius * scale.X;

                var path = new Path
                {
                    Fill = Brushes.Orange,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };

                var figure = new PathFigure
                {
                    StartPoint = new Avalonia.Point(vfCenter.X - world.Bounds.Min.X, vfCenter.Y - world.Bounds.Min.Y),
                    IsClosed = true,
                    IsFilled = false
                };
                figure.Segments!.Add(new LineSegment
                {
                    Point = new Avalonia.Point(leftBoundaryTail.X - world.Bounds.Min.X, leftBoundaryTail.Y - world.Bounds.Min.Y)
                });
                figure.Segments.Add(new ArcSegment
                {
                    Point = new Avalonia.Point(rightBoundaryTail.X - world.Bounds.Min.X, rightBoundaryTail.Y - world.Bounds.Min.Y),
                    Size = new Avalonia.Size(vf.Radius, vf.Radius),
                    SweepDirection = SweepDirection.Clockwise,
                    IsLargeArc = false,
                });

                var geometry = new PathGeometry();
                geometry.Figures!.Add(figure);


                context.DrawGeometry(brush, pen, geometry);
            }
        }


        /*//var a = 15;
        var a = 5;
        var d = a * 2;
        var s = a * Math.Sqrt(3);

        foreach (var field in world.Map.Fields)
        {
            var xShift = field.Position.OddRowOffsetCoordinates.Y % 2 == 0 ? 0 : s / 2;

            var x0 = field.Position.OddRowOffsetCoordinates.X * s + xShift;
            var y0 = field.Position.OddRowOffsetCoordinates.Y * (a + (d - a) / 2) + (d - a) / 2;
            var x1 = x0 + s / 2;
            var y1 = y0 - (d - a) / 2;
            var x2 = x1 + s / 2;
            var y2 = y0;
            var x3 = x2;
            var y3 = y2 + a;
            var x4 = x1;
            var y4 = y3 + a / 2;
            var x5 = x0;
            var y5 = y3;

            var rgb = (100, 100, 100);
            var color = new Color(255, (byte)rgb.Item1, (byte)rgb.Item2, (byte)rgb.Item3);

            var brush = new SolidColorBrush(color);
            var pen = new Pen(brush);


            var polygone = new PolylineGeometry(
                new Avalonia.Point[]
                {
                    new(x0, y0),
                    new(x1, y1),
                    new(x2, y2),
                    new(x3, y3),
                    new(x4, y4),
                    new(x5, y5),
                },
                isFilled: true);
            context.DrawGeometry(brush, pen, polygone);
        }

        var fff = world.Map.Fields.Where(x => x.Pawn != null);
        //var fff = map.GetFields(new Miscellaneous.Numerics.HexagonalPosition(0, 0), 2);  // 19
        //var fff = map.GetFields(new Miscellaneous.Numerics.HexagonalPosition(5, 5), 10); // 317

        foreach (var field in fff)
        {
            var playerIndex = field.Pawn?.PlayerIndex;

            var color = playerIndex switch
            {
                0 => new Color(255, 255, 0, 0),
                1 => new Color(255, 0, 0, 255),
                _ => new Color(255, 100, 100, 100)
            };

            var brush = new SolidColorBrush(color);
            var pen = new Pen(brush);

            var xShift = field.Position.OddRowOffsetCoordinates.Y % 2 == 0 ? 0 : s / 2;

            var x0 = field.Position.OddRowOffsetCoordinates.X * s + xShift;
            var y0 = field.Position.OddRowOffsetCoordinates.Y * (a + (d - a) / 2) + (d - a) / 2;
            var x1 = x0 + s / 2;
            var y1 = y0 - (d - a) / 2;
            var x2 = x1 + s / 2;
            var y2 = y0;
            var x3 = x2;
            var y3 = y2 + a;
            var x4 = x1;
            var y4 = y3 + a / 2;
            var x5 = x0;
            var y5 = y3;

            var circle = new EllipseGeometry()
            {
                Center = new Avalonia.Point((x0 + x2) / 2, (y0 + y5) / 2),
                RadiusX = a * 1.2 / 2,
                RadiusY = a * 1.2 / 2
            };

            context.DrawGeometry(brush, pen, circle);

        }*/

    }
}
