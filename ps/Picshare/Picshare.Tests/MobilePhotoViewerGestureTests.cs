using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Input.Raw;
using Picshare.Views;
using Pointer = Avalonia.Input.Pointer;

namespace Picshare.Tests;

public sealed class MobilePhotoViewerGestureTests
{
    [Fact]
    public void ShortTouchTaps_RaiseOneTapEachToHideThenShowControls()
    {
        var host = new Canvas();
        var recognizer = CreateTapFriendlyPinch(host);
        var controlsVisible = true;
        var taps = 0;
        host.Tapped += (_, _) => { controlsVisible = !controlsVisible; taps++; };
        for (var index = 0; index < 2; index++)
        {
            using var pointer = new Pointer(index + 1, PointerType.Touch, true);
            var pressed = new PointerPressedEventArgs(host, pointer, host, default, 100,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None);
            Send(recognizer, "PointerPressedInternal", pressed);
            host.RaiseEvent(pressed);
            var released = new PointerReleasedEventArgs(host, pointer, host, default, 200,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left);
            Send(recognizer, "PointerReleasedInternal", released);
            host.RaiseEvent(released);
            Assert.Equal(index + 1, taps);
            Assert.Equal(index == 1, controlsVisible);
        }
    }

    [Theory]
    [InlineData(PointerType.Touch)]
    [InlineData(PointerType.Pen)]
    public void SingleContactRelease_LeavesNormalTapRecognitionEnabledAndClearsContact(PointerType type)
    {
        var host = new Canvas();
        var recognizer = CreateTapFriendlyPinch(host);
        // Repeated taps must each remain a single-contact gesture, with no stale pinch state.
        for (var index = 0; index < 3; index++)
        {
            using var pointer = new Pointer(index + 1, type, true);
            Press(recognizer, host, pointer);
            Release(recognizer, host, pointer);
            Assert.False(TapRecognitionSuppressed(pointer));
        }
    }

    [Fact]
    public void TwoContactRelease_StillSuppressesTapsAndAllowsTheNextSingleTap()
    {
        var host = new Canvas();
        var recognizer = CreateTapFriendlyPinch(host);
        using var first = new Pointer(1, PointerType.Touch, true);
        using var second = new Pointer(2, PointerType.Touch, false);
        Press(recognizer, host, first);
        Press(recognizer, host, second);
        Release(recognizer, host, first);
        Release(recognizer, host, second);
        Assert.True(TapRecognitionSuppressed(first));
        Assert.True(TapRecognitionSuppressed(second));

        using var tap = new Pointer(3, PointerType.Touch, true);
        Press(recognizer, host, tap);
        Release(recognizer, host, tap);
        Assert.False(TapRecognitionSuppressed(tap));
    }

    [Fact]
    public void PanRelease_DoesNotReenableTapRecognition()
    {
        var host = new Canvas();
        var pinch = CreateTapFriendlyPinch(host);
        var scroll = new ScrollGestureRecognizer { CanHorizontallyScroll = true, IsScrollInertiaEnabled = false };
        host.GestureRecognizers.Add(scroll);
        host.AddHandler(Gestures.ScrollGestureEvent, (_, e) => e.Handled = true);
        using var pointer = new Pointer(1, PointerType.Touch, true);
        Press(pinch, host, pointer);
        Press(scroll, host, pointer);
        Send(scroll, "PointerMovedInternal", new PointerEventArgs(InputElement.PointerMovedEvent, host, pointer,
            host, new Point(100, 0), 150, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
        Release(pinch, host, pointer);
        Release(scroll, host, pointer);
        Assert.True(TapRecognitionSuppressed(pointer));
    }

    private static GestureRecognizer CreateTapFriendlyPinch(Canvas host)
    {
        var type = typeof(PhotoViewerView).GetNestedType("TapFriendlyPinchGestureRecognizer", BindingFlags.NonPublic)!;
        var recognizer = (GestureRecognizer)Activator.CreateInstance(type, nonPublic: true)!;
        host.GestureRecognizers.Add(recognizer);
        return recognizer;
    }

    private static void Press(GestureRecognizer recognizer, Canvas host, Pointer pointer)
        => Send(recognizer, "PointerPressedInternal", new PointerPressedEventArgs(host, pointer, host, default, 100,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));

    private static void Release(GestureRecognizer recognizer, Canvas host, Pointer pointer)
        => Send(recognizer, "PointerReleasedInternal", new PointerReleasedEventArgs(host, pointer, host, default, 200,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

    private static void Send(GestureRecognizer recognizer, string method, PointerEventArgs args)
        => typeof(GestureRecognizer).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(recognizer, [args]);

    private static bool TapRecognitionSuppressed(Pointer pointer)
        => (bool)typeof(Pointer).GetProperty("IsGestureRecognitionSkipped", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pointer)!;

    [Fact]
    public void Pinch_UsesScaleFromGestureStartRatherThanCompoundingEachEvent()
    {
        var first = Pinch(2, new Point(-50, -80), new Point(100, 100), new Point(100, 100), 1.2);
        var second = Pinch(2, new Point(-50, -80), new Point(100, 100), new Point(100, 100), 1.4);
        Assert.Equal(2.4, first.Zoom, 6);
        Assert.Equal(2.8, second.Zoom, 6);
    }

    [Fact]
    public void Pinch_PreservesTheContentUnderTheFingersWhileTheirCenterMoves()
    {
        var result = Pinch(1, new Point(0, 0), new Point(100, 150), new Point(120, 170), 2);
        Assert.Equal(new Point(-80, -130), result.Origin);
        Assert.Equal(120, result.Origin.X + 100 * result.Zoom, 6);
        Assert.Equal(170, result.Origin.Y + 150 * result.Zoom, 6);
    }

    [Theory]
    [InlineData(0.1, 1)]
    [InlineData(100, 8)]
    [InlineData(double.NaN, 2)]
    public void Pinch_BoundsZoomAndIgnoresInvalidGestureScale(double scale, double expected)
    {
        Assert.Equal(expected, Pinch(2, default, default, default, scale).Zoom);
    }

    [Fact]
    public void RotatedImage_PansUsingItsVisibleBounds()
    {
        var method = typeof(PhotoViewerView).GetMethod("ClampMobileImageOrigin", BindingFlags.Static | BindingFlags.NonPublic)!;
        var fitted = (Point)method.Invoke(null, [new Point(-10000, 10000), new Size(400, 600), 600d, 400d, true])!;
        Assert.Equal(new Point(-100, 100), fitted);
        var zoomed = (Point)method.Invoke(null, [new Point(-10000, 10000), new Size(400, 600), 1200d, 800d, true])!;
        Assert.Equal(new Point(-600, 200), zoomed);
    }

    private static (double Zoom, Point Origin) Pinch(double startZoom, Point startOrigin, Point startAnchor, Point center, double scale)
    {
        var method = typeof(PhotoViewerView).GetMethod("CalculateMobilePinch", BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((double, Point))method.Invoke(null, [startZoom, startOrigin, startAnchor, center, scale, 8d])!;
    }
}
