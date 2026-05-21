using System.Collections.Generic;

namespace Yap.Server.Model;

public sealed class World
{
    public IReadOnlyCollection<Participant> Participants { get; }
}
