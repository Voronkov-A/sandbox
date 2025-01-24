namespace Ml.DummyNumbers;

internal static class DummyNumbersGym
{
    public static void Run()
    {
        var bot = new AgentDummyNumbersPlayer();

        var players = new IDummyNumbersPlayer[]
        {
            //new AgentDummyNumbersPlayer(),
            bot
        };

        for (var i = 0; i < 5000; ++i)
        {
            var game = new DummyNumbersGame(players, 500);
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
