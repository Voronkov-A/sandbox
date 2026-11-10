namespace Picshare.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Picshare.ViewModels;

public partial class CreateAlbumView : UserControl
{
    public CreateAlbumView()
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

    private void AddSelectedImportCandidates_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            var selectedPhotos = viewModel.ImportCandidates.Where(photo => photo.IsSelected).ToList();
            viewModel.AddImportCandidates(selectedPhotos);
        }
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
        await GoogleSignInHelper.SignInAsync(this, loadDriveFoldersAfterSignIn: true);
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

    private static async Task OpenManualPhotoPickerAsync(TopLevel topLevel, MainViewModel viewModel)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose photos",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif", "*.bmp"],
                    MimeTypes = ["image/jpeg", "image/png", "image/webp", "image/gif", "image/bmp"],
                    AppleUniformTypeIdentifiers = ["public.image"]
                }
            ]
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
}
