using System;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using Picshare.Services;

namespace Picshare.Android;

internal sealed class AndroidDiagnosticShareHost(MainActivity activity) : IDiagnosticShareHost
{
    public string ExportDirectory => Path.Combine(activity.CacheDir!.AbsolutePath, "diagnostic-exports");

    public Task ShareAsync(string archivePath)
    {
        using var file = new Java.IO.File(archivePath);
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity, activity.PackageName + ".diagnostics", file)
            ?? throw new InvalidOperationException("Could not prepare the log attachment.");
        using var send = new Intent(Intent.ActionSend);
        send.SetType("application/zip");
        send.PutExtra(Intent.ExtraStream, uri);
        send.ClipData = ClipData.NewRawUri("Picshare diagnostic logs", uri);
        send.AddFlags(ActivityFlags.GrantReadUriPermission);
        using var chooser = Intent.CreateChooser(send, "Send diagnostic logs")
            ?? throw new InvalidOperationException("Could not open the share sheet.");

        // Android does not guarantee a file-saving target is installed. Supply our own
        // chooser entry that delegates saving to the system Storage Access Framework.
        using var save = new Intent(activity, typeof(SaveDiagnosticsActivity));
        save.SetDataAndType(uri, "application/zip");
        save.PutExtra(Intent.ExtraTitle, Path.GetFileName(archivePath));
        save.AddFlags(ActivityFlags.GrantReadUriPermission);
        chooser.PutExtra(Intent.ExtraInitialIntents, new global::Android.OS.IParcelable[] { save });
        activity.StartActivity(chooser);
        return Task.CompletedTask;
    }
}
