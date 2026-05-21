namespace Yap.Common.Channels;

public sealed class CommandEnvelope(Identity originator, object command)
{
    public Identity Originator { get; } = originator;

    public object Command { get; } = command;
}
