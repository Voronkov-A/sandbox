namespace Yaprt.Domain;

public sealed class Participant
{
    public Participant(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
