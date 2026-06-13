using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Picshare.ViewModels;

namespace Picshare.Views;

public partial class MainView : UserControl
{
    private const double MinimumPhotoViewerZoom = 1;
    private const double MaximumPhotoViewerZoom = 8;

    private readonly Dictionary<object, Point> _photoViewerPointers = new();
    private double _photoViewerBaseWidth;
    private double _photoViewerBaseHeight;
    private double _photoViewerZoom = MinimumPhotoViewerZoom;
    private double _pinchStartDistance;
    private double _pinchStartZoom = MinimumPhotoViewerZoom;
    private bool _photoViewerPointerPressed;
    private bool _photoViewerPointerMoved;
    private bool _photoViewerPinchActive;
    private Point _photoViewerPointerStart;

    public MainView()
    {
        InitializeComponent();
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
            "Choose uncategorized default download directory",
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
            "Choose ok default download directory",
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
            var folders = await OpenSingleFolderPickerAsync(
                topLevel,
                "Choose download folder",
                viewModel.PictureDefaultDownloadDirectoryPath);

            if (folders.Count > 0)
            {
                await viewModel.DownloadCurrentPhotoAsync(folders[0].TryGetLocalPath() ?? "");
            }
        }
    }

    private async void ChooseAlbumDownloadCategoryDestination_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AlbumDownloadCategoryViewModel category } ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await OpenSingleFolderPickerAsync(
            topLevel,
            $"Choose destination for {category.CategoryName}",
            category.DestinationDirectoryPath);

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } destinationPath)
        {
            category.DestinationDirectoryPath = destinationPath;
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

        var selectedItems = ImportCandidatesList.SelectedItems;
        if (selectedItems is null)
        {
            return;
        }

        var selectedPhotos = selectedItems.OfType<AlbumPhotoSourceViewModel>().ToList();
        viewModel.AddImportCandidates(selectedPhotos);
    }

    private void RemoveSelectedAlbumPhotos_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var selectedItems = AlbumPhotosList.SelectedItems;
        if (selectedItems is null)
        {
            return;
        }

        var selectedPhotos = selectedItems.OfType<AlbumPhotoSourceViewModel>().ToList();
        viewModel.RemoveAlbumPhotos(selectedPhotos);
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
            sender is Control { DataContext: AlbumPhotoViewModel photo })
        {
            await viewModel.StartPhotoViewportLoadAsync(photo);
        }
    }

    private void AlbumPhoto_Unloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: AlbumPhotoViewModel photo })
        {
            viewModel.StopPhotoViewportLoad(photo);
        }
    }

    private async void AlbumPhoto_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: AlbumPhotoViewModel photo })
        {
            await viewModel.OpenPhotoViewerAsync(photo);
            await Dispatcher.UIThread.InvokeAsync(ResetPhotoViewerZoom, DispatcherPriority.Render);
        }
    }

    private void AlbumReviewTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is TabControl { SelectedItem: TabItem { Tag: "Flow" } })
        {
            viewModel.SetFlowTabActive(true);
            return;
        }

        if (DataContext is MainViewModel inactiveViewModel)
        {
            inactiveViewModel.SetFlowTabActive(false);
        }
    }

    private void PhotoViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (PhotoViewerImageControl.Source is null)
        {
            return;
        }

        var factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        SetPhotoViewerZoom(_photoViewerZoom * factor);
        e.Handled = true;
    }

    private void PhotoViewer_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _photoViewerPointers[e.Pointer] = e.GetPosition(PhotoViewerViewport);
        _photoViewerPointerPressed = _photoViewerPointers.Count == 1;
        _photoViewerPointerStart = e.GetPosition(PhotoViewerViewport);
        _photoViewerPointerMoved = false;

        if (_photoViewerPointers.Count == 2)
        {
            _pinchStartDistance = GetActivePointerDistance();
            _pinchStartZoom = _photoViewerZoom;
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
            SetPhotoViewerZoom(_pinchStartZoom * GetActivePointerDistance() / _pinchStartDistance);
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

        if (_photoViewerPointers.Count == 2)
        {
            _pinchStartDistance = GetActivePointerDistance();
            _pinchStartZoom = _photoViewerZoom;
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

    private void ResetPhotoViewerZoom()
    {
        _photoViewerPointers.Clear();
        _photoViewerZoom = MinimumPhotoViewerZoom;

        if (DataContext is not MainViewModel { PhotoViewerImage: { } image })
        {
            return;
        }

        var viewportWidth = Math.Max(1, PhotoViewerScrollViewer.Bounds.Width > 1 ? PhotoViewerScrollViewer.Bounds.Width : Bounds.Width);
        var viewportHeight = Math.Max(1, PhotoViewerScrollViewer.Bounds.Height > 1 ? PhotoViewerScrollViewer.Bounds.Height : Bounds.Height);
        var pixelWidth = Math.Max(1, image.PixelSize.Width);
        var pixelHeight = Math.Max(1, image.PixelSize.Height);
        var fitScale = Math.Min(viewportWidth / pixelWidth, viewportHeight / pixelHeight);

        _photoViewerBaseWidth = pixelWidth * fitScale;
        _photoViewerBaseHeight = pixelHeight * fitScale;
        ApplyPhotoViewerZoom();
    }

    private void SetPhotoViewerZoom(double zoom)
    {
        _photoViewerZoom = Math.Clamp(zoom, MinimumPhotoViewerZoom, MaximumPhotoViewerZoom);
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
