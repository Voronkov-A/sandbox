namespace Yap.Common;

public sealed record Identity
{
    private readonly string _value;

    public Identity(string value)
    {
        _value = value;
    }

    public override string ToString()
    {
        return _value;
    }
}
