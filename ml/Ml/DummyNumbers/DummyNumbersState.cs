using System;

namespace Ml.DummyNumbers;

internal readonly record struct DummyNumbersState
{
    private static readonly Random Random = new();

    public DummyNumbersState(int maxNumber)
    {
        var minNumber = int.Min(maxNumber, 5);
        CurrentNumber = Random.Next(maxNumber + minNumber) - minNumber - (maxNumber - minNumber) / 2;
        StepsLeft = Math.Abs(CurrentNumber);
    }

    private DummyNumbersState(int currentNumber, int stepsLeft)
    {
        CurrentNumber = currentNumber;
        StepsLeft = stepsLeft;
    }

    public int CurrentNumber { get; }

    public int StepsLeft { get; }

    public DummyNumbersState Apply(DummyNumbersAction action)
    {
        return new DummyNumbersState(CurrentNumber + action.Number, StepsLeft - 1);
    }
}
