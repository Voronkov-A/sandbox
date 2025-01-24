using System.Collections.Generic;

namespace Ml.DummyNumbers;

internal interface IDummyNumbersPlayer
{
    IEnumerable<DummyNumbersAction> Decide(DummyNumbersState state);

    void CommitDecision(DummyNumbersState state, DummyNumbersAction decision);

    void FinalFeedback(DummyNumbersState finalState);
}
