namespace Picshare.Views;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Picshare.ViewModels;

public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        InitializeComponent();
    }

    private async void CopyAlbumLink_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RecentAlbumViewModel album } &&
            TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(album.Link);
            e.Handled = true;
        }
    }

    private void SidebarScrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CloseSidebarCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void RecentAlbum_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Control { DataContext: RecentAlbumViewModel recentAlbum } &&
            viewModel.OpenRecentAlbumCommand.CanExecute(recentAlbum))
        {
            viewModel.CloseSidebarCommand.Execute(null);
            viewModel.OpenRecentAlbumCommand.Execute(recentAlbum);
            e.Handled = true;
        }
    }
}
