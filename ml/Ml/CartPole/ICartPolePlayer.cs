using System.Collections.Generic;

namespace Ml.CartPole;

internal interface ICartPolePlayer
{
    IEnumerable<CartPoleAction> Decide(CartPoleState state);

    void CommitDecision(CartPoleState state, CartPoleAction decision);

    void FinalFeedback(CartPoleState finalState);
}
