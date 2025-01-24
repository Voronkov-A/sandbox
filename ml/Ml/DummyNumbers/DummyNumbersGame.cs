using System.Collections.Generic;
using System.Linq;

namespace Ml.DummyNumbers;

internal class DummyNumbersGame
{
    private readonly IDummyNumbersPlayer[] _players;
    private readonly int _maxNumber;

    public DummyNumbersGame(IEnumerable<IDummyNumbersPlayer> players, int maxNumber)
    {
        _players = players.ToArray();
        _maxNumber = maxNumber;
    }

    public void Run()
    {
        var state = new DummyNumbersState(_maxNumber);
        int currentPlayerIndex = 0;

        while (state.CurrentNumber != 0 && state.StepsLeft > 0)
        {
            sbyte nextPlayerIndex = (sbyte)((currentPlayerIndex + 1) % _players.Length);
            var player = _players[currentPlayerIndex];
            var action = player.Decide(state).First();
            var nextState = state.Apply(action);
            player.CommitDecision(state, action);
            state = nextState;
            currentPlayerIndex = nextPlayerIndex;
        }

        for (var i = 0; i < _players.Length; ++i)
        {
            _players[i].FinalFeedback(state);
        }
    }
}
