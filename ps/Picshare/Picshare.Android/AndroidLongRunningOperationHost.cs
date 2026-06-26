using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Picshare.Services;

namespace Picshare.Android;

internal sealed class AndroidLongRunningOperationHost(Context context) : ILongRunningOperationHost
{
    private readonly Context _context = context.ApplicationContext ?? context;

    public ValueTask<ILongRunningOperationScope> BeginAsync(
        LongRunningOperationKind kind,
        string title,
        string message,
        int value,
        int maximum,
        CancellationToken cancellationToken)
    {
        var operation = new LongRunningOperationInfo(
            Guid.NewGuid(),
            kind,
            title,
            message,
            value,
            maximum);
        PicshareForegroundService.StartOrUpdate(_context, operation);
        return ValueTask.FromResult<ILongRunningOperationScope>(
            new AndroidLongRunningOperationScope(_context, operation.OperationId));
    }

    public void Update(LongRunningOperationInfo operation)
    {
        PicshareForegroundService.StartOrUpdate(_context, operation);
    }

    private sealed class AndroidLongRunningOperationScope(Context context, Guid operationId) : ILongRunningOperationScope
    {
        public Guid OperationId { get; } = operationId;

        public ValueTask DisposeAsync()
        {
            PicshareForegroundService.Stop(context, OperationId);
            return ValueTask.CompletedTask;
        }
    }
}
