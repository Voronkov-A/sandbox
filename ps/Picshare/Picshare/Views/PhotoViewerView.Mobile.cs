using Avalonia;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Media;
using Picshare.ViewModels;

namespace Picshare.Views;

public partial class PhotoViewerView
{
    private static bool UsesMobileGestures => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
    private bool _mobilePinching;
    private double _mobilePinchStartZoom;
    private Point _mobilePinchStartOrigin;
    private Point _mobilePinchAnchor;
    private Point _mobileSwipeStart;
    private ScrollGestureRecognizer? _mobileScrollRecognizer;

    private void ConfigureMobileGestures()
    {
        // Let the framework own touch contacts/capture rather than mixing them with the
        // desktop mouse drag and wheel anchoring code. The host covers the full viewport.
        PhotoViewerImageHost.Background = Brushes.Transparent;
        var pinchRecognizer = new TapFriendlyPinchGestureRecognizer
        {
            MultipleContactsPressed = CancelPhotoViewerSwipe
        };
        PhotoViewerImageHost.GestureRecognizers.Add(pinchRecognizer);
        _mobileScrollRecognizer = new SwipeAwareScrollGestureRecognizer
        {
            CanHorizontallyScroll = true,
            CanVerticallyScroll = true,
            IsScrollInertiaEnabled = false,
            Starting = e =>
            {
                if (pinchRecognizer.HadMultipleContacts) return;
                _mobileSwipeStart = e.GetPosition(null);
                BeginPhotoViewerSwipe(e.GetPosition(PhotoViewerViewport));
            },
            // Use the full contact displacement, including the framework's scroll-start tolerance.
            Moving = e => UpdatePhotoViewerSwipe(e.GetPosition(null) - _mobileSwipeStart),
            Canceled = CancelPhotoViewerSwipe,
            Released = e =>
            {
                // A short diagonal can select an action before the scroll recognizer starts.
                if (_photoViewerSwipeActive && PhotoViewerSwipeWheel.SelectedDirection != PhotoViewerSwipeDirection.None)
                {
                    e.PreventGestureRecognition();
                    e.Handled = true;
                }
                CompletePhotoViewerSwipe();
            }
        };
        PhotoViewerImageHost.GestureRecognizers.Add(_mobileScrollRecognizer);
        PhotoViewerImageHost.AddHandler(Gestures.PinchEvent, MobilePhoto_Pinch);
        PhotoViewerImageHost.AddHandler(Gestures.PinchEndedEvent, (_, _) => _mobilePinching = false);
        PhotoViewerImageHost.AddHandler(Gestures.ScrollGestureEvent, MobilePhoto_Scroll);
        PhotoViewerImageHost.AddHandler(Gestures.ScrollGestureEndedEvent, MobilePhoto_ScrollEnded);
        PhotoViewerImageHost.Tapped += (_, e) =>
        {
            if (!_mobilePinching && DataContext is MainViewModel viewModel)
                viewModel.TogglePhotoViewerActionsCommand.Execute(null);
            e.Handled = true;
        };
        PhotoViewerZoomSliderContainer.IsVisible = false;
    }

    private sealed class TapFriendlyPinchGestureRecognizer : PinchGestureRecognizer
    {
        private readonly HashSet<IPointer> _contacts = [];
        private bool _hadMultipleContacts;
        public bool HadMultipleContacts => _hadMultipleContacts;
        public Action? MultipleContactsPressed { get; init; }

        protected override void PointerPressed(PointerPressedEventArgs e)
        {
            if (e.Pointer.Type is PointerType.Touch or PointerType.Pen)
            {
                _contacts.Add(e.Pointer);
                _hadMultipleContacts |= _contacts.Count > 1;
                if (_hadMultipleContacts) MultipleContactsPressed?.Invoke();
            }
            base.PointerPressed(e);
        }

        protected override void PointerReleased(PointerReleasedEventArgs e)
        {
            // Avalonia's pinch recognizer prevents Tapped even when only one contact was
            // tracked. Use its capture-loss cleanup to end tracking without claiming a tap.
            if (_hadMultipleContacts)
            {
                base.PointerReleased(e);
                e.PreventGestureRecognition();
            }
            else base.PointerCaptureLost(e.Pointer);
            RemoveContact(e.Pointer);
        }

        protected override void PointerCaptureLost(IPointer pointer)
        {
            base.PointerCaptureLost(pointer);
            RemoveContact(pointer);
        }

        private void RemoveContact(IPointer pointer)
        {
            _contacts.Remove(pointer);
            if (_contacts.Count == 0) _hadMultipleContacts = false;
        }
    }

    private sealed class SwipeAwareScrollGestureRecognizer : ScrollGestureRecognizer
    {
        private readonly HashSet<IPointer> _contacts = [];
        private bool _hadMultipleContacts;
        public Action<PointerPressedEventArgs>? Starting { get; init; }
        public Action<PointerEventArgs>? Moving { get; init; }
        public Action? Canceled { get; init; }
        public Action<PointerReleasedEventArgs>? Released { get; init; }

        protected override void PointerPressed(PointerPressedEventArgs e)
        {
            // End any previous scroll without treating its interruption as a swipe.
            Canceled?.Invoke();
            if (e.Pointer.Type is PointerType.Touch or PointerType.Pen)
            {
                _contacts.Add(e.Pointer);
                _hadMultipleContacts |= _contacts.Count > 1;
            }
            base.PointerPressed(e);
            if (!_hadMultipleContacts && e.Pointer.Type is PointerType.Touch or PointerType.Pen)
                Starting?.Invoke(e);
        }

        protected override void PointerMoved(PointerEventArgs e)
        {
            base.PointerMoved(e);
            if (_contacts.Contains(e.Pointer)) Moving?.Invoke(e);
        }

        protected override void PointerCaptureLost(IPointer pointer)
        {
            // The base class raises ScrollGestureEnded for cancellation as well as release.
            Canceled?.Invoke();
            base.PointerCaptureLost(pointer);
            RemoveContact(pointer);
        }

        protected override void PointerReleased(PointerReleasedEventArgs e)
        {
            if (_contacts.Contains(e.Pointer)) Moving?.Invoke(e);
            base.PointerReleased(e);
            Released?.Invoke(e);
            RemoveContact(e.Pointer);
        }

        private void RemoveContact(IPointer pointer)
        {
            _contacts.Remove(pointer);
            if (_contacts.Count == 0) _hadMultipleContacts = false;
        }
    }

    private void MobilePhoto_Pinch(object? sender, PinchEventArgs e)
    {
        if (PhotoViewerImageControl.Source is null) return;
        if (!_mobilePinching)
        {
            _mobilePinching = true;
            CancelPhotoViewerSwipe();
            _mobilePinchStartZoom = _photoViewerZoom;
            _mobilePinchStartOrigin = _photoViewerImageOrigin;
            _mobilePinchAnchor = e.ScaleOrigin;
        }
        var result = CalculateMobilePinch(_mobilePinchStartZoom, _mobilePinchStartOrigin, _mobilePinchAnchor,
            e.ScaleOrigin, e.Scale, Math.Min(8, GetMaximumPhotoViewerZoom()));
        _photoViewerZoom = result.Zoom;
        _photoViewerImageOrigin = result.Origin;
        ApplyPhotoViewerZoom();
        if (_mobileScrollRecognizer is not null) _mobileScrollRecognizer.IsScrollInertiaEnabled = _photoViewerZoom > MinimumPhotoViewerZoom;
        e.Handled = true;
    }

    private void MobilePhoto_Scroll(object? sender, ScrollGestureEventArgs e)
    {
        if (!_mobilePinching && PhotoViewerImageControl.Source is not null)
        {
            if (_photoViewerZoom > MinimumPhotoViewerZoom)
                SetPhotoViewerImageOrigin(_photoViewerImageOrigin - e.Delta);
        }
        e.Handled = true;
    }

    private void MobilePhoto_ScrollEnded(object? sender, ScrollGestureEndedEventArgs e)
    {
        CompletePhotoViewerSwipe();
        e.Handled = true;
    }

    private static (double Zoom, Point Origin) CalculateMobilePinch(double startZoom, Point startOrigin,
        Point startAnchor, Point currentAnchor, double cumulativeScale, double maximumZoom)
    {
        if (!double.IsFinite(cumulativeScale) || cumulativeScale <= 0) return (startZoom, startOrigin);
        var zoom = Math.Clamp(startZoom * cumulativeScale, MinimumPhotoViewerZoom, Math.Max(MinimumPhotoViewerZoom, maximumZoom));
        var ratio = zoom / startZoom;
        return (zoom, new Point(currentAnchor.X - (startAnchor.X - startOrigin.X) * ratio,
            currentAnchor.Y - (startAnchor.Y - startOrigin.Y) * ratio));
    }

    private static Point ClampMobileImageOrigin(Point origin, Size viewport, double width, double height, bool quarterTurn)
    {
        var renderedWidth = quarterTurn ? height : width;
        var renderedHeight = quarterTurn ? width : height;
        var offsetX = (width - renderedWidth) / 2;
        var offsetY = (height - renderedHeight) / 2;
        return new Point(ClampPhotoViewerAxisOrigin(origin.X + offsetX, viewport.Width, renderedWidth) - offsetX,
            ClampPhotoViewerAxisOrigin(origin.Y + offsetY, viewport.Height, renderedHeight) - offsetY);
    }
}
