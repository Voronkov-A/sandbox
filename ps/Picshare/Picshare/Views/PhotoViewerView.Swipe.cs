using Avalonia;
using Avalonia.Controls;

namespace Picshare.Views;

public partial class PhotoViewerView
{
    private bool _photoViewerSwipeActive;
    private Point _photoViewerSwipeStart;

    private void BeginPhotoViewerSwipe(Point start)
    {
        CancelPhotoViewerSwipe();
        _photoViewerSwipeActive = _photoViewerZoom <= MinimumPhotoViewerZoom && PhotoViewerImageControl.Source is not null;
        _photoViewerSwipeStart = start;
        // Center the control exactly at the contact, including near viewport edges.
        Canvas.SetLeft(PhotoViewerSwipeWheel, start.X - PhotoViewerSwipeWheel.Width / 2);
        Canvas.SetTop(PhotoViewerSwipeWheel, start.Y - PhotoViewerSwipeWheel.Height / 2);
    }

    private void UpdatePhotoViewerSwipe(Vector delta)
    {
        if (!_photoViewerSwipeActive) return;
        if (_photoViewerZoom > MinimumPhotoViewerZoom || _photoViewerPinchActive || _mobilePinching)
        {
            CancelPhotoViewerSwipe();
            return;
        }
        PhotoViewerSwipeWheel.SelectedDirection = PhotoViewerSwipeControl.GetDirection(delta);
        PhotoViewerSwipeWheel.IsVisible = PhotoViewerSwipeWheel.SelectedDirection != PhotoViewerSwipeDirection.None;
    }

    private void CompletePhotoViewerSwipe()
    {
        var canExecute = _photoViewerSwipeActive && _photoViewerZoom <= MinimumPhotoViewerZoom &&
            !_photoViewerPinchActive && !_mobilePinching;
        _photoViewerSwipeActive = false;
        PhotoViewerSwipeWheel.IsVisible = false;
        if (canExecute) PhotoViewerSwipeWheel.TryExecuteSelectedAction();
        else PhotoViewerSwipeWheel.SelectedDirection = PhotoViewerSwipeDirection.None;
    }

    private void CancelPhotoViewerSwipe()
    {
        _photoViewerSwipeActive = false;
        PhotoViewerSwipeWheel.IsVisible = false;
        PhotoViewerSwipeWheel.SelectedDirection = PhotoViewerSwipeDirection.None;
    }
}
