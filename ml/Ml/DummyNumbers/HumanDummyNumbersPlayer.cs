using System;
using System.Collections.Generic;

namespace Ml.DummyNumbers;

internal class HumanDummyNumbersPlayer : IDummyNumbersPlayer
{
    public IEnumerable<DummyNumbersAction> Decide(DummyNumbersState state)
    {
        while (true)
        {
            DrawState(state);
            Console.Write("add: ");

            var input = Console.ReadLine();

            if (input != null && int.TryParse(input, out var number) && Math.Abs(number) <= 5)
            {
                yield return new DummyNumbersAction(number);
            }
        }
    }

    public void CommitDecision(DummyNumbersState state, DummyNumbersAction decision)
    {
        //
    }

    public void FinalFeedback(DummyNumbersState finalState)
    {
        DrawState(finalState);
        var message = finalState.CurrentNumber == 0 ? "You won." : "You lost.";
        Console.WriteLine(message);
        Console.ReadKey();
    }

    private static void DrawState(DummyNumbersState state)
    {
        Console.WriteLine($"CurrentNumber: {state.CurrentNumber}; StepsLeft: {state.StepsLeft}");
    }
}
