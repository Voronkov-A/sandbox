using System;
using System.Collections.Generic;
using System.Linq;
using TorchSharp;
using TorchSharp.Modules;

namespace Ml.DummyNumbers;

// source: https://github.com/KonstantinAlexeevich/Ksoshin.QLearning/blob/main/QLearning.Avalonia/QLearning.Avalonia.Desktop/QLearning.fs
internal class AgentDummyNumbersPlayer : IDummyNumbersPlayer
{
    private class Dqn : torch.nn.Module<torch.Tensor, torch.Tensor>
    {
        private readonly Linear _fc1;
        private readonly Linear _fc2;
        private readonly Linear _fc3;

        public Dqn(int stateSize, int actionSize) : base("DQN")
        {
            _fc1 = torch.nn.Linear(stateSize, 32);
            _fc2 = torch.nn.Linear(32, 64);
            _fc3 = torch.nn.Linear(64, actionSize);
        }

        public override torch.Tensor forward(torch.Tensor input)
        {
            var result = _fc1.forward(input);
            result = torch.nn.ReLU().forward(result);
            result = _fc2.forward(result);
            result = torch.nn.ReLU().forward(result);
            result = _fc3.forward(result);
            return result;
        }
    }

    private class Transition
    {
        public Transition(DummyNumbersState state, DummyNumbersAction action, float reward, DummyNumbersState nextState)
        {
            State = state;
            Action = action;
            Reward = reward;
            NextState = nextState;
        }

        public DummyNumbersState State { get; }

        public DummyNumbersAction Action { get; }

        public float Reward { get; }

        public DummyNumbersState NextState { get; }
    }



    private const float EpsStart = 1.0f;
    private const float EpsEnd = 0.05f;
    private const float EpsDecay = 0.995f;
    private const float GammaLearn = 0.99f;
    private const float LearningRate = 0.01f;
    private const float TauLearn = 0.001f;

    private readonly Random _random;
    private readonly Dqn _policyNet;
    private readonly Dqn _targetNet;
    private readonly List<Transition> _memory;
    private readonly Adam _optimizer;
    private float _eps;
    private DummyNumbersState? _lastState;
    private DummyNumbersAction? _lastAction;
    private bool _isCompleted;

    public AgentDummyNumbersPlayer()
    {
        _random = new Random();
        _eps = EpsStart;

        int stateSize = 1;
        int actionSize = 11;

        _policyNet = new Dqn(stateSize, actionSize);
        _targetNet = new Dqn(stateSize, actionSize);
        //_targetNet = _policyNet;
        _memory = new List<Transition>();
        _optimizer = torch.optim.Adam(_policyNet.parameters(), lr: LearningRate);
    }

    public void Complete()
    {
        _isCompleted = true;
    }

    public IEnumerable<DummyNumbersAction> Decide(DummyNumbersState state)
    {
        Learn(state);

        var stateTensor = torch.tensor(GetFeatures(state));
        using var _ = torch.no_grad();
        _policyNet.eval();

        var actionValues = _policyNet.forward(stateTensor);
        _policyNet.train();

        var actionIndices = _isCompleted || _random.NextSingle() > _eps
            ? actionValues.data<float>()
                .Select((weight, index) => (weight, index))
                .OrderByDescending(x => x.weight)
                .Select(x => x.index)
            : Enumerable.Range(0, 11)
                .OrderBy(_ => _random.Next());
        return actionIndices.Select(x => new DummyNumbersAction(x - 5));
    }

    public void CommitDecision(DummyNumbersState state, DummyNumbersAction decision)
    {
        _lastState = state;
        _lastAction = decision;
    }

    public void FinalFeedback(DummyNumbersState finalState)
    {
        if (finalState != _lastState)
        {
            Learn(finalState);
        }

        _eps = float.Max(EpsEnd, _eps * EpsDecay);
    }

    private static int GetActionIndex(DummyNumbersAction action)
    {
        return action.Number + 5;
    }

    private static float[] GetFeatures(DummyNumbersState state)
    {
        return new float[] { state.CurrentNumber };
    }

    private void Learn(DummyNumbersState state)
    {
        if (_lastState != null && _lastAction != null)
        {
            float reward = Math.Abs(_lastState.Value.CurrentNumber)
                //- Math.Abs(state.CurrentNumber);
                - Math.Abs(_lastState.Value.CurrentNumber + _lastAction.Value.Number);

            if (state.CurrentNumber != 0 && state.StepsLeft == 0)
            {
                reward = -10;
            }

            if (state.CurrentNumber == 0)
            {
                reward = 10;
            }

            var lastTransition = new Transition(_lastState.Value, _lastAction.Value, reward, state);
            _memory.Add(lastTransition);
        }

        if (state.CurrentNumber == 0 || state.StepsLeft == 0 || _memory.Count >= 50)
        {
            /*var rewards = _memory.Last().NextState.WinnerIndex == _index
                ? _memory.Select((_, i) => 100.0f / (i + 1))
                : _memory.Select((_, i) => -100.0f / (i + 1));*/

            foreach (var transition/*, reward*/ in _memory/*.Zip(rewards.Reverse())*/)
            {
                int action = GetActionIndex(transition.Action);

                var statesTensor = torch.tensor(GetFeatures(transition.State));
                var actionsTensor = torch.tensor(new float[] { action });
                var nextStatesTensor = torch.tensor(GetFeatures(transition.NextState));
                var rewardsTensor = torch.tensor(new float[] { transition.Reward });
                //var rewardsTensor = torch.tensor(new float[] { reward });
                //var donesTensor = torch.tensor(new float[] { transition.NextState.WinnerIndex >= 0 ? 1 : 0 });

                var qTargetNext = _targetNet.forward(nextStatesTensor).max();
                //var qTargets = rewardsTensor + (GammaLearn * qTargetNext) * (1 - donesTensor);
                var qTargets = rewardsTensor + (GammaLearn * qTargetNext);
                var qExpected = _policyNet.forward(statesTensor).gather(0, (long)action).unsqueeze(0);

                var loss = torch.nn.functional.mse_loss(qExpected, qTargets, torch.nn.Reduction.Sum);
                _optimizer.zero_grad();
                loss.backward();
                _optimizer.step();
            }

            var localParams = _policyNet.parameters().ToList();
            var targetParams = _targetNet.parameters().ToList();

            foreach (var (localParam, targetParam) in localParams.Zip(targetParams))
            {
                targetParam.requires_grad = false;
                targetParam.copy_(TauLearn * localParam + (1.0 - TauLearn) * targetParam);
            }

            _memory.Clear();
        }
    }
}
