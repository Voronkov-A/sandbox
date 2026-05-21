using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using Yap.Domain;

namespace Yap.Views.Controls;

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

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (DataContext is not World world)
        {
            return;
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
