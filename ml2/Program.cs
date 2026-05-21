using System.Globalization;

const int TrainingGames = 12_000;
const int EvaluationGames = 200;

var random = new Random();
var agent1 = new PpoAgent("Agent 1", random, new PpoBrain(random));
var agent2 = new PpoAgent("Agent 2", random, new PpoBrain(random));
var game = new NumberZeroGame(random);

Console.WriteLine("Training PPO agents...");

for (var gameNumber = 0; gameNumber < TrainingGames; gameNumber++)
{
    var explorationTemperature = Lerp(1.35, 0.70, (double)gameNumber / TrainingGames);
    agent1.Temperature = explorationTemperature;
    agent2.Temperature = explorationTemperature;

    game.PlayTrainingGame(agent1, agent2, greedy: false, train: true);
}

agent1.Temperature = 0.01;
agent2.Temperature = 0.01;

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

    public GameResult PlayTrainingGame(PpoAgent first, PpoAgent second, bool greedy, bool train)
    {
        first.BeginEpisode();
        second.BeginEpisode();

        var state = NewInitialState();
        var maxTurns = GetMaxTurns(state);
        var turns = 0;
        var current = first;

        while (turns < maxTurns && state != 0)
        {
            var decision = current.ChooseAction(state, greedy);
            var nextState = state + decision.Action;
            turns++;

            var done = nextState == 0 || turns >= maxTurns;
            var reward = GetReward(state, decision.Action, nextState, turns, maxTurns);
            current.ObserveReward(reward, done);

            state = nextState;
            current = ReferenceEquals(current, first) ? second : first;
        }

        if (train)
        {
            first.FinishEpisode();
            second.FinishEpisode();
        }

        return new GameResult(state == 0, turns);
    }

    public bool PlayHumanGame(PpoAgent agent)
    {
        agent.BeginEpisode();

        var state = NewInitialState();
        var maxTurns = GetMaxTurns(state);
        var turns = 0;
        var agentTurn = true;

        Console.WriteLine($"New game. Initial N = {state}, MaxTurns = {maxTurns}.");

        while (turns < maxTurns && state != 0)
        {
            if (agentTurn)
            {
                var decision = agent.ChooseAction(state, greedy: true);
                var nextState = state + decision.Action;
                turns++;

                var done = nextState == 0 || turns >= maxTurns;
                var reward = GetReward(state, decision.Action, nextState, turns, maxTurns);
                agent.ObserveReward(reward, done);

                Console.WriteLine($"Turn {turns}/{maxTurns}: agent applies {FormatAction(decision.Action)}, N = {nextState}");
                state = nextState;
            }
            else
            {
                Console.Write($"Turn {turns + 1}/{maxTurns}: N = {state}, your update: ");
                var input = Console.ReadLine()?.Trim();
                if (input?.Equals("q", StringComparison.OrdinalIgnoreCase) == true)
                {
                    agent.FinishEpisode();
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

                Console.WriteLine($"Turn {turns}/{maxTurns}: you apply {FormatAction(action)}, N = {nextState}");
                state = nextState;
            }

            agentTurn = !agentTurn;
        }

        agent.FinishEpisode();

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

internal sealed class PpoAgent(string name, Random random, PpoBrain brain)
{
    private static readonly int[] Actions = [-2, -1, 0, 1, 2];
    private readonly List<PpoStep> episode = [];
    private PendingDecision? pendingDecision;

    public string Name { get; } = name;
    public double Temperature { get; set; } = 1.0;

    public AgentDecision ChooseAction(int state, bool greedy)
    {
        var prediction = brain.Predict(state, Temperature);
        var actionIndex = greedy
            ? ArgMax(prediction.Probabilities)
            : Sample(prediction.Probabilities);

        pendingDecision = new PendingDecision(
            state,
            actionIndex,
            Math.Log(Math.Max(prediction.Probabilities[actionIndex], 1e-12)),
            prediction.Value);

        return new AgentDecision(Actions[actionIndex]);
    }

    public void ObserveReward(double reward, bool done)
    {
        if (pendingDecision is not { } decision)
        {
            return;
        }

        episode.Add(new PpoStep(
            decision.State,
            decision.ActionIndex,
            decision.LogProbability,
            decision.Value,
            reward,
            done));

        pendingDecision = null;
    }

    public void BeginEpisode()
    {
        episode.Clear();
        pendingDecision = null;
    }

    public void FinishEpisode()
    {
        if (episode.Count > 0)
        {
            brain.Update([.. episode]);
        }

        episode.Clear();
        pendingDecision = null;
    }

    private int Sample(double[] probabilities)
    {
        var roll = random.NextDouble();
        var cumulative = 0.0;
        for (var i = 0; i < probabilities.Length; i++)
        {
            cumulative += probabilities[i];
            if (roll <= cumulative)
            {
                return i;
            }
        }

        return probabilities.Length - 1;
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

internal sealed class PpoBrain
{
    private const double DiscountFactor = 0.96;
    private const double ClipEpsilon = 0.20;
    private const double ValueLossCoefficient = 0.50;
    private const double EntropyCoefficient = 0.0;
    private const int UpdateEpochs = 4;

    private readonly PolicyValueNetwork network;

    public PpoBrain(Random random)
    {
        network = new PolicyValueNetwork(random, inputSize: 7, hiddenSize: 32, actionCount: 5, learningRate: 0.001);
    }

    public PolicyPrediction Predict(int state, double temperature)
    {
        return network.Predict(StateFeatures(state), temperature);
    }

    public void Update(PpoStep[] steps)
    {
        var returns = ComputeReturns(steps);
        var advantages = new double[steps.Length];
        for (var i = 0; i < steps.Length; i++)
        {
            advantages[i] = returns[i] - steps[i].Value;
        }

        NormalizeInPlace(advantages);

        for (var epoch = 0; epoch < UpdateEpochs; epoch++)
        {
            foreach (var index in ShuffledIndexes(steps.Length))
            {
                var step = steps[index];
                var features = StateFeatures(step.State);
                var prediction = network.Predict(features, temperature: 1.0);
                var probability = Math.Max(prediction.Probabilities[step.ActionIndex], 1e-12);
                var ratio = Math.Exp(Math.Log(probability) - step.LogProbability);
                var advantage = advantages[index];

                var unclipped = ratio * advantage;
                var clippedRatio = Math.Clamp(ratio, 1.0 - ClipEpsilon, 1.0 + ClipEpsilon);
                var clipped = clippedRatio * advantage;
                var usePolicyGradient = unclipped <= clipped;

                var policyLogProbabilityGradient = usePolicyGradient ? -advantage * ratio : 0.0;
                var valueGradient = ValueLossCoefficient * (prediction.Value - returns[index]);

                network.Train(
                    features,
                    step.ActionIndex,
                    policyLogProbabilityGradient,
                    valueGradient,
                    EntropyCoefficient);
            }
        }
    }

    private static double[] ComputeReturns(PpoStep[] steps)
    {
        var returns = new double[steps.Length];
        var nextReturn = 0.0;
        for (var i = steps.Length - 1; i >= 0; i--)
        {
            nextReturn = steps[i].Reward + (steps[i].Done ? 0.0 : DiscountFactor * nextReturn);
            returns[i] = nextReturn;
        }

        return returns;
    }

    private static void NormalizeInPlace(double[] values)
    {
        if (values.Length == 0)
        {
            return;
        }

        var mean = values.Average();
        var variance = values.Select(value => Math.Pow(value - mean, 2.0)).Average();
        var standardDeviation = Math.Sqrt(variance) + 1e-8;

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (values[i] - mean) / standardDeviation;
        }
    }

    private static IEnumerable<int> ShuffledIndexes(int count)
    {
        var indexes = Enumerable.Range(0, count).ToArray();
        Random.Shared.Shuffle(indexes);
        return indexes;
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
}

internal sealed class PolicyValueNetwork
{
    private readonly Random random;
    private readonly double learningRate;
    private readonly double[,] inputToHidden;
    private readonly double[] hiddenBias;
    private readonly double[,] hiddenToPolicy;
    private readonly double[] policyBias;
    private readonly double[] hiddenToValue;
    private double valueBias;

    public PolicyValueNetwork(Random random, int inputSize, int hiddenSize, int actionCount, double learningRate)
    {
        this.random = random;
        this.learningRate = learningRate;
        inputToHidden = new double[inputSize, hiddenSize];
        hiddenBias = new double[hiddenSize];
        hiddenToPolicy = new double[hiddenSize, actionCount];
        policyBias = new double[actionCount];
        hiddenToValue = new double[hiddenSize];

        FillRandom(inputToHidden);
        FillRandom(hiddenToPolicy);
        FillRandom(hiddenToValue);
    }

    public PolicyPrediction Predict(double[] input, double temperature)
    {
        var hidden = ForwardHidden(input, out _);
        var logits = ForwardPolicy(hidden);
        var probabilities = Softmax(logits, temperature);
        var value = ForwardValue(hidden);

        return new PolicyPrediction(probabilities, value);
    }

    public void Train(
        double[] input,
        int actionIndex,
        double policyLogProbabilityGradient,
        double valueGradient,
        double entropyCoefficient)
    {
        var hidden = ForwardHidden(input, out var hiddenPreActivation);
        var logits = ForwardPolicy(hidden);
        var probabilities = Softmax(logits, temperature: 1.0);

        var policyLogitGradients = new double[probabilities.Length];
        for (var i = 0; i < probabilities.Length; i++)
        {
            var logProbabilityGradient = i == actionIndex ? 1.0 - probabilities[i] : -probabilities[i];
            var entropyGradient = -probabilities[i] * (Math.Log(Math.Max(probabilities[i], 1e-12)) + 1.0);
            policyLogitGradients[i] =
                (policyLogProbabilityGradient * logProbabilityGradient) -
                (entropyCoefficient * entropyGradient);
        }

        var hiddenGradients = new double[hidden.Length];
        for (var h = 0; h < hidden.Length; h++)
        {
            var gradient = valueGradient * hiddenToValue[h];
            for (var a = 0; a < probabilities.Length; a++)
            {
                gradient += policyLogitGradients[a] * hiddenToPolicy[h, a];
            }

            hiddenGradients[h] = hiddenPreActivation[h] > 0.0 ? Clip(gradient, -5.0, 5.0) : 0.0;
        }

        for (var h = 0; h < hidden.Length; h++)
        {
            for (var a = 0; a < probabilities.Length; a++)
            {
                hiddenToPolicy[h, a] -= learningRate * policyLogitGradients[a] * hidden[h];
            }
        }

        for (var a = 0; a < probabilities.Length; a++)
        {
            policyBias[a] -= learningRate * policyLogitGradients[a];
        }

        for (var h = 0; h < hidden.Length; h++)
        {
            hiddenToValue[h] -= learningRate * valueGradient * hidden[h];
        }

        valueBias -= learningRate * valueGradient;

        for (var i = 0; i < input.Length; i++)
        {
            for (var h = 0; h < hidden.Length; h++)
            {
                inputToHidden[i, h] -= learningRate * hiddenGradients[h] * input[i];
            }
        }

        for (var h = 0; h < hidden.Length; h++)
        {
            hiddenBias[h] -= learningRate * hiddenGradients[h];
        }
    }

    private double[] ForwardHidden(double[] input, out double[] preActivation)
    {
        preActivation = new double[hiddenBias.Length];
        var hidden = new double[hiddenBias.Length];
        for (var h = 0; h < hidden.Length; h++)
        {
            var sum = hiddenBias[h];
            for (var i = 0; i < input.Length; i++)
            {
                sum += input[i] * inputToHidden[i, h];
            }

            preActivation[h] = sum;
            hidden[h] = Relu(sum);
        }

        return hidden;
    }

    private double[] ForwardPolicy(double[] hidden)
    {
        var logits = new double[policyBias.Length];
        for (var a = 0; a < logits.Length; a++)
        {
            var sum = policyBias[a];
            for (var h = 0; h < hidden.Length; h++)
            {
                sum += hidden[h] * hiddenToPolicy[h, a];
            }

            logits[a] = sum;
        }

        return logits;
    }

    private double ForwardValue(double[] hidden)
    {
        var value = valueBias;
        for (var h = 0; h < hidden.Length; h++)
        {
            value += hidden[h] * hiddenToValue[h];
        }

        return value;
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

    private void FillRandom(double[] weights)
    {
        var limit = Math.Sqrt(2.0 / weights.Length);
        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] = ((random.NextDouble() * 2.0) - 1.0) * limit;
        }
    }

    private static double[] Softmax(double[] logits, double temperature)
    {
        var stableTemperature = Math.Max(temperature, 0.01);
        var maxLogit = logits.Max();
        var exponentials = new double[logits.Length];
        var sum = 0.0;

        for (var i = 0; i < logits.Length; i++)
        {
            exponentials[i] = Math.Exp((logits[i] - maxLogit) / stableTemperature);
            sum += exponentials[i];
        }

        for (var i = 0; i < exponentials.Length; i++)
        {
            exponentials[i] /= sum;
        }

        return exponentials;
    }

    private static double Relu(double value) => Math.Max(0.0, value);

    private static double Clip(double value, double min, double max) => Math.Min(Math.Max(value, min), max);
}

internal sealed record PolicyPrediction(double[] Probabilities, double Value);

internal readonly record struct AgentDecision(int Action);

internal readonly record struct PendingDecision(int State, int ActionIndex, double LogProbability, double Value);

internal readonly record struct PpoStep(
    int State,
    int ActionIndex,
    double LogProbability,
    double Value,
    double Reward,
    bool Done);

internal readonly record struct GameResult(bool Won, int Turns);
