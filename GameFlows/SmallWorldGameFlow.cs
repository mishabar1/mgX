using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // =====================================================================================
    // SMALL WORLD — full core rules, 2..5 players, server-authoritative.
    //
    //  * The BOARD is ONE token item (the scanned map, downscaled: smallworld/map_N.jpg).
    //    Regions are pure SERVER data, loaded from GameContent/games/smallworld/regions_N.json
    //    (produced by the offline segmentation pipeline: id, terrain, symbols, adjacency,
    //    isBorder, and a centre point in PERCENT of the image). Everything clickable is a
    //    marker item the server places at a region centre — the client knows no geography.
    //  * Turn = pick a race+power combo from the queue of 6 (paying 1 coin onto each combo you
    //    skip) OR conquer with the race you already have OR put that race into decline; then
    //    conquer regions, then redeploy leftover tokens, then score.
    //  * Conquest cost = 2 + every defending token + 1 for a mountain + 1 for a troll lair;
    //    a Lost Tribe token counts as one defender. The last conquest of a turn may be helped
    //    by the reinforcement die (0,0,0,1,2,3), once per turn.
    //  * Scoring at the end of your turn: 1 coin per region you hold (active + in decline)
    //    plus your race's and power's bonuses.
    //  * Rounds: 10 / 10 / 9 / 8 for 2 / 3 / 4 / 5 players. Most coins wins.
    //
    // State lives in GameData.Attributes (see the key map in the "state" region below); the
    // scene and both panels are rebuilt from that state on every action, so undo/reload work.
    // =====================================================================================
    public class SmallWorldGameFlow : BaseGameFlow
    {
        public override int MinPlayers => 2;

        public SmallWorldGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.SMALL_WORLD;
        }

        // player colours (token tint), by seat order
        private static readonly string[] PCOL = { "0xd94444", "0x2b6fd9", "0xf2f2f2", "0xe8a33d", "0x8b5cf6" };
        private static readonly string[] PCOLHEX = { "d94444", "2b6fd9", "f2f2f2", "e8a33d", "8b5cf6" };

        private const int COMBOS = 6;              // visible race/power combinations
        private const double MAPW = 44.0;          // board width in world units

        internal static class Assets
        {
            internal static AssetData TEXT => new Text3dAssetData("sw");
            internal static AssetData MARKER => new CylinderAssetData("swmark");
            internal static AssetData TOKEN => new CylinderAssetData("swtok");
            internal static AssetData MAT => new CylinderAssetData("swmat");
            internal static AssetData DIE => new DieAssetData("swdie");
        }
        private AssetData Img(string f) => addAsset(new TokenAssetData("smallworld/" + f));

        // ============================== races & powers ==============================
        // v1 = numeric abilities only (nothing needing extra UI). tokens = tokens supplied.
        internal record Race(string Key, string Name, int Tokens, string Text);
        internal record Power(string Key, string Name, int Tokens, string Text);

        private static readonly List<Race> RACES = new()
        {
            new("dwarves",  "Dwarves",   3, "+1 coin per Mine region (also in decline)"),
            new("elves",    "Elves",     6, "Lose no token when defeated"),
            new("giants",   "Giants",    6, "Conquer for 1 less next to your Mountain"),
            new("humans",   "Humans",    5, "+1 coin per Farmland region"),
            new("orcs",     "Orcs",      5, "+1 coin per occupied region conquered this turn"),
            new("ratmen",   "Ratmen",    8, "Sheer numbers — no special power"),
            new("skeletons","Skeletons", 6, "+1 token per 2 occupied regions conquered"),
            new("trolls",   "Trolls",    5, "Troll lair: +1 to defend every region"),
            new("wizards",  "Wizards",   5, "+1 coin per Magic Source region"),
            new("tritons",  "Tritons",   6, "Conquer coastal regions for 1 less"),
        };

        private static readonly List<Power> POWERS = new()
        {
            new("alchemist", "Alchemist",  4, "+2 coins every turn"),
            new("commando",  "Commando",   4, "Conquer every region for 1 less"),
            new("mounted",   "Mounted",    5, "Conquer Farmland and Hills for 1 less"),
            new("underworld","Underworld", 5, "Caverns are all adjacent; conquer them for 1 less"),
            new("forest",    "Forest",     4, "+1 coin per Forest region"),
            new("hill",      "Hill",       4, "+1 coin per Hill region"),
            new("swamp",     "Swamp",      4, "+1 coin per Swamp region"),
            new("merchant",  "Merchant",   2, "+1 coin per region you hold"),
            new("flying",    "Flying",     5, "Conquer any region on the board"),
            new("pillaging", "Pillaging",  5, "+1 coin per occupied region conquered this turn"),
        };

        private static Race R(string k) => RACES.First(r => r.Key == k);
        private static Power P(string k) => POWERS.First(p => p.Key == k);

        // ============================== board data ==============================
        internal class Region
        {
            public int id { get; set; }
            public string terrain { get; set; } = "";
            public string water { get; set; } = "";
            public double cx { get; set; }
            public double cy { get; set; }
            public List<string> symbols { get; set; } = new();
            public List<int> adj { get; set; } = new();
            public bool isBorder { get; set; }
            public int area { get; set; }
        }
        internal class MapData
        {
            public int players { get; set; }
            public int w { get; set; }
            public int h { get; set; }
            public List<Region> regions { get; set; } = new();
        }

        // Loaded lazily per game (the map depends on the seat count) and cached per flow instance.
        private MapData? _map;
        private Dictionary<int, Region>? _byId;

        private static readonly JsonSerializerOptions JOPT = new() { PropertyNameCaseInsensitive = true };

        private MapData Map
        {
            get
            {
                if (_map == null)
                {
                    int n = MapPlayers;
                    var file = ResolveContent($"games/smallworld/regions_{n}.json");
                    _map = JsonSerializer.Deserialize<MapData>(File.ReadAllText(file), JOPT)
                           ?? throw new InvalidOperationException("smallworld: bad regions json");
                    _byId = _map.regions.ToDictionary(r => r.id);
                }
                return _map!;
            }
        }
        private Dictionary<int, Region> ById { get { _ = Map; return _byId!; } }
        private List<Region> Land => Map.regions.Where(r => r.terrain != "WATER").ToList();

        // GameContent lives next to the server binary at run time and in the project root during
        // development — probe both so this works under `dotnet run` and a published build alike.
        private static string ResolveContent(string rel)
        {
            foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var p = Path.Combine(root, "GameContent", rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(p)) return p;
            }
            return Path.Combine(Directory.GetCurrentDirectory(), "GameContent", rel);
        }

        // The board used = the number of occupied seats (2..5), clamped to the maps we have.
        private int MapPlayers
        {
            get
            {
                var v = Attr("map");
                if (v != "") return int.Parse(v);
                return Math.Clamp(Order.Count == 0 ? 2 : Order.Count, 2, 5);
            }
        }

        private double MapH => MAPW * Map.h / Map.w;
        private (double x, double z) WorldOf(Region r)
            => ((r.cx / 100.0 - 0.5) * MAPW, (r.cy / 100.0 - 0.5) * MapH);

        // ============================== state helpers ==============================
        // order              seat ids, turn order
        // map                player count whose board is in play
        // round              1-based round counter
        // phase              pick | conquer | redeploy | over
        // cq / cqc           combo queue "race|power" x6  /  coins sitting on each combo
        // race:<s>/power:<s> the seat's ACTIVE combo ("" = none, must pick)
        // drace:<s>/dpower:<s> the seat's combo IN DECLINE
        // coins:<s>          victory coins
        // hand:<s>           tokens in hand awaiting deployment
        // own:<rid>          seat holding the region with ACTIVE tokens
        // tok:<rid>          how many active tokens sit there
        // dwn:<rid>/dtk:<rid> seat + token count of a region held by a race IN DECLINE
        // lt:<rid>           "1" while the Lost Tribe token is still there
        // conq:<s>           regions conquered this turn / conqOcc:<s> occupied ones
        // died:<s>           "1" once the reinforcement die was used this turn
        // firstConq          "1" once this turn's first conquest happened
        // over / result / winnerIds
        private string Attr(string k) => GameData.Attributes.GetValueOrDefault(k, "") ?? "";
        private void Set(string k, string v) => GameData.Attributes[k] = v;
        private void Set(string k, int v) => GameData.Attributes[k] = v.ToString();
        private int Num(string k) => int.TryParse(Attr(k), out var v) ? v : 0;
        private List<string> Order => Attr("order").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        private string Phase => Attr("phase");
        private bool Over => GameData.Attributes.ContainsKey("over");
        private static string Arg(ExecuteActionData d, string key)
            => d.args != null && d.args.TryGetValue(key, out var v) ? v : (d.Item?.GetStringAttribute(key) ?? "");
        private string Name(string seat) => PlayerDisplayName(GameData.Players.FirstOrDefault(p => p.Id == seat)!);
        private string Col(string seat) { int i = Order.IndexOf(seat); return PCOL[Math.Max(0, i) % PCOL.Length]; }
        private string ColHex(string seat) { int i = Order.IndexOf(seat); return PCOLHEX[Math.Max(0, i) % PCOLHEX.Length]; }
        private int MaxRounds => MapPlayers switch { 2 => 10, 3 => 10, 4 => 9, _ => 8 };

        private bool MyTurn(string seat) => seat == GameData.CurrentTurnId && !Over;

        // ============================== lifecycle ==============================
        protected override Task Create()
        {
            addAsset(Assets.TEXT); addAsset(Assets.MARKER); addAsset(Assets.TOKEN);
            addAsset(Assets.MAT); addAsset(Assets.DIE);
            GameData.Attributes["noAvatars"] = "1";      // top-down board, no seated figures
            GameData.Observer.Position.Set(0, 38, 24);
            for (int i = 0; i < 5; i++)
                new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                    .AddAttribute("type", "p" + (i + 1)).SetCameraPosition(0, 36, 22).SetAvatarPosition(0, 0, 34);
            // pre-register the art so a persisted game always resolves its assets
            for (int n = 2; n <= 5; n++) Img($"map_{n}.jpg");
            foreach (var r in RACES) { Img($"races/{r.Key}.png"); Img($"races/{r.Key}_declined.png"); }
            foreach (var p in POWERS) Img($"powers/{p.Key}.png");
            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        protected override Task StartGame()
        {
            var rnd = new Random();
            var seats = GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT).Select(p => p.Id).ToList();
            Set("order", string.Join(",", seats));
            Set("map", Math.Clamp(seats.Count, 2, 5));
            GameData.CurrentTurnId = seats[0];
            Set("round", 1);
            Set("phase", "pick");
            GameData.Attributes.Remove("over");
            GameData.Attributes.Remove("firstConq");
            Set("log", "");

            foreach (var s in seats)
            {
                Set("coins:" + s, 5);              // official starting purse
                Set("hand:" + s, 0);
                Set("race:" + s, ""); Set("power:" + s, "");
                Set("drace:" + s, ""); Set("dpower:" + s, "");
                Set("conq:" + s, 0); Set("conqOcc:" + s, 0);
            }

            // clean the board, then drop one Lost Tribe token on every region marked TRIBE
            foreach (var r in Map.regions)
            {
                GameData.Attributes.Remove("own:" + r.id);
                GameData.Attributes.Remove("tok:" + r.id);
                GameData.Attributes.Remove("dwn:" + r.id);
                GameData.Attributes.Remove("dtk:" + r.id);
                if (r.symbols.Contains("TRIBE") && r.terrain != "WATER") Set("lt:" + r.id, "1");
            }

            // build the shuffled race / power decks and deal the first six combos
            var races = RACES.Select(r => r.Key).ToList();
            var powers = POWERS.Select(p => p.Key).ToList();
            Shuffle(races, rnd); Shuffle(powers, rnd);
            var cq = new List<string>();
            for (int i = 0; i < COMBOS; i++) cq.Add(races[i] + "|" + powers[i]);
            Set("cq", string.Join(",", cq));
            Set("cqc", string.Join(",", Enumerable.Repeat("0", COMBOS)));
            Set("rdeck", string.Join(",", races.Skip(COMBOS)));
            Set("pdeck", string.Join(",", powers.Skip(COMBOS)));

            RefreshScreens();
            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(Over);
        protected override List<PlayerData> GetGameWinners()
        {
            var set = Attr("winnerIds").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            return GameData.Players.Where(p => set.Contains(p.Id)).ToList();
        }

        private static void Shuffle<T>(IList<T> list, Random rnd)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }

        // ============================== queries ==============================
        private List<string> Combos => Attr("cq").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        private List<int> ComboCoins => Attr("cqc").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();

        private string RaceOf(string seat) => Attr("race:" + seat);
        private string PowerOf(string seat) => Attr("power:" + seat);
        private bool HasActive(string seat) => RaceOf(seat) != "";

        private string OwnerOf(int rid) => Attr("own:" + rid);
        private int TokensOn(int rid) => Num("tok:" + rid);
        private string DeclOwnerOf(int rid) => Attr("dwn:" + rid);
        private int DeclTokensOn(int rid) => Num("dtk:" + rid);
        private bool HasLostTribe(int rid) => Attr("lt:" + rid) == "1";

        private IEnumerable<Region> ActiveRegions(string seat) => Land.Where(r => OwnerOf(r.id) == seat);
        private IEnumerable<Region> DeclinedRegions(string seat) => Land.Where(r => DeclOwnerOf(r.id) == seat);

        /// <summary>Every token defending a region (active tokens, declined tokens or a Lost Tribe).</summary>
        private int Defenders(int rid) => TokensOn(rid) + DeclTokensOn(rid) + (HasLostTribe(rid) ? 1 : 0);

        /// <summary>Regions reachable for a conquest: adjacency (plus Underworld cavern links) or,
        /// for the FIRST conquest of a race, any board-edge region. Flying ignores all of it.</summary>
        private bool Reachable(string seat, Region target)
        {
            if (PowerOf(seat) == "flying") return true;
            var mine = ActiveRegions(seat).ToList();
            if (mine.Count == 0) return target.isBorder;

            if (mine.Any(m => m.adj.Contains(target.id))) return true;
            if (PowerOf(seat) == "underworld" && target.symbols.Contains("CAVERN")
                && mine.Any(m => m.symbols.Contains("CAVERN"))) return true;
            return false;
        }

        /// <summary>Tokens needed to take a region, with every v1 modifier applied (never below 1).</summary>
        private int Cost(string seat, Region t)
        {
            int c = 2 + Defenders(t.id);
            if (t.terrain == "MOUNTAIN") c += 1;
            var defender = OwnerOf(t.id);
            if (defender != "" && Attr("race:" + defender) == "trolls") c += 1;   // troll lair

            string race = RaceOf(seat), power = PowerOf(seat);
            if (power == "commando") c -= 1;
            if (power == "mounted" && (t.terrain == "FARM" || t.terrain == "HILL")) c -= 1;
            if (power == "underworld" && t.symbols.Contains("CAVERN")) c -= 1;
            if (race == "giants" && t.adj.Any(a => OwnerOf(a) == seat && ById.TryGetValue(a, out var n) && n.terrain == "MOUNTAIN")) c -= 1;
            if (race == "tritons" && t.adj.Any(a => ById.TryGetValue(a, out var n) && n.terrain == "WATER")) c -= 1;
            return Math.Max(1, c);
        }

        private bool CanConquer(string seat, Region t)
            => t.terrain != "WATER" && OwnerOf(t.id) != seat && DeclOwnerOf(t.id) != seat && Reachable(seat, t);

        // ============================== actions ==============================
        [GameAction]
        public async Task PickCombo(ExecuteActionData d)
        {
            var seat = d.Player!.Id;
            if (!MyTurn(seat) || Phase != "pick") { await Task.CompletedTask; return; }
            int idx = int.Parse(Arg(d, "idx"));
            var combos = Combos; var coins = ComboCoins;
            if (idx < 0 || idx >= combos.Count) { await Task.CompletedTask; return; }
            int price = idx;                                  // 1 coin onto each combo you skip
            if (Num("coins:" + seat) < price) { await Task.CompletedTask; return; }

            SaveUndoPoint();
            for (int i = 0; i < idx; i++) coins[i] += 1;
            Set("coins:" + seat, Num("coins:" + seat) - price);

            var parts = combos[idx].Split('|');
            Set("race:" + seat, parts[0]); Set("power:" + seat, parts[1]);
            // troops = race tokens + power tokens; the coins resting on the combo are MONEY
            Set("hand:" + seat, R(parts[0]).Tokens + P(parts[1]).Tokens);
            Set("coins:" + seat, Num("coins:" + seat) + coins[idx]);

            combos.RemoveAt(idx); coins.RemoveAt(idx);
            RefillQueue(combos, coins);
            Set("cq", string.Join(",", combos));
            Set("cqc", string.Join(",", coins));

            Log($"{Name(seat)} takes {R(parts[0]).Name} {P(parts[1]).Name}");
            Set("phase", "conquer");
            GameData.Attributes.Remove("firstConq");
            await Task.CompletedTask;
        }

        // Top the queue back up to six. The v1 pool is 10 races / 10 powers (the numeric-ability
        // subset), which a long 5-player game can exhaust — so when a deck runs dry it is rebuilt
        // from everything currently RETIRED (not on the queue and not on the board, active or in
        // decline). If even that is empty the queue simply stays short, exactly as it does in the
        // real game when the deck runs out; a seat facing an empty queue passes (see PassTurn).
        private void RefillQueue(List<string> combos, List<int> coins)
        {
            var rdeck = Attr("rdeck").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            var pdeck = Attr("pdeck").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            var rnd = new Random();

            while (combos.Count < COMBOS)
            {
                if (rdeck.Count == 0) rdeck = Recycle(RACES.Select(r => r.Key), combos, 0, rnd);
                if (pdeck.Count == 0) pdeck = Recycle(POWERS.Select(x => x.Key), combos, 1, rnd);
                if (rdeck.Count == 0 || pdeck.Count == 0) break;
                combos.Add(rdeck[0] + "|" + pdeck[0]);
                coins.Add(0);
                rdeck.RemoveAt(0); pdeck.RemoveAt(0);
            }
            Set("rdeck", string.Join(",", rdeck));
            Set("pdeck", string.Join(",", pdeck));
        }

        /// <summary>Keys of the given pool that are neither on the queue nor in any seat's hands.</summary>
        private List<string> Recycle(IEnumerable<string> all, List<string> combos, int part, Random rnd)
        {
            var used = combos.Select(c => c.Split('|')[part]).ToHashSet();
            foreach (var s in Order)
            {
                used.Add(part == 0 ? Attr("race:" + s) : Attr("power:" + s));
                used.Add(part == 0 ? Attr("drace:" + s) : Attr("dpower:" + s));
            }
            var free = all.Where(k => !used.Contains(k)).ToList();
            Shuffle(free, rnd);
            return free;
        }

        [GameAction]
        public async Task PassTurn(ExecuteActionData d)
        {
            // only legal when there is genuinely nothing to pick
            var seat = d.Player!.Id;
            if (!MyTurn(seat) || Phase != "pick" || Combos.Count > 0) { await Task.CompletedTask; return; }
            SaveUndoPoint();
            Log($"{Name(seat)} passes — no combos left to take");
            ScoreAndEndTurn(seat);
            await Task.CompletedTask;
        }

        [GameAction]
        public async Task Conquer(ExecuteActionData d)
        {
            var seat = d.Player!.Id;
            if (!MyTurn(seat) || Phase != "conquer") { await Task.CompletedTask; return; }
            int rid = int.Parse(Arg(d, "rid"));
            if (!ById.TryGetValue(rid, out var t) || !CanConquer(seat, t)) { await Task.CompletedTask; return; }

            int hand = Num("hand:" + seat);
            int cost = Cost(seat, t);
            bool useDie = hand < cost;
            if (useDie)
            {
                // the reinforcement die is only for the LAST conquest, once per turn, and only when
                // at least one token is left in hand
                if (hand < 1 || Attr("died:" + seat) == "1") { await Task.CompletedTask; return; }
            }

            SaveUndoPoint();
            int spend;
            if (useDie)
            {
                int[] faces = { 0, 0, 0, 1, 2, 3 };
                int roll = faces[new Random().Next(faces.Length)];
                Set("died:" + seat, "1");
                Set("dieRoll:" + seat, roll);
                if (hand + roll < cost)
                {
                    Log($"{Name(seat)} rolls {roll} — the attack on region {rid} fails");
                    Set("phase", "redeploy");
                    await Task.CompletedTask;
                    return;
                }
                spend = hand;                       // an assisted conquest commits everything left
                Log($"{Name(seat)} rolls {roll} and takes region {rid} with the last {spend}");
            }
            else
            {
                spend = cost;
            }

            bool occupied = Defenders(rid) > 0;
            RemoveDefenders(seat, t);
            Set("own:" + rid, seat);
            Set("tok:" + rid, spend);
            Set("hand:" + seat, hand - spend);
            Set("conq:" + seat, Num("conq:" + seat) + 1);
            if (occupied) Set("conqOcc:" + seat, Num("conqOcc:" + seat) + 1);
            Set("firstConq", "1");

            // Skeletons: +1 token from the pool per 2 occupied regions conquered this turn
            if (RaceOf(seat) == "skeletons")
            {
                int occ = Num("conqOcc:" + seat);
                int earned = occ / 2, already = Num("skelGain:" + seat);
                if (earned > already)
                {
                    Set("hand:" + seat, Num("hand:" + seat) + (earned - already));
                    Set("skelGain:" + seat, earned);
                }
            }

            if (!useDie) Log($"{Name(seat)} conquers region {rid} with {spend}");
            if (useDie || Num("hand:" + seat) == 0) Set("phase", "redeploy");
            await Task.CompletedTask;
        }

        /// <summary>Throw the previous holder out: they lose one token, the rest go back to their hand
        /// and are auto-spread over their remaining regions (the official "retreat" simplification).</summary>
        private void RemoveDefenders(string attacker, Region t)
        {
            GameData.Attributes.Remove("lt:" + t.id);          // Lost Tribe tokens are removed for good

            var prev = OwnerOf(t.id);
            int n = TokensOn(t.id);
            GameData.Attributes.Remove("own:" + t.id);
            GameData.Attributes.Remove("tok:" + t.id);
            if (prev != "" && n > 0)
            {
                int back = Attr("race:" + prev) == "elves" ? n : Math.Max(0, n - 1);   // Elves lose nothing
                AutoRedeploy(prev, back);
                Log($"{Name(prev)} is driven out of region {t.id}" + (back > 0 ? $" and redeploys {back}" : ""));
            }
            // tokens of a race IN DECLINE are simply removed
            var dprev = DeclOwnerOf(t.id);
            if (dprev != "")
            {
                GameData.Attributes.Remove("dwn:" + t.id);
                GameData.Attributes.Remove("dtk:" + t.id);
            }
        }

        /// <summary>Spread n tokens over a seat's existing active regions, border regions first
        /// (used for the defender's forced retreat, and by the AI).</summary>
        private void AutoRedeploy(string seat, int n)
        {
            var mine = ActiveRegions(seat).OrderByDescending(r => r.isBorder ? 1 : 0)
                                         .ThenBy(r => TokensOn(r.id)).ToList();
            if (mine.Count == 0) return;                       // wiped off the board — tokens are lost
            for (int i = 0; i < n; i++)
            {
                var r = mine.OrderBy(x => TokensOn(x.id)).ThenByDescending(x => x.isBorder ? 1 : 0).First();
                Set("tok:" + r.id, TokensOn(r.id) + 1);
            }
        }

        [GameAction]
        public async Task GoIntoDecline(ExecuteActionData d)
        {
            var seat = d.Player!.Id;
            if (!MyTurn(seat) || Phase != "conquer" || !HasActive(seat)
                || Attr("firstConq") == "1") { await Task.CompletedTask; return; }
            SaveUndoPoint();

            // the race already in decline vanishes from the board
            foreach (var r in DeclinedRegions(seat).ToList())
            {
                GameData.Attributes.Remove("dwn:" + r.id);
                GameData.Attributes.Remove("dtk:" + r.id);
            }
            // the active race flips: exactly one token stays in each of its regions
            foreach (var r in ActiveRegions(seat).ToList())
            {
                GameData.Attributes.Remove("own:" + r.id);
                GameData.Attributes.Remove("tok:" + r.id);
                Set("dwn:" + r.id, seat);
                Set("dtk:" + r.id, 1);
            }
            Set("drace:" + seat, RaceOf(seat));
            Set("dpower:" + seat, PowerOf(seat));
            Set("race:" + seat, ""); Set("power:" + seat, "");
            Set("hand:" + seat, 0);
            Log($"{Name(seat)} puts {R(Attr("drace:" + seat)).Name} into decline");

            ScoreAndEndTurn(seat);
            await Task.CompletedTask;
        }

        [GameAction]
        public async Task DoneConquering(ExecuteActionData d)
        {
            var seat = d.Player!.Id;
            if (!MyTurn(seat) || Phase != "conquer") { await Task.CompletedTask; return; }
            SaveUndoPoint();
            Set("phase", "redeploy");
            await Task.CompletedTask;
        }

        [GameAction]
        public async Task DropToken(ExecuteActionData d)
        {
            var seat = d.Player!.Id;
            if (!MyTurn(seat) || Phase != "redeploy") { await Task.CompletedTask; return; }
            int rid = int.Parse(Arg(d, "rid"));
            if (OwnerOf(rid) != seat || Num("hand:" + seat) <= 0) { await Task.CompletedTask; return; }
            SaveUndoPoint();
            Set("tok:" + rid, TokensOn(rid) + 1);
            Set("hand:" + seat, Num("hand:" + seat) - 1);
            await Task.CompletedTask;
        }

        [GameAction]
        public async Task TakeToken(ExecuteActionData d)
        {
            // pick a token back UP during redeploy, as long as one stays behind
            var seat = d.Player!.Id;
            if (!MyTurn(seat) || Phase != "redeploy") { await Task.CompletedTask; return; }
            int rid = int.Parse(Arg(d, "rid"));
            if (OwnerOf(rid) != seat || TokensOn(rid) <= 1) { await Task.CompletedTask; return; }
            SaveUndoPoint();
            Set("tok:" + rid, TokensOn(rid) - 1);
            Set("hand:" + seat, Num("hand:" + seat) + 1);
            await Task.CompletedTask;
        }

        [GameAction]
        public async Task DoneRedeploy(ExecuteActionData d)
        {
            var seat = d.Player!.Id;
            if (!MyTurn(seat) || Phase != "redeploy") { await Task.CompletedTask; return; }
            SaveUndoPoint();
            // leftover tokens that could not be placed (no regions) simply go back to the pool
            AutoRedeploy(seat, Num("hand:" + seat));
            Set("hand:" + seat, 0);
            ScoreAndEndTurn(seat);
            await Task.CompletedTask;
        }

        // ============================== scoring & turn order ==============================
        private int TurnScore(string seat)
        {
            var act = ActiveRegions(seat).ToList();
            var dec = DeclinedRegions(seat).ToList();
            int pts = act.Count + dec.Count;                                   // 1 per region held

            string race = RaceOf(seat), power = PowerOf(seat);
            string drace = Attr("drace:" + seat);

            // Dwarves keep scoring their mines while in decline (their printed exception)
            if (race == "dwarves") pts += act.Count(r => r.symbols.Contains("MINE"));
            if (drace == "dwarves") pts += dec.Count(r => r.symbols.Contains("MINE"));

            if (race == "humans") pts += act.Count(r => r.terrain == "FARM");
            if (race == "wizards") pts += act.Count(r => r.symbols.Contains("MAGIC"));
            if (race == "orcs") pts += Num("conqOcc:" + seat);

            switch (power)
            {
                case "alchemist": pts += 2; break;
                case "forest": pts += act.Count(r => r.terrain == "FOREST"); break;
                case "hill": pts += act.Count(r => r.terrain == "HILL"); break;
                case "swamp": pts += act.Count(r => r.terrain == "SWAMP"); break;
                case "merchant": pts += act.Count; break;
                case "pillaging": pts += Num("conqOcc:" + seat); break;
            }
            return pts;
        }

        private void ScoreAndEndTurn(string seat)
        {
            int pts = TurnScore(seat);
            Set("coins:" + seat, Num("coins:" + seat) + pts);
            Log($"{Name(seat)} scores {pts} (total {Num("coins:" + seat)})");

            // reset per-turn counters
            Set("conq:" + seat, 0); Set("conqOcc:" + seat, 0);
            GameData.Attributes.Remove("died:" + seat);
            GameData.Attributes.Remove("dieRoll:" + seat);
            GameData.Attributes.Remove("skelGain:" + seat);
            GameData.Attributes.Remove("firstConq");

            var order = Order;
            int idx = order.IndexOf(seat);
            bool wrap = idx == order.Count - 1;
            var next = order[(idx + 1) % order.Count];

            if (wrap)
            {
                int round = Num("round") + 1;
                if (round > MaxRounds) { FinishGame(); return; }
                Set("round", round);
            }
            GameData.CurrentTurnId = next;
            Set("phase", HasActive(next) ? "conquer" : "pick");
        }

        private void FinishGame()
        {
            Set("over", "1");
            var order = Order;
            int best = order.Max(s => Num("coins:" + s));
            var winners = order.Where(s => Num("coins:" + s) == best).ToList();
            Set("winnerIds", string.Join(",", winners));
            Set("result", winners.Count == 1
                ? $"{Name(winners[0])} wins with {best} coins"
                : "Tie at " + best + " coins: " + string.Join(", ", winners.Select(Name)));
            Set("phase", "over");
        }

        private void Log(string line)
        {
            var log = Attr("log");
            var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            lines.Add(line);
            if (lines.Count > 12) lines = lines.Skip(lines.Count - 12).ToList();
            Set("log", string.Join("\n", lines));
        }

        // ============================== AI ==============================
        public override async Task<bool> PlayAI(PlayerData player, Random rnd)
        {
            var seat = player.Id;
            if (!MyTurn(seat)) return false;

            switch (Phase)
            {
                case "pick":
                    {
                        // the affordable combo offering the most tokens (+ coins already on it)
                        var combos = Combos; var coins = ComboCoins;
                        if (combos.Count == 0)
                        {
                            await PassTurn(new ExecuteActionData
                            { actionId = nameof(PassTurn), gameId = GameData.Id, playerId = seat, Player = player });
                            return true;
                        }
                        int purse = Num("coins:" + seat);
                        int bestIdx = 0, bestVal = int.MinValue;
                        for (int i = 0; i < combos.Count; i++)
                        {
                            if (i > purse) break;
                            var p = combos[i].Split('|');
                            int val = R(p[0]).Tokens + P(p[1]).Tokens + coins[i] - i;
                            if (val > bestVal) { bestVal = val; bestIdx = i; }
                        }
                        await PickCombo(new ExecuteActionData
                        {
                            actionId = nameof(PickCombo), gameId = GameData.Id, playerId = seat,
                            args = new() { ["idx"] = bestIdx.ToString() }, Player = player
                        });
                        return true;
                    }
                case "conquer":
                    {
                        // go into decline when fewer than two conquests are affordable
                        var targets = Land.Where(r => CanConquer(seat, r)).ToList();
                        int hand = Num("hand:" + seat);
                        var affordable = targets.Where(r => Cost(seat, r) <= hand).ToList();
                        if (HasActive(seat) && Attr("firstConq") != "1" && affordable.Count < 2)
                        {
                            await GoIntoDecline(new ExecuteActionData
                            { actionId = nameof(GoIntoDecline), gameId = GameData.Id, playerId = seat, Player = player });
                            return true;
                        }
                        if (affordable.Count > 0)
                        {
                            // cheapest first, then the most valuable region for our bonuses
                            var pick = affordable.OrderBy(r => Cost(seat, r))
                                                 .ThenByDescending(r => RegionValue(seat, r)).First();
                            await Conquer(new ExecuteActionData
                            {
                                actionId = nameof(Conquer), gameId = GameData.Id, playerId = seat,
                                args = new() { ["rid"] = pick.id.ToString() }, Player = player
                            });
                            return true;
                        }
                        // nothing affordable: try the die on the cheapest target, else stop
                        var stretch = targets.OrderBy(r => Cost(seat, r)).FirstOrDefault();
                        if (stretch != null && hand > 0 && Attr("died:" + seat) != "1"
                            && Cost(seat, stretch) - hand <= 3)
                        {
                            await Conquer(new ExecuteActionData
                            {
                                actionId = nameof(Conquer), gameId = GameData.Id, playerId = seat,
                                args = new() { ["rid"] = stretch.id.ToString() }, Player = player
                            });
                            return true;
                        }
                        await DoneConquering(new ExecuteActionData
                        { actionId = nameof(DoneConquering), gameId = GameData.Id, playerId = seat, Player = player });
                        return true;
                    }
                case "redeploy":
                    {
                        AutoRedeploy(seat, Num("hand:" + seat));
                        Set("hand:" + seat, 0);
                        await DoneRedeploy(new ExecuteActionData
                        { actionId = nameof(DoneRedeploy), gameId = GameData.Id, playerId = seat, Player = player });
                        return true;
                    }
            }
            return false;
        }

        /// <summary>How much a region is worth to this seat: 1 + how well it fits our bonuses.</summary>
        private int RegionValue(string seat, Region r)
        {
            int v = 1;
            string race = RaceOf(seat), power = PowerOf(seat);
            if (race == "dwarves" && r.symbols.Contains("MINE")) v += 2;
            if (race == "wizards" && r.symbols.Contains("MAGIC")) v += 2;
            if (race == "humans" && r.terrain == "FARM") v += 2;
            if (power == "forest" && r.terrain == "FOREST") v += 2;
            if (power == "hill" && r.terrain == "HILL") v += 2;
            if (power == "swamp" && r.terrain == "SWAMP") v += 2;
            if (power == "underworld" && r.symbols.Contains("CAVERN")) v += 2;
            if (r.terrain == "MOUNTAIN") v += 1;                    // easier to hold
            if (!r.isBorder) v += 1;                                 // interior regions are safer
            return v;
        }

        // ============================== rendering ==============================
        protected override void RefreshScreens() { Render(); BuildPanels(); }

        private void Render()
        {
            GameData.Table = ItemData.Table();
            foreach (var p in GameData.Players)
            { p.Hand = new ItemData("", null) { Name = "HAND" }; p.Table = new ItemData("", null) { Name = "TABLE" }; }

            string cur = GameData.CurrentTurnId ?? "";
            double mh = MapH;

            // ---- the in-scene ("3D") panel -------------------------------------------------
            // NOTE: this game sets NOTHING for the control panel. The panel is drawn in the scene
            // by the client for every game, positioned as a HUD in the camera's view, so there is
            // no per-game placement to configure here. BuildPanels() below still describes the
            // panel exactly as before — the description is the server's job, the drawing is not.
            // (A game that later wants its panel standing on the table can send panel3dAnchor /
            // panel3dRot / panel3dWidth and the client will place it there instead.)

            // felt + the board itself (ONE token item; regions are server data)
            addItem(Assets.MAT).SetPosition(0, -0.4, 0).SetScale(MAPW + 10, 0.3, mh + 10)
                .AddAttribute("tint", "0x14281c");
            addItem(Img($"map_{MapPlayers}.jpg")).SetPosition(0, 0, 0).SetScale(MAPW, 1, mh)
                .AddAttribute("board", "1");

            // Camera pulls back far enough to frame the whole board for every seat.
            int camY = (int)Math.Round(mh * 0.95 + 8);
            int camZ = (int)Math.Round(mh * 0.55 + 4);
            int camX = 0;                            // SetCameraPosition takes whole world units
            foreach (var p in GameData.Players) p.SetCameraPosition(camX, camY, camZ);
            GameData.Observer.Position.Set(camX, camY + 4, camZ + 3);

            // token stacks + lost tribes
            foreach (var r in Land)
            {
                var (x, z) = WorldOf(r);
                var owner = OwnerOf(r.id);
                int n = TokensOn(r.id);
                if (owner != "" && n > 0) Stack(x, z, n, Col(owner), false);
                var d = DeclOwnerOf(r.id);
                if (d != "") Stack(x + 0.9, z, DeclTokensOn(r.id), Col(d), true);
                if (HasLostTribe(r.id)) Stack(x - 0.9, z, 1, "0x9ca3af", true);
            }

            // clickable markers for whatever the current seat may do right now
            if (!Over && cur != "")
            {
                if (Phase == "conquer")
                {
                    int hand = Num("hand:" + cur);
                    foreach (var r in Land.Where(r => CanConquer(cur, r)))
                    {
                        int cost = Cost(cur, r);
                        bool afford = hand >= cost;
                        bool stretch = !afford && hand > 0 && Attr("died:" + cur) != "1";
                        if (!afford && !stretch) continue;
                        var (x, z) = WorldOf(r);
                        var mk = addItem(Assets.MARKER).SetPosition(x, 0.12, z).SetScale(1.5, 0.14, 1.5)
                            .AddAttribute("tint", afford ? "0x22c55e" : "0xf59e0b").AddAttribute("marker", "1")
                            .AddAttribute("rid", r.id.ToString());
                        mk.ClickActions[cur] = nameof(Conquer);
                        addTextItem(Assets.TEXT).SetText(cost.ToString()).SetPosition(x, 0.3, z + 1.4)
                            .SetScale(0.55).SetRotation(-90, 0, 0)
                            .AddAttribute("textColor", afford ? "bbf7d0" : "fed7aa");
                    }
                }
                else if (Phase == "redeploy")
                {
                    foreach (var r in ActiveRegions(cur))
                    {
                        var (x, z) = WorldOf(r);
                        var mk = addItem(Assets.MARKER).SetPosition(x, 0.12, z).SetScale(1.3, 0.14, 1.3)
                            .AddAttribute("tint", Num("hand:" + cur) > 0 ? "0x38bdf8" : "0x64748b")
                            .AddAttribute("marker", "1").AddAttribute("rid", r.id.ToString());
                        mk.ClickActions[cur] = Num("hand:" + cur) > 0 ? nameof(DropToken) : nameof(TakeToken);
                    }
                }
            }

            // headline + score row, laid out relative to the board so they never overlap it
            double headZ = -mh / 2 - 4;
            string head = Over ? Attr("result") : Phase switch
            {
                "pick" => $"SMALL WORLD · round {Num("round")}/{MaxRounds} · {Name(cur)} picks a combo",
                "conquer" => $"{Name(cur)} conquers — {Num("hand:" + cur)} tokens in hand",
                "redeploy" => $"{Name(cur)} redeploys — {Num("hand:" + cur)} left",
                _ => "SMALL WORLD"
            };
            addTextItem(Assets.TEXT).SetText(head).SetPosition(0, 6, headZ).SetScale(1.0)
                .SetRotation(-90, 0, 0).AddAttribute("textColor", "ffd166");

            var order = Order;
            for (int i = 0; i < order.Count; i++)
            {
                var s = order[i];
                string combo = HasActive(s) ? $"{R(RaceOf(s)).Name} {P(PowerOf(s)).Name}" : "(in decline)";
                addTextItem(Assets.TEXT).SetText($"{Name(s)} {Num("coins:" + s)}c · {combo}")
                    .SetPosition(-((order.Count - 1) * 9.5) / 2 + i * 9.5, 6, headZ + 2.6).SetScale(0.55)
                    .SetRotation(-90, 0, 0).AddAttribute("textColor", s == cur ? "ffd166" : "cbd5e1");
            }

            // the reinforcement die, shown once it has been rolled this turn
            if (cur != "" && Attr("died:" + cur) == "1")
                addItem(Assets.DIE).SetPosition(MAPW / 2 + 3, 0.6, headZ + 2).SetScale(1.4)
                    .AddAttribute("result", Attr("dieRoll:" + cur)).AddAttribute("sides", "6");

            void Stack(double x, double z, int n, string tint, bool flat)
            {
                for (int i = 0; i < Math.Min(n, 6); i++)
                    addItem(Assets.TOKEN).SetPosition(x, 0.16 + i * 0.16, z)
                        .SetScale(0.62, flat ? 0.09 : 0.14, 0.62).AddAttribute("tint", tint);
                if (n > 1)
                    addTextItem(Assets.TEXT).SetText(n.ToString()).SetPosition(x, 1.5, z)
                        .SetScale(0.5).SetRotation(-90, 0, 0).AddAttribute("textColor", "ffffff");
            }
        }

        // ============================== the 2D panel ==============================
        private void BuildPanels()
        {
            GameData.Attributes["panelMode"] = Phase == "pick" && !Over ? "full" : "side";
            foreach (var seat in GameData.Players)
            {
                if (seat.Type == PlayerTypeEnum.EMPTY_SEAT || seat.Type == PlayerTypeEnum.AI) { seat.Screen = null; continue; }
                seat.Screen = SeatPanel(seat.Id);
            }
        }

        private List<UiNode> SeatPanel(string id)
        {
            var col = new List<UiNode>();
            string cur = GameData.CurrentTurnId ?? "";

            if (Over)
            {
                bool won = Attr("winnerIds").Split(',').Contains(id);
                col.Add(UiNode.Banner(Attr("result"), won ? "win" : "lose"));
                col.Add(ScoreBlock());
                col.Add(UiNode.Log(Attr("log")));
                return col;
            }

            col.Add(UiNode.Title($"Round {Num("round")} / {MaxRounds}"));
            col.Add(ScoreBlock());

            if (id != cur)
            {
                col.Add(UiNode.Note($"Waiting for {Name(cur)}…"));
                col.Add(MyStuff(id));
                col.Add(UiNode.Log(Attr("log")));
                return col;
            }

            switch (Phase)
            {
                case "pick":
                    {
                        var combos = Combos; var coins = ComboCoins;
                        if (combos.Count == 0)
                        {
                            col.Add(UiNode.Text_("No race/power combinations are left to take.", "cbd5e1"));
                            col.Add(UiNode.Button("Pass turn", nameof(PassTurn), style: "primary"));
                            break;
                        }
                        col.Add(UiNode.Text_("Choose a race + special power. Taking the Nth combo costs "
                                             + "N−1 coins, one onto each combo you skip.", "cbd5e1"));
                        int purse = Num("coins:" + id);
                        for (int i = 0; i < combos.Count; i++)
                        {
                            var p = combos[i].Split('|');
                            var race = R(p[0]); var pow = P(p[1]);
                            int price = i;
                            bool can = purse >= price;
                            var row = UiNode.Row(
                                UiNode.Image($"smallworld/races/{race.Key}.png", 54),
                                UiNode.Image($"smallworld/powers/{pow.Key}.png", 54),
                                UiNode.Col(
                                    UiNode.Text_($"{pow.Name} {race.Name}", "ffffff", 16, "big"),
                                    UiNode.Text_($"{race.Tokens + pow.Tokens} tokens"
                                                 + (coins[i] > 0 ? $" · {coins[i]} coin(s) on it" : ""), "94a3b8", 13),
                                    UiNode.Text_(race.Text, "cbd5e1", 12),
                                    UiNode.Text_(pow.Text, "cbd5e1", 12)),
                                UiNode.Button(price == 0 ? "Take (free)" : $"Take (−{price})",
                                              nameof(PickCombo), new() { ["idx"] = i.ToString() },
                                              style: can ? "ok" : "no"));
                            col.Add(row);
                        }
                        break;
                    }
                case "conquer":
                    {
                        col.Add(MyStuff(id));
                        int hand = Num("hand:" + id);
                        col.Add(UiNode.Text_(hand > 0
                            ? $"Click a green marker to conquer ({hand} tokens in hand). Amber = only the die can help."
                            : "No tokens left in hand.", "cbd5e1"));
                        if (HasActive(id) && Attr("firstConq") != "1")
                            col.Add(UiNode.Button("Put race into decline", nameof(GoIntoDecline),
                                style: "no", confirm: "Flip your race into decline and end your turn?"));
                        col.Add(UiNode.Button("Done conquering →", nameof(DoneConquering), style: "primary"));
                        break;
                    }
                case "redeploy":
                    {
                        col.Add(MyStuff(id));
                        col.Add(UiNode.Text_(Num("hand:" + id) > 0
                            ? "Click your regions on the board to place the tokens you have left."
                            : "Click a region with 2+ tokens to pick one back up.", "cbd5e1"));
                        col.Add(UiNode.Text_($"This turn would score {TurnScore(id)} coins.", "ffd166", 14));
                        col.Add(UiNode.Button("End turn", nameof(DoneRedeploy), style: "ok"));
                        break;
                    }
            }
            col.Add(UiNode.Log(Attr("log")));
            return col;
        }

        private UiNode ScoreBlock()
        {
            var kids = new List<UiNode>();
            foreach (var s in Order)
                kids.Add(UiNode.Chip($"{Name(s)} {Num("coins:" + s)}c", ColHex(s),
                                     ColHex(s) == "f2f2f2" ? "111111" : "ffffff"));
            return UiNode.Row(kids.ToArray());
        }

        private UiNode MyStuff(string id)
        {
            var kids = new List<UiNode>();
            if (HasActive(id))
            {
                var r = R(RaceOf(id)); var p = P(PowerOf(id));
                kids.Add(UiNode.Row(
                    UiNode.Image($"smallworld/races/{r.Key}.png", 44),
                    UiNode.Image($"smallworld/powers/{p.Key}.png", 44),
                    UiNode.Col(UiNode.Text_($"{p.Name} {r.Name}", "ffffff", 15, "big"),
                               UiNode.Text_($"{Num("hand:" + id)} in hand · "
                                            + $"{ActiveRegions(id).Count()} regions", "94a3b8", 13))));
            }
            if (Attr("drace:" + id) != "")
            {
                var dr = R(Attr("drace:" + id));
                kids.Add(UiNode.Row(
                    UiNode.Image($"smallworld/races/{dr.Key}_declined.png", 34),
                    UiNode.Text_($"{dr.Name} in decline · {DeclinedRegions(id).Count()} regions", "94a3b8", 13)));
            }
            if (kids.Count == 0) kids.Add(UiNode.Note("No race yet — pick a combo."));
            return UiNode.Col(kids.ToArray());
        }
    }
}
