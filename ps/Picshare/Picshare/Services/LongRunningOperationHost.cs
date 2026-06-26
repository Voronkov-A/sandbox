namespace Picshare.Services;

public enum LongRunningOperationKind
{
    AlbumCreation,
    AlbumCreationCancellation,
    AlbumDeletion,
    AlbumDeletionCancellation,
    Download
}

public sealed record LongRunningOperationInfo(
    Guid OperationId,
    LongRunningOperationKind Kind,
    string Title,
    string Message,
    int Value,
    int Maximum);

public interface ILongRunningOperationScope : IAsyncDisposable
{
    Guid OperationId { get; }
}

public interface ILongRunningOperationHost
{
    ValueTask<ILongRunningOperationScope> BeginAsync(
        LongRunningOperationKind kind,
        string title,
        string message,
        int value,
        int maximum,
        CancellationToken cancellationToken);

    void Update(LongRunningOperationInfo operation);
}

public static class LongRunningOperationHost
{
    public static ILongRunningOperationHost Current { get; set; } = new NoOpLongRunningOperationHost();
}

internal sealed class NoOpLongRunningOperationHost : ILongRunningOperationHost
{
    public ValueTask<ILongRunningOperationScope> BeginAsync(
        LongRunningOperationKind kind,
        string title,
        string message,
        int value,
        int maximum,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<ILongRunningOperationScope>(
            new NoOpLongRunningOperationScope(Guid.NewGuid()));
    }

    public void Update(LongRunningOperationInfo operation)
    {
    }

    private sealed class NoOpLongRunningOperationScope(Guid operationId) : ILongRunningOperationScope
    {
        public Guid OperationId { get; } = operationId;

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
