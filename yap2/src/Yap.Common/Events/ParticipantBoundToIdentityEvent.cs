namespace Yap.Common.Events;

public sealed class ParticipantBoundToIdentityEvent
{
    public required string Identity { get; init; }

    public required string ParticipantId { get; init; }
}
