namespace Picshare.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Picshare.ViewModels;

public partial class ImageListFooterView : UserControl
{
    private const double FooterActionButtonMaxWidth = 92;
    private const double FooterActionButtonCompactThreshold = 70;
    private const double FooterActionButtonSpacing = 8;

    public ImageListFooterView()
    {
        InitializeComponent();
    }

    private void FooterActionButton_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Button button)
        {
            button.Classes.Set("compact-footer-action", e.NewSize.Width < FooterActionButtonCompactThreshold);
        }
    }

    private void FooterActionPanelHost_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateFooterActionButtonWidths();
    }

    private void UpdateFooterActionButtonWidths()
    {
        var visibleButtons = FooterActionPanel.Children
            .OfType<Button>()
            .Where(button => button.IsVisible && double.IsFinite(button.MaxWidth))
            .ToList();
        if (visibleButtons.Count == 0)
        {
            return;
        }

        var fixedWidth = FooterActionPanel.Children
            .OfType<Button>()
            .Where(button => button.IsVisible && !double.IsFinite(button.MaxWidth) && double.IsFinite(button.Width))
            .Sum(button => button.Width);
        var visibleButtonCount = FooterActionPanel.Children.OfType<Button>().Count(button => button.IsVisible);
        var availableWidth = Math.Max(0, FooterActionPanelHost.Bounds.Width - fixedWidth);
        var availableButtonWidth = (availableWidth - (visibleButtonCount - 1) * FooterActionButtonSpacing) / visibleButtons.Count;
        var buttonWidth = Math.Max(0, Math.Min(FooterActionButtonMaxWidth, availableButtonWidth));
        foreach (var button in visibleButtons)
        {
            button.Width = buttonWidth;
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
            if (viewModel.IsMobilePlatform)
            {
                await MobileDownloadHelper.SaveAsync(this, viewModel, viewModel.SelectedPhotosArchiveFileName,
                    zip: true, viewModel.SaveMobileSelectedPhotosAsync);
                return;
            }
            if (asArchive)
            {
                var file = await StoragePickerHelper.OpenSaveFilePickerAsync(
                    topLevel,
                    "Save selected photos archive",
                    viewModel.PictureDefaultDownloadDirectoryPath,
                    viewModel.SelectedPhotosArchiveFileName,
                    StoragePickerHelper.ZipFileTypeChoices);

                if (file?.TryGetLocalPath() is { } archivePath)
                {
                    await viewModel.DownloadSelectedPhotosAsync(archivePath, asArchive: true);
                }
            }
            else
            {
                var folders = await StoragePickerHelper.OpenSingleFolderPickerAsync(
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
}
