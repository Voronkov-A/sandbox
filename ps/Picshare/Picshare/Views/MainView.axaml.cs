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

public partial class MainView : UserControl
{
    private const double MinimumPhotoViewerZoom = 1;
    private const double AlbumReviewHeaderScrollStep = 180;
    private const double AlbumReviewHeaderDragThreshold = 6;
    private const double AlbumReviewSwipeThreshold = 72;
    private const double AlbumReviewSwipeDominance = 1.35;
    private const double ChromeSurfaceHeight = 56;
    private const double FooterActionButtonMaxWidth = 92;
    private const double FooterActionButtonMinWidth = 5;
    private const double FooterActionButtonCompactThreshold = 70;
    private const double FooterActionButtonSpacing = 8;
    private const double PhotoViewerFooterHorizontalPadding = 16;
    private const double OverlayCornerRadius = 6;
    private const double PhotoViewerZoomSliderDefaultHeight = 180;
    private const double PhotoViewerOverlayVerticalGap = 8;
    private const double PhotoViewerDuplicateStripScrollStep = 180;
    private static readonly IReadOnlyList<FilePickerFileType> ZipFileTypeChoices =
    [
        new("Zip archive")
        {
            Patterns = ["*.zip"],
            MimeTypes = ["application/zip"]
        }
    ];

    private readonly Dictionary<object, Point> _photoViewerPointers = new();
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
    private object? _albumReviewSwipePointer;
    private Point _albumReviewSwipeStart;
    private object? _albumReviewHeaderDragPointer;
    private Point _albumReviewHeaderDragStart;
    private Vector _albumReviewHeaderDragStartOffset;
    private bool _albumReviewHeaderDragMoved;
    private object? _photoViewerDuplicateStripDragPointer;
    private Point _photoViewerDuplicateStripDragStart;
    private Vector _photoViewerDuplicateStripDragStartOffset;
    private bool _photoViewerDuplicateStripDragMoved;
    private INotifyPropertyChanged? _viewModelPropertyChanged;
    private readonly Dictionary<ScrollViewer, double> _albumPhotoScrollOffsets = new();
    private readonly HashSet<ListBox> _albumPhotoLists = new();
    private readonly HashSet<ScrollViewer> _albumPhotoScrollViewers = new();
    private readonly Dictionary<ListBox, List<ScrollViewer>> _albumPhotoScrollViewersByList = new();
    private double _mainHeaderHiddenHeight;
    private double _albumReviewTabHeaderHiddenHeight;
    private double _imageListFooterHiddenHeight;
    private bool _isVisibleAlbumPhotoPriorityUpdateQueued;
    private DateTime _lastBlankPhotoDiagnosticsUtc = DateTime.MinValue;

    public MainView()
    {
        InitializeComponent();
        AlbumReviewContentHost.AddHandler(
            InputElement.PointerPressedEvent,
            AlbumReviewContent_PointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AlbumReviewContentHost.AddHandler(
            InputElement.PointerMovedEvent,
            AlbumReviewContent_PointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AlbumReviewContentHost.AddHandler(
            InputElement.PointerReleasedEvent,
            AlbumReviewContent_PointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AlbumReviewContentHost.AddHandler(
            InputElement.PointerCaptureLostEvent,
            AlbumReviewContent_PointerCaptureLost,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AlbumReviewTabHeaderScrollViewer.AddHandler(
            InputElement.PointerPressedEvent,
            AlbumReviewTabHeader_PointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AlbumReviewTabHeaderScrollViewer.AddHandler(
            InputElement.PointerMovedEvent,
            AlbumReviewTabHeader_PointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AlbumReviewTabHeaderScrollViewer.AddHandler(
            InputElement.PointerReleasedEvent,
            AlbumReviewTabHeader_PointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AlbumReviewTabHeaderScrollViewer.AddHandler(
            InputElement.PointerCaptureLostEvent,
            AlbumReviewTabHeader_PointerCaptureLost,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
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
        PhotoViewerScrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            PhotoViewer_PointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _ = Dispatcher.UIThread.InvokeAsync(UpdateAlbumReviewTabScrollButtonVisibility, DispatcherPriority.Loaded);
        _ = Dispatcher.UIThread.InvokeAsync(UpdatePhotoViewerDuplicateStripScrollButtonVisibility, DispatcherPriority.Loaded);
        _ = Dispatcher.UIThread.InvokeAsync(UpdateFooterActionButtonWidths, DispatcherPriority.Loaded);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModelPropertyChanged is not null)
        {
            _viewModelPropertyChanged.PropertyChanged -= ViewModel_PropertyChanged;
            if (_viewModelPropertyChanged is MainViewModel oldViewModel)
            {
                oldViewModel.ClearPhotoViewportLoads();
            }
        }

        _viewModelPropertyChanged = DataContext as INotifyPropertyChanged;
        if (_viewModelPropertyChanged is not null)
        {
            _viewModelPropertyChanged.PropertyChanged += ViewModel_PropertyChanged;
        }

        QueueVisibleAlbumPhotoPriorityUpdate();

        base.OnDataContextChanged(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModelPropertyChanged is not null)
        {
            _viewModelPropertyChanged.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModelPropertyChanged = null;
        }

        foreach (var scrollViewer in _albumPhotoScrollViewers.ToList())
        {
            DetachAlbumPhotoScrollViewer(scrollViewer);
        }

        foreach (var listBox in _albumPhotoLists.ToList())
        {
            DetachAlbumPhotoList(listBox);
        }

        _albumPhotoScrollViewersByList.Clear();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ClearPhotoViewportLoads();
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.PhotoViewerImage) or nameof(MainViewModel.PhotoViewerRotationDegrees))
        {
            _ = Dispatcher.UIThread.InvokeAsync(ResetPhotoViewerZoom, DispatcherPriority.Render);
        }
        else if (e.PropertyName is nameof(MainViewModel.IsPhotoViewerVisible))
        {
            _ = Dispatcher.UIThread.InvokeAsync(FocusPhotoViewerOverlay, DispatcherPriority.Render);
        }
        else if (e.PropertyName is nameof(MainViewModel.ZoomPower)
            or nameof(MainViewModel.PhotoViewerAspectRatioMode))
        {
            _ = Dispatcher.UIThread.InvokeAsync(ResetPhotoViewerZoom, DispatcherPriority.Render);
        }
        else if (e.PropertyName is nameof(MainViewModel.ActiveReviewTabId)
            or nameof(MainViewModel.UncategorizedTabHeader)
            or nameof(MainViewModel.NiceTabHeader)
            or nameof(MainViewModel.OkTabHeader)
            or nameof(MainViewModel.TrashTabHeader)
            or nameof(MainViewModel.UnresolvedDuplicatesTabHeader)
            or nameof(MainViewModel.HasUnresolvedDuplicatePhotos)
            or nameof(MainViewModel.IsFlowReviewTabVisible))
        {
            _ = Dispatcher.UIThread.InvokeAsync(UpdateAlbumReviewTabScrollButtonVisibility, DispatcherPriority.Render);
            _ = Dispatcher.UIThread.InvokeAsync(UpdateFooterActionButtonWidths, DispatcherPriority.Render);
            _ = Dispatcher.UIThread.InvokeAsync(UpdateStickyAlbumPhotoGroupHeader, DispatcherPriority.Render);
        }
        else if (e.PropertyName is nameof(MainViewModel.FixedHeader)
            or nameof(MainViewModel.FixedTabs)
            or nameof(MainViewModel.FixedActionPanel))
        {
            ApplyFixedSurfaceSettings();
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

    private async void ManualPhotoImport_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            viewModel.CloseFolderDateImport();
            await OpenManualPhotoPickerAsync(topLevel, viewModel);
        }
    }

    private async void FolderDatePhotoImport_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            await OpenFolderDatePickerAsync(topLevel, viewModel);
        }
    }

    private async void ChooseFolderDateImportFolders_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            await OpenFolderDatePickerAsync(topLevel, viewModel);
        }
    }

    private async void ChooseLocalAlbumDestination_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose local album destination",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                viewModel.SetLocalAlbumDestination(folders[0]);
            }
        }
    }

    private void AddGoogleContactSuggestion_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: GoogleContactSuggestionViewModel suggestion })
        {
            viewModel.AddGoogleAlbumShareSuggestion(suggestion);
        }
    }

    private void RemoveSharedGoogleAccount_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: SharedGoogleAccountViewModel account })
        {
            viewModel.RemoveSharedGoogleAccount(account);
        }
    }

    private async void ChoosePictureDefaultDownloadDirectory_Click(object? sender, RoutedEventArgs e)
    {
        await ChooseSettingsDownloadDirectoryAsync(
            "Choose picture default download directory",
            viewModel => viewModel.PictureDefaultDownloadDirectoryPath,
            (viewModel, folder) => viewModel.SetPictureDefaultDownloadDirectory(folder));
    }

    private async void ChooseUncategorizedDefaultDownloadDirectory_Click(object? sender, RoutedEventArgs e)
    {
        await ChooseSettingsDownloadDirectoryAsync(
            "Choose Todo default download directory",
            viewModel => viewModel.UncategorizedDefaultDownloadDirectoryPath,
            (viewModel, folder) => viewModel.SetUncategorizedDefaultDownloadDirectory(folder));
    }

    private async void ChooseNiceDefaultDownloadDirectory_Click(object? sender, RoutedEventArgs e)
    {
        await ChooseSettingsDownloadDirectoryAsync(
            "Choose nice default download directory",
            viewModel => viewModel.NiceDefaultDownloadDirectoryPath,
            (viewModel, folder) => viewModel.SetNiceDefaultDownloadDirectory(folder));
    }

    private async void ChooseOkDefaultDownloadDirectory_Click(object? sender, RoutedEventArgs e)
    {
        await ChooseSettingsDownloadDirectoryAsync(
            "Choose okay default download directory",
            viewModel => viewModel.OkDefaultDownloadDirectoryPath,
            (viewModel, folder) => viewModel.SetOkDefaultDownloadDirectory(folder));
    }

    private async void ChooseTrashDefaultDownloadDirectory_Click(object? sender, RoutedEventArgs e)
    {
        await ChooseSettingsDownloadDirectoryAsync(
            "Choose trash default download directory",
            viewModel => viewModel.TrashDefaultDownloadDirectoryPath,
            (viewModel, folder) => viewModel.SetTrashDefaultDownloadDirectory(folder));
    }

    private async void DownloadCurrentPhoto_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            var file = await OpenSaveFilePickerAsync(
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

    private void PhotoViewerOverlay_KeyDown(object? sender, KeyEventArgs e)
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
    }

    private void FocusPhotoViewerOverlay()
    {
        if (DataContext is MainViewModel { IsPhotoViewerVisible: true })
        {
            PhotoViewerOverlay.Focus();
        }
    }

    private async void CopyAlbumLink_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RecentAlbumViewModel album } &&
            TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(album.Link);
            e.Handled = true;
        }
    }

    private void SidebarScrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CloseSidebarCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void AlbumReviewTabHeader_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string tabId })
        {
            SelectAlbumReviewTab(tabId);
            e.Handled = true;
        }
    }

    private void ScrollAlbumReviewTabsLeft_Click(object? sender, RoutedEventArgs e)
    {
        ScrollAlbumReviewTabHeader(-AlbumReviewHeaderScrollStep);
        e.Handled = true;
    }

    private void ScrollAlbumReviewTabsRight_Click(object? sender, RoutedEventArgs e)
    {
        ScrollAlbumReviewTabHeader(AlbumReviewHeaderScrollStep);
        e.Handled = true;
    }

    private void AlbumReviewTabHeaderScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateAlbumReviewTabScrollButtonVisibility();
    }

    private void AlbumReviewTabHeaderScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = Dispatcher.UIThread.InvokeAsync(UpdateAlbumReviewTabScrollButtonVisibility, DispatcherPriority.Render);
    }

    private void AlbumReviewTabHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_albumReviewHeaderDragPointer is not null)
        {
            return;
        }

        _albumReviewHeaderDragPointer = e.Pointer;
        _albumReviewHeaderDragStart = e.GetPosition(AlbumReviewTabHeaderScrollViewer);
        _albumReviewHeaderDragStartOffset = AlbumReviewTabHeaderScrollViewer.Offset;
        _albumReviewHeaderDragMoved = false;
    }

    private void AlbumReviewTabHeader_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(_albumReviewHeaderDragPointer, e.Pointer))
        {
            return;
        }

        var delta = e.GetPosition(AlbumReviewTabHeaderScrollViewer) - _albumReviewHeaderDragStart;
        if (!_albumReviewHeaderDragMoved && Math.Abs(delta.X) < AlbumReviewHeaderDragThreshold)
        {
            return;
        }

        _albumReviewHeaderDragMoved = true;
        SetAlbumReviewTabHeaderOffset(_albumReviewHeaderDragStartOffset.X - delta.X);
        e.Handled = true;
    }

    private void AlbumReviewTabHeader_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(_albumReviewHeaderDragPointer, e.Pointer))
        {
            return;
        }

        var handled = _albumReviewHeaderDragMoved;
        _albumReviewHeaderDragPointer = null;
        _albumReviewHeaderDragMoved = false;
        e.Handled = handled;
    }

    private void AlbumReviewTabHeader_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(_albumReviewHeaderDragPointer, e.Pointer))
        {
            _albumReviewHeaderDragPointer = null;
            _albumReviewHeaderDragMoved = false;
        }
    }

    private void ScrollAlbumReviewTabHeader(double delta)
    {
        SetAlbumReviewTabHeaderOffset(AlbumReviewTabHeaderScrollViewer.Offset.X + delta);
    }

    private void SetAlbumReviewTabHeaderOffset(double offset)
    {
        var maxOffset = Math.Max(0, AlbumReviewTabHeaderScrollViewer.Extent.Width - AlbumReviewTabHeaderScrollViewer.Viewport.Width);
        var nextOffset = Math.Clamp(offset, 0, maxOffset);
        AlbumReviewTabHeaderScrollViewer.Offset = new Vector(nextOffset, AlbumReviewTabHeaderScrollViewer.Offset.Y);
        UpdateAlbumReviewTabScrollButtonVisibility();
    }

    private void UpdateAlbumReviewTabScrollButtonVisibility()
    {
        const double tolerance = 0.5;
        var maxOffset = Math.Max(0, AlbumReviewTabHeaderScrollViewer.Extent.Width - AlbumReviewTabHeaderScrollViewer.Viewport.Width);
        if (maxOffset <= tolerance)
        {
            AlbumReviewTabsScrollLeftButton.IsVisible = false;
            AlbumReviewTabsScrollRightButton.IsVisible = false;
            return;
        }

        AlbumReviewTabsScrollLeftButton.IsVisible = AlbumReviewTabHeaderScrollViewer.Offset.X > tolerance;
        AlbumReviewTabsScrollRightButton.IsVisible = AlbumReviewTabHeaderScrollViewer.Offset.X < maxOffset - tolerance;
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
        if (!_photoViewerDuplicateStripDragMoved && Math.Abs(delta.X) < AlbumReviewHeaderDragThreshold)
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

    private void SelectAlbumReviewTab(string tabId)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.SetActiveReviewTab(tabId);
        QueueVisibleAlbumPhotoPriorityUpdate();
        _ = Dispatcher.UIThread.InvokeAsync(BringSelectedAlbumReviewTabIntoView, DispatcherPriority.Render);
    }

    private void BringSelectedAlbumReviewTabIntoView()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var selectedButton = AlbumReviewTabHeaderScrollViewer
            .GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), viewModel.ActiveReviewTabId, StringComparison.Ordinal));
        selectedButton?.BringIntoView();
        _ = Dispatcher.UIThread.InvokeAsync(UpdateAlbumReviewTabScrollButtonVisibility, DispatcherPriority.Render);
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
        UpdateFooterActionButtonWidths(FooterActionPanelHost, FooterActionPanel);
        UpdateFooterActionButtonWidths(PhotoViewerActionFooterHost, PhotoViewerActionFooter);
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

    private static void UpdateFooterActionButtonWidths(Control host, Panel panel)
    {
        var visibleButtons = panel.Children
            .OfType<Button>()
            .Where(button => button.IsVisible && double.IsFinite(button.MaxWidth))
            .ToList();
        if (visibleButtons.Count == 0)
        {
            return;
        }

        var fixedWidth = panel.Children
            .OfType<Button>()
            .Where(button => button.IsVisible && !double.IsFinite(button.MaxWidth) && double.IsFinite(button.Width))
            .Sum(button => button.Width);
        var visibleButtonCount = panel.Children.OfType<Button>().Count(button => button.IsVisible);
        var availableWidth = Math.Max(0, host.Bounds.Width - fixedWidth);
        var availableButtonWidth = (availableWidth - (visibleButtonCount - 1) * FooterActionButtonSpacing) / visibleButtons.Count;
        var buttonWidth = Math.Max(0, Math.Min(FooterActionButtonMaxWidth, availableButtonWidth));
        foreach (var button in visibleButtons)
        {
            button.Width = buttonWidth;
        }
    }

    private async void DownloadSelectedPhotos_Click(object? sender, RoutedEventArgs e)
    {
        await DownloadSelectedPhotosAsync(asArchive: false);
    }

    private async void DownloadSelectedPhotosArchive_Click(object? sender, RoutedEventArgs e)
    {
        await DownloadSelectedPhotosAsync(asArchive: true);
    }

    private async Task DownloadSelectedPhotosAsync(bool asArchive)
    {
        if (DataContext is MainViewModel viewModel && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            if (asArchive)
            {
                var file = await OpenSaveFilePickerAsync(
                    topLevel,
                    "Save selected photos archive",
                    viewModel.PictureDefaultDownloadDirectoryPath,
                    viewModel.SelectedPhotosArchiveFileName,
                    ZipFileTypeChoices);

                if (file?.TryGetLocalPath() is { } archivePath)
                {
                    await viewModel.DownloadSelectedPhotosAsync(archivePath, asArchive: true);
                }
            }
            else
            {
                var folders = await OpenSingleFolderPickerAsync(
                    topLevel,
                    "Choose download folder",
                    viewModel.PictureDefaultDownloadDirectoryPath);

                if (folders.Count > 0)
                {
                    await viewModel.DownloadSelectedPhotosAsync(folders[0].TryGetLocalPath() ?? "", asArchive: false);
                }
            }
        }
    }

    private async void ChooseAlbumDownloadCategoryDestination_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            sender is not Control { DataContext: AlbumDownloadCategoryViewModel category } ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        if (category.IsArchiveMode)
        {
            var startPath = GetPickerStartPath(category.DestinationDirectoryPath);
            var file = await OpenSaveFilePickerAsync(
                topLevel,
                $"Save {category.CategoryName} archive",
                startPath,
                viewModel.GetAlbumCategoryArchiveFileName(category),
                ZipFileTypeChoices);

            if (file?.TryGetLocalPath() is { } destinationPath)
            {
                category.DestinationDirectoryPath = destinationPath;
            }
        }
        else
        {
            var folders = await OpenSingleFolderPickerAsync(
                topLevel,
                $"Choose destination for {category.CategoryName}",
                category.DestinationDirectoryPath);

            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } destinationPath)
            {
                category.DestinationDirectoryPath = destinationPath;
            }
        }
    }

    private async Task ChooseSettingsDownloadDirectoryAsync(
        string title,
        Func<MainViewModel, string> getCurrentPath,
        Action<MainViewModel, IStorageFolder> setFolder)
    {
        if (DataContext is not MainViewModel viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await OpenSingleFolderPickerAsync(topLevel, title, getCurrentPath(viewModel));
        if (folders.Count > 0)
        {
            setFolder(viewModel, folders[0]);
        }
    }

    private static async Task<IReadOnlyList<IStorageFolder>> OpenSingleFolderPickerAsync(
        TopLevel topLevel,
        string title,
        string? suggestedStartPath)
    {
        IStorageFolder? suggestedStartLocation = null;
        if (!string.IsNullOrWhiteSpace(suggestedStartPath))
        {
            try
            {
                suggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedStartPath);
            }
            catch
            {
                suggestedStartLocation = null;
            }
        }

        return await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStartLocation
        });
    }

    private static async Task<IStorageFile?> OpenSaveFilePickerAsync(
        TopLevel topLevel,
        string title,
        string? suggestedStartPath,
        string suggestedFileName,
        IReadOnlyList<FilePickerFileType>? fileTypeChoices)
    {
        IStorageFolder? suggestedStartLocation = null;
        var startPath = GetPickerStartPath(suggestedStartPath);
        if (!string.IsNullOrWhiteSpace(startPath))
        {
            try
            {
                suggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(startPath);
            }
            catch
            {
                suggestedStartLocation = null;
            }
        }

        var extension = Path.GetExtension(suggestedFileName);
        return await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedStartLocation = suggestedStartLocation,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = string.IsNullOrWhiteSpace(extension) ? null : extension,
            FileTypeChoices = fileTypeChoices,
            ShowOverwritePrompt = true
        });
    }

    private static string GetPickerStartPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase) ||
            (!Directory.Exists(path) && !string.IsNullOrWhiteSpace(Path.GetFileName(path)))
                ? Path.GetDirectoryName(path) ?? ""
                : path;
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

    private static async Task OpenManualPhotoPickerAsync(TopLevel topLevel, MainViewModel viewModel)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose photos",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif", "*.bmp" },
                    MimeTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif", "image/bmp" },
                    AppleUniformTypeIdentifiers = new[] { "public.image" }
                }
            }
        });

        if (files.Count > 0)
        {
            viewModel.AddManualPhotoFiles(files);
        }
    }

    private static async Task OpenFolderDatePickerAsync(TopLevel topLevel, MainViewModel viewModel)
    {
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose photo folders",
            AllowMultiple = true
        });

        if (folders.Count > 0)
        {
            viewModel.SetFolderDateImportFolders(folders);
        }
    }

    private void AddSelectedImportCandidates_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var selectedPhotos = viewModel.ImportCandidates.Where(photo => photo.IsSelected).ToList();
        viewModel.AddImportCandidates(selectedPhotos);
    }

    private void RemoveSelectedAlbumPhotos_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var selectedPhotos = viewModel.AlbumPhotos.Where(photo => photo.IsSelected).ToList();
        viewModel.RemoveAlbumPhotos(selectedPhotos);
    }

    private void AlbumPhotoSourceSelection_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            sender is not Control { DataContext: AlbumPhotoSourceViewModel photo })
        {
            return;
        }

        if (viewModel.ImportCandidates.Contains(photo))
        {
            viewModel.ToggleImportCandidateSelection(photo);
            return;
        }

        viewModel.ToggleAlbumPhotoSourceSelection(photo);
    }

    private async void AlbumPhotoSource_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: AlbumPhotoSourceViewModel photo })
        {
            await photo.LoadThumbnailAsync();
        }
    }

    private async void SignInGoogle_Click(object? sender, RoutedEventArgs e)
    {
        await SignInGoogleAsync(loadDriveFoldersAfterSignIn: true);
    }

    private async void SettingsSignInGoogle_Click(object? sender, RoutedEventArgs e)
    {
        await SignInGoogleAsync(loadDriveFoldersAfterSignIn: false);
    }

    private async void OpenAlbumSignInGoogle_Click(object? sender, RoutedEventArgs e)
    {
        await SignInGoogleAsync(loadDriveFoldersAfterSignIn: false);
    }

    private async void DriveItem_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: DriveItemViewModel item })
        {
            await viewModel.OpenDriveItemAsync(item);
        }
    }

    private async void OpenGoogleSignIn_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (Uri.TryCreate(viewModel.GoogleSignInUrl, UriKind.Absolute, out var uri))
        {
            await topLevel.Launcher.LaunchUriAsync(uri);
        }
    }

    private async void AlbumPhoto_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: AlbumPhotoViewModel photo } control)
        {
            if (IsInSelectedAlbumPhotoList(control))
            {
                await viewModel.StartPhotoViewportLoadAsync(photo);
            }

            QueueVisibleAlbumPhotoPriorityUpdate();
        }
    }

    private void AlbumPhoto_Unloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: AlbumPhotoViewModel photo })
        {
            viewModel.StopPhotoViewportLoad(photo);
            QueueVisibleAlbumPhotoPriorityUpdate();
        }
    }

    private void AlbumPhotoList_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        AttachAlbumPhotoList(listBox);
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (listBox.GetVisualRoot() is null)
            {
                return;
            }

            if (!AttachAlbumPhotoScrollViewers(listBox))
            {
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (listBox.GetVisualRoot() is null)
                    {
                        return;
                    }

                    AttachAlbumPhotoScrollViewers(listBox);
                    QueueVisibleAlbumPhotoPriorityUpdate();
                }, DispatcherPriority.Render);
            }

            QueueVisibleAlbumPhotoPriorityUpdate();
        }, DispatcherPriority.Loaded);
    }

    private void AttachAlbumPhotoList(ListBox listBox)
    {
        if (!_albumPhotoLists.Add(listBox))
        {
            return;
        }

        listBox.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            AlbumPhotoList_ScrollChanged,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private bool AttachAlbumPhotoScrollViewers(ListBox listBox)
    {
        if (listBox.GetVisualRoot() is null)
        {
            return true;
        }

        if (_albumPhotoScrollViewersByList.Remove(listBox, out var previousScrollViewers))
        {
            foreach (var scrollViewer in previousScrollViewers)
            {
                DetachAlbumPhotoScrollViewer(scrollViewer);
            }
        }

        var listScrollViewers = listBox.GetVisualDescendants().OfType<ScrollViewer>().ToList();
        if (listScrollViewers.Count == 0)
        {
            return false;
        }

        _albumPhotoScrollViewersByList[listBox] = listScrollViewers;
        foreach (var scrollViewer in listScrollViewers)
        {
            if (_albumPhotoScrollViewers.Add(scrollViewer))
            {
                _albumPhotoScrollOffsets[scrollViewer] = scrollViewer.Offset.Y;
                scrollViewer.ScrollChanged += AlbumPhotoScrollViewer_ScrollChanged;
                scrollViewer.SizeChanged += AlbumPhotoScrollViewer_SizeChanged;
            }
        }

        return true;
    }

    private void AlbumPhotoList_Unloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        DetachAlbumPhotoList(listBox);
        if (!_albumPhotoScrollViewersByList.Remove(listBox, out var scrollViewers))
        {
            scrollViewers = listBox.GetVisualDescendants().OfType<ScrollViewer>().ToList();
        }

        foreach (var scrollViewer in scrollViewers)
        {
            DetachAlbumPhotoScrollViewer(scrollViewer);
        }

        QueueVisibleAlbumPhotoPriorityUpdate();
    }

    private void DetachAlbumPhotoList(ListBox listBox)
    {
        if (!_albumPhotoLists.Remove(listBox))
        {
            return;
        }

        listBox.RemoveHandler(ScrollViewer.ScrollChangedEvent, AlbumPhotoList_ScrollChanged);
    }

    private void DetachAlbumPhotoScrollViewer(ScrollViewer scrollViewer)
    {
        if (!_albumPhotoScrollViewers.Remove(scrollViewer))
        {
            return;
        }

        scrollViewer.ScrollChanged -= AlbumPhotoScrollViewer_ScrollChanged;
        scrollViewer.SizeChanged -= AlbumPhotoScrollViewer_SizeChanged;
        _albumPhotoScrollOffsets.Remove(scrollViewer);
    }

    private void AlbumPhotoList_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (_albumPhotoScrollViewers.Contains(scrollViewer))
        {
            return;
        }

        _albumPhotoScrollOffsets.TryAdd(scrollViewer, scrollViewer.Offset.Y - e.OffsetDelta.Y);
        UpdateFixedSurfaceTransforms(scrollViewer);
        UpdateStickyAlbumPhotoGroupHeader();
        QueueVisibleAlbumPhotoPriorityUpdate();
    }

    private void AlbumPhotoScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            UpdateFixedSurfaceTransforms(scrollViewer);
        }

        UpdateStickyAlbumPhotoGroupHeader();
        QueueVisibleAlbumPhotoPriorityUpdate();
    }

    private void AlbumPhotoScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateStickyAlbumPhotoGroupHeader();
        QueueVisibleAlbumPhotoPriorityUpdate();
    }

    private void AlbumReviewContentHost_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateAlbumPhotoCardSize(e.NewSize.Width);
        }

        QueueVisibleAlbumPhotoPriorityUpdate();
    }

    private void MainTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl)
        {
            return;
        }

        QueueVisibleAlbumPhotoPriorityUpdate();
    }

    private void QueueVisibleAlbumPhotoPriorityUpdate()
    {
        if (_isVisibleAlbumPhotoPriorityUpdateQueued)
        {
            return;
        }

        _isVisibleAlbumPhotoPriorityUpdateQueued = true;
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isVisibleAlbumPhotoPriorityUpdateQueued = false;
            if (TopLevel.GetTopLevel(this) is null)
            {
                return;
            }

            UpdateVisibleAlbumPhotoPriorities();
            UpdateStickyAlbumPhotoGroupHeader();
        }, DispatcherPriority.Render);
    }

    private void AlbumPhotoList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: not null } listBox)
        {
            listBox.SelectedItem = null;
        }
    }

    private void ApplyFixedSurfaceSettings()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.FixedHeader)
        {
            _mainHeaderHiddenHeight = 0;
        }

        if (viewModel.FixedTabs)
        {
            _albumReviewTabHeaderHiddenHeight = 0;
        }

        if (viewModel.FixedActionPanel)
        {
            _imageListFooterHiddenHeight = 0;
        }

        ApplyChromeState(viewModel);
    }

    private void UpdateFixedSurfaceTransforms(ScrollViewer scrollViewer)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var currentOffset = scrollViewer.Offset.Y;
        if (!_albumPhotoScrollOffsets.TryGetValue(scrollViewer, out var previousOffset))
        {
            previousOffset = currentOffset;
        }

        _albumPhotoScrollOffsets[scrollViewer] = currentOffset;
        var delta = currentOffset - previousOffset;
        if (Math.Abs(delta) < 0.5)
        {
            return;
        }

        var mainHeaderHeight = GetChromeSurfaceHeight(MainHeader, MainHeaderRow());
        var tabHeaderHeight = GetChromeSurfaceHeight(AlbumReviewTabHeader, AlbumReviewTabHeaderRow());
        var footerHeight = GetChromeSurfaceHeight(ImageListFooter, ImageListFooterRow());

        UpdateHeaderChromeState(delta, viewModel, mainHeaderHeight, tabHeaderHeight);

        if (!viewModel.FixedActionPanel)
        {
            _imageListFooterHiddenHeight = Math.Clamp(_imageListFooterHiddenHeight - delta, 0, footerHeight);
        }

        var maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        if (currentOffset <= 0.5 && delta < 0)
        {
            _mainHeaderHiddenHeight = viewModel.FixedHeader ? 0 : 0;
            _albumReviewTabHeaderHiddenHeight = viewModel.FixedTabs ? 0 : 0;
            _imageListFooterHiddenHeight = viewModel.FixedActionPanel ? 0 : footerHeight;
        }
        else if (currentOffset >= maxOffset - 0.5 && delta > 0)
        {
            _mainHeaderHiddenHeight = viewModel.FixedHeader ? 0 : mainHeaderHeight;
            _albumReviewTabHeaderHiddenHeight = viewModel.FixedTabs ? 0 : tabHeaderHeight;
            _imageListFooterHiddenHeight = 0;
        }

        ApplyChromeState(viewModel);
    }

    private RowDefinition MainHeaderRow() => RootLayout.RowDefinitions[0];

    private RowDefinition ImageListFooterRow() => RootLayout.RowDefinitions[2];

    private RowDefinition AlbumReviewTabHeaderRow() => OpenAlbumLayout.RowDefinitions[0];

    private void UpdateHeaderChromeState(
        double delta,
        MainViewModel viewModel,
        double mainHeaderHeight,
        double tabHeaderHeight)
    {
        if (delta > 0)
        {
            var remaining = delta;
            if (!viewModel.FixedHeader)
            {
                var nextMainHeaderHiddenHeight = Math.Clamp(_mainHeaderHiddenHeight + remaining, 0, mainHeaderHeight);
                remaining -= nextMainHeaderHiddenHeight - _mainHeaderHiddenHeight;
                _mainHeaderHiddenHeight = nextMainHeaderHiddenHeight;
            }

            if (!viewModel.FixedTabs)
            {
                _albumReviewTabHeaderHiddenHeight = Math.Clamp(
                    _albumReviewTabHeaderHiddenHeight + remaining,
                    0,
                    tabHeaderHeight);
            }
        }
        else
        {
            var remaining = -delta;
            if (!viewModel.FixedTabs)
            {
                var nextTabHeaderHiddenHeight = Math.Clamp(_albumReviewTabHeaderHiddenHeight - remaining, 0, tabHeaderHeight);
                remaining -= _albumReviewTabHeaderHiddenHeight - nextTabHeaderHiddenHeight;
                _albumReviewTabHeaderHiddenHeight = nextTabHeaderHiddenHeight;
            }

            if (!viewModel.FixedHeader)
            {
                _mainHeaderHiddenHeight = Math.Clamp(
                    _mainHeaderHiddenHeight - remaining,
                    0,
                    mainHeaderHeight);
            }
        }
    }

    private void ApplyChromeState(MainViewModel viewModel)
    {
        ApplyChromeSurface(MainHeader, MainHeaderRow(), viewModel.FixedHeader ? 0 : _mainHeaderHiddenHeight, topSurface: true);
        ApplyTabHeaderChromeSurface(viewModel);
        ApplyChromeSurface(ImageListFooter, ImageListFooterRow(), viewModel.FixedActionPanel ? 0 : _imageListFooterHiddenHeight, topSurface: false);
    }

    private void ApplyTabHeaderChromeSurface(MainViewModel viewModel)
    {
        var row = AlbumReviewTabHeaderRow();
        var tabHeaderHeight = GetChromeSurfaceHeight(AlbumReviewTabHeader, row);
        var tabHiddenHeight = viewModel.FixedTabs ? 0 : Math.Clamp(_albumReviewTabHeaderHiddenHeight, 0, tabHeaderHeight);
        SetSurfaceTranslateY(AlbumReviewTabHeader, -tabHiddenHeight);
        SetChromeRowHeight(row, tabHeaderHeight - tabHiddenHeight);
    }

    private static void ApplyChromeSurface(Control surface, RowDefinition row, double hiddenHeight, bool topSurface)
    {
        var surfaceHeight = GetChromeSurfaceHeight(surface, row);
        var clampedHiddenHeight = Math.Clamp(hiddenHeight, 0, surfaceHeight);
        SetSurfaceTranslateY(surface, topSurface ? -clampedHiddenHeight : clampedHiddenHeight);
        SetChromeRowHeight(row, surfaceHeight - clampedHiddenHeight);
    }

    private static double GetChromeSurfaceHeight(Control surface, RowDefinition row)
    {
        var rowHeight = row.Height.IsAbsolute ? row.Height.Value : 0;
        return Math.Max(ChromeSurfaceHeight, Math.Max(surface.Bounds.Height, rowHeight));
    }

    private static void SetChromeRowHeight(RowDefinition row, double height)
    {
        if (row.Height.IsAbsolute && Math.Abs(row.Height.Value - height) < 0.5)
        {
            return;
        }

        row.Height = new GridLength(height, GridUnitType.Pixel);
    }

    private static void SetSurfaceTranslateY(Control surface, double targetY)
    {
        if (surface.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        transform.Y = targetY;
    }

    private void UpdateStickyAlbumPhotoGroupHeader()
    {
        var listBox = GetActiveAlbumPhotoListBox();
        if (listBox is null)
        {
            StickyAlbumPhotoGroupHeader.IsVisible = false;
            return;
        }

        var header = listBox.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.DataContext is AlbumPhotoGroupHeaderViewModel or AlbumPhotoRowViewModel)
            .Select(control => new
            {
                Header = control.DataContext is AlbumPhotoGroupHeaderViewModel groupHeader
                    ? groupHeader.Header
                    : ((AlbumPhotoRowViewModel)control.DataContext!).GroupHeader,
                Bounds = TransformBounds(control, listBox)
            })
            .Where(item => item.Bounds.Bottom > 0 && item.Bounds.Top < listBox.Bounds.Height)
            .OrderBy(item => Math.Max(0, item.Bounds.Top))
            .FirstOrDefault()?.Header;

        StickyAlbumPhotoGroupHeaderText.Text = header ?? "";
        StickyAlbumPhotoGroupHeader.IsVisible = !string.IsNullOrWhiteSpace(header);
    }

    private ListBox? GetActiveAlbumPhotoListBox()
    {
        return this.GetVisualDescendants()
            .OfType<ListBox>()
            .FirstOrDefault(listBox => listBox.Classes.Contains("album-photo-list") && IsControlEffectivelyVisible(listBox));
    }

    private static bool IsControlEffectivelyVisible(Control control)
    {
        return control.IsVisible &&
            control.GetVisualAncestors()
                .OfType<Control>()
                .All(ancestor => ancestor.IsVisible);
    }

    private void UpdateVisibleAlbumPhotoPriorities()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var albumPhotoControls = this.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => string.Equals(control.Name, "AlbumPhotoCard", StringComparison.Ordinal) &&
                control.IsVisible &&
                IsInSelectedAlbumPhotoList(control) &&
                control.DataContext is AlbumPhotoViewModel)
            .Select(control => new AlbumPhotoControlSnapshot(
                control,
                (AlbumPhotoViewModel)control.DataContext!,
                TransformBounds(control, this),
                GetControlBoundsInNearestScrollViewer(control),
                IsControlInViewport(control),
                IsControlFullyInViewport(control)))
            .ToList();

        var visiblePhotos = albumPhotoControls
            .Where(item => item.IsInViewport)
            .Select(item => new
            {
                item.Photo,
                item.Bounds,
                item.IsFullyInViewport
            })
            .OrderByDescending(item => item.IsFullyInViewport)
            .ThenBy(item => item.Bounds.Y)
            .ThenBy(item => item.Bounds.X)
            .Select(item => item.Photo)
            .ToList();

        viewModel.PrioritizePhotoViewportLoads(visiblePhotos);
        LogBlankPhotoDiagnostics(viewModel, albumPhotoControls);
    }

    private static bool IsInSelectedAlbumPhotoList(Control control)
    {
        var listBox = control.GetVisualAncestors()
            .OfType<ListBox>()
            .FirstOrDefault(list => list.Classes.Contains("album-photo-list"));
        if (listBox is null)
        {
            return false;
        }

        var tabItem = listBox.GetVisualAncestors().OfType<TabItem>().FirstOrDefault();
        return tabItem?.IsSelected ?? listBox.IsVisible;
    }

    private void LogBlankPhotoDiagnostics(
        MainViewModel viewModel,
        IEnumerable<AlbumPhotoControlSnapshot> albumPhotoControls)
    {
        var blankControls = albumPhotoControls
            .Where(item => item.Photo.Image is null)
            .Take(8)
            .ToList();
        if (blankControls.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastBlankPhotoDiagnosticsUtc < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _lastBlankPhotoDiagnosticsUtc = now;
        Console.WriteLine($"PicshareImageLoader: visible blank card diagnostics count={blankControls.Count}");
        foreach (var item in blankControls)
        {
            Console.WriteLine(
                $"PicshareImageLoader: blank {item.Photo.FileName} ({item.Photo.PhotoId}) inViewport={item.IsInViewport} fully={item.IsFullyInViewport} bounds={FormatRect(item.Bounds)} scrollBounds={FormatRect(item.ViewportBounds)} {viewModel.DescribePhotoImageLoadState(item.Photo)}");
        }
    }

    private static bool IsControlInViewport(Control control)
    {
        var scrollViewer = control.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
        {
            return false;
        }

        var transform = control.TransformToVisual(scrollViewer);
        if (transform is null)
        {
            return false;
        }

        var bounds = TransformBounds(control, scrollViewer);
        return bounds.Right > 0 &&
            bounds.Bottom > 0 &&
            bounds.Left < scrollViewer.Bounds.Width &&
            bounds.Top < scrollViewer.Bounds.Height;
    }

    private static bool IsControlFullyInViewport(Control control)
    {
        var scrollViewer = control.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null || control.TransformToVisual(scrollViewer) is null)
        {
            return false;
        }

        var bounds = TransformBounds(control, scrollViewer);
        return bounds.Left >= 0 &&
            bounds.Top >= 0 &&
            bounds.Right <= scrollViewer.Bounds.Width &&
            bounds.Bottom <= scrollViewer.Bounds.Height;
    }

    private static Rect GetControlBoundsInNearestScrollViewer(Control control)
    {
        var scrollViewer = control.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        return scrollViewer is null ? default : TransformBounds(control, scrollViewer);
    }

    private static Rect TransformBounds(Control control, Visual target)
    {
        var transform = control.TransformToVisual(target);
        if (transform is null)
        {
            return default;
        }

        var matrix = transform.Value;
        var topLeft = matrix.Transform(new Point(0, 0));
        var topRight = matrix.Transform(new Point(control.Bounds.Width, 0));
        var bottomLeft = matrix.Transform(new Point(0, control.Bounds.Height));
        var bottomRight = matrix.Transform(new Point(control.Bounds.Width, control.Bounds.Height));
        var left = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
        var top = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
        var right = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
        var bottom = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static string FormatRect(Rect rect)
    {
        return $"{rect.X:0.##},{rect.Y:0.##},{rect.Width:0.##},{rect.Height:0.##}";
    }

    private sealed record AlbumPhotoControlSnapshot(
        Control Control,
        AlbumPhotoViewModel Photo,
        Rect Bounds,
        Rect ViewportBounds,
        bool IsInViewport,
        bool IsFullyInViewport);

    private async void AlbumPhoto_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: AlbumPhotoViewModel photo })
        {
            await viewModel.OpenPhotoViewerAsync(photo);
            await Dispatcher.UIThread.InvokeAsync(ResetPhotoViewerZoom, DispatcherPriority.Render);
        }
    }

    private void AlbumPhotoSelection_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: AlbumPhotoViewModel photo })
        {
            viewModel.TogglePhotoSelection(photo);
            e.Handled = true;
        }
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

    private void RecentAlbum_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: RecentAlbumViewModel recentAlbum } &&
            viewModel.OpenRecentAlbumCommand.CanExecute(recentAlbum))
        {
            viewModel.CloseSidebarCommand.Execute(null);
            viewModel.OpenRecentAlbumCommand.Execute(recentAlbum);
            e.Handled = true;
        }
    }

    private void AlbumReviewTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is TabControl { SelectedItem: TabItem selectedTab })
        {
            viewModel.SetActiveReviewTab(selectedTab.Tag?.ToString() ?? "");
            QueueVisibleAlbumPhotoPriorityUpdate();
        }
    }

    private void AlbumReviewContent_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_albumReviewSwipePointer is not null)
        {
            return;
        }

        _albumReviewSwipePointer = e.Pointer;
        _albumReviewSwipeStart = e.GetPosition(AlbumReviewContentHost);
    }

    private void AlbumReviewContent_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(_albumReviewSwipePointer, e.Pointer))
        {
            return;
        }

        var delta = e.GetPosition(AlbumReviewContentHost) - _albumReviewSwipeStart;
        if (Math.Abs(delta.X) >= AlbumReviewSwipeThreshold &&
            Math.Abs(delta.X) > Math.Abs(delta.Y) * AlbumReviewSwipeDominance)
        {
            e.Handled = true;
        }
    }

    private void AlbumReviewContent_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(_albumReviewSwipePointer, e.Pointer))
        {
            return;
        }

        var delta = e.GetPosition(AlbumReviewContentHost) - _albumReviewSwipeStart;
        _albumReviewSwipePointer = null;
        if (Math.Abs(delta.X) < AlbumReviewSwipeThreshold ||
            Math.Abs(delta.X) <= Math.Abs(delta.Y) * AlbumReviewSwipeDominance)
        {
            return;
        }

        SwitchAlbumReviewTab(delta.X < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void AlbumReviewContent_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(_albumReviewSwipePointer, e.Pointer))
        {
            _albumReviewSwipePointer = null;
        }
    }

    private void SwitchAlbumReviewTab(int direction)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var tabIds = GetVisibleAlbumReviewTabIds(viewModel);
        if (tabIds.Count == 0)
        {
            return;
        }

        var currentIndex = tabIds.IndexOf(viewModel.ActiveReviewTabId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = Math.Clamp(currentIndex + direction, 0, tabIds.Count - 1);
        if (nextIndex == currentIndex)
        {
            return;
        }

        SelectAlbumReviewTab(tabIds[nextIndex]);
    }

    private static List<string> GetVisibleAlbumReviewTabIds(MainViewModel viewModel)
    {
        var tabIds = new List<string>
        {
            "uncategorized",
            "nice",
            "ok",
            "trash"
        };

        if (viewModel.HasUnresolvedDuplicatePhotos)
        {
            tabIds.Add("unresolved-duplicates");
        }

        if (viewModel.IsAuthorFlowVisible)
        {
            tabIds.Add("flow");
        }

        return tabIds;
    }

    private void PhotoViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
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
        if (_photoViewerPointers.Remove(e.Pointer) && _photoViewerPointers.Count < 2)
        {
            _pinchStartDistance = 0;
            _photoViewerPinchActive = false;
        }
    }

    private void ResetPhotoViewerZoom()
    {
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

    private double GetMaximumPhotoViewerZoom(Avalonia.Media.Imaging.Bitmap image)
    {
        var configuredZoomPower = DataContext is MainViewModel viewModel
            ? Math.Clamp(viewModel.ZoomPower, 2, 10000)
            : LocalUserSettings.DefaultZoomPower;
        var imageLimit = Math.Max(1, Math.Max(image.PixelSize.Width, image.PixelSize.Height));
        return Math.Max(MinimumPhotoViewerZoom, Math.Min(configuredZoomPower, imageLimit));
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

    private async Task SignInGoogleAsync(bool loadDriveFoldersAfterSignIn)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        await viewModel.SignInGoogleAsync(async uri =>
        {
            if (!await topLevel.Launcher.LaunchUriAsync(uri))
            {
                throw new InvalidOperationException("Could not open the Google sign-in page in the browser.");
            }
        }, loadDriveFoldersAfterSignIn);
    }
}
