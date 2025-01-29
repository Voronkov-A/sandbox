namespace Ml.DummyNumbers;

internal static class DummyNumbersGym
{
    public static void Run()
    {
        var bot = new AgentDummyNumbersPlayer();

        var players = new IDummyNumbersPlayer[]
        {
            new NullDummyNumbersPlayer(),
            bot
        };

        for (var i = 0; i < 10000; ++i)
        {
            var game = new DummyNumbersGame(players, 1000);
            game.Run();
        }

        //bot.Complete();

        players = new IDummyNumbersPlayer[]
        {
            new HumanDummyNumbersPlayer(),
            bot
        };

        for (var i = 0; i < 100; ++i)
        {
            var game = new DummyNumbersGame(players, 20);
            game.Run();
        }
    }
}
