namespace Yap.Server;

public interface ICommandReader
{
    bool TryRead(out CommandEnvelope? command);
}
