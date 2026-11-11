using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
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

    private void RecordAlbumPhotoScroll(ScrollViewer viewer, Vector delta)
    {
        if (delta.Length <= 0 || DataContext is not MainViewModel model ||
            model.IsPhotoViewerVisible || model.IsSettingsPanelVisible ||
            !IsControlEffectivelyVisible(viewer) ||
            viewer.GetVisualAncestors().OfType<ListBox>().FirstOrDefault() != GetActiveAlbumPhotoListBox()) return;

        if (_albumScrollingSource != viewer) ResetAlbumPhotoScrollLoading();
        _albumScrollingSource = viewer;
        _albumScrollLoading.RecordScroll(delta.Length, _albumScrollClock.Elapsed);
        ApplyAlbumPhotoScrollLoading();
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
        var paused = !model.PreloadImagesWhileScrolling && _albumScrollLoading.IsLoadingDeferred;
        // Refresh the current viewport before waking image workers after a deferred pass.
        if (!paused && _albumScrollLoadingWasPaused && TopLevel.GetTopLevel(this) is not null)
            UpdateVisibleAlbumPhotoPriorities();
        model.SetPhotoListLoadingPaused(paused);
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
