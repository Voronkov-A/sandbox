namespace Picshare.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;

public partial class AlbumModalDialogsView : UserControl
{
    public AlbumModalDialogsView()
    {
        InitializeComponent();
    }

    private async void OpenAlbumSignInGoogle_Click(object? sender, RoutedEventArgs e)
    {
        await GoogleSignInHelper.SignInAsync(this, loadDriveFoldersAfterSignIn: false);
    }
}
