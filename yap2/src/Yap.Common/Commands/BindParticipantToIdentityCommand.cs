namespace Yap.Common.Commands;

public sealed class BindParticipantToIdentityCommand
{
    public required string Identity { get; init; }

    public required string ParticipantId { get; init; }
}
