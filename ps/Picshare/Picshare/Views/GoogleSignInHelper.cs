namespace Picshare.Views;

using Avalonia.Controls;
using Picshare.ViewModels;

internal static class GoogleSignInHelper
{
    public static async Task SignInAsync(Control owner, bool loadDriveFoldersAfterSignIn)
    {
        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel is null || owner.DataContext is not MainViewModel viewModel)
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
