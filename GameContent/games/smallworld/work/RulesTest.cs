// Focused rule checks (not shipped): conquest cost modifiers, decline flip, Elves retreat.
using MG.Server.Controllers;
using MG.Server.Entities;
using MG.Server.GameFlows;
using System.Reflection;

public static class RulesTest
{
    static object? Call(object o, string m, params object[] a)
        => o.GetType().GetMethod(m, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, a);
    static object? Get(object o, string p)
        => o.GetType().GetProperty(p, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o);

    public static int Run()
    {
        int bad = 0;
        void Chk(string what, bool ok) { if (!ok) { bad++; Console.WriteLine("  RULE FAIL: " + what); } }

        var game = new GameData { GameType = GameTypeEnum.SMALL_WORLD };
        var flow = new SmallWorldGameFlow(game);
        flow.RunCreateFlow().GetAwaiter().GetResult();
        for (int i = 0; i < 3; i++) { game.Players[i].Type = PlayerTypeEnum.AI; game.Players[i].Name = "AI" + i; }
        flow.RunSetupFlow().GetAwaiter().GetResult();
        flow.RunStartFlow().GetAwaiter().GetResult();
        foreach (var p in game.Players) { p.AIAgent?.Stop(); p.AIAgent = null; }

        var A = game.Attributes;
        var order = A["order"].Split(',');
        string s0 = order[0], s1 = order[1];
        var regions = ((System.Collections.IEnumerable)Get(flow, "Land")!).Cast<object>().ToList();
        object RegT(string terr) => regions.First(r => (string)r.GetType().GetProperty("terrain")!.GetValue(r)! == terr
                                                    && ((System.Collections.IList)r.GetType().GetProperty("adj")!.GetValue(r)!).Count > 0);
        int Id(object r) => (int)r.GetType().GetProperty("id")!.GetValue(r)!;

        // empty region, plain race/power: cost = 2
        var mountain = RegT("MOUNTAIN"); var farm = RegT("FARM");
        A["race:" + s0] = "ratmen"; A["power:" + s0] = "merchant";
        foreach (var r in regions) { A.Remove("lt:" + Id(r)); A.Remove("own:" + Id(r)); A.Remove("tok:" + Id(r)); }
        Chk("empty farm costs 2", (int)Call(flow, "Cost", s0, farm)! == 2);
        Chk("empty mountain costs 3", (int)Call(flow, "Cost", s0, mountain)! == 3);

        // defenders add one each; a lost tribe counts as one
        A["own:" + Id(farm)] = s1; A["tok:" + Id(farm)] = "2";
        Chk("farm with 2 defenders costs 4", (int)Call(flow, "Cost", s0, farm)! == 4);
        A.Remove("own:" + Id(farm)); A.Remove("tok:" + Id(farm));
        A["lt:" + Id(farm)] = "1";
        Chk("farm with a lost tribe costs 3", (int)Call(flow, "Cost", s0, farm)! == 3);
        A.Remove("lt:" + Id(farm));

        // troll lair adds one; commando takes one off; mounted takes one off farm/hill
        A["own:" + Id(farm)] = s1; A["tok:" + Id(farm)] = "1"; A["race:" + s1] = "trolls";
        Chk("troll-held farm with 1 token costs 4", (int)Call(flow, "Cost", s0, farm)! == 4);
        A["power:" + s0] = "commando";
        Chk("commando pays 1 less", (int)Call(flow, "Cost", s0, farm)! == 3);
        A["power:" + s0] = "mounted";
        Chk("mounted pays 1 less on a farm", (int)Call(flow, "Cost", s0, farm)! == 3);
        A["power:" + s0] = "merchant";
        Chk("cost never drops below 1", (int)Call(flow, "Cost", s0, mountain)! >= 1);

        // decline: active regions keep exactly one token, the previously declined race vanishes
        A.Remove("own:" + Id(farm)); A.Remove("tok:" + Id(farm));
        A["race:" + s0] = "humans"; A["power:" + s0] = "hill"; A["hand:" + s0] = "3";
        A["own:" + Id(farm)] = s0; A["tok:" + Id(farm)] = "4";
        A["own:" + Id(mountain)] = s0; A["tok:" + Id(mountain)] = "2";
        A["drace:" + s0] = "orcs"; A["dpower:" + s0] = "swamp";
        var third = regions.First(r => Id(r) != Id(farm) && Id(r) != Id(mountain));
        A["dwn:" + Id(third)] = s0; A["dtk:" + Id(third)] = "1";
        game.CurrentTurnId = s0; A["phase"] = "conquer"; A.Remove("firstConq");
        flow.GoIntoDecline(new ExecuteActionData
        { actionId = "GoIntoDecline", gameId = game.Id, playerId = s0, Player = game.Players.First(p => p.Id == s0) })
            .GetAwaiter().GetResult();
        Chk("declined race keeps 1 token per region", A.GetValueOrDefault("dtk:" + Id(farm)) == "1"
                                                   && A.GetValueOrDefault("dtk:" + Id(mountain)) == "1");
        Chk("old declined race is gone", !A.ContainsKey("dwn:" + Id(third)));
        Chk("active race cleared", A["race:" + s0] == "" && A["hand:" + s0] == "0");
        Chk("newly declined race recorded", A["drace:" + s0] == "humans");
        Chk("turn passed on", game.CurrentTurnId != s0);

        // Elves lose no token when driven out
        foreach (var r in regions) { A.Remove("own:" + Id(r)); A.Remove("tok:" + Id(r)); A.Remove("dwn:" + Id(r)); A.Remove("dtk:" + Id(r)); A.Remove("lt:" + Id(r)); }
        A["race:" + s1] = "elves"; A["power:" + s1] = "merchant";
        var keep = regions.First(r => Id(r) != Id(farm));
        A["own:" + Id(farm)] = s1; A["tok:" + Id(farm)] = "3";
        A["own:" + Id(keep)] = s1; A["tok:" + Id(keep)] = "1";
        Call(flow, "RemoveDefenders", s0, farm);
        Chk("elves keep all 3 tokens on retreat", A["tok:" + Id(keep)] == "4");

        Console.WriteLine(bad == 0 ? "RULES OK" : bad + " RULE CHECKS FAILED");
        return bad;
    }
}
