using System;
using System.Collections.Generic;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Carcassonne — tile-laying (2..5 players). Base rules for CITIES, ROADS and MONASTERIES are
    // implemented fully (edge-matched placement, connectivity, completion scoring, meeple majority,
    // end-game partial scoring). FARMS/fields use a pragmatic model (a field component scores 3 per
    // completed city sharing a tile with it) — good enough to play; refine later.
    //
    // The client stays dumb: the growing tile map is built as 3D items (GameData.Table), and the
    // per-seat control panel (current tile, rotate, meeple choices) is a server-driven UiNode screen.
    //
    // A tile TYPE = edges NESW (F=field, R=road, C=city) + monastery + shield. Connectivity is
    // DERIVED from the edges (all city-edges = one city; two road-edges connect, 3+ don't; a field is
    // one region unless a straight road bisects it). Rotation just rotates the edges.
    public class CarcassonneGameFlow : BaseGameFlow
    {
        private class TileType { public string e = "FFFF"; public bool mon, shield; public int count;
            public TileType(string e, bool m, bool s, int c) { this.e = e; mon = m; shield = s; count = c; } }

        // Curated base-style bag (edges N,E,S,W). Counts sum to the deck size.
        private static readonly List<TileType> TYPES = new()
        {
            new("FFFF", true,  false, 4),   // 0 monastery
            new("FFRF", true,  false, 2),   // 1 monastery + road (south)
            new("RFRF", false, false, 8),   // 2 straight road
            new("RRFF", false, false, 9),   // 3 road curve
            new("RRRF", false, false, 4),   // 4 road T-junction
            new("RRRR", false, false, 1),   // 5 crossroads
            new("CFFF", false, false, 5),   // 6 city cap (one edge)
            new("CFRF", false, false, 4),   // 7 city cap + road opposite? (city N, road S)
            new("CFRR", false, false, 3),   // 8 city + road bend
            new("CCFF", false, false, 3),   // 9 city corner (adjacent)
            new("CCFF", false, true,  2),   // 10 city corner + shield
            new("CFCF", false, false, 3),   // 11 city across (tunnel)
            new("CCCF", false, false, 3),   // 12 city three sides
            new("CCCF", false, true,  1),   // 13 city three sides + shield
            new("CCCC", false, true,  1),   // 14 full city + shield
            new("CCRR", false, false, 3),   // 15 city corner + road curve
        };
        private const double SZ = 4.0;      // world size per tile

        public override int MinPlayers => 2;

        public CarcassonneGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.CARCASSONNE;
        }

        private static string Arg(ExecuteActionData d, string key)
            => d.args != null && d.args.TryGetValue(key, out var v) ? v : (d.Item?.GetStringAttribute(key) ?? "");

        // Computed properties (not static fields): each returns a fresh asset with a DETERMINISTIC
        // Name, so addAsset stays idempotent. This deliberately avoids process-global static field
        // initializers, which `dotnet watch` Hot Reload does NOT run when a field is added to an
        // already-initialized type (leaving it null). Properties are recomputed every call, so they
        // are always correct under hot reload and after a cold start alike.
        internal static class Assets
        {
            internal static AssetData TEXT   => new Text3dAssetData("carc");
            internal static AssetData MARKER => new CylinderAssetData("carcmark");
            internal static AssetData MEEPLE => new CylinderAssetData("carcmeeple");
            internal static AssetData MAT    => new CylinderAssetData("carcmat");
        }

        // ============================ lifecycle ============================
        protected override Task Create()
        {
            addAsset(Assets.TEXT); addAsset(Assets.MARKER); addAsset(Assets.MEEPLE); addAsset(Assets.MAT);
            GameData.Attributes["noAvatars"] = "1";   // top-down map — no seated figures
            GameData.Observer.Position.Set(0, 32, 20);
            // Shared close-ish top-down view (the map is the same for everyone).
            for (int i = 0; i < 5; i++)   // 2..5 players
                new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                    .AddAttribute("type", "p" + (i + 1)).SetCameraPosition(0, 28, 18).SetAvatarPosition(0, 0, 30);
            // pre-register tile face assets so they resolve
            for (int i = 0; i < TYPES.Count; i++) addAsset(new TokenAssetData($"carcassonne/tiles/t{i}.svg"));
            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        protected override Task StartGame()
        {
            var rnd = new Random();
            var seats = GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT).Select(p => p.Id).ToList();
            GameData.Attributes["order"] = string.Join(",", seats);
            GameData.CurrentTurnId = seats[0];
            foreach (var s in seats) { GameData.Attributes["pts:" + s] = "0"; GameData.Attributes["meeplesLeft:" + s] = "7"; }

            // Build the bag (start tile 3 placed at 0,0 rot0 — a road curve is fine as a start).
            var bag = new List<int>();
            for (int t = 0; t < TYPES.Count; t++) for (int k = 0; k < TYPES[t].count; k++) bag.Add(t);
            // start tile: a straight road across the middle (type 2)
            SetBoard(new() { ["0,0"] = "2,0" });
            bag.Remove(2);
            Shuffle(bag, rnd);
            GameData.Attributes["bag"] = string.Join(",", bag);
            GameData.Attributes["meeples"] = "";
            GameData.Attributes["log"] = "";
            GameData.Attributes.Remove("over");
            DrawNext();
            Render();
            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(GameData.Attributes.ContainsKey("over"));
        protected override List<PlayerData> GetGameWinners()
        {
            var ids = GameData.Attributes.GetValueOrDefault("winnerIds", "");
            var set = ids.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            return GameData.Players.Where(p => set.Contains(p.Id)).ToList();
        }

        // ============================ actions ============================
        [GameAction] public async Task RotateTile(ExecuteActionData d) { if (MyTurn(d.Player!.Id) && Phase() == "place") { GameData.Attributes["currot"] = ((Rot() + 1) % 4).ToString(); Render(); } await Task.CompletedTask; }
        [GameAction] public async Task PlaceTile(ExecuteActionData d) { DoPlace(d.Player!.Id, int.Parse(Arg(d, "x")), int.Parse(Arg(d, "y"))); await Task.CompletedTask; }
        [GameAction] public async Task PlaceMeeple(ExecuteActionData d) { DoMeeple(d.Player!.Id, Arg(d, "kind"), int.Parse(Arg(d, "side"))); await Task.CompletedTask; }
        [GameAction] public async Task SkipMeeple(ExecuteActionData d) { if (MyTurn(d.Player!.Id) && Phase() == "meeple") EndTurnC(d.Player!.Id); await Task.CompletedTask; }

        private bool MyTurn(string seat) => seat == GameData.CurrentTurnId && !GameData.Attributes.ContainsKey("over");

        private void DoPlace(string seat, int x, int y)
        {
            if (!MyTurn(seat) || Phase() != "place") return;
            int cur = Cur(); int rot = Rot();
            if (!Legal(x, y, cur, rot)) return;
            var b = GetBoard(); b[$"{x},{y}"] = $"{cur},{rot}"; SetBoard(b);
            GameData.Attributes["lastX"] = x.ToString(); GameData.Attributes["lastY"] = y.ToString();
            GameData.Attributes["phase"] = "meeple";
            Render();
        }

        private void DoMeeple(string seat, string kind, int side)
        {
            if (!MyTurn(seat) || Phase() != "meeple") return;
            int x = int.Parse(GameData.Attributes["lastX"]), y = int.Parse(GameData.Attributes["lastY"]);
            int left = int.Parse(GameData.Attributes.GetValueOrDefault("meeplesLeft:" + seat, "0"));
            if (left > 0 && FeatureFree(x, y, kind, side))
            {
                var m = MeepleList(); m.Add($"{x}.{y}.{kind}.{side}.{seat}"); SetMeeples(m);
                GameData.Attributes["meeplesLeft:" + seat] = (left - 1).ToString();
            }
            EndTurnC(seat);
        }

        private void EndTurnC(string seat)
        {
            ScoreCompleted();
            var order = ListAttr("order");
            if (Bag().Count == 0) { EndGameScore(); Render(); return; }
            int i = order.IndexOf(seat);
            GameData.CurrentTurnId = order[(i + 1) % order.Count];
            GameData.Attributes["phase"] = "place";
            DrawNext();
            Render();
        }

        private void DrawNext()
        {
            var bag = Bag();
            if (bag.Count == 0) { GameData.Attributes["cur"] = "-1"; return; }
            // draw the first tile that has at least one legal placement; else discard.
            for (int k = 0; k < bag.Count; k++)
            {
                int t = bag[k];
                if (AnyLegal(t)) { bag.RemoveAt(k); SetBag(bag); GameData.Attributes["cur"] = t.ToString(); GameData.Attributes["currot"] = "0"; return; }
            }
            // none placeable (extremely rare) — clear the bag to end.
            SetBag(new List<int>()); GameData.Attributes["cur"] = "-1";
        }

        // ============================ placement / geometry ============================
        private static readonly (int dx, int dy, int side, int opp)[] DIRS =
        { (0, -1, 0, 2), (1, 0, 1, 3), (0, 1, 2, 0), (-1, 0, 3, 1) };   // N,E,S,W  side/opposite

        private static char EdgeAt(int type, int rot, int side) => TYPES[type].e[(side - rot + 4) % 4];

        private bool Legal(int x, int y, int type, int rot)
        {
            var b = GetBoard();
            if (b.ContainsKey($"{x},{y}")) return false;
            bool touches = false;
            foreach (var (dx, dy, side, opp) in DIRS)
            {
                if (b.TryGetValue($"{x + dx},{y + dy}", out var nv))
                {
                    touches = true;
                    var np = nv.Split(','); int nt = int.Parse(np[0]), nr = int.Parse(np[1]);
                    if (EdgeAt(type, rot, side) != EdgeAt(nt, nr, opp)) return false;
                }
            }
            return touches;
        }

        private bool AnyLegal(int type) => EmptyFrontier().Any(c => Enumerable.Range(0, 4).Any(r => Legal(c.x, c.y, type, r)));
        private List<(int x, int y)> LegalCells(int type, int rot) => EmptyFrontier().Where(c => Legal(c.x, c.y, type, rot)).ToList();

        private List<(int x, int y)> EmptyFrontier()
        {
            var b = GetBoard(); var set = new HashSet<(int, int)>();
            foreach (var key in b.Keys)
            {
                var p = key.Split(','); int x = int.Parse(p[0]), y = int.Parse(p[1]);
                foreach (var (dx, dy, _, _) in DIRS) { var c = (x + dx, y + dy); if (!b.ContainsKey($"{c.Item1},{c.Item2}")) set.Add(c); }
            }
            return set.Select(t => (t.Item1, t.Item2)).ToList();
        }

        // ============================ feature engine (flood fill over half-edges) ============================
        // A node is (x,y,side). Cities: all C-sides of a tile are one group. Roads: 2 R-sides connect,
        // else each is its own end. Fields: all F-sides are one group unless a straight road bisects.
        private List<HashSet<(int x, int y, int side)>> Components(char terrain)
        {
            var b = GetBoard();
            var nodes = new List<(int, int, int)>();
            foreach (var key in b.Keys)
            {
                var p = key.Split(','); int x = int.Parse(p[0]), y = int.Parse(p[1]);
                var v = b[key].Split(','); int t = int.Parse(v[0]), r = int.Parse(v[1]);
                for (int s = 0; s < 4; s++) if (EdgeAt(t, r, s) == terrain) nodes.Add((x, y, s));
            }
            var parent = new Dictionary<(int, int, int), (int, int, int)>();
            foreach (var n in nodes) parent[n] = n;
            (int, int, int) Find((int, int, int) n) { while (!parent[n].Equals(n)) { parent[n] = parent[parent[n]]; n = parent[n]; } return n; }
            void Union((int, int, int) a, (int, int, int) c) { var ra = Find(a); var rc = Find(c); if (!ra.Equals(rc)) parent[ra] = rc; }

            foreach (var key in b.Keys)
            {
                var p = key.Split(','); int x = int.Parse(p[0]), y = int.Parse(p[1]);
                var v = b[key].Split(','); int t = int.Parse(v[0]), r = int.Parse(v[1]);
                var sides = Enumerable.Range(0, 4).Where(s => EdgeAt(t, r, s) == terrain).ToList();
                // intra-tile unions
                if (terrain == 'C') { for (int i = 1; i < sides.Count; i++) Union((x, y, sides[0]), (x, y, sides[i])); }
                else if (terrain == 'R') { if (sides.Count == 2) Union((x, y, sides[0]), (x, y, sides[1])); }
                else /* F */ {
                    var roadSides = Enumerable.Range(0, 4).Where(s => EdgeAt(t, r, s) == 'R').ToList();
                    bool bisected = roadSides.Count == 2 && (roadSides[0] + 2) % 4 == roadSides[1];
                    if (!bisected) { for (int i = 1; i < sides.Count; i++) Union((x, y, sides[0]), (x, y, sides[i])); }
                    else { // split into the two halves either side of the straight road
                        int r0 = roadSides[0];
                        foreach (var grp in sides.GroupBy(s => ((s - r0 + 4) % 4) < 2)) { var g = grp.ToList(); for (int i = 1; i < g.Count; i++) Union((x, y, g[0]), (x, y, g[i])); }
                    }
                }
                // cross-tile unions
                foreach (var (dx, dy, side, opp) in DIRS)
                    if (EdgeAt(t, r, side) == terrain && b.ContainsKey($"{x + dx},{y + dy}"))
                        Union((x, y, side), (x + dx, y + dy, opp));
            }
            var groups = new Dictionary<(int, int, int), HashSet<(int, int, int)>>();
            foreach (var n in nodes) { var root = Find(n); if (!groups.ContainsKey(root)) groups[root] = new(); groups[root].Add(n); }
            return groups.Values.ToList();
        }

        private bool Complete(HashSet<(int x, int y, int side)> comp)
        {
            var b = GetBoard();
            foreach (var (x, y, side) in comp)
            { var (dx, dy, _, _) = DIRS[side]; if (!b.ContainsKey($"{x + dx},{y + dy}")) return false; }
            return true;
        }

        private void ScoreCompleted()
        {
            var meeples = MeepleList();
            foreach (var terrain in new[] { 'C', 'R' })
            {
                foreach (var comp in Components(terrain))
                {
                    if (!Complete(comp)) continue;
                    var tiles = comp.Select(n => (n.x, n.y)).Distinct().ToList();
                    var onit = meeples.Where(m => Parse(m) is var pm && pm.kind == terrain.ToString() && comp.Contains((pm.x, pm.y, pm.side))).ToList();
                    if (onit.Count == 0) continue;   // nobody scores an unclaimed feature
                    int pts = terrain == 'C' ? tiles.Count * 2 + Shields(tiles) * 2 : tiles.Count;
                    AwardMajority(onit, pts, terrain == 'C' ? "city" : "road");
                    meeples = meeples.Where(m => !onit.Contains(m)).ToList();   // return scored meeples
                }
            }
            // monasteries
            foreach (var m in meeples.ToList())
            {
                var pm = Parse(m);
                if (pm.kind != "M") continue;
                if (MonasterySurrounded(pm.x, pm.y)) { Award(pm.seat, 9, "monastery"); ReturnMeeple(pm.seat); meeples.Remove(m); }
            }
            SetMeeples(meeples);
        }

        private bool MonasterySurrounded(int x, int y)
        {
            var b = GetBoard();
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (!b.ContainsKey($"{x + dx},{y + dy}")) return false;
            return true;
        }

        private void EndGameScore()
        {
            var meeples = MeepleList();
            // incomplete cities/roads (reduced), monasteries (partial), then farms.
            foreach (var terrain in new[] { 'C', 'R' })
                foreach (var comp in Components(terrain))
                {
                    var onit = meeples.Where(m => Parse(m) is var pm && pm.kind == terrain.ToString() && comp.Contains((pm.x, pm.y, pm.side))).ToList();
                    if (onit.Count == 0) continue;
                    var tiles = comp.Select(n => (n.x, n.y)).Distinct().ToList();
                    int pts = terrain == 'C' ? tiles.Count + Shields(tiles) : tiles.Count;   // 1/tile at end
                    if (!Complete(comp)) AwardMajority(onit, pts, "end " + (terrain == 'C' ? "city" : "road"));
                }
            foreach (var m in meeples)
            {
                var pm = Parse(m);
                if (pm.kind == "M") { int cnt = MonasteryNeighbors(pm.x, pm.y) + 1; Award(pm.seat, cnt, "end monastery"); }
            }
            // farms: 3 per completed city sharing a tile with the field component
            var completedCities = Components('C').Where(Complete).ToList();
            foreach (var comp in Components('F'))
            {
                var onit = meeples.Where(m => Parse(m) is var pm && pm.kind == "F" && comp.Contains((pm.x, pm.y, pm.side))).ToList();
                if (onit.Count == 0) continue;
                var fieldTiles = comp.Select(n => (n.x, n.y)).Distinct().ToHashSet();
                int cities = completedCities.Count(city => city.Select(n => (n.x, n.y)).Any(fieldTiles.Contains));
                if (cities > 0) AwardMajority(onit, cities * 3, "farm");
            }
            GameData.Attributes["over"] = "1";
            var order = ListAttr("order");
            int best = order.Max(Pts);
            var top = order.Where(s => Pts(s) == best).ToList();
            GameData.Attributes["winnerIds"] = string.Join(",", top);
            GameData.Attributes["result"] = top.Count == 1 ? $"{Name(top[0])} wins with {best} points!" : "Tie!";
        }

        private void AwardMajority(List<string> meeplesOn, int pts, string what)
        {
            var byOwner = meeplesOn.GroupBy(m => Parse(m).seat).ToDictionary(g => g.Key, g => g.Count());
            int mx = byOwner.Values.Max();
            foreach (var kv in byOwner.Where(kv => kv.Value == mx)) Award(kv.Key, pts, what);
            foreach (var m in meeplesOn) ReturnMeeple(Parse(m).seat);
        }
        private void Award(string seat, int pts, string what) { GameData.Attributes["pts:" + seat] = (Pts(seat) + pts).ToString(); Log($"{Name(seat)} +{pts} ({what})"); }
        private void ReturnMeeple(string seat) { int l = int.Parse(GameData.Attributes.GetValueOrDefault("meeplesLeft:" + seat, "0")); GameData.Attributes["meeplesLeft:" + seat] = (l + 1).ToString(); }

        private int Shields(List<(int x, int y)> tiles) => tiles.Count(t => { var v = GetBoard()[$"{t.x},{t.y}"].Split(','); return TYPES[int.Parse(v[0])].shield; });
        private int MonasteryNeighbors(int x, int y) { var b = GetBoard(); int c = 0; for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (!(dx == 0 && dy == 0) && b.ContainsKey($"{x + dx},{y + dy}")) c++; return c; }

        // Is the feature the meeple would join currently unclaimed?
        private bool FeatureFree(int x, int y, string kind, int side)
        {
            var meeples = MeepleList();
            if (kind == "M") return !meeples.Any(m => Parse(m) is var p && p.x == x && p.y == y && p.kind == "M");
            char terr = kind == "C" ? 'C' : kind == "R" ? 'R' : 'F';
            var comp = Components(terr).FirstOrDefault(c => c.Contains((x, y, side)));
            if (comp == null) return true;
            return !meeples.Any(m => Parse(m) is var p && p.kind == kind && comp.Contains((p.x, p.y, p.side)));
        }

        // Features on the just-placed tile a meeple could go on (kind,side,label), free ones only.
        private List<(string kind, int side, string label)> MeepleOptions(int x, int y)
        {
            var b = GetBoard(); var v = b[$"{x},{y}"].Split(','); int t = int.Parse(v[0]), r = int.Parse(v[1]);
            var res = new List<(string, int, string)>();
            if (TYPES[t].mon && FeatureFree(x, y, "M", -1)) res.Add(("M", -1, "Monk (monastery)"));
            var cSides = Enumerable.Range(0, 4).Where(s => EdgeAt(t, r, s) == 'C').ToList();
            if (cSides.Count > 0 && FeatureFree(x, y, "C", cSides[0])) res.Add(("C", cSides[0], "Knight (city)"));
            // road groups: each straight/curve pair once, each end once
            var rSides = Enumerable.Range(0, 4).Where(s => EdgeAt(t, r, s) == 'R').ToList();
            var seen = new HashSet<int>();
            // 3+ road-edges = a junction: each arm is a separate road, so label it with its
            // direction (N/E/S/W) — otherwise the buttons all read the same.
            var DIR = new[] { "N", "E", "S", "W" };
            foreach (var s in rSides) { if (seen.Contains(s)) continue; if (FeatureFree(x, y, "R", s)) res.Add(("R", s, rSides.Count >= 3 ? $"Thief (road {DIR[s]})" : "Thief (road)")); if (rSides.Count == 2) { seen.Add(rSides[0]); seen.Add(rSides[1]); } else seen.Add(s); }
            var fSides = Enumerable.Range(0, 4).Where(s => EdgeAt(t, r, s) == 'F').ToList();
            if (fSides.Count > 0 && FeatureFree(x, y, "F", fSides[0])) res.Add(("F", fSides[0], "Farmer (field)"));
            return res;
        }

        // ============================ AI ============================
        public override bool IsAITurn(PlayerData player) => MyTurn(player.Id);
        public override async Task<bool> PlayAI(PlayerData player, Random rnd)
        {
            if (!MyTurn(player.Id)) { await Task.CompletedTask; return false; }
            string seat = player.Id;
            if (Phase() == "place")
            {
                int cur = Cur();
                for (int r = 0; r < 4; r++) { var cells = LegalCells(cur, r); if (cells.Count > 0) { GameData.Attributes["currot"] = r.ToString(); var c = cells[rnd.Next(cells.Count)]; DoPlace(seat, c.x, c.y); break; } }
            }
            if (Phase() == "meeple")
            {
                int x = int.Parse(GameData.Attributes["lastX"]), y = int.Parse(GameData.Attributes["lastY"]);
                var opts = MeepleOptions(x, y);
                int left = int.Parse(GameData.Attributes.GetValueOrDefault("meeplesLeft:" + seat, "0"));
                // claim a city or road ~60% of the time if a meeple is free
                var pick = opts.FirstOrDefault(o => o.kind == "C") ;
                if (pick.label == null) pick = opts.FirstOrDefault(o => o.kind == "R" || o.kind == "M");
                if (left > 0 && pick.label != null && rnd.NextDouble() < 0.6) DoMeeple(seat, pick.kind, pick.side);
                else EndTurnC(seat);
            }
            await Task.CompletedTask; return true;
        }

        // ============================ 3D RENDER (board + player zones; no panel) ============================
        protected override void RefreshScreens() => Render();

        private void Render()
        {
            GameData.Table = ItemData.Table();

            bool over = GameData.Attributes.ContainsKey("over");
            string cur = GameData.CurrentTurnId ?? "";
            var b = GetBoard();

            // ---- board bounding box (in cells). The whole scene is RE-CENTRED on the origin every
            // render: cell (x,y) draws at (x*SZ - cx, 0, y*SZ - cz). The camera orbits (0,0,0), so
            // the growing map always stays centred in view, and the HUD is laid out relative to the
            // board's edges (title/scores above the far edge, current tile + buttons below the near
            // edge) — never colliding with tiles no matter how the map grows. Legal-placement markers
            // extend one cell beyond the tiles, so include them in the bounds during the place phase.
            var legal = (!over && Cur() >= 0 && Phase() == "place") ? LegalCells(Cur(), Rot()) : new List<(int x, int y)>();
            var cells = b.Keys.Select(k => { var p = k.Split(','); return (x: int.Parse(p[0]), y: int.Parse(p[1])); }).Concat(legal).ToList();
            if (cells.Count == 0) cells.Add((0, 0));
            double minX = cells.Min(c => c.x) * SZ - SZ / 2, maxX = cells.Max(c => c.x) * SZ + SZ / 2;
            double minZ = cells.Min(c => c.y) * SZ - SZ / 2, maxZ = cells.Max(c => c.y) * SZ + SZ / 2;
            double cx = (minX + maxX) / 2, cz = (minZ + maxZ) / 2;   // board centre → drawn at origin
            double halfZ = (maxZ - minZ) / 2;
            double hudTopZ = -halfZ - 5;    // title/scores row, above the far edge
            double hudBotZ = halfZ + 6;     // current tile / meeple buttons, below the near edge

            // The server owns the camera: pull it up/back as the board grows so map + HUD always fit.
            // The client re-applies a camera only when this value CHANGES (manual orbit stays free).
            double span = Math.Max(maxX - minX, (maxZ - minZ) + 14);
            double s = Math.Max(1.0, span / 36.0);
            int camY = (int)Math.Round(28 * s), camZ = (int)Math.Round(18 * s);
            foreach (var p in GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT))
                p.SetCameraPosition(0, camY, camZ);
            GameData.Observer.Position.Set(0, camY + 4, camZ + 2);

            // a big neutral mat under the map, for contrast/framing against the skybox — growing
            // with the board (always comfortably larger than map + HUD).
            double matSize = Math.Max(60, Math.Max(maxX - minX, maxZ - minZ) + 30);
            addItem(Assets.MAT).SetPosition(0, -0.15, 0).SetScale(matSize, 0.2, matSize).AddAttribute("tint", "0x123021");

            foreach (var key in b.Keys)
            {
                var p = key.Split(','); int x = int.Parse(p[0]), y = int.Parse(p[1]);
                var v = b[key].Split(','); int t = int.Parse(v[0]), r = int.Parse(v[1]);
                addItem(TileAsset(t)).SetPosition(x * SZ - cx, 0, y * SZ - cz).SetRotation(0, -90 * r, 0).SetScale(SZ, 1, SZ).AddAttribute("tile", "1");
            }
            var order = ListAttr("order");
            foreach (var m in MeepleList())
            {
                var pm = Parse(m); int oi = Math.Max(0, order.IndexOf(pm.seat));
                double ox = pm.side == 1 ? 1.1 : pm.side == 3 ? -1.1 : 0, oz = pm.side == 0 ? -1.1 : pm.side == 2 ? 1.1 : 0;
                addItem(Assets.MEEPLE).SetPosition(pm.x * SZ + ox - cx, 0.6, pm.y * SZ + oz - cz).SetScale(0.9, 1.6, 0.9)
                    .AddAttribute("tint", MEEPLE_COLORS[oi % MEEPLE_COLORS.Length]).AddAttribute("meeple", "1");
            }

            // HUD lives in world space but ELEVATED (y>0) so it floats above the tile plane, and is
            // anchored to the CURRENT board bounds (hudTopZ/hudBotZ/cx) so it tracks the map as it
            // grows. Scores across the far edge, the current tile + meeple choices across the near
            // edge. Text laid flat (-90) to read from above.
            addTextItem(Assets.TEXT).SetText(over ? GameData.Attributes.GetValueOrDefault("result", "Game over")
                : $"CARCASSONNE   ·   {Name(cur)}'s turn   ·   {Bag().Count} tiles left")
                .SetPosition(0, 6, hudTopZ - 3).SetScale(1.3).SetRotation(-90, 0, 0).AddAttribute("textColor", "ffd166");

            for (int i = 0; i < order.Count; i++)
            {
                var s = order[i];
                addTextItem(Assets.TEXT)
                    .SetText($"{Name(s)}  {Pts(s)}  ({GameData.Attributes.GetValueOrDefault("meeplesLeft:" + s, "0")}m)")
                    .SetPosition(-((order.Count - 1) * 8.0) / 2 + i * 8.0, 6, hudTopZ).SetScale(0.8).SetRotation(-90, 0, 0)
                    .AddAttribute("textColor", s == cur ? "ffd166" : "cbd5e1");
            }

            if (over || Cur() < 0) return;

            if (Phase() == "place")
            {
                // green placement markers on the board (click to place)
                foreach (var c in legal)
                {
                    var mk = addItem(Assets.MARKER).SetPosition(c.x * SZ - cx, 0.05, c.y * SZ - cz).SetScale(SZ * 0.9, 0.2, SZ * 0.9)
                        .AddAttribute("tint", "0x22c55e").AddAttribute("marker", "1");
                    mk.ClickActions[cur] = nameof(PlaceTile);
                    mk.AddAttribute("x", c.x.ToString()).AddAttribute("y", c.y.ToString());
                }
                // the current tile floats just below the board's near edge — click it to rotate,
                // then a green square. Tracks the board as it grows.
                addItem(TileAsset(Cur())).SetPosition(-6, 4, hudBotZ).SetRotation(0, -90 * Rot(), 0).SetScale(SZ, 1, SZ)
                    .AddAttribute("tile", "1").ClickActions[cur] = nameof(RotateTile);
                addTextItem(Assets.TEXT).SetText("click tile = rotate  ·  click a green square")
                    .SetPosition(1.5, 4, hudBotZ).SetScale(0.5).SetRotation(-90, 0, 0).AddAttribute("textColor", "cbd5e1");
            }
            else if (Phase() == "meeple")
            {
                int x = int.Parse(GameData.Attributes["lastX"]), y = int.Parse(GameData.Attributes["lastY"]);
                var opts = MeepleOptions(x, y);
                int left = int.Parse(GameData.Attributes.GetValueOrDefault("meeplesLeft:" + cur, "0"));
                var buttons = new List<(string label, string col, string action, Dictionary<string, string>? args)>();
                if (left > 0) foreach (var o in opts) buttons.Add((o.label, "0x2f7a45", nameof(PlaceMeeple), new() { { "kind", o.kind }, { "side", o.side.ToString() } }));
                buttons.Add(("Skip", "0x6a4a25", nameof(SkipMeeple), null));
                for (int i = 0; i < buttons.Count; i++)
                {
                    double bx = -((buttons.Count - 1) * 5.0) / 2 + i * 5.0;
                    var bt = addItem(Assets.MARKER).SetPosition(bx, 4, hudBotZ).SetScale(4.6, 0.4, 1.8).AddAttribute("tint", buttons[i].col).AddAttribute("button", "1");
                    bt.ClickActions[cur] = buttons[i].action;
                    if (buttons[i].args != null) foreach (var kv in buttons[i].args!) bt.AddAttribute(kv.Key, kv.Value);
                    addTextItem(Assets.TEXT).SetText(buttons[i].label).SetPosition(bx, 4.3, hudBotZ).SetScale(0.34).SetRotation(-90, 0, 0).AddAttribute("textColor", "ffffff");
                }
            }
        }

        private static readonly string[] MEEPLE_COLORS = { "0xef4444", "0x2563eb", "0x16a34a", "0xf59e0b", "0x9333ea" };
        private AssetData TileAsset(int t) => addAsset(new TokenAssetData($"carcassonne/tiles/t{t}.svg"));

        // ============================ state helpers ============================
        private string Phase() => GameData.Attributes.GetValueOrDefault("phase", "place");
        private int Cur() => int.TryParse(GameData.Attributes.GetValueOrDefault("cur", "-1"), out var v) ? v : -1;
        private int Rot() => int.TryParse(GameData.Attributes.GetValueOrDefault("currot", "0"), out var v) ? v : 0;
        private int Pts(string seat) => int.TryParse(GameData.Attributes.GetValueOrDefault("pts:" + seat, "0"), out var v) ? v : 0;

        private Dictionary<string, string> GetBoard()
        {
            var d = new Dictionary<string, string>();
            foreach (var e in (GameData.Attributes.GetValueOrDefault("board", "") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            { var i = e.IndexOf('='); d[e.Substring(0, i)] = e.Substring(i + 1); }
            return d;
        }
        private void SetBoard(Dictionary<string, string> b) => GameData.Attributes["board"] = string.Join(";", b.Select(kv => kv.Key + "=" + kv.Value));
        private List<int> Bag() => (GameData.Attributes.GetValueOrDefault("bag", "") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        private void SetBag(List<int> b) => GameData.Attributes["bag"] = string.Join(",", b);
        private List<string> MeepleList() => (GameData.Attributes.GetValueOrDefault("meeples", "") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        private void SetMeeples(List<string> m) => GameData.Attributes["meeples"] = string.Join(";", m);
        private (int x, int y, string kind, int side, string seat) Parse(string m)
        { var p = m.Split('.'); return (int.Parse(p[0]), int.Parse(p[1]), p[2], int.Parse(p[3]), p[4]); }

        private List<string> ListAttr(string key) => (GameData.Attributes.GetValueOrDefault(key, "") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        private string Name(string seat) { var p = GameData.Players.Find(x => x.Id == seat); return p != null ? PlayerDisplayName(p) : "?"; }
        private void Log(string line) { var cur = GameData.Attributes.GetValueOrDefault("log", ""); var lines = (cur + (string.IsNullOrEmpty(cur) ? "" : "\n") + line).Split('\n'); GameData.Attributes["log"] = string.Join("\n", lines.TakeLast(12)); }
        private static void Shuffle<T>(List<T> l, Random r) { for (int i = l.Count - 1; i > 0; i--) { int j = r.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); } }
    }
}
