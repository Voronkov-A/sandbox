using System;
using System.Collections.Generic;
using System.Linq;
using TorchSharp;
using TorchSharp.Modules;

namespace Ml.DummyNumbers;

// source: https://github.com/KonstantinAlexeevich/Ksoshin.QLearning/blob/main/QLearning.Avalonia/QLearning.Avalonia.Desktop/QLearning.fs
internal class AgentDummyNumbersPlayer : IDummyNumbersPlayer, IDisposable
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fc3?.Dispose();
                _fc2?.Dispose();
                _fc1?.Dispose();
            }

            base.Dispose(disposing);
        }

        public override torch.Tensor forward(torch.Tensor input)
        {
            var result = _fc1.forward(input);
            result = torch.nn.functional.relu(result);
            result = _fc2.forward(result);
            result = torch.nn.functional.relu(result);
            result = _fc3.forward(result);
            return result;
        }
    }

    private readonly struct Transition
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
    private const float EpsDecay = 0.9995f;
    private const float GammaLearn = 0.99f;
    private const float LearningRate = 0.001f;
    private const float TauLearn = 0.01f;

    private static readonly Random _random = new();
    private readonly Dqn _policyNet;
    private readonly Dqn _targetNet;
    private readonly List<Transition> _memory;
    private readonly Adam _optimizer;
    private float _eps;
    private DummyNumbersState? _lastState;
    private DummyNumbersAction? _lastAction;
    private bool _isCompleted;
    private readonly ExactArrayPool<float> _arrayPool;

    public AgentDummyNumbersPlayer()
    {
        _arrayPool = new ExactArrayPool<float>();
        _eps = EpsStart;

        int stateSize = 1;
        int actionSize = 11;

        _policyNet = new Dqn(stateSize, actionSize);
        _targetNet = new Dqn(stateSize, actionSize);
        _memory = new List<Transition>();
        _optimizer = torch.optim.Adam(_policyNet.parameters(), lr: LearningRate);
    }

    public void Dispose()
    {
        _optimizer?.Dispose();
        _targetNet?.Dispose();
        _policyNet?.Dispose();
    }

    public void Complete()
    {
        _isCompleted = true;
    }

    public IEnumerable<DummyNumbersAction> Decide(DummyNumbersState state)
    {
        using var scope = torch.NewDisposeScope();

        Learn(state);

        using var features = GetFeatures(state);
        using var stateTensor = torch.tensor(features.Items);
        using var _ = torch.no_grad();
        _policyNet.eval();

        using var actionValues = _policyNet.forward(stateTensor);
        _policyNet.train();

        var actionIndices = _isCompleted || _random.NextSingle() > _eps
            ? actionValues.data<float>()
                .Select((weight, index) => (weight, index))
                .OrderByDescending(x => x.weight)
                .Select(x => x.index)
            : Enumerable.Range(0, 11)
                .OrderBy(_ => _random.Next());
        var result = actionIndices.Select(x => new DummyNumbersAction(x - 5));

        foreach (var item in result)
        {
            yield return item;
        }
    }

    public void CommitDecision(DummyNumbersState state, DummyNumbersAction decision)
    {
        _lastState = state;
        _lastAction = decision;
    }

    public void FinalFeedback(DummyNumbersState finalState)
    {
        using var scope = torch.NewDisposeScope();

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

    private RentedArray<float> GetFeatures(DummyNumbersState state)
    {
        var result = _arrayPool.Rent(1);
        result.Items[0] = state.CurrentNumber;
        return result;
    }

    private struct RentedArray<T> : IDisposable
    {
        private readonly ExactArrayPoolBucket<T> _bucket;
        private readonly int _index;
        private bool _disposed;

        internal RentedArray(ExactArrayPoolBucket<T> bucket)
        {
            _bucket = bucket;
            (Items, _index) = bucket.Rent();
        }

        public T[] Items { get; }

        internal int Index { get; }

        public void Dispose()
        {
            if (!_disposed)
            {
                _bucket.Return(_index);
                _disposed = false;
            }
        }
    }

    private class ExactArrayPoolBucket<T>
    {
        private readonly int _length;
        private readonly List<RentableArray> _arrays;

        public ExactArrayPoolBucket(int length)
        {
            _length = length;
            _arrays = new List<RentableArray>();
        }

        public (T[] Items, int Index) Rent()
        {
            var index = _arrays.FindIndex(x => !x.IsRented);

            if (index < 0)
            {
                _arrays.Add(new RentableArray(_length));
                index = _arrays.Count - 1;
            }

            var array = _arrays[index];
            array.IsRented = true;
            return (array.Items, index);
        }

        public void Return(int index)
        {
            _arrays[index].IsRented = false;
        }

        private class RentableArray(int length)
        {
            public T[] Items { get; } = new T[length];

            public bool IsRented { get; set; }
        }
    }

    private class ExactArrayPool<T>
    {
        private readonly Dictionary<int, ExactArrayPoolBucket<T>> _buckets;

        public ExactArrayPool()
        {
            _buckets = new Dictionary<int, ExactArrayPoolBucket<T>>();
        }

        public RentedArray<T> Rent(int length)
        {
            if (!_buckets.TryGetValue(length, out var bucket))
            {
                bucket = new ExactArrayPoolBucket<T>(length);
                _buckets[length] = bucket;
            }

            return new RentedArray<T>(bucket);
        }
    }

    private void Learn(DummyNumbersState state)
    {
        if (_lastState != null && _lastAction != null)
        {
            /*float reward = Math.Abs(_lastState.Value.CurrentNumber)
                - Math.Abs(_lastState.Value.CurrentNumber + _lastAction.Value.Number);*/

            float reward = -Math.Abs(state.CurrentNumber);

            if (state.CurrentNumber == 0)
            {
                reward = 100;
            }
            else if (state.StepsLeft == 0)
            {
                reward = -100;
            }

            var lastTransition = new Transition(_lastState.Value, _lastAction.Value, reward, state);
            _memory.Add(lastTransition);
        }

        if (state.CurrentNumber == 0 || state.StepsLeft == 0 || _memory.Count >= 500)
        {
            foreach (var transition in _memory)
            {
                int action = GetActionIndex(transition.Action);

                using var features = GetFeatures(transition.State);
                using var nextFeatures = GetFeatures(transition.NextState);

                using var actions = _arrayPool.Rent(1);
                actions.Items[0] = action;
                using var rewards = _arrayPool.Rent(1);
                rewards.Items[0] = transition.Reward;

                using var statesTensor = torch.tensor(features.Items);
                using var actionsTensor = torch.tensor(actions.Items);
                using var nextStatesTensor = torch.tensor(nextFeatures.Items);
                using var rewardsTensor = torch.tensor(rewards.Items);

                using var qTargetNext = _targetNet.forward(nextStatesTensor).max();
                using var qTargets = rewardsTensor + (GammaLearn * qTargetNext);
                using var qExpected = _policyNet.forward(statesTensor).gather(0, (long)action).unsqueeze(0);

                using var loss = torch.nn.functional.mse_loss(qExpected, qTargets, torch.nn.Reduction.Sum);
                //var loss = torch.nn.functional.huber_loss(qExpected, qTargets, delta: 1.0f, reduction: torch.nn.Reduction.Sum);
                _optimizer.zero_grad();
                loss.backward();
                _optimizer.step();
            }

            var localParams = _policyNet.parameters().ToList();
            var targetParams = _targetNet.parameters().ToList();

            // adjust target network
            foreach (var (localParam, targetParam) in localParams.Zip(targetParams))
            {
                targetParam.requires_grad = false;
                targetParam.copy_(TauLearn * localParam + (1.0 - TauLearn) * targetParam);
            }

            _memory.Clear();

            // copy target network to policy network
            foreach (var (policyParam, targetParam) in _policyNet.parameters().Zip(_targetNet.parameters()))
            {
                policyParam.copy_(targetParam);
            }
        }
    }
}
