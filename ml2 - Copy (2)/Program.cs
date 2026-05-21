using System.Globalization;

const int TrainingGames = 10_000;
const int EvaluationGames = 200;
const int ReplayWarmupGames = 500;

var random = new Random();
var agent1 = new DqnAgent("Agent 1", random, new DqnBrain(random));
var agent2 = new DqnAgent("Agent 2", random, new DqnBrain(random));
var game = new NumberZeroGame(random);

Console.WriteLine("Training DQN agents...");

agent1.Epsilon = 1.0;
agent2.Epsilon = 1.0;
for (var gameNumber = 0; gameNumber < ReplayWarmupGames; gameNumber++)
{
    game.PlayTrainingGame(agent1, agent2, greedy: false, train: true, replayTraining: false);
}

for (var gameNumber = 0; gameNumber < TrainingGames; gameNumber++)
{
    var exploration = Lerp(0.45, 0.02, (double)gameNumber / TrainingGames);
    agent1.Epsilon = exploration;
    agent2.Epsilon = exploration;

    game.PlayTrainingGame(agent1, agent2, train: true, replayTraining: true);

    if (gameNumber % 100 == 0)
    {
        agent1.SyncTargetNetwork();
        agent2.SyncTargetNetwork();
    }
}

agent1.SyncTargetNetwork();
agent2.SyncTargetNetwork();
agent1.Epsilon = 0.0;
agent2.Epsilon = 0.0;

var wins = 0;
var totalWinningTurns = 0;
for (var i = 0; i < EvaluationGames; i++)
{
    var result = game.PlayTrainingGame(agent1, agent2, greedy: true, train: false);
    if (result.Won)
    {
        wins++;
        totalWinningTurns += result.Turns;
    }
}

agent1.Epsilon = 0.0;
agent2.Epsilon = 0.0;

var averageWinningTurns = wins == 0 ? 0 : (double)totalWinningTurns / wins;
Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"Training finished. Greedy evaluation: {wins}/{EvaluationGames} wins, average winning turns {averageWinningTurns:0.00}."));
Console.WriteLine("Enter -2, -1, 0, 1, or 2 on your turns. Enter q to quit.");
Console.WriteLine();

while (true)
{
    var keepPlaying = game.PlayHumanGame(agent1);
    if (!keepPlaying)
    {
        break;
    }

    Console.Write("Play another game? [Y/n]: ");
    var answer = Console.ReadLine()?.Trim();
    if (answer?.Equals("n", StringComparison.OrdinalIgnoreCase) == true ||
        answer?.Equals("q", StringComparison.OrdinalIgnoreCase) == true)
    {
        break;
    }

    Console.WriteLine();
}

Console.WriteLine("Done.");

static double Lerp(double start, double end, double amount) => start + ((end - start) * amount);

internal sealed class NumberZeroGame(Random random)
{
    private static readonly int[] Actions = [-2, -1, 0, 1, 2];

    public GameResult PlayTrainingGame(
        DqnAgent first,
        DqnAgent second,
        bool greedy = false,
        bool train = true,
        bool replayTraining = true)
    {
        var state = NewInitialState();
        var maxTurns = GetMaxTurns(state);
        var turns = 0;
        var current = first;

        while (turns < maxTurns && state != 0)
        {
            var action = current.ChooseAction(state, greedy);
            var nextState = state + action;
            turns++;

            var done = nextState == 0 || turns >= maxTurns;
            var reward = GetReward(state, action, nextState, turns, maxTurns);
            if (train)
            {
                current.Learn(state, action, reward, nextState, done, replayTraining);
            }

            state = nextState;
            current = ReferenceEquals(current, first) ? second : first;
        }

        return new GameResult(state == 0, turns);
    }

    public bool PlayHumanGame(DqnAgent agent)
    {
        var state = NewInitialState();
        var maxTurns = GetMaxTurns(state);
        var turns = 0;
        var agentTurn = true;

        Console.WriteLine($"New game. Initial N = {state}, MaxTurns = {maxTurns}.");

        while (turns < maxTurns && state != 0)
        {
            if (agentTurn)
            {
                var action = agent.ChooseAction(state, greedy: false);
                var nextState = state + action;
                turns++;

                var done = nextState == 0 || turns >= maxTurns;
                var reward = GetReward(state, action, nextState, turns, maxTurns);
                agent.Learn(state, action, reward, nextState, done);

                Console.WriteLine($"Turn {turns}/{maxTurns}: agent applies {FormatAction(action)}, N = {nextState}");
                state = nextState;
            }
            else
            {
                Console.Write($"Turn {turns + 1}/{maxTurns}: N = {state}, your update: ");
                var input = Console.ReadLine()?.Trim();
                if (input?.Equals("q", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return false;
                }

                if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var action) ||
                    !Actions.Contains(action))
                {
                    Console.WriteLine("Invalid move. Use -2, -1, 0, 1, 2, or q.");
                    continue;
                }

                var nextState = state + action;
                turns++;

                agent.LearnFromObservedMove(
                    state,
                    action,
                    GetReward(state, action, nextState, turns, maxTurns),
                    nextState,
                    nextState == 0 || turns >= maxTurns);

                Console.WriteLine($"Turn {turns}/{maxTurns}: you apply {FormatAction(action)}, N = {nextState}");
                state = nextState;
            }

            agentTurn = !agentTurn;
        }

        if (state == 0)
        {
            Console.WriteLine($"Win in {turns} turn(s).");
        }
        else
        {
            Console.WriteLine($"Loss. N = {state} after {turns} turn(s).");
        }

        return true;
    }

    private int NewInitialState()
    {
        var magnitude = random.Next(20, 101);
        return random.Next(2) == 0 ? -magnitude : magnitude;
    }

    private static int GetMaxTurns(int initialState) => (int)Math.Floor(Math.Abs(initialState) / 1.5);

    private static double GetReward(int state, int action, int nextState, int turns, int maxTurns)
    {
        if (nextState == 0)
        {
            return 10.0 - (turns * 0.05);
        }

        if (turns >= maxTurns)
        {
            return -10.0 - (Math.Abs(nextState) / 25.0);
        }

        var progress = Math.Abs(state) - Math.Abs(nextState);
        var crossedZeroPenalty = state != 0 &&
            nextState != 0 &&
            Math.Sign(state) != Math.Sign(nextState)
                ? 2.0
                : 0.0;

        return (progress * 0.8) - 0.05 - (action == 0 ? 0.35 : 0.0) - crossedZeroPenalty;
    }

    private static string FormatAction(int action) => action switch
    {
        > 0 => $"+{action}",
        _ => action.ToString(CultureInfo.InvariantCulture)
    };
}

internal sealed class DqnAgent(string name, Random random, DqnBrain brain)
{
    private static readonly int[] Actions = [-2, -1, 0, 1, 2];
    private const int ReplayTrainStepsPerTurn = 1;

    public string Name { get; } = name;
    public double Epsilon { get; set; } = 0.2;

    public void SyncTargetNetwork() => brain.SyncTargetNetwork();

    public int ChooseAction(int state, bool greedy = false)
    {
        if (!greedy && random.NextDouble() < Epsilon)
        {
            return Actions[random.Next(Actions.Length)];
        }

        var qValues = brain.PredictOnline(state);
        var bestIndex = 0;
        for (var i = 1; i < qValues.Length; i++)
        {
            if (qValues[i] > qValues[bestIndex])
            {
                bestIndex = i;
            }
        }

        return Actions[bestIndex];
    }

    public void LearnFromObservedMove(int state, int action, double reward, int nextState, bool done)
    {
        Learn(state, action, reward, nextState, done);
    }

    public void Learn(int state, int action, double reward, int nextState, bool done, bool replayTraining = true)
    {
        brain.Remember(new Transition(state, ActionToIndex(action), reward, nextState, done));
        if (!replayTraining)
        {
            return;
        }

        for (var i = 0; i < ReplayTrainStepsPerTurn; i++)
        {
            brain.TrainReplayBatch();
        }
    }

    private static int ActionToIndex(int action)
    {
        var index = Array.IndexOf(Actions, action);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action.");
        }

        return index;
    }
}

internal sealed class DqnBrain
{
    private const int BatchSize = 32;
    private const int ReplayCapacity = 5_000;
    private const double DiscountFactor = 0.96;

    private readonly Random random;
    private readonly NeuralNetwork onlineNetwork;
    private readonly NeuralNetwork targetNetwork;
    private readonly ReplayBuffer replayBuffer;

    public DqnBrain(Random random)
    {
        this.random = random;
        onlineNetwork = new NeuralNetwork(random, inputSize: 7, hiddenSize: 32, outputSize: 5, learningRate: 0.001);
        targetNetwork = onlineNetwork.Clone();
        replayBuffer = new ReplayBuffer(ReplayCapacity, random);
    }

    public double[] PredictOnline(int state) => onlineNetwork.Predict(StateFeatures(state));

    public void Remember(Transition transition) => replayBuffer.Add(transition);

    public void SyncTargetNetwork() => targetNetwork.CopyWeightsFrom(onlineNetwork);

    public void TrainReplayBatch()
    {
        if (replayBuffer.Count < BatchSize)
        {
            return;
        }

        foreach (var transition in replayBuffer.Sample(BatchSize))
        {
            var target = onlineNetwork.Predict(StateFeatures(transition.State));
            var nextOnlineQValues = onlineNetwork.Predict(StateFeatures(transition.NextState));
            var bestNextActionIndex = ArgMax(nextOnlineQValues);
            var nextTargetQValues = targetNetwork.Predict(StateFeatures(transition.NextState));
            var bestFutureQ = transition.Done ? 0.0 : nextTargetQValues[bestNextActionIndex];

            target[transition.ActionIndex] = transition.Reward + (DiscountFactor * bestFutureQ);
            onlineNetwork.Train(StateFeatures(transition.State), target);
        }
    }

    private static double[] StateFeatures(int state)
    {
        var clippedState = Math.Clamp(state, -200, 200);
        var smallState = Math.Clamp(state, -10, 10);
        return
        [
            clippedState / 200.0,
            Math.Abs(clippedState) / 200.0,
            smallState / 10.0,
            Math.Abs(smallState) / 10.0,
            Math.Sign(clippedState),
            state == 0 ? 1.0 : 0.0,
            Math.Abs(state) <= 2 ? 1.0 : 0.0
        ];
    }

    private static int ArgMax(double[] values)
    {
        var bestIndex = 0;
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] > values[bestIndex])
            {
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}

internal sealed class ReplayBuffer(int capacity, Random random)
{
    private readonly List<Transition> transitions = new(capacity);
    private int nextIndex;

    public int Count => transitions.Count;

    public void Add(Transition transition)
    {
        if (transitions.Count < capacity)
        {
            transitions.Add(transition);
            return;
        }

        transitions[nextIndex] = transition;
        nextIndex = (nextIndex + 1) % capacity;
    }

    public IEnumerable<Transition> Sample(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return transitions[random.Next(transitions.Count)];
        }
    }
}

internal sealed class NeuralNetwork
{
    private readonly Random random;
    private readonly double learningRate;
    private readonly double[,] inputToHidden;
    private readonly double[] hiddenBias;
    private readonly double[,] hiddenToOutput;
    private readonly double[] outputBias;

    public NeuralNetwork(Random random, int inputSize, int hiddenSize, int outputSize, double learningRate)
    {
        this.random = random;
        this.learningRate = learningRate;
        inputToHidden = new double[inputSize, hiddenSize];
        hiddenBias = new double[hiddenSize];
        hiddenToOutput = new double[hiddenSize, outputSize];
        outputBias = new double[outputSize];

        FillRandom(inputToHidden);
        FillRandom(hiddenToOutput);
    }

    private NeuralNetwork(
        Random random,
        double learningRate,
        double[,] inputToHidden,
        double[] hiddenBias,
        double[,] hiddenToOutput,
        double[] outputBias)
    {
        this.random = random;
        this.learningRate = learningRate;
        this.inputToHidden = Copy(inputToHidden);
        this.hiddenBias = [.. hiddenBias];
        this.hiddenToOutput = Copy(hiddenToOutput);
        this.outputBias = [.. outputBias];
    }

    public double[] Predict(double[] input)
    {
        var hidden = ForwardHidden(input);
        return ForwardOutput(hidden);
    }

    public void Train(double[] input, double[] target)
    {
        var hiddenPreActivation = new double[hiddenBias.Length];
        var hidden = new double[hiddenBias.Length];

        for (var h = 0; h < hidden.Length; h++)
        {
            var sum = hiddenBias[h];
            for (var i = 0; i < input.Length; i++)
            {
                sum += input[i] * inputToHidden[i, h];
            }

            hiddenPreActivation[h] = sum;
            hidden[h] = Relu(sum);
        }

        var output = ForwardOutput(hidden);
        var outputErrors = new double[output.Length];
        for (var o = 0; o < output.Length; o++)
        {
            outputErrors[o] = Clip(output[o] - target[o], -5.0, 5.0);
        }

        var hiddenErrors = new double[hidden.Length];
        for (var h = 0; h < hidden.Length; h++)
        {
            var error = 0.0;
            for (var o = 0; o < output.Length; o++)
            {
                error += outputErrors[o] * hiddenToOutput[h, o];
            }

            hiddenErrors[h] = hiddenPreActivation[h] > 0.0 ? error : 0.0;
        }

        for (var h = 0; h < hidden.Length; h++)
        {
            for (var o = 0; o < output.Length; o++)
            {
                hiddenToOutput[h, o] -= learningRate * outputErrors[o] * hidden[h];
            }
        }

        for (var o = 0; o < output.Length; o++)
        {
            outputBias[o] -= learningRate * outputErrors[o];
        }

        for (var i = 0; i < input.Length; i++)
        {
            for (var h = 0; h < hidden.Length; h++)
            {
                inputToHidden[i, h] -= learningRate * hiddenErrors[h] * input[i];
            }
        }

        for (var h = 0; h < hidden.Length; h++)
        {
            hiddenBias[h] -= learningRate * hiddenErrors[h];
        }
    }

    public NeuralNetwork Clone()
    {
        return new NeuralNetwork(random, learningRate, inputToHidden, hiddenBias, hiddenToOutput, outputBias);
    }

    public void CopyWeightsFrom(NeuralNetwork source)
    {
        CopyInto(source.inputToHidden, inputToHidden);
        Array.Copy(source.hiddenBias, hiddenBias, hiddenBias.Length);
        CopyInto(source.hiddenToOutput, hiddenToOutput);
        Array.Copy(source.outputBias, outputBias, outputBias.Length);
    }

    private double[] ForwardHidden(double[] input)
    {
        var hidden = new double[hiddenBias.Length];
        for (var h = 0; h < hidden.Length; h++)
        {
            var sum = hiddenBias[h];
            for (var i = 0; i < input.Length; i++)
            {
                sum += input[i] * inputToHidden[i, h];
            }

            hidden[h] = Relu(sum);
        }

        return hidden;
    }

    private double[] ForwardOutput(double[] hidden)
    {
        var output = new double[outputBias.Length];
        for (var o = 0; o < output.Length; o++)
        {
            var sum = outputBias[o];
            for (var h = 0; h < hidden.Length; h++)
            {
                sum += hidden[h] * hiddenToOutput[h, o];
            }

            output[o] = sum;
        }

        return output;
    }

    private void FillRandom(double[,] weights)
    {
        var inputCount = weights.GetLength(0);
        var limit = Math.Sqrt(2.0 / inputCount);
        for (var row = 0; row < weights.GetLength(0); row++)
        {
            for (var column = 0; column < weights.GetLength(1); column++)
            {
                weights[row, column] = ((random.NextDouble() * 2.0) - 1.0) * limit;
            }
        }
    }

    private static double Relu(double value) => Math.Max(0.0, value);

    private static double Clip(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    private static double[,] Copy(double[,] source)
    {
        var copy = new double[source.GetLength(0), source.GetLength(1)];
        CopyInto(source, copy);
        return copy;
    }

    private static void CopyInto(double[,] source, double[,] destination)
    {
        for (var row = 0; row < source.GetLength(0); row++)
        {
            for (var column = 0; column < source.GetLength(1); column++)
            {
                destination[row, column] = source[row, column];
            }
        }
    }
}

internal readonly record struct Transition(int State, int ActionIndex, double Reward, int NextState, bool Done);

internal readonly record struct GameResult(bool Won, int Turns);
