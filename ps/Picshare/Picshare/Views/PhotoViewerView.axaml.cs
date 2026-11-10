using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Picshare.Services;
using Picshare.ViewModels;

namespace Picshare.Views;

public partial class PhotoViewerView : UserControl, ILazyActivated
{
    private const double MinimumPhotoViewerZoom = 1;
    private const double FooterActionButtonMaxWidth = 92;
    private const double FooterActionButtonMinWidth = 5;
    private const double FooterActionButtonCompactThreshold = 70;
    private const double FooterActionButtonSpacing = 8;
    private const double PhotoViewerFooterHorizontalPadding = 16;
    private const double OverlayCornerRadius = 6;
    private const double PhotoViewerZoomSliderDefaultHeight = 180;
    private const double PhotoViewerOverlayVerticalGap = 8;
    private const double PhotoViewerDuplicateStripScrollStep = 180;
    private const double PhotoViewerDuplicateStripDragThreshold = 6;

    private readonly Dictionary<object, Point> _photoViewerPointers = [];
    private double _photoViewerBaseWidth;
    private double _photoViewerBaseHeight;
    private double _photoViewerZoom = MinimumPhotoViewerZoom;
    private double _pinchStartDistance;
    private double _pinchStartZoom = MinimumPhotoViewerZoom;
    private Point _pinchStartCenter;
    private bool _photoViewerPointerPressed;
    private bool _photoViewerPointerMoved;
    private bool _photoViewerPinchActive;
    private Point _photoViewerPointerStart;
    private Point _photoViewerPointerStartImageOrigin;
    private Point _photoViewerImageOrigin;
    private bool _isUpdatingPhotoViewerZoomSlider;
    private object? _photoViewerDuplicateStripDragPointer;
    private Point _photoViewerDuplicateStripDragStart;
    private Vector _photoViewerDuplicateStripDragStartOffset;
    private bool _photoViewerDuplicateStripDragMoved;
    private INotifyPropertyChanged? _viewModelPropertyChanged;

    public PhotoViewerView()
    {
        InitializeComponent();
        PhotoViewerDuplicateStripScrollViewer.AddHandler(
            InputElement.PointerPressedEvent,
            PhotoViewerDuplicateStrip_PointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        PhotoViewerDuplicateStripScrollViewer.AddHandler(
            InputElement.PointerMovedEvent,
            PhotoViewerDuplicateStrip_PointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        PhotoViewerDuplicateStripScrollViewer.AddHandler(
            InputElement.PointerReleasedEvent,
            PhotoViewerDuplicateStrip_PointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        PhotoViewerDuplicateStripScrollViewer.AddHandler(
            InputElement.PointerCaptureLostEvent,
            PhotoViewerDuplicateStrip_PointerCaptureLost,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        if (UsesMobileGestures)
        {
            ConfigureMobileGestures();
        }
        else PhotoViewerScrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            PhotoViewer_PointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    public void OnLazyActivated()
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            FocusPhotoViewerOverlay();
            ResetPhotoViewerZoom();
            UpdatePhotoViewerDuplicateStripScrollButtonVisibility();
            UpdateFooterActionButtonWidths();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModelPropertyChanged is not null)
        {
            _viewModelPropertyChanged.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModelPropertyChanged = DataContext as INotifyPropertyChanged;
        if (_viewModelPropertyChanged is not null)
        {
            _viewModelPropertyChanged.PropertyChanged += ViewModel_PropertyChanged;
        }

        base.OnDataContextChanged(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModelPropertyChanged is not null)
        {
            _viewModelPropertyChanged.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModelPropertyChanged = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.PhotoViewerImage)
            or nameof(MainViewModel.PhotoViewerRotationDegrees)
            or nameof(MainViewModel.ZoomPower)
            or nameof(MainViewModel.PhotoViewerAspectRatioMode))
        {
            _ = Dispatcher.UIThread.InvokeAsync(ResetPhotoViewerZoom, DispatcherPriority.Render);
        }
        else if (e.PropertyName is nameof(MainViewModel.IsPhotoViewerDuplicateStripVisible))
        {
            _ = Dispatcher.UIThread.InvokeAsync(UpdatePhotoViewerDuplicateStripScrollButtonVisibility, DispatcherPriority.Render);
            _ = Dispatcher.UIThread.InvokeAsync(UpdatePhotoViewerAdaptiveOverlayWidths, DispatcherPriority.Render);
        }
        else if (e.PropertyName is nameof(MainViewModel.IsCurrentPhotoScoreVisible)
            or nameof(MainViewModel.IsPhotoViewerActionsVisible)
            or nameof(MainViewModel.ShowPhotoViewerPreviousNextButtons)
            or nameof(MainViewModel.ShouldShowCurrentPhotoUncategorizedAction)
            or nameof(MainViewModel.ShouldShowCurrentPhotoNiceAction)
            or nameof(MainViewModel.ShouldShowCurrentPhotoOkAction))
        {
            _ = Dispatcher.UIThread.InvokeAsync(UpdatePhotoViewerAdaptiveOverlayWidths, DispatcherPriority.Render);
        }
    }

    private async void DownloadCurrentPhoto_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            if (viewModel.IsMobilePlatform)
            {
                await MobileDownloadHelper.SaveAsync(this, viewModel, viewModel.CurrentPhotoDownloadFileName,
                    zip: false, viewModel.SaveMobileCurrentPhotoAsync);
                return;
            }
            var file = await StoragePickerHelper.OpenSaveFilePickerAsync(
                topLevel,
                "Save photo",
                viewModel.PictureDefaultDownloadDirectoryPath,
                viewModel.CurrentPhotoDownloadFileName,
                GetImageFileTypeChoices(viewModel.CurrentPhotoDownloadFileName));

            if (file?.TryGetLocalPath() is { } destinationPath)
            {
                await viewModel.DownloadCurrentPhotoAsync(destinationPath);
            }
        }
    }

    private async void PhotoViewerOverlay_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel { IsPhotoViewerVisible: true } viewModel)
        {
            return;
        }

        if (e.Key is Key.Escape or Key.BrowserBack or Key.Back)
        {
            viewModel.ClosePhotoViewerCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right)
        {
            var direction = e.Key == Key.Left ? -1 : 1;
            if (_photoViewerZoom > MinimumPhotoViewerZoom + 0.001)
            {
                PanPhotoViewerHorizontally(direction);
            }
            else if (viewModel.CanNavigatePhotoViewerDuplicateStrip)
            {
                await viewModel.MovePhotoViewerInDuplicateStripAsync(direction);
            }
            else if (direction < 0)
            {
                viewModel.ShowPreviousPhotoInCategoryCommand.Execute(null);
            }
            else
            {
                viewModel.ShowNextPhotoInCategoryCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void PanPhotoViewerHorizontally(int direction)
    {
        var viewport = GetPhotoViewerViewportSize();
        var step = Math.Clamp(viewport.Width * 0.16, 48, 180);
        SetPhotoViewerImageOrigin(new Point(
            _photoViewerImageOrigin.X - direction * step,
            _photoViewerImageOrigin.Y));
    }

    private void FocusPhotoViewerOverlay()
    {
        if (DataContext is MainViewModel { IsPhotoViewerVisible: true })
        {
            PhotoViewerOverlay.Focus();
        }
    }

    private void ScrollPhotoViewerDuplicateStripLeft_Click(object? sender, RoutedEventArgs e)
    {
        ScrollPhotoViewerDuplicateStrip(-PhotoViewerDuplicateStripScrollStep);
        e.Handled = true;
    }

    private void ScrollPhotoViewerDuplicateStripRight_Click(object? sender, RoutedEventArgs e)
    {
        ScrollPhotoViewerDuplicateStrip(PhotoViewerDuplicateStripScrollStep);
        e.Handled = true;
    }

    private void PhotoViewerDuplicateStripScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdatePhotoViewerDuplicateStripScrollButtonVisibility();
    }

    private void PhotoViewerDuplicateStripScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = Dispatcher.UIThread.InvokeAsync(UpdatePhotoViewerDuplicateStripScrollButtonVisibility, DispatcherPriority.Render);
        _ = Dispatcher.UIThread.InvokeAsync(UpdatePhotoViewerActionFooterShape, DispatcherPriority.Render);
    }

    private void PhotoViewerDuplicateStrip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_photoViewerDuplicateStripDragPointer is not null)
        {
            return;
        }

        _photoViewerDuplicateStripDragPointer = e.Pointer;
        _photoViewerDuplicateStripDragStart = e.GetPosition(PhotoViewerDuplicateStripScrollViewer);
        _photoViewerDuplicateStripDragStartOffset = PhotoViewerDuplicateStripScrollViewer.Offset;
        _photoViewerDuplicateStripDragMoved = false;
    }

    private void PhotoViewerDuplicateStrip_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(_photoViewerDuplicateStripDragPointer, e.Pointer))
        {
            return;
        }

        var delta = e.GetPosition(PhotoViewerDuplicateStripScrollViewer) - _photoViewerDuplicateStripDragStart;
        if (!_photoViewerDuplicateStripDragMoved && Math.Abs(delta.X) < PhotoViewerDuplicateStripDragThreshold)
        {
            return;
        }

        _photoViewerDuplicateStripDragMoved = true;
        SetPhotoViewerDuplicateStripOffset(_photoViewerDuplicateStripDragStartOffset.X - delta.X);
        e.Handled = true;
    }

    private void PhotoViewerDuplicateStrip_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(_photoViewerDuplicateStripDragPointer, e.Pointer))
        {
            return;
        }

        var handled = _photoViewerDuplicateStripDragMoved;
        _photoViewerDuplicateStripDragPointer = null;
        _photoViewerDuplicateStripDragMoved = false;
        e.Handled = handled;
    }

    private void PhotoViewerDuplicateStrip_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(_photoViewerDuplicateStripDragPointer, e.Pointer))
        {
            _photoViewerDuplicateStripDragPointer = null;
            _photoViewerDuplicateStripDragMoved = false;
        }
    }

    private void ScrollPhotoViewerDuplicateStrip(double delta)
    {
        SetPhotoViewerDuplicateStripOffset(PhotoViewerDuplicateStripScrollViewer.Offset.X + delta);
    }

    private void SetPhotoViewerDuplicateStripOffset(double offset)
    {
        var maxOffset = Math.Max(0, PhotoViewerDuplicateStripScrollViewer.Extent.Width - PhotoViewerDuplicateStripScrollViewer.Viewport.Width);
        var nextOffset = Math.Clamp(offset, 0, maxOffset);
        PhotoViewerDuplicateStripScrollViewer.Offset = new Vector(nextOffset, PhotoViewerDuplicateStripScrollViewer.Offset.Y);
        UpdatePhotoViewerDuplicateStripScrollButtonVisibility();
    }

    private void UpdatePhotoViewerDuplicateStripScrollButtonVisibility()
    {
        const double tolerance = 0.5;
        var maxOffset = Math.Max(0, PhotoViewerDuplicateStripScrollViewer.Extent.Width - PhotoViewerDuplicateStripScrollViewer.Viewport.Width);
        if (maxOffset <= tolerance)
        {
            PhotoViewerDuplicateStripScrollLeftButton.IsVisible = false;
            PhotoViewerDuplicateStripScrollRightButton.IsVisible = false;
            return;
        }

        PhotoViewerDuplicateStripScrollLeftButton.IsVisible = PhotoViewerDuplicateStripScrollViewer.Offset.X > tolerance;
        PhotoViewerDuplicateStripScrollRightButton.IsVisible = PhotoViewerDuplicateStripScrollViewer.Offset.X < maxOffset - tolerance;
    }

    private void FooterActionButton_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Button button)
        {
            button.Classes.Set("compact-footer-action", e.NewSize.Width < FooterActionButtonCompactThreshold);
        }
    }

    private void FooterActionPanelHost_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateFooterActionButtonWidths();
    }

    private void PhotoViewerOverlaySurface_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = Dispatcher.UIThread.InvokeAsync(UpdatePhotoViewerActionFooterShape, DispatcherPriority.Render);
    }

    private void PhotoViewerViewport_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = Dispatcher.UIThread.InvokeAsync(UpdatePhotoViewerAdaptiveOverlayWidths, DispatcherPriority.Render);
    }

    private void UpdateFooterActionButtonWidths()
    {
        UpdatePhotoViewerAdaptiveOverlayWidths();
    }

    private void UpdatePhotoViewerAdaptiveOverlayWidths()
    {
        UpdatePhotoViewerHeaderWidths();
        UpdatePhotoViewerZoomSliderHeight();
        UpdatePhotoViewerCleanFooterActionButtonWidths();
        UpdatePhotoViewerActionFooterShape();
    }

    private void UpdatePhotoViewerHeaderWidths()
    {
        var viewportWidth = Math.Max(0, PhotoViewerViewport.Bounds.Width);
        var horizontalMargin = 16d;
        var sideGap = 8d;
        var edgeMargin = horizontalMargin / 2;
        var squareWidth = 42d;
        var closeWidth = squareWidth;
        var titleContainerPadding = 16d;
        var titleLabelNaturalWidth = PhotoViewerTitleLabel.Bounds.Width is > 0 and < 520
            ? PhotoViewerTitleLabel.Bounds.Width
            : 520;
        var titleContainerNaturalWidth = titleLabelNaturalWidth + titleContainerPadding;
        var scoreWidth = PhotoViewerScoreLabel.IsVisible ? FooterActionButtonMaxWidth : 0;

        if (PhotoViewerScoreLabel.IsVisible)
        {
            var maxSideWidthForNaturalTitle = (viewportWidth - titleContainerNaturalWidth) / 2 - edgeMargin - sideGap;
            scoreWidth = Math.Clamp(maxSideWidthForNaturalTitle, squareWidth, FooterActionButtonMaxWidth);
        }

        var sideWidth = Math.Max(scoreWidth, closeWidth);
        var titleContainerMaxWidth = viewportWidth - 2 * (edgeMargin + sideGap + sideWidth);
        if (titleContainerMaxWidth < FooterActionButtonMinWidth + titleContainerPadding)
        {
            titleContainerMaxWidth = FooterActionButtonMinWidth + titleContainerPadding;
            var maxSideWidthForMinimumTitle = (viewportWidth - titleContainerMaxWidth) / 2 - edgeMargin - sideGap;
            closeWidth = Math.Clamp(maxSideWidthForMinimumTitle, FooterActionButtonMinWidth, squareWidth);
            scoreWidth = PhotoViewerScoreLabel.IsVisible
                ? Math.Clamp(maxSideWidthForMinimumTitle, FooterActionButtonMinWidth, squareWidth)
                : 0;
        }

        var titleLabelMaxWidth = Math.Max(FooterActionButtonMinWidth, titleContainerMaxWidth - titleContainerPadding);

        PhotoViewerScoreLabel.Width = scoreWidth;
        PhotoViewerScoreIcon.IsVisible = PhotoViewerScoreLabel.Width >= FooterActionButtonCompactThreshold;
        PhotoViewerCloseButton.Width = closeWidth;
        PhotoViewerTitleContainer.Width = double.NaN;
        PhotoViewerTitleContainer.MaxWidth = Math.Min(536, titleContainerMaxWidth);
        PhotoViewerTitleLabel.Width = double.NaN;
        PhotoViewerTitleLabel.MaxWidth = Math.Min(520, titleLabelMaxWidth);
        PhotoViewerBestInGroupLabel.MaxWidth = Math.Max(
            FooterActionButtonMinWidth,
            viewportWidth - horizontalMargin - titleContainerPadding);
    }

    private void UpdatePhotoViewerZoomSliderHeight()
    {
        var viewportHeight = Math.Max(0, PhotoViewerViewport.Bounds.Height);
        var topReservedHeight = 56d;
        if (PhotoViewerDuplicateStripContainer.IsVisible)
        {
            topReservedHeight += 56d;
        }

        var bottomReservedHeight = 56d;
        if (PhotoViewerDuplicateStripContainer.IsVisible)
        {
            bottomReservedHeight += 68d;
        }

        var availableHeight = viewportHeight - topReservedHeight - bottomReservedHeight - PhotoViewerOverlayVerticalGap * 2;
        PhotoViewerZoomSliderContainer.Height = Math.Clamp(availableHeight, FooterActionButtonMinWidth, PhotoViewerZoomSliderDefaultHeight);
        UpdatePhotoViewerZoomSliderThumb(_photoViewerZoom);
    }

    private void UpdatePhotoViewerCleanFooterActionButtonWidths()
    {
        var variableButtons = PhotoViewerActionFooterClean.Children
            .OfType<Button>()
            .Where(button => button.IsVisible && double.IsFinite(button.MaxWidth))
            .ToList();
        if (variableButtons.Count == 0)
        {
            return;
        }

        var visibleButtons = PhotoViewerActionFooterClean.Children.OfType<Button>().Where(button => button.IsVisible).ToList();
        var fixedButtons = visibleButtons
            .Where(button => !double.IsFinite(button.MaxWidth))
            .ToList();
        var visibleButtonCount = visibleButtons.Count;
        var maxContentWidth = Math.Max(0, PhotoViewerViewport.Bounds.Width - PhotoViewerFooterHorizontalPadding);
        var availableButtonWidth = maxContentWidth - (visibleButtonCount - 1) * FooterActionButtonSpacing;
        var variableButtonWidth = FooterActionButtonMaxWidth;
        var fixedButtonWidth = 42d;
        var availableVariableWidth = availableButtonWidth - fixedButtons.Count * fixedButtonWidth;
        if (variableButtons.Count > 0)
        {
            variableButtonWidth = Math.Clamp(availableVariableWidth / variableButtons.Count, fixedButtonWidth, FooterActionButtonMaxWidth);
        }

        if (variableButtons.Count > 0 && availableVariableWidth / variableButtons.Count < fixedButtonWidth)
        {
            var commonWidth = Math.Clamp(availableButtonWidth / visibleButtonCount, FooterActionButtonMinWidth, fixedButtonWidth);
            variableButtonWidth = commonWidth;
            fixedButtonWidth = commonWidth;
        }

        foreach (var button in variableButtons)
        {
            button.Width = variableButtonWidth;
            button.Classes.Set("compact-footer-action", variableButtonWidth < FooterActionButtonCompactThreshold);
        }

        foreach (var button in fixedButtons)
        {
            button.Width = fixedButtonWidth;
        }
    }

    private void UpdatePhotoViewerActionFooterShape()
    {
        var footerNaturalWidth = GetPhotoViewerActionFooterNaturalWidth();
        var stripWidth = PhotoViewerDuplicateStripContainer.IsVisible
            ? PhotoViewerDuplicateStripContainer.Bounds.Width
            : 0;

        if (stripWidth > 0 && footerNaturalWidth <= stripWidth)
        {
            PhotoViewerActionFooterCleanContainer.MinWidth = stripWidth;
            PhotoViewerActionFooterCleanContainer.CornerRadius = new CornerRadius(0);
        }
        else
        {
            PhotoViewerActionFooterCleanContainer.MinWidth = 0;
            PhotoViewerActionFooterCleanContainer.CornerRadius = new CornerRadius(OverlayCornerRadius, OverlayCornerRadius, 0, 0);
        }
    }

    private double GetPhotoViewerActionFooterNaturalWidth()
    {
        var visibleButtons = PhotoViewerActionFooterClean.Children
            .OfType<Button>()
            .Where(button => button.IsVisible)
            .ToList();
        if (visibleButtons.Count == 0)
        {
            return PhotoViewerFooterHorizontalPadding;
        }

        var buttonWidth = visibleButtons.Sum(button =>
        {
            if (double.IsFinite(button.Width) && button.Width > 0)
            {
                return button.Width;
            }

            if (double.IsFinite(button.MaxWidth))
            {
                return Math.Min(FooterActionButtonMaxWidth, button.MaxWidth);
            }

            return button.Bounds.Width;
        });

        return buttonWidth
            + (visibleButtons.Count - 1) * FooterActionButtonSpacing
            + PhotoViewerFooterHorizontalPadding;
    }

    private static IReadOnlyList<FilePickerFileType>? GetImageFileTypeChoices(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        return
        [
            new("Image")
            {
                Patterns = [$"*{extension}"]
            }
        ];
    }

    private async void DuplicatePhotoViewerItem_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: AlbumPhotoViewModel photo })
        {
            await viewModel.ShowDuplicatePhotoInViewerAsync(photo);
            await Dispatcher.UIThread.InvokeAsync(ResetPhotoViewerZoom, DispatcherPriority.Render);
            e.Handled = true;
        }
    }

    private void PhotoViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (UsesMobileGestures) return;
        if (PhotoViewerImageControl.Source is null)
        {
            return;
        }

        var factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        SetPhotoViewerZoom(_photoViewerZoom * factor, e.GetPosition(PhotoViewerScrollViewer));
        e.Handled = true;
    }

    private void PhotoViewer_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (UsesMobileGestures) return;
        _photoViewerPointers[e.Pointer] = e.GetPosition(PhotoViewerViewport);
        _photoViewerPointerPressed = _photoViewerPointers.Count == 1;
        _photoViewerPointerStart = e.GetPosition(PhotoViewerViewport);
        _photoViewerPointerStartImageOrigin = _photoViewerImageOrigin;
        _photoViewerPointerMoved = false;
        e.Pointer.Capture(PhotoViewerViewport);

        if (_photoViewerPointers.Count == 2)
        {
            _pinchStartDistance = GetActivePointerDistance();
            _pinchStartZoom = _photoViewerZoom;
            _pinchStartCenter = GetActivePointerCenter(PhotoViewerScrollViewer);
            _photoViewerPinchActive = true;
        }
    }

    private void PhotoViewer_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (UsesMobileGestures) return;
        if (!_photoViewerPointers.ContainsKey(e.Pointer))
        {
            return;
        }

        _photoViewerPointers[e.Pointer] = e.GetPosition(PhotoViewerViewport);
        _photoViewerPointerMoved = true;
        if (_photoViewerPointers.Count >= 2 && _pinchStartDistance > 0)
        {
            SetPhotoViewerZoom(_pinchStartZoom * GetActivePointerDistance() / _pinchStartDistance, _pinchStartCenter);
            _pinchStartCenter = GetActivePointerCenter(PhotoViewerScrollViewer);
            _pinchStartZoom = _photoViewerZoom;
            _pinchStartDistance = GetActivePointerDistance();
            e.Handled = true;
        }
        else if (_photoViewerPointers.Count == 1 && _photoViewerZoom > MinimumPhotoViewerZoom)
        {
            var delta = e.GetPosition(PhotoViewerViewport) - _photoViewerPointerStart;
            SetPhotoViewerImageOrigin(_photoViewerPointerStartImageOrigin + delta);
            e.Handled = true;
        }
    }

    private void PhotoViewer_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (UsesMobileGestures) return;
        var endPosition = e.GetPosition(PhotoViewerViewport);
        var delta = endPosition - _photoViewerPointerStart;
        var shouldToggleActions = _photoViewerPointerPressed &&
            !_photoViewerPointerMoved &&
            !_photoViewerPinchActive &&
            _photoViewerPointers.Count == 1 &&
            DataContext is MainViewModel;
        var swipeDirection = GetPhotoViewerSwipeDirection(delta);

        _photoViewerPointers.Remove(e.Pointer);
        e.Pointer.Capture(null);

        if (_photoViewerPointers.Count == 2)
        {
            _pinchStartDistance = GetActivePointerDistance();
            _pinchStartZoom = _photoViewerZoom;
            _pinchStartCenter = GetActivePointerCenter(PhotoViewerScrollViewer);
        }
        else if (_photoViewerPointers.Count < 2)
        {
            _pinchStartDistance = 0;
            _photoViewerPinchActive = false;
        }

        if (shouldToggleActions && DataContext is MainViewModel viewModel)
        {
            viewModel.TogglePhotoViewerActionsCommand.Execute(null);
        }
        else if (swipeDirection is not 0 && DataContext is MainViewModel swipeViewModel)
        {
            if (swipeDirection > 0)
            {
                swipeViewModel.ShowPreviousPhotoInCategoryCommand.Execute(null);
            }
            else
            {
                swipeViewModel.ShowNextPhotoInCategoryCommand.Execute(null);
            }
        }
    }

    private void PhotoViewer_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (UsesMobileGestures) return;
        if (_photoViewerPointers.Remove(e.Pointer) && _photoViewerPointers.Count < 2)
        {
            _pinchStartDistance = 0;
            _photoViewerPinchActive = false;
        }
    }

    private void ResetPhotoViewerZoom()
    {
        if (UsesMobileGestures)
        {
            _mobilePinching = false;
            _mobileSwipeDelta = default;
            if (_mobileScrollRecognizer is not null) _mobileScrollRecognizer.IsScrollInertiaEnabled = false;
        }
        _photoViewerPointers.Clear();
        if (DataContext is not MainViewModel { PhotoViewerImage: { } image } viewModel)
        {
            return;
        }

        _photoViewerZoom = MinimumPhotoViewerZoom;
        var viewportWidth = Math.Max(1, PhotoViewerScrollViewer.Bounds.Width > 1 ? PhotoViewerScrollViewer.Bounds.Width : Bounds.Width);
        var viewportHeight = Math.Max(1, PhotoViewerScrollViewer.Bounds.Height > 1 ? PhotoViewerScrollViewer.Bounds.Height : Bounds.Height);
        var pixelWidth = Math.Max(1, image.PixelSize.Width);
        var pixelHeight = Math.Max(1, image.PixelSize.Height);
        var isQuarterTurn = viewModel.PhotoViewerRotationDegrees is 90 or 270;
        var effectiveWidth = isQuarterTurn ? pixelHeight : pixelWidth;
        var effectiveHeight = isQuarterTurn ? pixelWidth : pixelHeight;
        var fitScale = Math.Min(viewportWidth / effectiveWidth, viewportHeight / effectiveHeight);
        var maximumZoom = GetMaximumPhotoViewerZoom(image);

        if (viewModel.IsPhotoViewerStretchToFitAspectRatioMode)
        {
            _photoViewerBaseWidth = isQuarterTurn ? viewportHeight : viewportWidth;
            _photoViewerBaseHeight = isQuarterTurn ? viewportWidth : viewportHeight;
            PhotoViewerImageControl.Stretch = Stretch.Fill;
        }
        else
        {
            _photoViewerBaseWidth = pixelWidth * fitScale;
            _photoViewerBaseHeight = pixelHeight * fitScale;
            PhotoViewerImageControl.Stretch = Stretch.Uniform;
        }
        PhotoViewerZoomSlider.Maximum = 1;
        SetPhotoViewerSliderValue(_photoViewerZoom);
        var imageWidth = _photoViewerBaseWidth * _photoViewerZoom;
        var imageHeight = _photoViewerBaseHeight * _photoViewerZoom;
        var viewport = GetPhotoViewerViewportSize();
        _photoViewerImageOrigin = new Point(
            (viewport.Width - imageWidth) / 2,
            (viewport.Height - imageHeight) / 2);
        ApplyPhotoViewerZoom();
    }

    private void SetPhotoViewerZoom(double zoom)
    {
        SetPhotoViewerZoom(zoom, new Point(PhotoViewerScrollViewer.Bounds.Width / 2, PhotoViewerScrollViewer.Bounds.Height / 2));
    }

    private void ApplyPhotoViewerZoom()
    {
        if (_photoViewerBaseWidth <= 0 || _photoViewerBaseHeight <= 0)
        {
            return;
        }

        PhotoViewerImageControl.Width = _photoViewerBaseWidth * _photoViewerZoom;
        PhotoViewerImageControl.Height = _photoViewerBaseHeight * _photoViewerZoom;
        var viewport = GetPhotoViewerViewportSize();
        PhotoViewerImageHost.Width = viewport.Width;
        PhotoViewerImageHost.Height = viewport.Height;
        _photoViewerImageOrigin = ClampPhotoViewerImageOrigin(_photoViewerImageOrigin);
        Canvas.SetLeft(PhotoViewerImageControl, _photoViewerImageOrigin.X);
        Canvas.SetTop(PhotoViewerImageControl, _photoViewerImageOrigin.Y);
    }

    private void SetPhotoViewerImageOrigin(Point origin)
    {
        _photoViewerImageOrigin = ClampPhotoViewerImageOrigin(origin);
        Canvas.SetLeft(PhotoViewerImageControl, _photoViewerImageOrigin.X);
        Canvas.SetTop(PhotoViewerImageControl, _photoViewerImageOrigin.Y);
    }

    private Point ClampPhotoViewerImageOrigin(Point origin)
    {
        var viewport = GetPhotoViewerViewportSize();
        var imageWidth = Math.Max(1, _photoViewerBaseWidth * _photoViewerZoom);
        var imageHeight = Math.Max(1, _photoViewerBaseHeight * _photoViewerZoom);
        if (UsesMobileGestures)
            return ClampMobileImageOrigin(origin, viewport, imageWidth, imageHeight,
                DataContext is MainViewModel { PhotoViewerRotationDegrees: 90 or 270 });
        return new Point(
            ClampPhotoViewerAxisOrigin(origin.X, viewport.Width, imageWidth),
            ClampPhotoViewerAxisOrigin(origin.Y, viewport.Height, imageHeight));
    }

    private static double ClampPhotoViewerAxisOrigin(double value, double viewportSize, double imageSize)
    {
        if (imageSize <= viewportSize)
        {
            return (viewportSize - imageSize) / 2;
        }

        return Math.Clamp(value, viewportSize - imageSize, 0);
    }

    private Size GetPhotoViewerViewportSize()
    {
        return new Size(
            Math.Max(1, PhotoViewerScrollViewer.Bounds.Width > 1 ? PhotoViewerScrollViewer.Bounds.Width : Bounds.Width),
            Math.Max(1, PhotoViewerScrollViewer.Bounds.Height > 1 ? PhotoViewerScrollViewer.Bounds.Height : Bounds.Height));
    }

    private double GetMaximumPhotoViewerZoom()
    {
        return DataContext is MainViewModel { PhotoViewerImage: { } image }
            ? GetMaximumPhotoViewerZoom(image)
            : MinimumPhotoViewerZoom;
    }

    private void SetPhotoViewerSliderValue(double value)
    {
        _isUpdatingPhotoViewerZoomSlider = true;
        PhotoViewerZoomSlider.Value = GetPhotoViewerZoomSliderPosition(value);
        UpdatePhotoViewerZoomSliderThumb(value);
        _isUpdatingPhotoViewerZoomSlider = false;
    }

    private void UpdatePhotoViewerZoomSliderThumb(double value)
    {
        const double trackInset = 14;
        const double thumbHeight = 24;
        var availableHeight = Math.Max(1, PhotoViewerZoomSliderThumb.Parent is Control parent && parent.Bounds.Height > 1
            ? parent.Bounds.Height
            : 180);
        var trackHeight = Math.Max(1, availableHeight - trackInset * 2);
        var normalized = GetPhotoViewerZoomSliderPosition(value);
        var thumbCenterY = trackInset + trackHeight * (1 - normalized);
        PhotoViewerZoomSliderThumb.RenderTransform = new TranslateTransform(0, thumbCenterY - thumbHeight / 2);
    }

    private double GetPhotoViewerZoomSliderPosition(double zoom)
    {
        var maximumZoom = Math.Max(MinimumPhotoViewerZoom + 0.001, GetMaximumPhotoViewerZoom());
        var clampedZoom = Math.Clamp(zoom, MinimumPhotoViewerZoom, maximumZoom);
        return Math.Log(clampedZoom / MinimumPhotoViewerZoom) /
            Math.Log(maximumZoom / MinimumPhotoViewerZoom);
    }

    private double GetPhotoViewerZoomFromSliderPosition(double position)
    {
        var maximumZoom = Math.Max(MinimumPhotoViewerZoom + 0.001, GetMaximumPhotoViewerZoom());
        var normalized = Math.Clamp(position, 0, 1);
        return MinimumPhotoViewerZoom * Math.Pow(maximumZoom / MinimumPhotoViewerZoom, normalized);
    }

    private void PhotoViewerZoomSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingPhotoViewerZoomSlider || PhotoViewerImageControl.Source is null)
        {
            return;
        }

        SetPhotoViewerZoom(GetPhotoViewerZoomFromSliderPosition(e.NewValue));
    }

    private void PhotoViewerScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ResetPhotoViewerZoom();
    }

    private double GetActivePointerDistance()
    {
        var points = _photoViewerPointers.Values.Take(2).ToArray();
        if (points.Length < 2)
        {
            return 0;
        }

        var x = points[0].X - points[1].X;
        var y = points[0].Y - points[1].Y;
        return Math.Sqrt(x * x + y * y);
    }

    private Point GetActivePointerCenter(Visual relativeTo)
    {
        if (_photoViewerPointers.Count < 2)
        {
            return new Point(PhotoViewerScrollViewer.Bounds.Width / 2, PhotoViewerScrollViewer.Bounds.Height / 2);
        }

        var viewportPoint = new Point(
            _photoViewerPointers.Values.Take(2).Average(point => point.X),
            _photoViewerPointers.Values.Take(2).Average(point => point.Y));
        var transform = PhotoViewerViewport.TransformToVisual(relativeTo);
        return transform?.Transform(viewportPoint) ?? viewportPoint;
    }

    private int GetPhotoViewerSwipeDirection(Vector delta)
    {
        if (_photoViewerZoom > MinimumPhotoViewerZoom || _photoViewerPinchActive)
        {
            return 0;
        }

        var horizontal = Math.Abs(delta.X);
        var vertical = Math.Abs(delta.Y);
        if (horizontal < 80 || horizontal < vertical * 1.8)
        {
            return 0;
        }

        return delta.X > 0 ? 1 : -1;
    }

    private void SetPhotoViewerZoom(double zoom, Point anchor)
    {
        var maximumZoom = GetMaximumPhotoViewerZoom();
        var oldWidth = Math.Max(1, PhotoViewerImageControl.Bounds.Width > 1 ? PhotoViewerImageControl.Bounds.Width : _photoViewerBaseWidth * _photoViewerZoom);
        var oldHeight = Math.Max(1, PhotoViewerImageControl.Bounds.Height > 1 ? PhotoViewerImageControl.Bounds.Height : _photoViewerBaseHeight * _photoViewerZoom);
        var relativeX = (anchor.X - _photoViewerImageOrigin.X) / oldWidth;
        var relativeY = (anchor.Y - _photoViewerImageOrigin.Y) / oldHeight;

        _photoViewerZoom = Math.Clamp(zoom, MinimumPhotoViewerZoom, maximumZoom);
        SetPhotoViewerSliderValue(_photoViewerZoom);

        var newWidth = Math.Max(1, _photoViewerBaseWidth * _photoViewerZoom);
        var newHeight = Math.Max(1, _photoViewerBaseHeight * _photoViewerZoom);
        _photoViewerImageOrigin = ClampPhotoViewerImageOrigin(new Point(
            anchor.X - relativeX * newWidth,
            anchor.Y - relativeY * newHeight));
        ApplyPhotoViewerZoom();
    }

    private double GetMaximumPhotoViewerZoom(Avalonia.Media.Imaging.Bitmap image)
    {
        var configuredZoomPower = DataContext is MainViewModel viewModel
            ? Math.Clamp(viewModel.ZoomPower, 2, 10000)
            : LocalUserSettings.DefaultZoomPower;
        var imageLimit = Math.Max(1, Math.Max(image.PixelSize.Width, image.PixelSize.Height));
        return Math.Max(MinimumPhotoViewerZoom, Math.Min(configuredZoomPower, imageLimit));
    }
}
