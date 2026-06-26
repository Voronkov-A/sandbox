using System;
using System.Threading;
using System.Threading.Tasks;
using Picshare.Services;
using UIKit;

namespace Picshare.iOS;

internal sealed class IosLongRunningOperationHost : ILongRunningOperationHost
{
    public ValueTask<ILongRunningOperationScope> BeginAsync(
        LongRunningOperationKind kind,
        string title,
        string message,
        int value,
        int maximum,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid();
        nint taskId = 0;
        taskId = UIApplication.SharedApplication.BeginBackgroundTask(title, () =>
        {
            if (taskId != 0)
            {
                UIApplication.SharedApplication.EndBackgroundTask(taskId);
                taskId = 0;
            }
        });

        return ValueTask.FromResult<ILongRunningOperationScope>(
            new IosLongRunningOperationScope(operationId, taskId));
    }

    public void Update(LongRunningOperationInfo operation)
    {
    }

    private sealed class IosLongRunningOperationScope(Guid operationId, nint taskId) : ILongRunningOperationScope
    {
        private nint _taskId = taskId;

        public Guid OperationId { get; } = operationId;

        public ValueTask DisposeAsync()
        {
            if (_taskId != 0)
            {
                UIApplication.SharedApplication.EndBackgroundTask(_taskId);
                _taskId = 0;
            }

            return ValueTask.CompletedTask;
        }
    }
}
