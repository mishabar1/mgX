using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Catan — full base rules, 3D board (2..4 players).
    //   * 19 land hexes (official terrain mix), number chips on the official spiral, robber.
    //   * settlements / cities on intersections (distance rule), roads on edges, longest road (>=5),
    //     dev cards (14 knights / 5 VP / 2 road-building / 2 year-of-plenty / 2 monopoly),
    //     largest army (>=3), bank 4:1 + ports 3:1 / 2:1. First to 10 VP on their turn wins.
    //   * Trading with the bank/ports: click one of YOUR resource tiles (give), then a BANK tile
    //     (receive) — the server applies your best rate. Player-to-player trading: not yet.
    //   * The 2D panel is used only for the "rare" moments (discard on 7, pick a steal victim,
    //     year-of-plenty / monopoly picks) — everything else is clickable 3D, Carcassonne-style.
    //   * Pragmatic secrecy: hands/devs are in the broadcast state (hidden in UI only).
    public class CatanGameFlow : BaseGameFlow
    {
        private const double SZ = 3.0;                       // hex circumradius (world units)
        private static readonly string[] RES = { "wood", "sheep", "wheat", "brick", "ore" };
        private static readonly string[] RESNAME = { "Wood", "Sheep", "Wheat", "Brick", "Ore" };
        private static readonly string[] DEVNAME = { "Knight", "Victory Point", "Road Building", "Year of Plenty", "Monopoly" };
        private const int DKNIGHT = 0, DVP = 1, DROAD = 2, DYOP = 3, DMONO = 4;
        // classic player colours: red / blue / white / orange
        private static readonly string[] PCOL = { "0xd94444", "0x2b6fd9", "0xf2f2f2", "0xe8a33d" };

        public override int MinPlayers => 3;
        public CatanGameFlow(GameData gameData) : base(gameData) { gameData.GameType = GameTypeEnum.CATAN; }
        private static string Arg(ExecuteActionData d, string key)
            => d.args != null && d.args.TryGetValue(key, out var v) ? v : (d.Item?.GetStringAttribute(key) ?? "");

        internal static class Assets
        {
            internal static AssetData TEXT   => new Text3dAssetData("cat");
            internal static AssetData MARKER => new CylinderAssetData("catmark");
            internal static AssetData PIECE  => new CylinderAssetData("catpiece");
            internal static AssetData MAT    => new CylinderAssetData("catmat");
            internal static AssetData DIE    => new DieAssetData("catdie");
        }
        private AssetData Img(string f) => addAsset(new TokenAssetData("catan/" + f));

        // ============================ board geometry (computed once) ============================
        // Pointy-top hexes on an axial grid; corner/edge ids are their rounded world coordinates,
        // so adjacency is pure geometry and needs no bespoke index math.
        private static readonly List<(int q, int r)> HEXES = new();
        private static readonly List<(double x, double z)> HEXPOS = new();
        private static readonly List<string[]> HEXC = new();                       // hex -> its 6 corner keys
        private static readonly Dictionary<string, (double x, double z)> CPOS = new();
        private static readonly Dictionary<string, List<int>> CHEX = new();        // corner -> touching hexes
        private static readonly Dictionary<string, List<string>> CADJ = new();     // corner -> neighbour corners
        private static readonly Dictionary<string, List<string>> CEDGE = new();    // corner -> its edges
        private static readonly List<string> EDGES = new();
        private static readonly Dictionary<string, (string a, string b)> EC = new();
        private static readonly List<(string a, string b)> PORTSPOTS = new();      // 9 coastal corner pairs
        private static readonly List<(double x, double z)> SEA = new();            // decorative ring-3 sea hexes
        private static readonly List<int> SPIRAL = new();                          // hex indices, outside-in

        private static string K(double x, double z)
            => x.ToString("0.0", CultureInfo.InvariantCulture) + "_" + z.ToString("0.0", CultureInfo.InvariantCulture);

        static CatanGameFlow()
        {
            // FLAT-TOP hexes (matching the scanned tile art): corners at 0°,60°,…,300°
            for (int q = -2; q <= 2; q++) for (int r = -2; r <= 2; r++)
                if (Math.Abs(q + r) <= 2) { HEXES.Add((q, r)); HEXPOS.Add((SZ * 1.5 * q, SZ * Math.Sqrt(3) * (r + q / 2.0))); }

            for (int i = 0; i < HEXES.Count; i++)
            {
                var cs = new string[6];
                for (int k = 0; k < 6; k++)
                {
                    double a = Math.PI / 180 * (60 * k);
                    double x = HEXPOS[i].x + SZ * Math.Cos(a), z = HEXPOS[i].z + SZ * Math.Sin(a);
                    var key = K(x, z); cs[k] = key;
                    if (!CPOS.ContainsKey(key)) { CPOS[key] = (x, z); CHEX[key] = new(); CADJ[key] = new(); CEDGE[key] = new(); }
                    if (!CHEX[key].Contains(i)) CHEX[key].Add(i);
                }
                HEXC.Add(cs);
                for (int k = 0; k < 6; k++)
                {
                    string a = cs[k], b = cs[(k + 1) % 6];
                    string e = string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
                    if (!EC.ContainsKey(e))
                    {
                        EDGES.Add(e); EC[e] = (a, b);
                        CEDGE[a].Add(e); CEDGE[b].Add(e);
                        CADJ[a].Add(b); CADJ[b].Add(a);
                    }
                }
            }

            // number-chip spiral: outer ring, middle ring, centre — each ring walked by angle
            int Ring((int q, int r) h) { int s = -h.q - h.r; return Math.Max(Math.Abs(h.q), Math.Max(Math.Abs(h.r), Math.Abs(s))); }
            foreach (var ring in new[] { 2, 1, 0 })
                SPIRAL.AddRange(Enumerable.Range(0, HEXES.Count).Where(i => Ring(HEXES[i]) == ring)
                    .OrderBy(i => Math.Atan2(HEXPOS[i].z, HEXPOS[i].x)));

            // 9 ports on the coast: coastal corners form a 30-corner loop; official-ish spacing
            var coast = CPOS.Keys.Where(c => CHEX[c].Count < 3)
                                 .OrderBy(c => Math.Atan2(CPOS[c].z, CPOS[c].x)).ToList();
            foreach (var s in new[] { 0, 3, 7, 10, 13, 17, 20, 23, 27 })
            {
                int i = s;
                // make sure the pair is really edge-adjacent (angle sort is almost always right)
                while (!CADJ[coast[i % coast.Count]].Contains(coast[(i + 1) % coast.Count])) i++;
                PORTSPOTS.Add((coast[i % coast.Count], coast[(i + 1) % coast.Count]));
            }

            for (int q = -3; q <= 3; q++) for (int r = -3; r <= 3; r++)
            {
                int s = -q - r;
                if (Math.Max(Math.Abs(q), Math.Max(Math.Abs(r), Math.Abs(s))) == 3)
                    SEA.Add((SZ * 1.5 * q, SZ * Math.Sqrt(3) * (r + q / 2.0)));
            }
        }

        // ============================ lifecycle ============================
        protected override Task Create()
        {
            addAsset(Assets.TEXT); addAsset(Assets.MARKER); addAsset(Assets.PIECE); addAsset(Assets.MAT);
            foreach (var t in new[] { "wood", "sheep", "wheat", "brick", "ore", "desert", "sea" }) Img($"hex_{t}.png");
            foreach (var n in new[] { 2, 3, 4, 5, 6, 8, 9, 10, 11, 12 }) Img($"chip_{n}.png");
            foreach (var p in new[] { "any", "wood", "sheep", "wheat", "brick", "ore" }) Img($"port_{p}.png");
            foreach (var r in RES) Img($"res_{r}.svg");
            Img("robber.svg"); Img("dev_back.svg");

            GameData.Observer.Position.Set(0, 34, 24);
            var pos = new (int x, int z)[] { (0, 22), (0, -22), (22, 0), (-22, 0) };
            for (int i = 0; i < 4; i++)
                new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                    .AddAttribute("type", "p" + (i + 1))
                    .SetCameraPosition((int)Math.Round(pos[i].x * 1.15), 30, (int)Math.Round(pos[i].z * 1.15))
                    .SetAvatarPosition(pos[i].x, 0, pos[i].z);
            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        protected override Task StartGame()
        {
            var rnd = new Random();
            var seats = GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT).Select(p => p.Id).ToList();
            GameData.Attributes["order"] = string.Join(",", seats);

            // terrain + the official number spiral (desert skipped, robber starts there)
            var terr = new List<string>();
            terr.AddRange(Enumerable.Repeat("wood", 4)); terr.AddRange(Enumerable.Repeat("sheep", 4));
            terr.AddRange(Enumerable.Repeat("wheat", 4)); terr.AddRange(Enumerable.Repeat("brick", 3));
            terr.AddRange(Enumerable.Repeat("ore", 3)); terr.Add("desert");
            Shuffle(terr, rnd);
            var nums = new Queue<int>(new[] { 5, 2, 6, 3, 8, 10, 9, 12, 11, 4, 8, 10, 9, 4, 5, 6, 3, 11 });
            for (int i = 0; i < HEXES.Count; i++) GameData.Attributes["terr:" + i] = terr[i];
            foreach (var i in SPIRAL)
                if (terr[i] == "desert") GameData.Attributes["robber"] = i.ToString();
                else GameData.Attributes["num:" + i] = nums.Dequeue().ToString();

            // ports: 4x 3:1 + one 2:1 per resource, shuffled onto the 9 spots
            var kinds = new List<string> { "any", "any", "any", "any", "wood", "sheep", "wheat", "brick", "ore" };
            Shuffle(kinds, rnd);
            for (int i = 0; i < PORTSPOTS.Count; i++) GameData.Attributes["port:" + i] = kinds[i];

            // dev deck
            var deck = new List<int>();
            deck.AddRange(Enumerable.Repeat(DKNIGHT, 14)); deck.AddRange(Enumerable.Repeat(DVP, 5));
            deck.AddRange(Enumerable.Repeat(DROAD, 2)); deck.AddRange(Enumerable.Repeat(DYOP, 2)); deck.AddRange(Enumerable.Repeat(DMONO, 2));
            Shuffle(deck, rnd);
            GameData.Attributes["deck"] = string.Join(",", deck);

            foreach (var s in seats)
            {
                SetInts("res:" + s, new int[5]); SetInts("dev:" + s, new int[5]); SetInts("devnew:" + s, new int[5]);
                GameData.Attributes["army:" + s] = "0";
            }
            GameData.Attributes["phase"] = "setup"; GameData.Attributes["setupstep"] = "s";
            var q = seats.Concat(Enumerable.Reverse(seats)).ToList();       // snake order
            GameData.Attributes["setupq"] = string.Join(",", q);
            GameData.CurrentTurnId = q[0];
            GameData.Attributes["log"] = "";
            foreach (var k in new[] { "lroadOwner", "larmyOwner", "over", "lastRoll" }) GameData.Attributes.Remove(k);

            // own stuff (resource row) sits on the sea mat just in front of each seat
            GameData.Attributes["tableAnchor"] = "0,0.25,5";
            GameData.Attributes["handAnchor"] = "0,0.25,2";
            Render();
            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(GameData.Attributes.ContainsKey("over"));
        protected override List<PlayerData> GetGameWinners()
        {
            var set = Attr("winnerIds").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            return GameData.Players.Where(p => set.Contains(p.Id)).ToList();
        }

        // ============================ state helpers ============================
        private static void Shuffle<T>(List<T> list, Random rnd)
        { for (int i = list.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); } }

        private string Attr(string k) => GameData.Attributes.GetValueOrDefault(k, "");
        private void Set(string k, string v) => GameData.Attributes[k] = v;
        private int[] Ints(string k) => Attr(k).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).DefaultIfEmpty(0).ToArray() is var a && a.Length == 5 ? a : new int[5];
        private void SetInts(string k, int[] v) => Set(k, string.Join(",", v));
        private List<string> ListAttr(string k) => Attr(k).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        private string Phase => Attr("phase");
        private List<string> Order => ListAttr("order");
        private string Name(string id) => GameData.Players.FirstOrDefault(p => p.Id == id)?.Name ?? "?";
        private string Col(string seat) => PCOL[Math.Max(0, Order.IndexOf(seat)) % PCOL.Length];
        private void Log(string line) { var c = Attr("log"); Set("log", string.IsNullOrEmpty(c) ? line : c + "\n" + line); }
        private bool IsAISeat(string id) => GameData.Players.FirstOrDefault(p => p.Id == id)?.Type == PlayerTypeEnum.AI;

        private (string owner, bool city)? BuildingAt(string corner)
        {
            var v = Attr("b:" + corner);
            if (string.IsNullOrEmpty(v)) return null;
            var p = v.Split(':'); return (p[0], p[1] == "c");
        }
        private string RoadAt(string edge) => Attr("r:" + edge);
        private IEnumerable<string> SeatCorners(string seat) => CPOS.Keys.Where(c => BuildingAt(c)?.owner == seat);
        private IEnumerable<string> SeatRoads(string seat) => EDGES.Where(e => RoadAt(e) == seat);

        private int VP(string seat)
        {
            int v = 0;
            foreach (var c in SeatCorners(seat)) v += BuildingAt(c)!.Value.city ? 2 : 1;
            if (Attr("lroadOwner") == seat) v += 2;
            if (Attr("larmyOwner") == seat) v += 2;
            v += Ints("dev:" + seat)[DVP] + Ints("devnew:" + seat)[DVP];
            return v;
        }

        // ============================ legality ============================
        private bool CanSettle(string corner, string seat, bool setup)
        {
            if (BuildingAt(corner) != null) return false;
            if (CADJ[corner].Any(n => BuildingAt(n) != null)) return false;            // distance rule
            if (setup) return true;
            return CEDGE[corner].Any(e => RoadAt(e) == seat);                          // must touch own road
        }
        private bool CanRoad(string edge, string seat, string? mustTouchCorner)
        {
            if (!string.IsNullOrEmpty(RoadAt(edge))) return false;
            var (a, b) = EC[edge];
            if (mustTouchCorner != null) return a == mustTouchCorner || b == mustTouchCorner;
            foreach (var c in new[] { a, b })
            {
                var bl = BuildingAt(c);
                if (bl?.owner == seat) return true;                                    // extends from own building
                if (bl != null) continue;                                              // opponent building blocks pass-through
                if (CEDGE[c].Any(e2 => e2 != edge && RoadAt(e2) == seat)) return true; // continues own road
            }
            return false;
        }
        private int PieceCount(string seat, bool city) => SeatCorners(seat).Count(c => BuildingAt(c)!.Value.city == city);

        private bool Pay(string seat, int[] cost)
        {
            var r = Ints("res:" + seat);
            for (int i = 0; i < 5; i++) if (r[i] < cost[i]) return false;
            for (int i = 0; i < 5; i++) r[i] -= cost[i];
            SetInts("res:" + seat, r);
            return true;
        }
        private void Gain(string seat, int res, int n = 1) { var r = Ints("res:" + seat); r[res] += n; SetInts("res:" + seat, r); }
        private static readonly int[] COST_ROAD = { 1, 0, 0, 1, 0 };          // wood+brick
        private static readonly int[] COST_SETTLE = { 1, 1, 1, 1, 0 };        // wood+sheep+wheat+brick
        private static readonly int[] COST_CITY = { 0, 0, 2, 0, 3 };          // 2 wheat + 3 ore
        private static readonly int[] COST_DEV = { 0, 1, 1, 0, 1 };           // sheep+wheat+ore

        // best bank rate for a seat giving a resource: 2 (its 2:1 port) / 3 (any 3:1 port) / 4
        private int Rate(string seat, int res)
        {
            bool any = false, two = false;
            for (int i = 0; i < PORTSPOTS.Count; i++)
            {
                var kind = Attr("port:" + i);
                foreach (var c in new[] { PORTSPOTS[i].a, PORTSPOTS[i].b })
                    if (BuildingAt(c)?.owner == seat)
                    { if (kind == "any") any = true; else if (kind == RES[res]) two = true; }
            }
            return two ? 2 : any ? 3 : 4;
        }

        // ============================ actions ============================
        [GameAction] public async Task RollDice(ExecuteActionData d) { if (Phase == "roll" && d.Player!.Id == GameData.CurrentTurnId) DoRoll(d.Player!.Id, new Random()); Render(); await Task.CompletedTask; }
        [GameAction] public async Task BuildSettlement(ExecuteActionData d) { DoSettle(d.Player!.Id, Arg(d, "corner")); Render(); await Task.CompletedTask; }
        [GameAction] public async Task BuildRoad(ExecuteActionData d) { DoRoad(d.Player!.Id, Arg(d, "edge")); Render(); await Task.CompletedTask; }
        [GameAction] public async Task BuildCity(ExecuteActionData d) { DoCity(d.Player!.Id, Arg(d, "corner")); Render(); await Task.CompletedTask; }
        [GameAction] public async Task MoveRobber(ExecuteActionData d) { DoRobber(d.Player!.Id, int.TryParse(Arg(d, "hex"), out var h) ? h : -1, new Random()); Render(); await Task.CompletedTask; }
        [GameAction] public async Task Steal(ExecuteActionData d) { DoSteal(d.Player!.Id, Arg(d, "victim"), new Random()); Render(); await Task.CompletedTask; }
        [GameAction] public async Task BuyDev(ExecuteActionData d) { DoBuyDev(d.Player!.Id); Render(); await Task.CompletedTask; }
        [GameAction] public async Task PlayDev(ExecuteActionData d) { DoPlayDev(d.Player!.Id, int.TryParse(Arg(d, "kind"), out var k) ? k : -1); Render(); await Task.CompletedTask; }
        [GameAction] public async Task DiscardCards(ExecuteActionData d) { DoDiscard(d.Player!.Id, Arg(d, "cards")); Render(); await Task.CompletedTask; }
        [GameAction] public async Task PickYop(ExecuteActionData d) { DoYop(d.Player!.Id, Arg(d, "res")); Render(); await Task.CompletedTask; }
        [GameAction] public async Task PickMono(ExecuteActionData d) { DoMono(d.Player!.Id, int.TryParse(Arg(d, "res"), out var r) ? r : -1); Render(); await Task.CompletedTask; }
        [GameAction] public async Task TradeGive(ExecuteActionData d) { if (Phase == "main" && d.Player!.Id == GameData.CurrentTurnId) Set("tsel:" + d.Player!.Id, Arg(d, "res")); Render(); await Task.CompletedTask; }
        [GameAction] public async Task TradeBank(ExecuteActionData d) { DoTradeBank(d.Player!.Id, int.TryParse(Arg(d, "res"), out var r) ? r : -1); Render(); await Task.CompletedTask; }
        [GameAction] public async Task CancelTrade(ExecuteActionData d) { GameData.Attributes.Remove("tsel:" + d.Player!.Id); Render(); await Task.CompletedTask; }
        [GameAction] public async Task EndTurn(ExecuteActionData d) { DoEndTurn(d.Player!.Id); Render(); await Task.CompletedTask; }

        // ============================ core moves ============================
        private void DoRoll(string seat, Random rnd)
        {
            int a = rnd.Next(1, 7), b = rnd.Next(1, 7);
            Set("lastRoll", $"{a},{b}");
            Log($"{Name(seat)} rolled {a + b}");
            if (a + b == 7)
            {
                var mustDiscard = new List<string>();
                foreach (var s in Order)
                {
                    int total = Ints("res:" + s).Sum();
                    if (total <= 7) continue;
                    if (IsAISeat(s)) AIDiscard(s, total / 2, rnd);
                    else mustDiscard.Add(s);
                }
                if (mustDiscard.Count > 0) { Set("discardq", string.Join(",", mustDiscard)); Set("phase", "discard"); }
                else Set("phase", "robber");
            }
            else
            {
                Produce(a + b);
                Set("phase", "main");
            }
        }

        private void Produce(int roll)
        {
            int robber = int.Parse(Attr("robber"));
            for (int i = 0; i < HEXES.Count; i++)
            {
                if (i == robber || Attr("num:" + i) != roll.ToString()) continue;
                int res = Array.IndexOf(RES, Attr("terr:" + i));
                if (res < 0) continue;
                foreach (var c in HEXC[i])
                {
                    var bl = BuildingAt(c);
                    if (bl != null) Gain(bl.Value.owner, res, bl.Value.city ? 2 : 1);
                }
            }
        }

        private void DoDiscard(string seat, string cardsCsv)
        {
            var q = ListAttr("discardq");
            if (Phase != "discard" || !q.Contains(seat)) return;
            var res = Ints("res:" + seat);
            int need = res.Sum() / 2;
            var picks = cardsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (picks.Length != need) return;
            var drop = new int[5];
            foreach (var p in picks) { int i = int.Parse(p.Split(':')[0]); drop[i]++; }
            for (int i = 0; i < 5; i++) if (drop[i] > res[i]) return;
            for (int i = 0; i < 5; i++) res[i] -= drop[i];
            SetInts("res:" + seat, res);
            Log($"{Name(seat)} discarded {need}");
            q.Remove(seat); Set("discardq", string.Join(",", q));
            if (q.Count == 0) Set("phase", "robber");
        }

        private void AIDiscard(string seat, int n, Random rnd)
        {
            var res = Ints("res:" + seat);
            for (int k = 0; k < n; k++) { int i = Array.IndexOf(res, res.Max()); res[i]--; }
            SetInts("res:" + seat, res);
            Log($"{Name(seat)} discarded {n}");
        }

        private void DoRobber(string seat, int hex, Random rnd)
        {
            if (Phase != "robber" || seat != GameData.CurrentTurnId || hex < 0 || hex >= HEXES.Count) return;
            if (Attr("robber") == hex.ToString()) return;
            Set("robber", hex.ToString());
            var victims = HEXC[hex].Select(c => BuildingAt(c)?.owner).Where(o => o != null && o != seat && Ints("res:" + o).Sum() > 0)
                                   .Distinct().Cast<string>().ToList();
            if (victims.Count == 0) { Set("phase", "main"); return; }
            if (victims.Count == 1) { StealCard(seat, victims[0], rnd); Set("phase", "main"); return; }
            Set("stealopts", string.Join(",", victims)); Set("phase", "steal");
        }

        private void DoSteal(string seat, string victim, Random rnd)
        {
            if (Phase != "steal" || seat != GameData.CurrentTurnId || !ListAttr("stealopts").Contains(victim)) return;
            StealCard(seat, victim, rnd);
            GameData.Attributes.Remove("stealopts");
            Set("phase", "main");
        }

        private void StealCard(string thief, string victim, Random rnd)
        {
            var r = Ints("res:" + victim); int total = r.Sum(); if (total == 0) return;
            int pick = rnd.Next(total), i = 0; while (pick >= r[i]) { pick -= r[i]; i++; }
            r[i]--; SetInts("res:" + victim, r); Gain(thief, i);
            Log($"{Name(thief)} stole a card from {Name(victim)}");
        }

        private void DoSettle(string seat, string corner)
        {
            if (seat != GameData.CurrentTurnId || !CPOS.ContainsKey(corner)) return;
            if (Phase == "setup" && Attr("setupstep") == "s")
            {
                if (!CanSettle(corner, seat, true)) return;
                Set("b:" + corner, seat + ":s"); Set("lastSettle:" + seat, corner); Set("setupstep", "r");
                var q = ListAttr("setupq");
                if (q.Count <= Order.Count)   // second settlement (reversed half) pays out
                    foreach (var hi in CHEX[corner])
                    { int res = Array.IndexOf(RES, Attr("terr:" + hi)); if (res >= 0) Gain(seat, res); }
                Log($"{Name(seat)} placed a settlement");
                return;
            }
            if (Phase != "main" || !CanSettle(corner, seat, false) || PieceCount(seat, false) >= 5) return;
            if (!Pay(seat, COST_SETTLE)) return;
            Set("b:" + corner, seat + ":s");
            Log($"{Name(seat)} built a settlement");
            RecomputeLongestRoad();   // a new settlement can cut an opponent's road
            CheckWin(seat);
        }

        private void DoRoad(string seat, string edge)
        {
            if (seat != GameData.CurrentTurnId || !EC.ContainsKey(edge)) return;
            if (Phase == "setup" && Attr("setupstep") == "r")
            {
                if (!CanRoad(edge, seat, Attr("lastSettle:" + seat))) return;
                Set("r:" + edge, seat);
                AdvanceSetup();
                return;
            }
            if (Phase == "freeroad")
            {
                if (!CanRoad(edge, seat, null) || SeatRoads(seat).Count() >= 15) return;
                Set("r:" + edge, seat);
                int left = int.Parse(Attr("freeroads")) - 1;
                if (left > 0) Set("freeroads", left.ToString()); else { GameData.Attributes.Remove("freeroads"); Set("phase", "main"); }
                RecomputeLongestRoad(); CheckWin(seat);
                return;
            }
            if (Phase != "main" || !CanRoad(edge, seat, null) || SeatRoads(seat).Count() >= 15) return;
            if (!Pay(seat, COST_ROAD)) return;
            Set("r:" + edge, seat);
            RecomputeLongestRoad(); CheckWin(seat);
        }

        private void DoCity(string seat, string corner)
        {
            if (Phase != "main" || seat != GameData.CurrentTurnId) return;
            var bl = BuildingAt(corner);
            if (bl?.owner != seat || bl.Value.city || PieceCount(seat, true) >= 4) return;
            if (!Pay(seat, COST_CITY)) return;
            Set("b:" + corner, seat + ":c");
            Log($"{Name(seat)} upgraded to a city");
            CheckWin(seat);
        }

        private void AdvanceSetup()
        {
            var q = ListAttr("setupq");
            q.RemoveAt(0); Set("setupq", string.Join(",", q)); Set("setupstep", "s");
            if (q.Count == 0)
            {
                Set("phase", "roll");
                GameData.CurrentTurnId = Order[0];
                Log("Setup complete — game on!");
            }
            else GameData.CurrentTurnId = q[0];
        }

        private void DoBuyDev(string seat)
        {
            if (Phase != "main" || seat != GameData.CurrentTurnId) return;
            var deck = ListAttr("deck"); if (deck.Count == 0) return;
            if (!Pay(seat, COST_DEV)) return;
            int kind = int.Parse(deck[0]); deck.RemoveAt(0); Set("deck", string.Join(",", deck));
            var dn = Ints("devnew:" + seat); dn[kind]++; SetInts("devnew:" + seat, dn);
            Log($"{Name(seat)} bought a development card");
            CheckWin(seat);   // a VP card can finish the game
        }

        private void DoPlayDev(string seat, int kind)
        {
            if (Phase != "main" || seat != GameData.CurrentTurnId || kind is < 0 or > 4 || kind == DVP) return;
            if (Attr("devPlayed") == "1") return;
            var dev = Ints("dev:" + seat); if (dev[kind] <= 0) return;
            dev[kind]--; SetInts("dev:" + seat, dev);
            Set("devPlayed", "1");
            Log($"{Name(seat)} played {DEVNAME[kind]}");
            switch (kind)
            {
                case DKNIGHT:
                    Set("army:" + seat, (int.Parse(Attr("army:" + seat)) + 1).ToString());
                    RecomputeLargestArmy();
                    Set("phase", "robber");
                    CheckWin(seat);
                    break;
                case DROAD: Set("freeroads", "2"); Set("phase", "freeroad"); break;
                case DYOP: Set("phase", "yop"); break;
                case DMONO: Set("phase", "mono"); break;
            }
        }

        private void DoYop(string seat, string resCsv)
        {
            if (Phase != "yop" || seat != GameData.CurrentTurnId) return;
            var picks = resCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (picks.Length != 2) return;
            foreach (var p in picks) Gain(seat, int.Parse(p.Split(':')[0]));
            Set("phase", "main");
        }

        private void DoMono(string seat, int res)
        {
            if (Phase != "mono" || seat != GameData.CurrentTurnId || res is < 0 or > 4) return;
            int got = 0;
            foreach (var s in Order.Where(s => s != seat))
            {
                var r = Ints("res:" + s); got += r[res]; r[res] = 0; SetInts("res:" + s, r);
            }
            Gain(seat, res, got);
            Log($"{Name(seat)} monopolised {RESNAME[res]} (+{got})");
            Set("phase", "main");
        }

        private void DoTradeBank(string seat, int get)
        {
            if (Phase != "main" || seat != GameData.CurrentTurnId || get is < 0 or > 4) return;
            if (!int.TryParse(Attr("tsel:" + seat), out var give) || give == get) return;
            int rate = Rate(seat, give);
            var r = Ints("res:" + seat); if (r[give] < rate) return;
            r[give] -= rate; r[get]++; SetInts("res:" + seat, r);
            GameData.Attributes.Remove("tsel:" + seat);
            Log($"{Name(seat)} traded {rate} {RESNAME[give]} → 1 {RESNAME[get]}");
        }

        private void DoEndTurn(string seat)
        {
            if (Phase != "main" || seat != GameData.CurrentTurnId) return;
            // freshly bought devs become playable
            var dev = Ints("dev:" + seat); var dn = Ints("devnew:" + seat);
            for (int i = 0; i < 5; i++) { dev[i] += dn[i]; dn[i] = 0; }
            SetInts("dev:" + seat, dev); SetInts("devnew:" + seat, dn);
            GameData.Attributes.Remove("devPlayed"); GameData.Attributes.Remove("tsel:" + seat);
            var o = Order; GameData.CurrentTurnId = o[(o.IndexOf(seat) + 1) % o.Count];
            Set("phase", "roll");
        }

        // ============================ longest road / largest army / win ============================
        private int LongestFor(string seat)
        {
            var mine = SeatRoads(seat).ToHashSet();
            int best = 0;
            foreach (var start in mine)
            {
                foreach (var c0 in new[] { EC[start].a, EC[start].b })
                    best = Math.Max(best, 1 + Walk(c0, new HashSet<string> { start }));
            }
            return best;

            int Walk(string corner, HashSet<string> used)
            {
                var bl = BuildingAt(corner);
                if (bl != null && bl.Value.owner != seat) return 0;       // opponent building cuts the road
                int b = 0;
                foreach (var e in CEDGE[corner])
                {
                    if (used.Contains(e) || !mine.Contains(e)) continue;
                    used.Add(e);
                    var (x, y) = EC[e];
                    b = Math.Max(b, 1 + Walk(x == corner ? y : x, used));
                    used.Remove(e);
                }
                return b;
            }
        }

        private void RecomputeLongestRoad()
        {
            string holder = Attr("lroadOwner");
            int holderLen = string.IsNullOrEmpty(holder) ? 0 : LongestFor(holder);
            if (!string.IsNullOrEmpty(holder) && holderLen < 5) { holder = ""; holderLen = 0; }
            string best = holder; int bestLen = Math.Max(holderLen, 4);
            foreach (var s in Order)
            {
                int len = LongestFor(s);
                if (len > bestLen) { best = s; bestLen = len; }   // strictly longer takes it; holder keeps ties
            }
            if (best != Attr("lroadOwner"))
            {
                if (string.IsNullOrEmpty(best)) GameData.Attributes.Remove("lroadOwner");
                else { Set("lroadOwner", best); Log($"{Name(best)} has the Longest Road ({bestLen})"); }
            }
        }

        private void RecomputeLargestArmy()
        {
            string holder = Attr("larmyOwner");
            int holderN = string.IsNullOrEmpty(holder) ? 2 : int.Parse(Attr("army:" + holder));
            foreach (var s in Order)
                if (int.Parse(Attr("army:" + s)) > holderN && int.Parse(Attr("army:" + s)) >= 3)
                { holder = s; holderN = int.Parse(Attr("army:" + s)); }
            if (holder != Attr("larmyOwner") && !string.IsNullOrEmpty(holder))
            { Set("larmyOwner", holder); Log($"{Name(holder)} has the Largest Army ({holderN})"); }
        }

        private void CheckWin(string seat)
        {
            if (VP(seat) < 10 || GameData.Attributes.ContainsKey("over")) return;
            Set("over", "1"); Set("winnerIds", seat);
            Set("result", $"{Name(seat)} wins with {VP(seat)} points!");
        }

        // ============================ AI ============================
        public override async Task<bool> PlayAI(PlayerData player, Random rnd)
        {
            string seat = player.Id;
            if (GameData.CurrentTurnId != seat || GameData.Attributes.ContainsKey("over")) return false;
            switch (Phase)
            {
                case "setup":
                    if (Attr("setupstep") == "s")
                    {
                        var best = CPOS.Keys.Where(c => CanSettle(c, seat, true)).OrderByDescending(CornerScore).First();
                        DoSettle(seat, best);
                    }
                    else
                    {
                        var last = Attr("lastSettle:" + seat);
                        var e = CEDGE[last].Where(x => CanRoad(x, seat, last))
                                .OrderByDescending(x => CornerScore(EC[x].a == last ? EC[x].b : EC[x].a)).FirstOrDefault();
                        if (e != null) DoRoad(seat, e); else AdvanceSetup();
                    }
                    break;
                case "roll": DoRoll(seat, rnd); break;
                case "discard": return false;   // waiting on humans
                case "robber": DoRobber(seat, BestRobberHex(seat), rnd); break;
                case "steal":
                    var v = ListAttr("stealopts").OrderByDescending(s => Ints("res:" + s).Sum()).First();
                    DoSteal(seat, v, rnd); break;
                case "freeroad":
                    var fr = EDGES.FirstOrDefault(e => CanRoad(e, seat, null));
                    if (fr != null) DoRoad(seat, fr); else { GameData.Attributes.Remove("freeroads"); Set("phase", "main"); }
                    break;
                case "yop": { var need = AINeed(seat); DoYop(seat, $"{need}:a,{need}:b"); break; }
                case "mono": DoMono(seat, AINeed(seat)); break;
                case "main": AIMain(seat, rnd); break;
                default: return false;
            }
            Render();
            return true;
        }

        private double CornerScore(string c)
        {
            double s = 0; var kinds = new HashSet<string>();
            foreach (var hi in CHEX[c])
            {
                var t = Attr("terr:" + hi);
                if (int.TryParse(Attr("num:" + hi), out var n)) { s += 6 - Math.Abs(7 - n); kinds.Add(t); }
            }
            return s + kinds.Count * 0.6;
        }

        private int BestRobberHex(string seat)
        {
            int robber = int.Parse(Attr("robber"));
            int best = -1; double bestScore = -1;
            for (int i = 0; i < HEXES.Count; i++)
            {
                if (i == robber) continue;
                var owners = HEXC[i].Select(c => BuildingAt(c)?.owner).Where(o => o != null).Distinct().ToList();
                if (owners.Contains(seat)) continue;
                double sc = owners.Count == 0 ? 0 : owners.Max(o => VP(o!)) + (int.TryParse(Attr("num:" + i), out var n) ? (6 - Math.Abs(7 - n)) * 0.3 : 0);
                if (sc > bestScore) { bestScore = sc; best = i; }
            }
            return best < 0 ? (robber + 1) % HEXES.Count : best;
        }

        private int AINeed(string seat)
        {
            var r = Ints("res:" + seat);
            return Array.IndexOf(r, r.Min());
        }

        private void AIMain(string seat, Random rnd)
        {
            // 1. city upgrade
            var settle = SeatCorners(seat).FirstOrDefault(c => !BuildingAt(c)!.Value.city);
            if (settle != null && Ints("res:" + seat).Zip(COST_CITY, (a, b) => a >= b).All(x => x) && PieceCount(seat, true) < 4)
            { DoCity(seat, settle); return; }
            // 2. settlement
            var spot = CPOS.Keys.Where(c => CanSettle(c, seat, false)).OrderByDescending(CornerScore).FirstOrDefault();
            if (spot != null && Ints("res:" + seat).Zip(COST_SETTLE, (a, b) => a >= b).All(x => x) && PieceCount(seat, false) < 5)
            { DoSettle(seat, spot); return; }
            // 3. knight if the robber squats on us
            int robber = int.Parse(Attr("robber"));
            if (Attr("devPlayed") != "1" && Ints("dev:" + seat)[DKNIGHT] > 0 && HEXC[robber].Any(c => BuildingAt(c)?.owner == seat))
            { DoPlayDev(seat, DKNIGHT); return; }
            // 4. dev card sometimes
            if (rnd.Next(3) == 0 && Ints("res:" + seat).Zip(COST_DEV, (a, b) => a >= b).All(x => x) && ListAttr("deck").Count > 0)
            { DoBuyDev(seat); return; }
            // 5. road toward good corners
            if (Ints("res:" + seat).Zip(COST_ROAD, (a, b) => a >= b).All(x => x) && SeatRoads(seat).Count() < 15 && rnd.Next(2) == 0)
            {
                var e = EDGES.Where(x => CanRoad(x, seat, null))
                             .OrderByDescending(x => Math.Max(CornerScore(EC[x].a), CornerScore(EC[x].b))).FirstOrDefault();
                if (e != null) { DoRoad(seat, e); return; }
            }
            // 6. bank trade surplus toward what's missing
            var res = Ints("res:" + seat);
            for (int give = 0; give < 5; give++)
            {
                int rate = Rate(seat, give);
                if (res[give] >= rate + 1)
                { Set("tsel:" + seat, give.ToString()); DoTradeBank(seat, AINeed(seat)); return; }
            }
            DoEndTurn(seat);
        }

        // ============================ 3D render ============================
        protected override void RefreshScreens() { Render(); BuildPanels(); }

        private void Render()
        {
            GameData.Table = ItemData.Table();
            foreach (var p in GameData.Players) { p.Hand = new ItemData("", null) { Name = "HAND" }; p.Table = new ItemData("", null) { Name = "TABLE" }; }

            string cur = GameData.CurrentTurnId ?? "";
            bool over = GameData.Attributes.ContainsKey("over");

            // sea mat + decorative sea ring
            addItem(Assets.MAT).SetPosition(0, -0.3, 0).SetScale(46, 0.3, 46).AddAttribute("tint", "0x1d3f63");
            foreach (var (x, z) in SEA) addItem(Img("hex_sea.png")).SetPosition(x, -0.09, z).SetScale(SZ * 2.03, 1, SZ * 2.03);

            // land hexes + chips + robber
            int robber = int.Parse(Attr("robber") == "" ? "0" : Attr("robber"));
            for (int i = 0; i < HEXES.Count; i++)
            {
                var (x, z) = HEXPOS[i];
                addItem(Img($"hex_{Attr("terr:" + i)}.png")).SetPosition(x, 0, z).SetScale(SZ * 2.03, 1, SZ * 2.03).AddAttribute("hex", i.ToString());
                if (Attr("num:" + i) != "")
                    addItem(Img($"chip_{Attr("num:" + i)}.png")).SetPosition(x, 0.08, z).SetScale(1.25, 1, 1.25);
            }
            addItem(Img("robber.svg")).SetPosition(HEXPOS[robber].x, 0.16, HEXPOS[robber].z + 0.8).SetScale(1.5, 1, 1.5);

            // ports
            for (int i = 0; i < PORTSPOTS.Count; i++)
            {
                var (a, b) = PORTSPOTS[i];
                double mx = (CPOS[a].x + CPOS[b].x) / 2, mz = (CPOS[a].z + CPOS[b].z) / 2;
                double len = Math.Sqrt(mx * mx + mz * mz);
                addItem(Img($"port_{Attr("port:" + i)}.png")).SetPosition(mx * (len + 1.6) / len, 0.02, mz * (len + 1.6) / len).SetScale(1.7, 1, 1.7);
            }

            // roads + buildings
            foreach (var e in EDGES)
            {
                var owner = RoadAt(e); if (string.IsNullOrEmpty(owner)) continue;
                var (a, b) = EC[e];
                double mx = (CPOS[a].x + CPOS[b].x) / 2, mz = (CPOS[a].z + CPOS[b].z) / 2;
                double ang = Math.Atan2(CPOS[b].z - CPOS[a].z, CPOS[b].x - CPOS[a].x) * 180 / Math.PI;
                addItem(Assets.PIECE).SetPosition(mx, 0.14, mz).SetRotation(0, -ang, 0).SetScale(2.0, 0.28, 0.5).AddAttribute("tint", Col(owner));
            }
            foreach (var c in CPOS.Keys)
            {
                var bl = BuildingAt(c); if (bl == null) continue;
                var (x, z) = CPOS[c];
                if (bl.Value.city) addItem(Assets.PIECE).SetPosition(x, 0.35, z).SetScale(1.05, 1.5, 1.05).AddAttribute("tint", Col(bl.Value.owner)).AddAttribute("city", "1");
                else addItem(Assets.PIECE).SetPosition(x, 0.25, z).SetScale(0.8, 0.8, 0.8).AddAttribute("tint", Col(bl.Value.owner)).AddAttribute("settlement", "1");
            }

            // last roll, as flat text (a rebuilt-every-action scene can't host async-loading die
            // models — their meshes arrive after the item was already replaced and leak)
            var lr = Attr("lastRoll").Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (lr.Length == 2)
                addTextItem(Assets.TEXT).SetText($"🎲 {lr[0]} + {lr[1]} = {int.Parse(lr[0]) + int.Parse(lr[1])}")
                    .SetPosition(13, 0.1, 11).SetScale(0.7).SetRotation(-90, 0, 0).AddAttribute("textColor", "ffe9b0");

            // bank column (right): click to RECEIVE in a bank trade
            addTextItem(Assets.TEXT).SetText("BANK").SetPosition(15.5, 0.1, -7.9).SetScale(0.4).SetRotation(-90, 0, 0).AddAttribute("textColor", "cbd5e1");
            for (int r = 0; r < 5; r++)
            {
                var t = addItem(Img($"res_{RES[r]}.svg")).SetPosition(15.5, 0.02, -6 + r * 2.2).SetScale(1.5, 1, 1.5).AddAttribute("bank", "1");
                if (!over && Phase == "main") { t.ClickActions[cur] = nameof(TradeBank); t.AddAttribute("res", r.ToString()); }
            }

            // per-seat: resources row + stats in the player's own zone
            foreach (var seat in GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT))
                PlayerZone(seat, cur);

            // markers + buttons for the current actor
            if (!over) Markers(cur);
            if (!over) ActionButtons(cur);

            // headline
            string head = over ? Attr("result")
                : Phase switch
                {
                    "setup" => $"SETUP · {Name(cur)} places a {(Attr("setupstep") == "s" ? "settlement" : "road")}",
                    "roll" => $"CATAN · {Name(cur)} — roll the dice",
                    "discard" => "A 7! Waiting for discards…",
                    "robber" => $"{Name(cur)} moves the robber",
                    "steal" => $"{Name(cur)} picks who to rob",
                    "freeroad" => $"{Name(cur)} places free roads ({Attr("freeroads")})",
                    "yop" or "mono" => $"{Name(cur)} resolves a development card",
                    _ => $"CATAN · {Name(cur)}'s turn" + (Attr("lastRoll") != "" ? $" · rolled {Attr("lastRoll").Split(',').Select(int.Parse).Sum()}" : "")
                };
            addTextItem(Assets.TEXT).SetText(head).SetPosition(0, 7, -17).SetScale(1.1).SetRotation(-90, 0, 0).AddAttribute("textColor", "ffd166");

            // scores row
            var ord = Order;
            for (int i = 0; i < ord.Count; i++)
            {
                var s = ord[i];
                string extra = (Attr("lroadOwner") == s ? " ·LR" : "") + (Attr("larmyOwner") == s ? " ·LA" : "");
                addTextItem(Assets.TEXT).SetText($"{Name(s)}  {VP(s)}vp{extra}")
                    .SetPosition(-((ord.Count - 1) * 9.0) / 2 + i * 9.0, 7, -14.5).SetScale(0.7).SetRotation(-90, 0, 0)
                    .AddAttribute("textColor", s == cur ? "ffd166" : "cbd5e1");
            }
        }

        private void PlayerZone(PlayerData seat, string cur)
        {
            var res = Ints("res:" + seat.Id);
            var dev = Ints("dev:" + seat.Id); var dn = Ints("devnew:" + seat.Id);
            string tsel = Attr("tsel:" + seat.Id);
            for (int r = 0; r < 5; r++)
            {
                var t = addItemToPlayerTable(seat, Img($"res_{RES[r]}.svg")).SetPosition(-4 + r * 2.0, 0, 0).SetScale(1.4, 1, 1.4);
                if (tsel == r.ToString()) t.AddAttribute("selected", "1");
                if (seat.Id == cur && Phase == "main") { t.ClickActions[cur] = nameof(TradeGive); t.AddAttribute("res", r.ToString()); }
                addItemToPlayerTable(seat, Assets.TEXT).SetText(res[r].ToString()).SetPosition(-4 + r * 2.0, 0.15, 1.3).SetScale(0.45).SetRotation(-90, 0, 0).AddAttribute("textColor", "ffffff");
            }
            int devTotal = dev.Sum() + dn.Sum();
            if (devTotal > 0)
            {
                addItemToPlayerTable(seat, Img("dev_back.svg")).SetPosition(6.4, 0, 0).SetScale(1.3, 1, 1.8);
                addItemToPlayerTable(seat, Assets.TEXT).SetText(devTotal.ToString()).SetPosition(6.4, 0.15, 1.3).SetScale(0.45).SetRotation(-90, 0, 0).AddAttribute("textColor", "e8ddff");
            }
        }

        private void Markers(string cur)
        {
            switch (Phase)
            {
                case "setup":
                    if (Attr("setupstep") == "s")
                        foreach (var c in CPOS.Keys.Where(c => CanSettle(c, cur, true))) CornerMarker(c, cur, nameof(BuildSettlement));
                    else
                        foreach (var e in CEDGE[Attr("lastSettle:" + cur)].Where(e => CanRoad(e, cur, Attr("lastSettle:" + cur)))) EdgeMarker(e, cur);
                    break;
                case "robber":
                    int robber = int.Parse(Attr("robber"));
                    for (int i = 0; i < HEXES.Count; i++)
                    {
                        if (i == robber) continue;
                        var mk = addItem(Assets.MARKER).SetPosition(HEXPOS[i].x, 0.13, HEXPOS[i].z).SetScale(2.4, 0.12, 2.4)
                            .AddAttribute("tint", "0x22c55e").AddAttribute("marker", "1");
                        mk.ClickActions[cur] = nameof(MoveRobber); mk.AddAttribute("hex", i.ToString());
                    }
                    break;
                case "freeroad":
                    foreach (var e in EDGES.Where(e => CanRoad(e, cur, null))) EdgeMarker(e, cur);
                    break;
                case "main":
                    var res = Ints("res:" + cur);
                    if (res.Zip(COST_SETTLE, (a, b) => a >= b).All(x => x) && PieceCount(cur, false) < 5)
                        foreach (var c in CPOS.Keys.Where(c => CanSettle(c, cur, false))) CornerMarker(c, cur, nameof(BuildSettlement));
                    if (res.Zip(COST_ROAD, (a, b) => a >= b).All(x => x) && SeatRoads(cur).Count() < 15)
                        foreach (var e in EDGES.Where(e => CanRoad(e, cur, null))) EdgeMarker(e, cur);
                    if (res.Zip(COST_CITY, (a, b) => a >= b).All(x => x) && PieceCount(cur, true) < 4)
                        foreach (var c in SeatCorners(cur).Where(c => !BuildingAt(c)!.Value.city))
                        {
                            var mk = addItem(Assets.MARKER).SetPosition(CPOS[c].x, 0.55, CPOS[c].z).SetScale(1.2, 0.1, 1.2)
                                .AddAttribute("tint", "0x38bdf8").AddAttribute("marker", "1");
                            mk.ClickActions[cur] = nameof(BuildCity); mk.AddAttribute("corner", c);
                        }
                    break;
            }

            void CornerMarker(string c, string seat, string action)
            {
                var mk = addItem(Assets.MARKER).SetPosition(CPOS[c].x, 0.1, CPOS[c].z).SetScale(0.62, 0.14, 0.62)
                    .AddAttribute("tint", "0x22c55e").AddAttribute("marker", "1");
                mk.ClickActions[seat] = action; mk.AddAttribute("corner", c);
            }
            void EdgeMarker(string e, string seat)
            {
                var (a, b) = EC[e];
                double mx = (CPOS[a].x + CPOS[b].x) / 2, mz = (CPOS[a].z + CPOS[b].z) / 2;
                double ang = Math.Atan2(CPOS[b].z - CPOS[a].z, CPOS[b].x - CPOS[a].x) * 180 / Math.PI;
                var mk = addItem(Assets.MARKER).SetPosition(mx, 0.08, mz).SetRotation(0, -ang, 0).SetScale(1.5, 0.1, 0.42)
                    .AddAttribute("tint", "0x22c55e").AddAttribute("marker", "1");
                mk.ClickActions[seat] = nameof(BuildRoad); mk.AddAttribute("edge", e);
            }
        }

        private void ActionButtons(string cur)
        {
            var buttons = new List<(string label, string action, Dictionary<string, string>? args, string col)>();
            var dev = Ints("dev:" + cur);
            switch (Phase)
            {
                case "roll": buttons.Add(("🎲 ROLL", nameof(RollDice), null, "0x2f7a45")); break;
                case "main":
                    if (Attr("tsel:" + cur) != "") buttons.Add(("✖ CANCEL TRADE", nameof(CancelTrade), null, "0x7a2f2f"));
                    if (Ints("res:" + cur).Zip(COST_DEV, (a, b) => a >= b).All(x => x) && ListAttr("deck").Count > 0)
                        buttons.Add(("BUY DEV", nameof(BuyDev), null, "0x4b3a6b"));
                    if (Attr("devPlayed") != "1")
                        for (int k = 0; k < 5; k++)
                            if (k != DVP && dev[k] > 0)
                                buttons.Add(($"{DEVNAME[k].ToUpper()} ×{dev[k]}", nameof(PlayDev), new() { { "kind", k.ToString() } }, "0x4b3a6b"));
                    buttons.Add(("END TURN", nameof(EndTurn), null, "0x6a4a25"));
                    break;
            }
            if (buttons.Count == 0) return;

            var cp = GameData.Players.Find(p => p.Id == cur);
            double ax = cp?.Avatar.Position.X ?? 0, az = cp?.Avatar.Position.Z ?? 20, len = Math.Sqrt(ax * ax + az * az); if (len < 0.1) len = 1;
            double ux = -ax / len, uz = -az / len;
            double baseX = ax + ux * 5.5, baseZ = az + uz * 5.5;
            double tx = uz, tz = -ux, yaw = Math.Atan2(ux, uz) * 180 / Math.PI;
            for (int i = 0; i < buttons.Count; i++)
            {
                double off = -((buttons.Count - 1) * 4.2) / 2 + i * 4.2;
                double bx = baseX + tx * off, bz = baseZ + tz * off;
                var bt = addItem(Assets.MARKER).SetPosition(bx, 0.15, bz).SetRotation(0, yaw, 0).SetScale(3.8, 0.3, 1.2)
                    .AddAttribute("tint", buttons[i].col).AddAttribute("button", "1");
                bt.ClickActions[cur] = buttons[i].action;
                if (buttons[i].args != null) foreach (var kv in buttons[i].args!) bt.AddAttribute(kv.Key, kv.Value);
                addTextItem(Assets.TEXT).SetText(buttons[i].label).SetPosition(bx, 0.4, bz).SetScale(0.36).SetRotation(-90, 0, 0).AddAttribute("textColor", "ffffff");
            }
        }

        // ============================ rare-case panels (server-driven UI) ============================
        private void BuildPanels()
        {
            GameData.Attributes["panelMode"] = "side";
            foreach (var seat in GameData.Players)
            {
                if (seat.Type == PlayerTypeEnum.EMPTY_SEAT || seat.Type == PlayerTypeEnum.AI) { seat.Screen = null; continue; }
                seat.Screen = SeatPanel(seat.Id);
            }
        }

        private List<UiNode>? SeatPanel(string id)
        {
            // discard on a 7
            if (Phase == "discard" && ListAttr("discardq").Contains(id))
            {
                var res = Ints("res:" + id);
                int need = res.Sum() / 2;
                var opts = new List<UiOption>();
                for (int r = 0; r < 5; r++) for (int k = 0; k < res[r]; k++) opts.Add(new($"{RESNAME[r]}", $"{r}:{k}"));
                return new()
                {
                    UiNode.Title("A 7 was rolled!"),
                    UiNode.Note($"You hold {res.Sum()} cards — discard exactly {need}."),
                    new UiNode { Type = "checks", Options = opts, Need = need, Action = nameof(DiscardCards), ArgKey = "cards", Text = $"Discard {need}" },
                };
            }
            if (id != GameData.CurrentTurnId) return null;
            // pick a steal victim
            if (Phase == "steal")
                return new()
                {
                    UiNode.Title("Rob whom?"),
                    UiNode.Col(ListAttr("stealopts").Select(v =>
                        UiNode.Button($"{Name(v)} ({Ints("res:" + v).Sum()} cards)", nameof(Steal), new() { { "victim", v } })).ToArray()),
                };
            // year of plenty: pick any 2 resources from the bank
            if (Phase == "yop")
            {
                var opts = new List<UiOption>();
                for (int r = 0; r < 5; r++) { opts.Add(new(RESNAME[r], $"{r}:a")); opts.Add(new(RESNAME[r] + " (2nd)", $"{r}:b")); }
                return new()
                {
                    UiNode.Title("Year of Plenty"),
                    UiNode.Note("Take any 2 resources from the bank."),
                    new UiNode { Type = "checks", Options = opts, Need = 2, Action = nameof(PickYop), ArgKey = "res", Text = "Take 2" },
                };
            }
            // monopoly: name a resource
            if (Phase == "mono")
                return new()
                {
                    UiNode.Title("Monopoly"),
                    UiNode.Note("Every player gives you ALL their cards of one resource."),
                    UiNode.Col(Enumerable.Range(0, 5).Select(r =>
                        UiNode.Button(RESNAME[r], nameof(PickMono), new() { { "res", r.ToString() } })).ToArray()),
                };
            return null;
        }
    }
}
