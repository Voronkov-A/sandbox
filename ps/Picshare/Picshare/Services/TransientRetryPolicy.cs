using System.Net.Http;

namespace Picshare.Services;

public static class TransientRetryPolicy
{
    private const int WarningAttemptThreshold = 3;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(1);

    public static async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        Action<string>? reportWarning,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            async token =>
            {
                await action(token);
                return true;
            },
            reportWarning,
            cancellationToken);
    }

    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        Action<string>? reportWarning,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var delay = InitialDelay;
        var warningWasReported = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await action(cancellationToken);
                if (warningWasReported)
                {
                    reportWarning?.Invoke("");
                }

                return result;
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken))
            {
                attempt++;
                var waitDelay = GetRetryDelay(delay);
                if (attempt >= WarningAttemptThreshold)
                {
                    warningWasReported = true;
                    reportWarning?.Invoke(
                        $"Temporary connection problem. Retrying in {FormatDelay(waitDelay)}. You can cancel this operation if it keeps happening. Last error: {GetShortMessage(ex)}");
                }

                await Task.Delay(waitDelay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaximumDelay.TotalMilliseconds));
            }
        }
    }

    public static async Task<bool> TryExecuteAsync(
        Func<CancellationToken, Task> action,
        int maximumAttempts,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var delay = InitialDelay;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await action(cancellationToken);
                return true;
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken))
            {
                attempt++;
                if (attempt >= Math.Max(1, maximumAttempts))
                {
                    return false;
                }

                await Task.Delay(GetRetryDelay(delay), cancellationToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaximumDelay.TotalMilliseconds));
            }
        }
    }

    public static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return false;
        }

        return exception is HttpRequestException or TimeoutException or IOException ||
            exception is TaskCanceledException ||
            exception is InvalidOperationException invalidOperationException &&
            IsTransientGoogleDriveFailure(invalidOperationException.Message);
    }

    private static TimeSpan GetRetryDelay(TimeSpan baseDelay)
    {
        var multiplier = 0.8d + Random.Shared.NextDouble() * 0.4d;
        return TimeSpan.FromMilliseconds(Math.Min(baseDelay.TotalMilliseconds * multiplier, MaximumDelay.TotalMilliseconds));
    }

    private static string FormatDelay(TimeSpan delay)
    {
        return delay >= TimeSpan.FromMinutes(1)
            ? "1 minute"
            : $"{Math.Max(1, (int)Math.Round(delay.TotalSeconds))} seconds";
    }

    private static bool IsTransientGoogleDriveFailure(string message)
    {
        const string prefix = "Google Drive request failed:";
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var statusStart = prefix.Length;
        while (statusStart < message.Length && char.IsWhiteSpace(message[statusStart]))
        {
            statusStart++;
        }

        var statusEnd = statusStart;
        while (statusEnd < message.Length && char.IsDigit(message[statusEnd]))
        {
            statusEnd++;
        }

        if (!int.TryParse(message[statusStart..statusEnd], out var statusCode))
        {
            return true;
        }

        return statusCode == 403 ||
            statusCode == 408 ||
            statusCode == 409 ||
            statusCode == 429 ||
            statusCode >= 500;
    }

    private static string GetShortMessage(Exception exception)
    {
        var message = exception.Message.ReplaceLineEndings(" ").Trim();
        return message.Length <= 220 ? message : message[..220] + "...";
    }
}
