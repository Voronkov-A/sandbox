using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CoreGraphics;
using Foundation;
using Picshare.Services;
using UIKit;

namespace Picshare.iOS;

internal sealed class IosDiagnosticShareHost : IDiagnosticShareHost
{
    public string ExportDirectory => Path.Combine(Path.GetTempPath(), "diagnostic-exports");

    public async Task ShareAsync(string archivePath)
    {
        var window = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
            .Where(scene => scene.ActivationState == UISceneActivationState.ForegroundActive)
            .SelectMany(scene => scene.Windows).FirstOrDefault(candidate => candidate.IsKeyWindow);
        // Avalonia hosts can also use the application delegate's window without a scene manifest.
        var presenter = (window ?? UIApplication.SharedApplication.Delegate?.Window)?.RootViewController
            ?? throw new InvalidOperationException("No active window is available for sharing.");
        while (presenter.PresentedViewController is { } presented) presenter = presented;
        using var url = NSUrl.FromFilename(archivePath);
        using var sheet = new UIActivityViewController(new NSObject[] { url }, null);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sheet.CompletionWithItemsHandler = (_, _, _, error) =>
        {
            if (error is null) completion.TrySetResult();
            else completion.TrySetException(new NSErrorException(error));
        };
        // iPad requires an anchored popover; on iPhone UIKit presents the standard modal sheet.
        if (sheet.PopoverPresentationController is { } popover && presenter.View is { } view)
        {
            popover.SourceView = view;
            popover.SourceRect = new CGRect(view.Bounds.GetMidX(), view.Bounds.GetMidY(), 1, 1);
            popover.PermittedArrowDirections = 0;
        }
        await presenter.PresentViewControllerAsync(sheet, true);
        await completion.Task;
    }
}
