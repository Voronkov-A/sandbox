namespace Yap.Server;

public interface IEventWriter
{
    void Write(object evt);
}
