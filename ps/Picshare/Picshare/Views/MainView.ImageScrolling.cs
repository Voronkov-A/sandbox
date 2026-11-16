using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Picshare.Services;
using Picshare.ViewModels;

namespace Picshare.Views;

public partial class MainView
{
    private readonly AlbumScrollLoadingPolicy _albumScrollLoading = new();
    private readonly Stopwatch _albumScrollClock = Stopwatch.StartNew();
    private DispatcherTimer? _albumScrollIdleTimer;
    private ScrollViewer? _albumScrollingSource;
    private bool _albumScrollLoadingWasPaused;
    private bool _albumRowsAreLightweight;

    private void AlbumScrollInput_Wheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y != 0) ObserveAlbumScrollInput(e);
    }

    private void AlbumScrollInput_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch ||
            (e.Pointer.Type == PointerType.Pen && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed))
            ObserveAlbumScrollInput(e);
    }

    private void ObserveAlbumScrollInput(PointerEventArgs e)
    {
        if (DataContext is not MainViewModel model || model.IsPhotoViewerVisible || model.IsSettingsPanelVisible ||
            e.Source is not Control source || GetActiveAlbumPhotoListBox() is not { } list) return;
        var viewer = source.GetVisualAncestors().OfType<ScrollViewer>()
            .FirstOrDefault(candidate => candidate.GetVisualAncestors().OfType<ListBox>().FirstOrDefault() == list);
        if (viewer is null) return;
        SetAlbumScrollingSource(viewer);
        _albumScrollLoading.RecordInput(_albumScrollClock.Elapsed);
        ApplyAlbumPhotoScrollLoading();
        RestartAlbumScrollIdleTimer();
    }

    private void RecordAlbumPhotoScroll(ScrollViewer viewer, Vector delta)
    {
        if (delta.Length <= 0 || DataContext is not MainViewModel model ||
            model.IsPhotoViewerVisible || model.IsSettingsPanelVisible ||
            !IsControlEffectivelyVisible(viewer) ||
            viewer.GetVisualAncestors().OfType<ListBox>().FirstOrDefault() != GetActiveAlbumPhotoListBox()) return;
        SetAlbumScrollingSource(viewer);
        _albumScrollLoading.RecordScroll(delta.Length, _albumScrollClock.Elapsed);
        ApplyAlbumPhotoScrollLoading();
        RestartAlbumScrollIdleTimer();
    }

    private void SetAlbumScrollingSource(ScrollViewer viewer)
    {
        if (_albumScrollingSource is not null && _albumScrollingSource != viewer) ResetAlbumPhotoScrollLoading();
        _albumScrollingSource = viewer;
    }

    private void RestartAlbumScrollIdleTimer()
    {
        if (_albumScrollIdleTimer is null)
        {
            _albumScrollIdleTimer = new DispatcherTimer { Interval = AlbumScrollLoadingPolicy.IdleDelay };
            _albumScrollIdleTimer.Tick += (_, _) =>
            {
                _albumScrollLoading.ResumeIfIdle(_albumScrollClock.Elapsed);
                if (_albumScrollLoading.HasActivity) return;
                _albumScrollIdleTimer.Stop();
                _albumScrollingSource = null;
                ApplyAlbumPhotoScrollLoading();
            };
        }
        _albumScrollIdleTimer.Stop();
        _albumScrollIdleTimer.Start();
    }

    private void ApplyAlbumPhotoScrollLoading()
    {
        if (DataContext is not MainViewModel model) return;
        var lightweight = _albumScrollLoading.IsLoadingDeferred;
        var paused = !model.PreloadImagesWhileScrolling && lightweight;
        // Pause bitmap assignments before removing the old full cards and their leases.
        model.SetPhotoListLoadingPaused(paused);
        if (_albumRowsAreLightweight != lightweight)
        {
            _albumRowsAreLightweight = lightweight;
            foreach (var list in _albumPhotoLists.OfType<AlbumPhotoListBox>())
                list.SetLightweightRendering(lightweight && IsControlEffectivelyVisible(list));
            if (lightweight)
            {
                model.ClearPhotoViewportLoads();
                StickyAlbumPhotoGroupHeader.IsVisible = false;
            }
            else ReportAlbumScrollRendering();
            QueueVisibleAlbumPhotoPriorityUpdate();
        }
        if (!paused && _albumScrollLoadingWasPaused && TopLevel.GetTopLevel(this) is not null)
            UpdateVisibleAlbumPhotoPriorities();
        _albumScrollLoadingWasPaused = paused;
    }

    private void ResetAlbumPhotoScrollLoading()
    {
        _albumScrollIdleTimer?.Stop();
        _albumScrollLoading.Reset();
        _albumScrollingSource = null;
        ApplyAlbumPhotoScrollLoading();
    }
}
