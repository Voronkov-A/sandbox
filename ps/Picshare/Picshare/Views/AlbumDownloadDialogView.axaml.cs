namespace Picshare.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Picshare.ViewModels;

public partial class AlbumDownloadDialogView : UserControl, ILazyActivated
{
    private const double DropdownMaxWidth = 182;
    private const double ActionButtonPreferredWidth = 128;
    private const double ControlMinWidth = 5;

    public AlbumDownloadDialogView()
    {
        InitializeComponent();
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            AlbumDownloadButton.Command = null;
            AlbumDownloadButton.Click += DownloadMobileAlbum_Click;
        }
    }

    public void FocusOverlay()
    {
        if (DataContext is MainViewModel { IsAlbumDownloadDialogVisible: true })
        {
            AlbumDownloadDialogOverlay.Focus();
        }
    }

    private async void DownloadMobileAlbum_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (!viewModel.AlbumDownloadCategories.Any(category => category.SelectedMode.Id != "exclude" && category.PhotoCount > 0))
        {
            viewModel.Status = "Select at least one category with photos.";
            return;
        }
        await MobileDownloadHelper.SaveAsync(this, viewModel, viewModel.AlbumArchiveFileName,
            zip: true, viewModel.SaveMobileAlbumAsync);
    }

    public void OnLazyActivated()
    {
        _ = Dispatcher.UIThread.InvokeAsync(FocusOverlay, DispatcherPriority.Render);
    }

    private void AlbumDownloadDialogOverlay_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel { IsAlbumDownloadDialogVisible: true } viewModel)
        {
            return;
        }

        if (e.Key is Key.Escape or Key.BrowserBack or Key.Back)
        {
            viewModel.CancelAlbumDownloadDialogCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void AlbumDownloadDialogOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel { IsAlbumDownloadDialogVisible: true } viewModel)
        {
            viewModel.CancelAlbumDownloadDialogCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void AlbumDownloadDialog_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
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
            var startPath = StoragePickerHelper.GetPickerStartPath(category.DestinationDirectoryPath);
            var file = await StoragePickerHelper.OpenSaveFilePickerAsync(
                topLevel,
                $"Save {category.CategoryName} archive",
                startPath,
                viewModel.GetAlbumCategoryArchiveFileName(category),
                StoragePickerHelper.ZipFileTypeChoices);

            if (file?.TryGetLocalPath() is { } destinationPath)
            {
                category.DestinationDirectoryPath = destinationPath;
            }
        }
        else
        {
            var folders = await StoragePickerHelper.OpenSingleFolderPickerAsync(
                topLevel,
                $"Choose destination for {category.CategoryName}",
                category.DestinationDirectoryPath);

            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } destinationPath)
            {
                category.DestinationDirectoryPath = destinationPath;
            }
        }
    }

    private void AlbumDownloadRowGrid_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Grid { ColumnDefinitions.Count: >= 3 } grid)
        {
            UpdateAlbumDownloadRowWidths(grid);
        }
    }

    private void AlbumDownloadActionHost_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Control host)
        {
            return;
        }

        var width = Math.Max(
            ControlMinWidth,
            Math.Min(ActionButtonPreferredWidth, (host.Bounds.Width - 8) / 2));
        AlbumDownloadButton.Width = width;
        CancelAlbumDownloadButton.Width = width;
    }

    private static void UpdateAlbumDownloadRowWidths(Grid grid)
    {
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            grid.ColumnSpacing = 0;
            grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(0);
            grid.ColumnDefinitions[2].Width = new GridLength(0);
            return;
        }
        const double squareWidth = 42;
        const double spacing = 8;
        var controlWidth = Math.Max(
            ControlMinWidth * 3,
            grid.Bounds.Width - spacing * 2);

        double dropdownWidth;
        double textBoxWidth;
        double selectWidth;
        if (controlWidth >= DropdownMaxWidth * 2 + squareWidth)
        {
            dropdownWidth = DropdownMaxWidth;
            selectWidth = squareWidth;
            textBoxWidth = controlWidth - dropdownWidth - selectWidth;
        }
        else if (controlWidth >= squareWidth * 3)
        {
            selectWidth = squareWidth;
            dropdownWidth = (controlWidth - selectWidth) / 2;
            textBoxWidth = dropdownWidth;
        }
        else
        {
            dropdownWidth = textBoxWidth = selectWidth = Math.Max(
                ControlMinWidth,
                controlWidth / 3);
        }

        grid.ColumnDefinitions[0].Width = new GridLength(dropdownWidth);
        grid.ColumnDefinitions[1].Width = new GridLength(textBoxWidth);
        grid.ColumnDefinitions[2].Width = new GridLength(selectWidth);
    }
}
