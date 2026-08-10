using System;
using System.Collections.Generic;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Gomoku / five-in-a-row on an effectively UNLIMITED board, in the classic
    // paper-and-pencil style: a white graph-paper grid with X (red) and O (blue) marks.
    // The grid is built from white tiles that abut into a continuous grid and auto-expand
    // around the played area, so you can always keep playing outward in any direction.
    // Freestyle rules: five OR MORE in a row wins. X moves first.
    public class GomokuGameFlow : BoardGameFlow
    {
        internal class Assets
        {
            // White grid tile (thin border) — tiles abut into a seamless graph-paper grid.
            internal static AssetData CELL = new TokenAssetData("gomoku/cell.png");
            // The marks, reusing the tic-tac-toe X / O models (tinted red / blue).
            internal static AssetData MARK_X = new ObjectAssetData("ticktacktoe/x.glb");
            internal static AssetData MARK_O = new ObjectAssetData("ticktacktoe/o.glb");
            internal static AssetData TURN_TEXT = new Text3dAssetData("turn");
        }

        private const double SPACING = 1.0; // one world unit per cell (tiles are 1×1, so they tile)
        private const int INIT = 3;         // initial half-size → 7x7 starting grid
        private const int MARGIN = 2;       // empty ring kept around the played area

        private const string X_COLOR = "0xD83A34"; // red
        private const string O_COLOR = "0x2E6FD6"; // blue

        public GomokuGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.GOMOKU;
        }

        protected override Task Create()
        {
            addAsset(Assets.CELL);
            addAsset(Assets.MARK_X);
            addAsset(Assets.MARK_O);
            addAsset(Assets.TURN_TEXT);

            GameData.Observer.Position.Set(0, 16, 0);

            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "X")
                .SetCameraPosition(0, 12, -12)
                .SetAvatarPosition(0, 2, -11);

            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "O")
                .SetCameraPosition(0, 12, 12)
                .SetAvatarPosition(0, 2, 11);

            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        protected override Task StartGame()
        {
            GameData.Attributes["turn"] = "X"; // X moves first
            EnsureCells();
            RebindCellActions();
            UpdateTurnText();
            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;

        // ------------------------------------------------------------------
        // Placing a mark (human clicks an empty intersection tile).
        // ------------------------------------------------------------------
        [GameAction]
        public async Task PlaceStone(ExecuteActionData data)
        {
            if (GameData.Attributes.ContainsKey("over")) { await Task.CompletedTask; return; }

            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "X";
            var current = getPlayerByAttribute("type", turn);
            if (current == null || data.Player == null) { await Task.CompletedTask; return; }
            if (current.User != null && data.Player.User?.Id != current.User.Id) { await Task.CompletedTask; return; } // not your turn
            if (data.Item == null || !data.Item.HaveAttribute("cell")) { await Task.CompletedTask; return; }

            PlaceStoneAt(data.Item.GetIntAttribute("gx"), data.Item.GetIntAttribute("gy"), turn);
            await Task.CompletedTask;
        }

        // Core placement, shared by the human action and the AI.
        private void PlaceStoneAt(int gx, int gy, string mark)
        {
            SaveUndoPoint();
            char c = mark == "X" ? 'b' : 'w'; // engine uses b/w internally (X→b, O→w)

            if (BuildStoneMap().ContainsKey((gx, gy))) return; // occupied

            addItem(mark == "X" ? Assets.MARK_X : Assets.MARK_O)
                .SetPosition(gx * SPACING, 0.15, gy * SPACING)
                .SetScale(0.65)
                .AddAttribute("stone", "1")
                .AddAttribute("color", mark)          // "X" or "O"
                .AddAttribute("gx", gx.ToString())
                .AddAttribute("gy", gy.ToString())
                .AddAttribute("tint", mark == "X" ? X_COLOR : O_COLOR);
            // NOTE: the grid tile underneath is kept, so the graph-paper grid stays continuous.

            var map = BuildStoneMap(); // includes the mark just added
            if (GomokuRules.IsWinningMove(map, gx, gy, c))
            {
                var wp = getPlayerByAttribute("type", mark);
                string who = wp != null ? PlayerDisplayName(wp) : mark;
                GameData.Attributes["over"] = "1";
                GameData.Attributes["winnerColor"] = mark;
                GameData.Attributes["result"] = mark + " (" + who + ") wins!";
                SetBoardText(mark + " WINS!", mark == "X" ? X_COLOR : O_COLOR);

                var line = GomokuRules.WinningLine(map, gx, gy, c);
                if (line != null) DrawWinLine(line.Value.Item1, line.Value.Item2);
                return;
            }

            GameData.Attributes["turn"] = mark == "X" ? "O" : "X";
            EnsureCells();
            RebindCellActions();
            UpdateTurnText();
        }

        // ------------------------------------------------------------------
        // Heuristic AI (win / block / best threat near the fight).
        // ------------------------------------------------------------------
        public override bool IsAITurn(PlayerData player)
        {
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "X";
            return player.GetStringAttribute("type") == turn;
        }

        public override async Task<bool> PlayAI(PlayerData player, Random rnd)
        {
            if (GameData.Attributes.ContainsKey("over")) { await Task.CompletedTask; return false; }

            string myType = player.GetStringAttribute("type"); // "X" / "O"
            char me = myType == "X" ? 'b' : 'w';
            var map = BuildStoneMap();

            int mx, my;
            if (map.Count == 0)
            {
                mx = 0; my = 0; // open in the centre
            }
            else
            {
                var cand = new HashSet<(int, int)>();
                foreach (var kv in map)
                    for (int dx = -2; dx <= 2; dx++)
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            var p = (kv.Key.Item1 + dx, kv.Key.Item2 + dy);
                            if (!map.ContainsKey(p)) cand.Add(p);
                        }
                var choice = GomokuRules.ChooseMove(map, me, cand);
                if (choice == null) { await Task.CompletedTask; return false; }
                mx = choice.Value.x; my = choice.Value.y;
            }

            PlaceStoneAt(mx, my, myType);
            await Task.CompletedTask;
            return true;
        }

        // ------------------------------------------------------------------
        // Board helpers.
        // ------------------------------------------------------------------
        private Dictionary<(int, int), char> BuildStoneMap()
        {
            var map = new Dictionary<(int, int), char>();
            foreach (var s in getItemsByAttribute("stone"))
                map[(s.GetIntAttribute("gx"), s.GetIntAttribute("gy"))] =
                    s.GetStringAttribute("color") == "X" ? 'b' : 'w';
            return map;
        }

        // Grow the grid to cover the played area + a margin ring. Tiles are NEVER removed,
        // so the graph-paper grid stays continuous under the marks.
        private void EnsureCells()
        {
            var stones = getItemsByAttribute("stone");

            int minx = -INIT, maxx = INIT, miny = -INIT, maxy = INIT;
            if (stones.Count > 0)
            {
                minx = int.MaxValue; maxx = int.MinValue; miny = int.MaxValue; maxy = int.MinValue;
                foreach (var s in stones)
                {
                    int gx = s.GetIntAttribute("gx"), gy = s.GetIntAttribute("gy");
                    minx = Math.Min(minx, gx); maxx = Math.Max(maxx, gx);
                    miny = Math.Min(miny, gy); maxy = Math.Max(maxy, gy);
                }
                minx -= MARGIN; maxx += MARGIN; miny -= MARGIN; maxy += MARGIN;
            }

            var haveCell = new HashSet<(int, int)>();
            foreach (var cItem in getItemsByAttribute("cell"))
                haveCell.Add((cItem.GetIntAttribute("gx"), cItem.GetIntAttribute("gy")));

            for (int gx = minx; gx <= maxx; gx++)
                for (int gy = miny; gy <= maxy; gy++)
                    if (!haveCell.Contains((gx, gy))) AddCell(gx, gy);
        }

        // Draw a green bar over the winning run (from stone (sx,sy) to (ex,ey)).
        private void DrawWinLine((int sx, int sy) a, (int ex, int ey) b)
        {
            double x1 = a.sx * SPACING, z1 = a.sy * SPACING;
            double x2 = b.ex * SPACING, z2 = b.ey * SPACING;
            double midx = (x1 + x2) / 2.0, midz = (z1 + z2) / 2.0;
            double dxw = x2 - x1, dzw = z2 - z1;
            double len = Math.Sqrt(dxw * dxw + dzw * dzw) + 0.7; // extend a touch past the end stones
            double angDeg = Math.Atan2(-dzw, dxw) * 180.0 / Math.PI;

            addItem(Assets.CELL) // reuse the tile mesh, tinted green, stretched thin
                .SetPosition(midx, 0.3, midz) // above the marks
                .SetScale(len, 1, 0.22)
                .SetRotation(0, angDeg, 0)
                .AddAttribute("winbar", "1")
                .AddAttribute("tint", "0x22C55E");
        }

        private void AddCell(int gx, int gy)
        {
            addItem(Assets.CELL)
                .SetPosition(gx * SPACING, 0, gy * SPACING)
                .SetScale(SPACING) // 1×1 tile → tiles abut into a seamless grid
                .AddAttribute("cell", "1")
                .AddAttribute("gx", gx.ToString())
                .AddAttribute("gy", gy.ToString());
        }

        // Bind place-stone to every seat controlled by the current-turn user, but ONLY on
        // empty tiles (occupied tiles keep the grid look but aren't clickable).
        private void RebindCellActions()
        {
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "X";
            var current = getPlayerByAttribute("type", turn);

            var seatIds = GameData.Players
                .Where(p => current?.User != null && p.User?.Id == current.User.Id)
                .Select(p => p.Id)
                .ToList();
            if (seatIds.Count == 0 && current != null) seatIds.Add(current.Id); // AI / empty seat

            var occupied = new HashSet<(int, int)>();
            foreach (var s in getItemsByAttribute("stone"))
                occupied.Add((s.GetIntAttribute("gx"), s.GetIntAttribute("gy")));

            foreach (var cell in getItemsByAttribute("cell"))
            {
                cell.ClickActions = new Dictionary<string, string>();
                if (occupied.Contains((cell.GetIntAttribute("gx"), cell.GetIntAttribute("gy")))) continue;
                foreach (var sid in seatIds) cell.AddAction(sid, PlaceStone);
            }
        }

        private void UpdateTurnText()
        {
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "X";
            SetBoardText(turn + " TO MOVE", turn == "X" ? X_COLOR : O_COLOR);
        }

        private void SetBoardText(string label, string tint)
        {
            foreach (var t in getItemsByAttribute("turnText")) removeItem(t.Id);

            int ext = INIT;
            foreach (var it in getItemsByAttribute("cell").Concat(getItemsByAttribute("stone")))
                ext = Math.Max(ext, Math.Max(Math.Abs(it.GetIntAttribute("gx")), Math.Abs(it.GetIntAttribute("gy"))));
            double d = (ext + 1.2) * SPACING;

            (double x, double z, double roll)[] sides =
            {
                (0, -d, 180), (0, d, 0), (-d, 0, -90), (d, 0, 90),
            };
            foreach (var s in sides)
            {
                addTextItem(Assets.TURN_TEXT)
                    .SetText(label)
                    .SetPosition(s.x, 0.1, s.z)
                    .SetScale(0.7)
                    .SetRotation(-90, 0, s.roll)
                    .AddAttribute("turnText", "1")
                    .AddAttribute("tint", tint);
            }
        }
    }
}
