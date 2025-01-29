using System.Collections.Generic;

namespace Ml.DummyNumbers;

internal sealed class NullDummyNumbersPlayer : IDummyNumbersPlayer
{
    public void CommitDecision(DummyNumbersState state, DummyNumbersAction decision)
    {
        // pass
    }

    public IEnumerable<DummyNumbersAction> Decide(DummyNumbersState state)
    {
        return new[] { new DummyNumbersAction(0) };
    }

    public void FinalFeedback(DummyNumbersState finalState)
    {
        // pass
    }
}
