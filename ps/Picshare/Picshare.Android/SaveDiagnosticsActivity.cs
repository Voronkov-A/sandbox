using System;
using System.IO;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using Picshare.Services;

namespace Picshare.Android;

[Activity(Label = "Save to device", Exported = false, Theme = "@android:style/Theme.Translucent.NoTitleBar")]
public sealed class SaveDiagnosticsActivity : Activity
{
    private const int SaveRequest = 2417;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (savedInstanceState is not null) return;
        try
        {
            using var create = new Intent(Intent.ActionCreateDocument);
            create.AddCategory(Intent.CategoryOpenable);
            create.SetType("application/zip");
            create.PutExtra(Intent.ExtraTitle, Intent?.GetStringExtra(Intent.ExtraTitle) ?? "picshare-logs.zip");
            StartActivityForResult(create, SaveRequest);
        }
        catch (Exception ex)
        {
            DiagnosticLogging.Current.Write("Error", "Diagnostics", "Could not open Android save picker", ex);
            Toast.MakeText(this, "Could not open the save picker.", ToastLength.Long)?.Show();
            Finish();
        }
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != SaveRequest) return;
        try
        {
            if (resultCode != Result.Ok || data?.Data is not { } destination) return;
            var sourceUri = Intent?.Data ?? throw new IOException("The diagnostic attachment is missing.");
            await using var source = ContentResolver!.OpenInputStream(sourceUri) ?? throw new IOException("Could not open the diagnostic attachment.");
            await using var output = ContentResolver.OpenOutputStream(destination, "wt") ?? throw new IOException("Could not open the selected destination.");
            await source.CopyToAsync(output);
            await output.FlushAsync();
            Toast.MakeText(this, "Diagnostic logs saved.", ToastLength.Long)?.Show();
        }
        catch (Exception ex)
        {
            DiagnosticLogging.Current.Write("Error", "Diagnostics", "Saving diagnostics on Android failed", ex);
            Toast.MakeText(this, "Could not save diagnostic logs.", ToastLength.Long)?.Show();
        }
        finally { Finish(); }
    }
}
