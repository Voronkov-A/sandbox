using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Picshare.ViewModels;

public partial class MainViewModel
{
    public IReadOnlyList<PhotoViewerActionOption> SwipeActionOptions => PhotoViewerActionOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSwipeLeftAction), nameof(SwipeLeftCommand), nameof(CanSwipeLeft))]
    private string _swipeLeftAction = "next";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSwipeRightAction), nameof(SwipeRightCommand), nameof(CanSwipeRight))]
    private string _swipeRightAction = "previous";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSwipeTopAction), nameof(SwipeTopCommand), nameof(CanSwipeTop))]
    private string _swipeTopAction = "nice";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSwipeBottomAction), nameof(SwipeBottomCommand), nameof(CanSwipeBottom))]
    private string _swipeBottomAction = "okay";

    public PhotoViewerActionOption SelectedSwipeLeftAction
    {
        get => PhotoViewerActionOption.Find(SwipeLeftAction, "next");
        set { if (value is not null) SwipeLeftAction = value.Id; }
    }
    public PhotoViewerActionOption SelectedSwipeRightAction
    {
        get => PhotoViewerActionOption.Find(SwipeRightAction, "previous");
        set { if (value is not null) SwipeRightAction = value.Id; }
    }
    public PhotoViewerActionOption SelectedSwipeTopAction
    {
        get => PhotoViewerActionOption.Find(SwipeTopAction, "nice");
        set { if (value is not null) SwipeTopAction = value.Id; }
    }
    public PhotoViewerActionOption SelectedSwipeBottomAction
    {
        get => PhotoViewerActionOption.Find(SwipeBottomAction, "okay");
        set { if (value is not null) SwipeBottomAction = value.Id; }
    }

    public ICommand SwipeLeftCommand => GetSwipeCommand(SelectedSwipeLeftAction.Id);
    public ICommand SwipeRightCommand => GetSwipeCommand(SelectedSwipeRightAction.Id);
    public ICommand SwipeTopCommand => GetSwipeCommand(SelectedSwipeTopAction.Id);
    public ICommand SwipeBottomCommand => GetSwipeCommand(SelectedSwipeBottomAction.Id);
    public bool CanSwipeLeft => CanExecuteSwipe(SelectedSwipeLeftAction.Id);
    public bool CanSwipeRight => CanExecuteSwipe(SelectedSwipeRightAction.Id);
    public bool CanSwipeTop => CanExecuteSwipe(SelectedSwipeTopAction.Id);
    public bool CanSwipeBottom => CanExecuteSwipe(SelectedSwipeBottomAction.Id);

    private ICommand GetSwipeCommand(string action) => action switch
    {
        "previous" => ShowPreviousPhotoInCategoryCommand,
        "next" => ShowNextPhotoInCategoryCommand,
        "nice" => MarkCurrentPhotoNiceCommand,
        "okay" => MarkCurrentPhotoOkCommand,
        "rotate-left" => RotateCurrentPhotoLeftCommand,
        "rotate-right" => RotateCurrentPhotoRightCommand,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private bool CanExecuteSwipe(string action) => action switch
    {
        "previous" or "next" => CanNavigatePhotoViewerCategory,
        "nice" => CanMarkCurrentPhotoNice,
        "okay" => CanMarkCurrentPhotoOk,
        "rotate-left" or "rotate-right" => CanDownloadCurrentPhoto,
        _ => false
    };

    private void NotifySwipeAvailabilityChanged(string? property)
    {
        if (property is not (nameof(CanNavigatePhotoViewerCategory) or nameof(CanMarkCurrentPhotoNice)
            or nameof(CanMarkCurrentPhotoOk) or nameof(CanDownloadCurrentPhoto))) return;
        OnPropertyChanged(nameof(CanSwipeLeft));
        OnPropertyChanged(nameof(CanSwipeRight));
        OnPropertyChanged(nameof(CanSwipeTop));
        OnPropertyChanged(nameof(CanSwipeBottom));
    }

    partial void OnSwipeLeftActionChanged(string value) => PersistLocalUserSettingsIfReady();
    partial void OnSwipeRightActionChanged(string value) => PersistLocalUserSettingsIfReady();
    partial void OnSwipeTopActionChanged(string value) => PersistLocalUserSettingsIfReady();
    partial void OnSwipeBottomActionChanged(string value) => PersistLocalUserSettingsIfReady();
}
