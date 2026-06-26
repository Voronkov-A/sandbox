using System;
using Android.App;
using Android.Content;
using Android.OS;
using Picshare.Services;

namespace Picshare.Android;

#pragma warning disable CA1416, CA1422
[Service(
    Name = "com.CompanyName.Picshare.PicshareForegroundService",
    Exported = false,
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
internal sealed class PicshareForegroundService : Service
{
    private const string ChannelId = "picshare.long-running";
    private const int NotificationId = 1001;
    private const string ActionUpdate = "picshare.longRunning.UPDATE";
    private const string ActionStop = "picshare.longRunning.STOP";
    private const string ExtraOperationId = "operationId";
    private const string ExtraKind = "kind";
    private const string ExtraTitle = "title";
    private const string ExtraMessage = "message";
    private const string ExtraValue = "value";
    private const string ExtraMaximum = "maximum";

    private static readonly object Sync = new();
    private static Guid? _activeOperationId;

    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            var operationId = ReadOperationId(intent);
            lock (Sync)
            {
                if (_activeOperationId is null || _activeOperationId == operationId)
                {
                    _activeOperationId = null;
                    StopForeground(StopForegroundFlags.Remove);
                    StopSelf();
                }
            }

            return StartCommandResult.NotSticky;
        }

        var operation = ReadOperation(intent);
        if (operation is null)
        {
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        lock (Sync)
        {
            _activeOperationId = operation.OperationId;
        }

        EnsureNotificationChannel();
        StartForeground(NotificationId, BuildNotification(operation));
        return StartCommandResult.Sticky;
    }

    public static void StartOrUpdate(Context context, LongRunningOperationInfo operation)
    {
        var intent = new Intent(context, typeof(PicshareForegroundService));
        intent.SetAction(ActionUpdate);
        WriteOperation(intent, operation);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public static void Stop(Context context, Guid operationId)
    {
        var intent = new Intent(context, typeof(PicshareForegroundService));
        intent.SetAction(ActionStop);
        intent.PutExtra(ExtraOperationId, operationId.ToString("N"));
        context.StartService(intent);
    }

    private Notification BuildNotification(LongRunningOperationInfo operation)
    {
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);
        var maximum = Math.Max(1, operation.Maximum);
        var value = Math.Clamp(operation.Value, 0, maximum);

        return builder
            .SetContentTitle(operation.Title)
            .SetContentText(operation.Message)
            .SetSmallIcon(Resource.Drawable.Icon)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetProgress(maximum, value, false)
            .SetContentIntent(CreateLaunchIntent())
            .Build();
    }

    private PendingIntent? CreateLaunchIntent()
    {
        var launchIntent = PackageManager?.GetLaunchIntentForPackage(PackageName ?? "");
        if (launchIntent is null)
        {
            return null;
        }

        var flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            flags |= PendingIntentFlags.Immutable;
        }

        return PendingIntent.GetActivity(this, 0, launchIntent, flags);
    }

    private void EnsureNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
        var channel = notificationManager?.GetNotificationChannel(ChannelId);
        if (channel is not null)
        {
            return;
        }

        channel = new NotificationChannel(
            ChannelId,
            "Picshare operations",
            NotificationImportance.Low)
        {
            Description = "Shows progress for album uploads, deletion, and downloads."
        };
        notificationManager?.CreateNotificationChannel(channel);
    }

    private static void WriteOperation(Intent intent, LongRunningOperationInfo operation)
    {
        intent.PutExtra(ExtraOperationId, operation.OperationId.ToString("N"));
        intent.PutExtra(ExtraKind, operation.Kind.ToString());
        intent.PutExtra(ExtraTitle, operation.Title);
        intent.PutExtra(ExtraMessage, operation.Message);
        intent.PutExtra(ExtraValue, operation.Value);
        intent.PutExtra(ExtraMaximum, operation.Maximum);
    }

    private static LongRunningOperationInfo? ReadOperation(Intent? intent)
    {
        if (intent is null || !Guid.TryParse(intent.GetStringExtra(ExtraOperationId), out var operationId))
        {
            return null;
        }

        var kindText = intent.GetStringExtra(ExtraKind) ?? "";
        var kind = Enum.TryParse<LongRunningOperationKind>(kindText, out var parsedKind)
            ? parsedKind
            : LongRunningOperationKind.Download;
        return new LongRunningOperationInfo(
            operationId,
            kind,
            intent.GetStringExtra(ExtraTitle) ?? "Picshare",
            intent.GetStringExtra(ExtraMessage) ?? "Working...",
            intent.GetIntExtra(ExtraValue, 0),
            intent.GetIntExtra(ExtraMaximum, 1));
    }

    private static Guid? ReadOperationId(Intent intent)
    {
        return Guid.TryParse(intent.GetStringExtra(ExtraOperationId), out var operationId)
            ? operationId
            : null;
    }
}
#pragma warning restore CA1416, CA1422
