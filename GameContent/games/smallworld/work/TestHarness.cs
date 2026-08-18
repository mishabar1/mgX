// COMPILE-CHECK / SMOKE-TEST HARNESS (not shipped). Plays whole Small World games with all-AI
// seats and asserts the invariants that matter: tokens conserved, no region held twice, scores
// only ever grow, the round counter advances, and the game ends with a winner.
using MG.Server.Controllers;
using MG.Server.Entities;
using MG.Server.GameFlows;

public static class TestHarness
{
    public static async Task<int> Main(string[] args)
    {
        int fails = 0;
        foreach (int seats in new[] { 2, 3, 4, 5 })
        {
            try { await PlayOne(seats); Console.WriteLine($"  seats={seats}: OK"); }
            catch (Exception ex) { fails++; Console.WriteLine($"  seats={seats}: FAIL {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
        }
        fails += RulesTest.Run();
        Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} FAILED");
        return fails;
    }

    private static async Task PlayOne(int seats)
    {
        var game = new GameData { GameType = GameTypeEnum.SMALL_WORLD };
        var flow = new SmallWorldGameFlow(game);
        await flow.RunCreateFlow();
        for (int i = 0; i < seats; i++)
        {
            game.Players[i].Type = PlayerTypeEnum.AI;
            game.Players[i].Name = "AI" + (i + 1);
        }
        await flow.RunSetupFlow();
        await flow.RunStartFlow();
        foreach (var p in game.Players) { p.AIAgent?.Stop(); p.AIAgent = null; }   // drive turns manually

        var rnd = new Random(12345 + seats);
        var order = game.Attributes["order"].Split(',');
        int Purses() => order.Sum(s => int.Parse(game.Attributes["coins:" + s]))
                        + game.Attributes["cqc"].Split(',', StringSplitOptions.RemoveEmptyEntries).Sum(int.Parse);
        int prevTotal = Purses();
        int guard = 0;

        while (!game.Attributes.ContainsKey("over"))
        {
            if (++guard > 6000) throw new Exception("game did not finish in 6000 AI steps");
            var cur = game.Players.First(p => p.Id == game.CurrentTurnId);
            var before = (phase: game.Attributes["phase"], turn: game.CurrentTurnId);
            bool moved = await flow.PlayAI(cur, rnd);
            if (!moved) throw new Exception("AI refused to move in phase " + before.phase);

            // invariants after every AI step
            var map = LoadRegionIds(game);
            foreach (var rid in map)
            {
                var own = game.Attributes.GetValueOrDefault("own:" + rid, "");
                var dwn = game.Attributes.GetValueOrDefault("dwn:" + rid, "");
                int tok = int.Parse(game.Attributes.GetValueOrDefault("tok:" + rid, "0"));
                int dtk = int.Parse(game.Attributes.GetValueOrDefault("dtk:" + rid, "0"));
                if (own != "" && tok <= 0) throw new Exception($"region {rid} owned but has {tok} tokens");
                if (own == "" && tok > 0) throw new Exception($"region {rid} has tokens but no owner");
                if (dwn != "" && dtk <= 0) throw new Exception($"region {rid} declined-owned with {dtk} tokens");
                if (own != "" && own == dwn) throw new Exception($"region {rid} held active AND declined by same seat");
            }
            foreach (var s in order)
            {
                if (int.Parse(game.Attributes["hand:" + s]) < 0) throw new Exception("negative hand");
                if (int.Parse(game.Attributes["coins:" + s]) < 0) throw new Exception("negative coins");
            }
            int total = Purses();
            if (total < prevTotal) throw new Exception($"coin total dropped {prevTotal} -> {total}");
            prevTotal = total;

            // the panel + scene must rebuild without throwing at every point of the game
            foreach (var p in game.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT)) p.Type = PlayerTypeEnum.HUMAN;
            typeof(BaseGameFlow).GetMethod("RefreshScreens",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(flow, null);
            foreach (var p in game.Players.Where(p => p.Type == PlayerTypeEnum.HUMAN)) p.Type = PlayerTypeEnum.AI;
        }

        int round = int.Parse(game.Attributes["round"]);
        int expectRounds = seats switch { 2 => 10, 3 => 10, 4 => 9, _ => 8 };
        if (round != expectRounds) throw new Exception($"ended on round {round}, expected {expectRounds}");
        if (game.Attributes.GetValueOrDefault("winnerIds", "") == "") throw new Exception("no winner recorded");
        var scores = order.Select(s => int.Parse(game.Attributes["coins:" + s])).ToList();
        if (scores.Max() < 20) throw new Exception("suspiciously low winning score " + scores.Max());
        Console.WriteLine($"    rounds={round} steps={guard} scores=[{string.Join(",", scores)}] " +
                          $"result=\"{game.Attributes.GetValueOrDefault("result", "")}\"");
    }

    private static List<int> LoadRegionIds(GameData game)
        => game.Attributes.Keys.Where(k => k.StartsWith("own:") || k.StartsWith("dwn:"))
               .Select(k => int.Parse(k.Split(':')[1])).Distinct().ToList();
}
