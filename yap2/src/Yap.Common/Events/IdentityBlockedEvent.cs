namespace Yap.Common.Events;

public sealed class IdentityBlockedEvent
{
    public required string Identity { get; init; }

    public required string Reason { get; init; }
}
