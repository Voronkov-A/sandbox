using System.Collections.Generic;
using System.Linq;

namespace Ml.CartPole;

internal class CartPoleGame
{
    private readonly ICartPolePlayer[] _players;

    public CartPoleGame(IEnumerable<ICartPolePlayer> players)
    {
        _players = players.ToArray();
    }

    public CartPoleStats Run()
    {
        var state = new CartPoleState();
        int currentPlayerIndex = 0;
        int stepCount = 0;

        while (!state.IsDone)
        {
            sbyte nextPlayerIndex = (sbyte)((currentPlayerIndex + 1) % _players.Length);
            var player = _players[currentPlayerIndex];
            var action = player.Decide(state).First();
            var nextState = state.Apply(action);
            player.CommitDecision(state, action);
            state = nextState;
            currentPlayerIndex = nextPlayerIndex;
            ++stepCount;
        }

        for (var i = 0; i < _players.Length; ++i)
        {
            _players[i].FinalFeedback(state);
        }

        return new CartPoleStats(stepCount);
    }
}
