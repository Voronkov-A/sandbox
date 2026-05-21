using System.Globalization;

const int TrainingGames = 50_000;
const int EvaluationGames = 1_000;
var random = new Random();

var agent1 = new QLearningAgent("Agent 1", random);
var agent2 = new QLearningAgent("Agent 2", random, agent1.QValues);
var game = new NumberZeroGame(random);

Console.WriteLine("Training agents...");

for (var i = 0; i < TrainingGames; i++)
{
    var exploration = Lerp(0.35, 0.02, (double)i / TrainingGames);
    agent1.Epsilon = exploration;
    agent2.Epsilon = exploration;

    game.PlayTrainingGame(agent1, agent2);
}

agent1.Epsilon = 0.02;
agent2.Epsilon = 0.02;

var wins = 0;
var totalWinningTurns = 0;
for (var i = 0; i < EvaluationGames; i++)
{
    var result = game.PlayTrainingGame(agent1, agent2, greedy: true);
    if (result.Won)
    {
        wins++;
        totalWinningTurns += result.Turns;
    }
}

agent1.Epsilon = 0.01;
agent2.Epsilon = 0.01;

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

    public GameResult PlayTrainingGame(QLearningAgent first, QLearningAgent second, bool greedy = false)
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
            current.Learn(state, action, reward, nextState, done);

            state = nextState;
            current = ReferenceEquals(current, first) ? second : first;
        }

        return new GameResult(state == 0, turns);
    }

    public bool PlayHumanGame(QLearningAgent agent)
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

                agent.LearnFromObservedMove(state, action, GetReward(state, action, nextState, turns, maxTurns), nextState, nextState == 0 || turns >= maxTurns);

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
            return 200.0 - turns;
        }

        if (turns >= maxTurns)
        {
            return -200.0 - Math.Abs(nextState);
        }

        var oldDistance = Math.Abs(state);
        var newDistance = Math.Abs(nextState);
        var progressReward = oldDistance - newDistance;

        return (progressReward * 4.0) - 1.0 - (Math.Abs(action) == 0 ? 2.0 : 0.0);
    }

    private static string FormatAction(int action) => action switch
    {
        > 0 => $"+{action}",
        _ => action.ToString(CultureInfo.InvariantCulture)
    };
}

internal sealed class QLearningAgent
{
    private static readonly int[] Actions = [-2, -1, 0, 1, 2];
    private readonly Dictionary<(int State, int Action), double> qValues;

    public QLearningAgent(string name, Random random, Dictionary<(int State, int Action), double>? qValues = null)
    {
        Name = name;
        this.random = random;
        this.qValues = qValues ?? new Dictionary<(int State, int Action), double>();
    }

    private readonly Random random;

    public string Name { get; }
    public Dictionary<(int State, int Action), double> QValues => qValues;
    public double LearningRate { get; set; } = 0.18;
    public double DiscountFactor { get; set; } = 0.92;
    public double Epsilon { get; set; } = 0.18;

    public int ChooseAction(int state, bool greedy = false)
    {
        if (!greedy && random.NextDouble() < Epsilon)
        {
            return Actions[random.Next(Actions.Length)];
        }

        var candidateActions = GetCandidateActions(state);
        var bestScore = double.NegativeInfinity;
        var bestActions = new List<int>();

        foreach (var action in candidateActions)
        {
            var score = GetQValue(state, action);
            if (score > bestScore)
            {
                bestScore = score;
                bestActions.Clear();
                bestActions.Add(action);
            }
            else if (score.Equals(bestScore))
            {
                bestActions.Add(action);
            }
        }

        return bestActions[random.Next(bestActions.Count)];
    }

    public void LearnFromObservedMove(int state, int action, double reward, int nextState, bool done)
    {
        Learn(state, action, reward, nextState, done);
    }

    public void Learn(int state, int action, double reward, int nextState, bool done)
    {
        var oldValue = GetQValue(state, action);
        var futureValue = done ? 0.0 : Actions.Max(nextAction => GetQValue(nextState, nextAction));
        var target = reward + (DiscountFactor * futureValue);

        qValues[(state, action)] = oldValue + (LearningRate * (target - oldValue));
    }

    private double GetQValue(int state, int action)
    {
        if (qValues.TryGetValue((state, action), out var value))
        {
            return value;
        }

        var nextState = state + action;
        return Math.Abs(state) - Math.Abs(nextState);
    }

    private static int[] GetCandidateActions(int state)
    {
        var reducingActions = Actions
            .Where(action => Math.Abs(state + action) < Math.Abs(state))
            .ToArray();

        return reducingActions.Length == 0 ? Actions : reducingActions;
    }
}

internal readonly record struct GameResult(bool Won, int Turns);
