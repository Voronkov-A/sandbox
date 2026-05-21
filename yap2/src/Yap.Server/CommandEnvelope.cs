using Yap.Common;

namespace Yap.Server;

public sealed class CommandEnvelope
{
    public Identity Originator { get; }

    public object Command { get; }
}
