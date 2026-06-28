using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Picshare.ViewModels;

namespace Picshare.Views;

public partial class MainView : UserControl
{
    private const double MinimumPhotoViewerZoom = 1;
    private const double MaximumPhotoViewerZoom = 8;
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
    private bool _photoViewerPointerPressed;
    private bool _photoViewerPointerMoved;
    private bool _photoViewerPinchActive;
    private Point _photoViewerPointerStart;
    private INotifyPropertyChanged? _viewModelPropertyChanged;
    private readonly HashSet<ScrollViewer> _albumPhotoScrollViewers = new();

    public MainView()
    {
        InitializeComponent();
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

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.PhotoViewerImage) or nameof(MainViewModel.PhotoViewerRotationDegrees))
        {
            _ = Dispatcher.UIThread.InvokeAsync(ResetPhotoViewerZoom, DispatcherPriority.Render);
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
            sender is Control { DataContext: AlbumPhotoViewModel photo })
        {
            UpdateVisibleAlbumPhotoPriorities();
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

    private void AlbumPhotoList_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var scrollViewer in listBox.GetVisualDescendants().OfType<ScrollViewer>())
            {
                if (_albumPhotoScrollViewers.Add(scrollViewer))
                {
                    scrollViewer.ScrollChanged += AlbumPhotoScrollViewer_ScrollChanged;
                }
            }

            UpdateVisibleAlbumPhotoPriorities();
        }, DispatcherPriority.Loaded);
    }

    private void AlbumPhotoScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateVisibleAlbumPhotoPriorities();
    }

    private void MainTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl)
        {
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(UpdateVisibleAlbumPhotoPriorities, DispatcherPriority.Render);
    }

    private void AlbumPhotoList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: not null } listBox)
        {
            listBox.SelectedItem = null;
        }
    }

    private void UpdateVisibleAlbumPhotoPriorities()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var visiblePhotos = this.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => string.Equals(control.Name, "AlbumPhotoCard", StringComparison.Ordinal) &&
                control.IsVisible &&
                control.DataContext is AlbumPhotoViewModel &&
                IsControlInViewport(control))
            .Select(control => new
            {
                Control = control,
                Photo = (AlbumPhotoViewModel)control.DataContext!
            })
            .Select(item => new
            {
                item.Photo,
                Bounds = TransformBounds(item.Control, this),
                IsFullyInViewport = IsControlFullyInViewport(item.Control)
            })
            .OrderByDescending(item => item.IsFullyInViewport)
            .ThenBy(item => item.Bounds.Y)
            .ThenBy(item => item.Bounds.X)
            .Select(item => item.Photo)
            .ToList();

        viewModel.PrioritizePhotoViewportLoads(visiblePhotos);
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
        return bounds.Right >= 0 &&
            bounds.Bottom >= 0 &&
            bounds.Left <= scrollViewer.Bounds.Width &&
            bounds.Top <= scrollViewer.Bounds.Height;
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
            viewModel.OpenRecentAlbumCommand.Execute(recentAlbum);
            e.Handled = true;
        }
    }

    private async void RecentPhoto_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: RecentPhotoViewModel recentPhoto })
        {
            await viewModel.OpenRecentPhotoAsync(recentPhoto);
            await Dispatcher.UIThread.InvokeAsync(ResetPhotoViewerZoom, DispatcherPriority.Render);
            e.Handled = true;
        }
    }

    private void AlbumReviewTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is TabControl { SelectedItem: TabItem selectedTab })
        {
            viewModel.SetActiveReviewTab(selectedTab.Tag?.ToString() ?? "");
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
        var isQuarterTurn = DataContext is MainViewModel viewModel &&
            viewModel.PhotoViewerRotationDegrees is 90 or 270;
        var effectiveWidth = isQuarterTurn ? pixelHeight : pixelWidth;
        var effectiveHeight = isQuarterTurn ? pixelWidth : pixelHeight;
        var fitScale = Math.Min(viewportWidth / effectiveWidth, viewportHeight / effectiveHeight);

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
