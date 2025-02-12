using System;

namespace Ml.DummyNumbers;

internal readonly record struct DummyNumbersState
{
    private static readonly Random Random = new();

    public DummyNumbersState(int maxNumber)
    {
        var minNumber = int.Min(maxNumber, 5);
        //CurrentNumber = Random.Next(minNumber, maxNumber + 1) * (int)Math.Pow(-1, Random.Next(2));
        CurrentNumber = 300;
        StepsLeft = Math.Abs(CurrentNumber);
    }

    private DummyNumbersState(int index, int currentNumber, int stepsLeft)
    {
        Index = index;
        CurrentNumber = currentNumber;
        StepsLeft = stepsLeft;
    }

    public int Index { get; }

    public int CurrentNumber { get; }

    public int StepsLeft { get; }

    public DummyNumbersState Apply(DummyNumbersAction action)
    {
        return new DummyNumbersState(Index + 1, CurrentNumber + action.Number, StepsLeft - 1);
    }
}
