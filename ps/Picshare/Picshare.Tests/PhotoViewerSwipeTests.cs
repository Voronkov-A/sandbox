using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Skia;
using Avalonia.Themes.Fluent;
using CommunityToolkit.Mvvm.Input;
using Picshare.Views;
using Pointer = Avalonia.Input.Pointer;

namespace Picshare.Tests;

public sealed partial class PhotoViewerSwipeTests
{
    static PhotoViewerSwipeTests() => SkiaPlatform.Initialize();

    [Fact]
    public void Buttons_AreCenteredAroundTheCircleWithTheApplicationTheme()
    {
        var wheel = new PhotoViewerSwipeControl();
        var theme = new FluentTheme();
        wheel.Styles.Add(theme);
        var surface = (Border)wheel.Content!;
        foreach (var name in new[] { "LeftButton", "TopButton", "RightButton", "BottomButton" })
        {
            var button = wheel.FindControl<Button>(name)!;
            button.Theme = (Avalonia.Styling.ControlTheme)theme.Resources[typeof(Button)]!;
            button.ApplyStyling();
        }
        surface.Measure(new Size(144, 144));
        surface.Arrange(new Rect(0, 0, 144, 144));

        AssertCenter("LeftButton", new Point(32, 72));
        AssertCenter("TopButton", new Point(72, 32));
        AssertCenter("RightButton", new Point(112, 72));
        AssertCenter("BottomButton", new Point(72, 112));

        void AssertCenter(string name, Point expected)
        {
            var button = wheel.FindControl<Button>(name)!;
            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), surface);
            Assert.Equal(expected, center);
        }
    }

    [Theory]
    [InlineData(-100, 10, PhotoViewerSwipeDirection.Left)]
    [InlineData(100, -10, PhotoViewerSwipeDirection.Right)]
    [InlineData(10, -100, PhotoViewerSwipeDirection.Top)]
    [InlineData(-10, 100, PhotoViewerSwipeDirection.Bottom)]
    [InlineData(-80, 0, PhotoViewerSwipeDirection.Left)]
    [InlineData(0, 80, PhotoViewerSwipeDirection.Bottom)]
    [InlineData(6, 0, PhotoViewerSwipeDirection.Right)]
    [InlineData(0, -6, PhotoViewerSwipeDirection.Top)]
    [InlineData(-6, 0, PhotoViewerSwipeDirection.Left)]
    [InlineData(0, 6, PhotoViewerSwipeDirection.Bottom)]
    [InlineData(5, 0, PhotoViewerSwipeDirection.None)]
    [InlineData(0, -5, PhotoViewerSwipeDirection.None)]
    [InlineData(6, 6, PhotoViewerSwipeDirection.Right)]
    [InlineData(6, -6, PhotoViewerSwipeDirection.Right)]
    [InlineData(-6, 6, PhotoViewerSwipeDirection.Left)]
    [InlineData(-6, -6, PhotoViewerSwipeDirection.Left)]
    [InlineData(7, 8, PhotoViewerSwipeDirection.Bottom)]
    [InlineData(-7, -8, PhotoViewerSwipeDirection.Top)]
    [InlineData(120, 120, PhotoViewerSwipeDirection.Right)]
    [InlineData(-120, -120, PhotoViewerSwipeDirection.Left)]
    [InlineData(0, 0, PhotoViewerSwipeDirection.None)]
    public void Direction_ShortSwipesChooseTheClosestAxisAndDiagonalsChooseHorizontally(double x, double y, PhotoViewerSwipeDirection expected)
        => Assert.Equal(expected, PhotoViewerSwipeControl.GetDirection(new Vector(x, y)));

    [Theory]
    [InlineData(PhotoViewerSwipeDirection.Left)]
    [InlineData(PhotoViewerSwipeDirection.Right)]
    [InlineData(PhotoViewerSwipeDirection.Top)]
    [InlineData(PhotoViewerSwipeDirection.Bottom)]
    public void Selection_ExecutesOnlyTheChosenCommandOnce(PhotoViewerSwipeDirection direction)
    {
        var actions = new List<PhotoViewerSwipeDirection>();
        var wheel = new PhotoViewerSwipeControl
        {
            CanGoLeft = true, CanGoRight = true, CanMoveTop = true, CanMoveBottom = true,
            LeftCommand = new RelayCommand(() => actions.Add(PhotoViewerSwipeDirection.Left)),
            RightCommand = new RelayCommand(() => actions.Add(PhotoViewerSwipeDirection.Right)),
            TopCommand = new RelayCommand(() => actions.Add(PhotoViewerSwipeDirection.Top)),
            BottomCommand = new RelayCommand(() => actions.Add(PhotoViewerSwipeDirection.Bottom)),
            SelectedDirection = direction
        };

        Assert.True(wheel.TryExecuteSelectedAction());
        Assert.False(wheel.TryExecuteSelectedAction());
        Assert.Equal([direction], actions);
        Assert.Equal(PhotoViewerSwipeDirection.None, wheel.SelectedDirection);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Selection_RechecksAvailabilityAndCommandBeforeExecuting(bool available, bool canExecute)
    {
        var calls = 0;
        var wheel = new PhotoViewerSwipeControl
        {
            CanMoveTop = true,
            TopCommand = new RelayCommand(() => calls++, () => canExecute),
            SelectedDirection = PhotoViewerSwipeDirection.Top
        };
        // Availability can change while the finger is still down.
        wheel.CanMoveTop = available;
        Assert.False(wheel.TryExecuteSelectedAction());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Swipe_ShowsAtContactHighlightsDirectionAndDisappearsBeforeAction()
    {
        var (viewer, wheel) = CreateViewer();
        var calls = 0;
        wheel.CanGoLeft = true;
        wheel.LeftCommand = new RelayCommand(() => { Assert.False(wheel.IsVisible); calls++; });

        Call(viewer, "BeginPhotoViewerSwipe", new Point(220, 180));
        Assert.False(wheel.IsVisible);
        Call(viewer, "UpdatePhotoViewerSwipe", new Vector(-100, 0));
        Assert.True(wheel.IsVisible);
        Assert.Equal(220, Canvas.GetLeft(wheel) + wheel.Width / 2);
        Assert.Equal(180, Canvas.GetTop(wheel) + wheel.Height / 2);
        Assert.Contains("selected", wheel.FindControl<Button>("LeftButton")!.Classes);
        Call(viewer, "CompletePhotoViewerSwipe");
        Call(viewer, "CompletePhotoViewerSwipe");
        Assert.False(wheel.IsVisible);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("CancelPhotoViewerSwipe")]
    [InlineData("ResetPhotoViewerZoom")]
    public void InterruptedSwipe_HidesAndCannotPerformAnAction(string interruption)
    {
        var (viewer, wheel) = CreateViewer();
        var calls = 0;
        wheel.CanMoveTop = true;
        wheel.TopCommand = new RelayCommand(() => calls++);
        Call(viewer, "BeginPhotoViewerSwipe", new Point(220, 180));
        Call(viewer, "UpdatePhotoViewerSwipe", new Vector(0, -100));
        Assert.True(wheel.IsVisible);
        Call(viewer, interruption);
        Call(viewer, "CompletePhotoViewerSwipe");
        Assert.False(wheel.IsVisible);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("_photoViewerZoom", 2d)]
    [InlineData("_photoViewerPinchActive", true)]
    [InlineData("_mobilePinching", true)]
    public void ZoomAndPinch_CancelSwipeWithoutChoosingACategory(string field, object value)
    {
        var (viewer, wheel) = CreateViewer();
        var calls = 0;
        wheel.CanMoveBottom = true;
        wheel.BottomCommand = new RelayCommand(() => calls++);
        Call(viewer, "BeginPhotoViewerSwipe", new Point(220, 180));
        typeof(PhotoViewerView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewer, value);
        Call(viewer, "UpdatePhotoViewerSwipe", new Vector(0, 100));
        Call(viewer, "CompletePhotoViewerSwipe");
        Assert.False(wheel.IsVisible);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false, 1, 150, 0)]
    [InlineData(true, 0, 150, 0)]
    [InlineData(false, 1, 6, 0)]
    [InlineData(false, 1, 6, 6)]
    [InlineData(false, 1, 6, -6)]
    [InlineData(false, 1, 4.5, 4.5)]
    [InlineData(false, 1, 5, -5)]
    [InlineData(false, 0, 2, 2)]
    public void MobileScroll_ReleaseCommitsButCaptureLossCancels(bool loseCapture, int expectedCalls, double deltaX, double deltaY)
    {
        var (viewer, wheel) = CreateViewer();
        Call(viewer, "ConfigureMobileGestures");
        var host = viewer.FindControl<Canvas>("PhotoViewerImageHost")!;
        var scroll = host.GestureRecognizers.Single(recognizer => recognizer is ScrollGestureRecognizer);
        var calls = 0;
        wheel.CanGoRight = true;
        wheel.RightCommand = new RelayCommand(() => calls++);
        using var pointer = new Pointer(1, PointerType.Touch, true);
        Send(scroll, "PointerPressedInternal", new PointerPressedEventArgs(host, pointer, host, new Point(150, 150), 100,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        Send(scroll, "PointerMovedInternal", new PointerEventArgs(InputElement.PointerMovedEvent, host, pointer,
            host, new Point(150 + deltaX, 150 + deltaY), 150,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
        Assert.Equal(expectedCalls > 0 || loseCapture, wheel.IsVisible);
        if (loseCapture)
            Call(scroll, "PointerCaptureLost", pointer);
        else
            Send(scroll, "PointerReleasedInternal", new PointerReleasedEventArgs(host, pointer, host, new Point(150 + deltaX, 150 + deltaY), 200,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Assert.False(wheel.IsVisible);
        Assert.Equal(expectedCalls, calls);
        if (!loseCapture)
            Assert.Equal(expectedCalls > 0,
                (bool)typeof(Pointer).GetProperty("IsGestureRecognitionSkipped", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(pointer)!);
    }

    [Fact]
    public void MobileSecondContact_CancelsSelectionUntilBothFingersAreReleased()
    {
        var (viewer, wheel) = CreateViewer();
        Call(viewer, "ConfigureMobileGestures");
        var host = viewer.FindControl<Canvas>("PhotoViewerImageHost")!;
        var scroll = host.GestureRecognizers.Single(recognizer => recognizer is ScrollGestureRecognizer);
        var calls = 0;
        wheel.CanGoRight = true;
        wheel.RightCommand = new RelayCommand(() => calls++);
        using var first = new Pointer(1, PointerType.Touch, true);
        using var second = new Pointer(2, PointerType.Touch, false);
        Press(first);
        Move(first);
        Assert.True(wheel.IsVisible);
        Press(second);
        Assert.False(wheel.IsVisible);
        Move(second);
        Release(first);
        Release(second);
        Assert.False(wheel.IsVisible);
        Assert.Equal(0, calls);

        using var nextSwipe = new Pointer(3, PointerType.Touch, true);
        Press(nextSwipe);
        Move(nextSwipe);
        Release(nextSwipe);
        Assert.Equal(1, calls);

        void Press(Pointer pointer) => Send(scroll, "PointerPressedInternal", new PointerPressedEventArgs(host, pointer, host,
            new Point(150, 150), 100,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        void Move(Pointer pointer) => Send(scroll, "PointerMovedInternal", new PointerEventArgs(InputElement.PointerMovedEvent, host, pointer,
            host, new Point(300, 150), 150,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
        void Release(Pointer pointer) => Send(scroll, "PointerReleasedInternal", new PointerReleasedEventArgs(host, pointer, host,
            new Point(300, 150), 200,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
    }

    private static (PhotoViewerView Viewer, PhotoViewerSwipeControl Wheel) CreateViewer()
    {
        var viewer = new PhotoViewerView();
        viewer.FindControl<Image>("PhotoViewerImageControl")!.Source = new DrawingImage();
        return (viewer, viewer.FindControl<PhotoViewerSwipeControl>("PhotoViewerSwipeWheel")!);
    }

    private static void Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    private static void Send(GestureRecognizer recognizer, string method, PointerEventArgs args)
        => typeof(GestureRecognizer).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(recognizer, [args]);
}
