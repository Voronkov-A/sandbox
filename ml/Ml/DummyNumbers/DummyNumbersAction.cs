namespace Ml.DummyNumbers;

internal readonly struct DummyNumbersAction
{
    public DummyNumbersAction(int number)
    {
        Number = number;
    }

    public int Number { get; }
}
