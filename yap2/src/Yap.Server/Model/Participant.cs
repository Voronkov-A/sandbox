using Yap.Common;

namespace Yap.Server.Model;

public sealed class Participant
{
    public string Id { get; }

    public Identity? Identity { get; set; }
}
