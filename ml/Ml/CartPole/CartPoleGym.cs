namespace Ml.CartPole;

internal static class CartPoleGym
{
    public static void Run()
    {
        var bot = new AgentCartPolePlayer();

        var players = new ICartPolePlayer[]
        {
            bot
        };

        double avgStepCount = 0;

        for (var i = 0; i < 100000; ++i)
        {
            var game = new CartPoleGame(players);
            var stats = game.Run();

            avgStepCount += stats.StepCount / 1000.0;

            if (i % 1000 == 0)
            {
                System.Console.WriteLine(avgStepCount);
                System.Console.WriteLine(stats.StepCount);

                avgStepCount = 0;
            }
        }

        //bot.Complete();

        /*
        players = new IDummyNumbersPlayer[]
        {
            new HumanDummyNumbersPlayer(),
            bot
        };

        for (var i = 0; i < 100; ++i)
        {
            var game = new DummyNumbersGame(players, 20);
            game.Run();
        }*/
    }
}
