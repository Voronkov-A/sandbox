using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Picshare.ViewModels;

namespace Picshare.Views;

public partial class MainView : UserControl
{
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
        });
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
}
