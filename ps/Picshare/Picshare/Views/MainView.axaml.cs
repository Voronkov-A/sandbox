using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Picshare.Services;
using Picshare.ViewModels;

namespace Picshare.Views;

public partial class MainView : UserControl
{
    private const double AlbumReviewHeaderScrollStep = 180;
    private const double AlbumReviewHeaderDragThreshold = 6;
    private const double AlbumReviewSwipeThreshold = 72;
    private const double AlbumReviewSwipeDominance = 1.35;
    private const double ChromeSurfaceHeight = 56;

    private object? _albumReviewSwipePointer;
    private Point _albumReviewSwipeStart;
    private object? _albumReviewHeaderDragPointer;
    private Point _albumReviewHeaderDragStart;
    private Vector _albumReviewHeaderDragStartOffset;
    private bool _albumReviewHeaderDragMoved;
    private INotifyPropertyChanged? _viewModelPropertyChanged;
    private readonly Dictionary<ScrollViewer, double> _albumPhotoScrollOffsets = new();
    private readonly HashSet<ListBox> _albumPhotoLists = new();
    private readonly HashSet<Control> _albumPhotoCards = new();
    private readonly HashSet<ScrollViewer> _albumPhotoScrollViewers = new();
    private readonly Dictionary<ListBox, List<ScrollViewer>> _albumPhotoScrollViewersByList = new();
    private double _mainHeaderHiddenHeight;
    private double _albumReviewTabHeaderHiddenHeight;
    private double _imageListFooterHiddenHeight;
    private bool _isVisibleAlbumPhotoPriorityUpdateQueued;
    private DateTime _lastBlankPhotoDiagnosticsUtc = DateTime.MinValue;
    private DateTime _lastSlowViewportDiagnosticsUtc = DateTime.MinValue;

    public MainView()
    {
        InitializeComponent();
        AddHandler(
            InputElement.KeyDownEvent,
            SettingsBack_KeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        SettingsViewHost.ControlCreated += SettingsViewHost_ControlCreated;
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
        _albumPhotoCards.Clear();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ClearPhotoViewportLoads();
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ActiveReviewTabId)
            or nameof(MainViewModel.UncategorizedTabHeader)
            or nameof(MainViewModel.NiceTabHeader)
            or nameof(MainViewModel.OkTabHeader)
            or nameof(MainViewModel.TrashTabHeader)
            or nameof(MainViewModel.UnresolvedDuplicatesTabHeader)
            or nameof(MainViewModel.HasUnresolvedDuplicatePhotos)
            or nameof(MainViewModel.IsFlowReviewTabVisible))
        {
            _ = Dispatcher.UIThread.InvokeAsync(UpdateAlbumReviewTabScrollButtonVisibility, DispatcherPriority.Render);
            QueueVisibleAlbumPhotoPriorityUpdate();
        }
        else if (e.PropertyName is nameof(MainViewModel.FixedHeader)
            or nameof(MainViewModel.FixedTabs)
            or nameof(MainViewModel.FixedActionPanel))
        {
            ApplyFixedSurfaceSettings();
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

    private void SettingsBack_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel { IsSettingsViewVisible: true } viewModel)
        {
            return;
        }

        if (e.Key is Key.BrowserBack)
        {
            viewModel.CloseSettingsCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SettingsViewHost_ControlCreated(object? sender, Control control)
    {
        if (control is SettingsView settingsView)
        {
            settingsView.ChromeScrollChanged += SettingsView_ChromeScrollChanged;
        }
    }

    private void SettingsView_ChromeScrollChanged(object? sender, SettingsChromeScrollEventArgs e)
    {
        if (DataContext is not MainViewModel { IsSettingsViewVisible: true } viewModel)
        {
            return;
        }

        var mainHeaderHeight = GetChromeSurfaceHeight(MainHeader, MainHeaderRow());
        if (!viewModel.FixedHeader)
        {
            _mainHeaderHiddenHeight = Math.Clamp(_mainHeaderHiddenHeight + e.Delta, 0, mainHeaderHeight);
        }

        if (e.CurrentOffset <= 0.5 && e.Delta < 0)
        {
            _mainHeaderHiddenHeight = 0;
        }
        else if (e.CurrentOffset >= e.MaximumOffset - 0.5 && e.Delta > 0)
        {
            _mainHeaderHiddenHeight = viewModel.FixedHeader ? 0 : mainHeaderHeight;
        }

        ApplyChromeSurface(MainHeader, MainHeaderRow(), viewModel.FixedHeader ? 0 : _mainHeaderHiddenHeight, topSurface: true);
    }

    private async void AlbumPhoto_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: AlbumPhotoViewModel photo } control)
        {
            _albumPhotoCards.Add(control);
            if (IsInSelectedAlbumPhotoList(control))
            {
                await viewModel.StartPhotoViewportLoadAsync(photo);
            }

            QueueVisibleAlbumPhotoPriorityUpdate();
        }
    }

    private void AlbumPhoto_Unloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control) return;
        _albumPhotoCards.Remove(control);
        if (DataContext is MainViewModel viewModel &&
            control.DataContext is AlbumPhotoViewModel photo)
        {
            viewModel.StopPhotoViewportLoad(photo);
        }
        QueueVisibleAlbumPhotoPriorityUpdate();
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
        QueueVisibleAlbumPhotoPriorityUpdate();
    }

    private void AlbumPhotoScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            UpdateFixedSurfaceTransforms(scrollViewer);
        }

        QueueVisibleAlbumPhotoPriorityUpdate();
    }

    private void AlbumPhotoScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
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

            var started = Stopwatch.GetTimestamp();
            UpdateVisibleAlbumPhotoPriorities();
            UpdateStickyAlbumPhotoGroupHeader();
            var elapsed = Stopwatch.GetElapsedTime(started);
            var now = DateTime.UtcNow;
            if (elapsed.TotalMilliseconds >= 32 && now - _lastSlowViewportDiagnosticsUtc >= TimeSpan.FromSeconds(5))
            {
                _lastSlowViewportDiagnosticsUtc = now;
                DiagnosticLogging.Current.Write("Debug", "Performance",
                    $"Slow album viewport update: {elapsed.TotalMilliseconds:F1}ms; realized cards={_albumPhotoCards.Count}; lists={_albumPhotoLists.Count}");
            }
        }, DispatcherPriority.Background);
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
        var footerHeight = GetChromeSurfaceHeight(ImageListFooterHost, ImageListFooterRow());

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
        ApplyChromeSurface(ImageListFooterHost, ImageListFooterRow(), viewModel.FixedActionPanel ? 0 : _imageListFooterHiddenHeight, topSurface: false);
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

        // Only row/header containers are relevant; scanning their descendants repeats the
        // same row once for every border, button, image and text in every realized card.
        var header = listBox.GetRealizedContainers()
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
        return _albumPhotoLists.FirstOrDefault(IsControlEffectivelyVisible);
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

        var albumPhotoControls = new List<AlbumPhotoControlSnapshot>(_albumPhotoCards.Count);
        var activeList = GetActiveAlbumPhotoListBox();
        foreach (var control in _albumPhotoCards)
        {
            if (!control.IsVisible || control.DataContext is not AlbumPhotoViewModel photo)
                continue;
            // Walk ancestors and calculate the viewport rectangle once per card, not four times.
            ScrollViewer? scrollViewer = null;
            ListBox? listBox = null;
            foreach (var ancestor in control.GetVisualAncestors())
            {
                if (ancestor is ScrollViewer viewer) scrollViewer ??= viewer;
                if (ancestor is ListBox list) { listBox = list; break; }
            }
            if (listBox != activeList || scrollViewer is null)
                continue;
            var transform = control.TransformToVisual(scrollViewer);
            if (transform is null) continue;
            var bounds = new Rect(control.Bounds.Size).TransformToAABB(transform.Value);
            var viewport = new Rect(scrollViewer.Bounds.Size);
            albumPhotoControls.Add(new AlbumPhotoControlSnapshot(control, photo, bounds, bounds,
                bounds.Intersects(viewport), viewport.Contains(bounds)));
        }

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
            await OpenAlbumPhotoInViewerAsync(viewModel, photo);
            e.Handled = true;
        }
    }

    private async void AlbumPhotoCard_Tapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: AlbumPhotoViewModel photo } &&
            e.Source is Control source &&
            !IsFromAlbumPhotoSelectionButton(source))
        {
            await OpenAlbumPhotoInViewerAsync(viewModel, photo);
            e.Handled = true;
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

    private async Task OpenAlbumPhotoInViewerAsync(MainViewModel viewModel, AlbumPhotoViewModel photo)
    {
        await viewModel.OpenPhotoViewerAsync(photo);
    }

    private static bool IsFromAlbumPhotoSelectionButton(Control source)
    {
        return source.Name == "AlbumPhotoSelectionButton" ||
            source.GetVisualAncestors()
                .OfType<Control>()
                .Any(control => control.Name == "AlbumPhotoSelectionButton");
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

}
