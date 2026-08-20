using System;
using System.Collections.Generic;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Reversi / Othello on the 8x8 board. Place a disc to flank and flip the opponent's
    // discs; most discs at the end wins. Black moves first. If a player has no legal move
    // they pass; if neither can move the game ends. Strong positional minimax AI.
    public class ReversiGameFlow : BoardGameFlow
    {
        internal class Assets
        {
            // Green Othello board (grid + hoshi dots) as a flat textured tile. Scale is applied
            // to the board ITEM (TOKENs render as a 1x1 tile), so the board is 8x8 world units.
            internal static AssetData BOARD = new TokenAssetData("reversi/board.png");
            // A true round disc, reused for discs and move markers (per-item scale + tint).
            internal static AssetData DISC = new CylinderAssetData("disc") { Scale = new V3(1) };
            internal static AssetData TURN_TEXT = new Text3dAssetData("turn");
        }

        // Cell centres on a clean 8x8 grid: cell = 1 world unit, board spans -4..+4.
        private static readonly double[] COORDS = { -3.5, -2.5, -1.5, -0.5, 0.5, 1.5, 2.5, 3.5 };
        private const string BLACK = "0x151515";
        private const string WHITE = "0xF2F2F2";

        public ReversiGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.REVERSI;
        }

        protected override Task Create()
        {
            addAsset(Assets.BOARD);
            addAsset(Assets.DISC);
            addAsset(Assets.TURN_TEXT);

            GameData.Observer.Position.Set(0, 12, 0);

            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "black").SetCameraPosition(0, 9, -9).SetAvatarPosition(0, 2, -8);
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "white").SetCameraPosition(0, 9, 9).SetAvatarPosition(0, 2, 8);

            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        protected override Task StartGame()
        {
            // Board centred at the origin. The texture is a 10-unit tile: an 8x8 green grid
            // (spanning -4..+4, matching COORDS) inside a 1-unit wooden frame on every side
            // (the frame is where the turn / score text is drawn).
            addItem(Assets.BOARD).SetPosition(0, 0, 0).SetScale(10, 1, 10);
            GameData.Attributes["turn"] = "black";

            // Standard opening four discs.
            AddDisc(3, 3, "white"); AddDisc(4, 4, "white");
            AddDisc(3, 4, "black"); AddDisc(4, 3, "black");

            RebuildMarkers();
            UpdateTurnText();
            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;

        // ------------------------------------------------------------------
        [GameAction]
        public async Task PlaceDisc(ExecuteActionData data)
        {
            if (GameData.Attributes.ContainsKey("over")) { await Task.CompletedTask; return; }
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "black";
            var current = getPlayerByAttribute("type", turn);
            if (current == null || data.Player == null) { await Task.CompletedTask; return; }
            if (!CallerToMove(data)) { await Task.CompletedTask; return; } // not your turn (AI turns included)
            if (data.Item == null || !data.Item.HaveAttribute("moveMarker")) { await Task.CompletedTask; return; }

            PlaceDiscAt(data.Item.GetIntAttribute("gx"), data.Item.GetIntAttribute("gy"), turn);
            await Task.CompletedTask;
        }

        private void PlaceDiscAt(int c, int r, string colorType)
        {
            SaveUndoPoint();
            var board = BuildBoard();
            char cc = colorType == "black" ? 'b' : 'w';
            var flips = ReversiRules.Flips(board, c, r, cc);
            if (flips.Count == 0) return; // not a legal move

            AddDisc(c, r, colorType);
            foreach (var (fc, fr) in flips)
            {
                var old = FindDisc(fc, fr);
                if (old != null) removeItem(old.Id);
                AddDisc(fc, fr, colorType); // recolour by recreating (client re-tints on create)
            }

            AdvanceTurnOrEnd(colorType);
        }

        private void AdvanceTurnOrEnd(string justMoved)
        {
            var board = BuildBoard();
            string other = justMoved == "black" ? "white" : "black";
            char oc = other == "black" ? 'b' : 'w';
            char jc = justMoved == "black" ? 'b' : 'w';

            if (ReversiRules.HasMove(board, oc)) GameData.Attributes["turn"] = other;
            else if (ReversiRules.HasMove(board, jc)) GameData.Attributes["turn"] = justMoved; // opponent passes
            else { EndReversi(board); return; }

            RebuildMarkers();
            UpdateTurnText();
        }

        private void EndReversi(char[,] board)
        {
            foreach (var m in getItemsByAttribute("moveMarker")) removeItem(m.Id);

            int nb = ReversiRules.Count(board, 'b'), nw = ReversiRules.Count(board, 'w');
            int hi = Math.Max(nb, nw), lo = Math.Min(nb, nw);
            GameData.Attributes["over"] = "1";

            string winner = nb > nw ? "black" : nw > nb ? "white" : "";
            if (winner != "")
            {
                var wp = getPlayerByAttribute("type", winner);
                string who = wp != null ? PlayerDisplayName(wp) : winner;
                GameData.Attributes["winnerColor"] = winner;
                GameData.Attributes["result"] = Cap(winner) + " (" + who + ") wins " + hi + "–" + lo + "!";
                SetBoardText(winner.ToUpper() + " WINS " + hi + "-" + lo, winner == "black" ? BLACK : WHITE);
            }
            else
            {
                GameData.Attributes["result"] = "Draw " + nb + "–" + nw + ".";
                SetBoardText("DRAW " + nb + "-" + nw, "0x888888");
            }
        }

        // ------------------------------------------------------------------
        public override bool IsAITurn(PlayerData player)
        {
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "black";
            return player.GetStringAttribute("type") == turn;
        }

        public override async Task<bool> PlayAI(PlayerData player, Random rnd)
        {
            if (GameData.Attributes.ContainsKey("over")) { await Task.CompletedTask; return false; }
            char me = player.GetStringAttribute("type") == "black" ? 'b' : 'w';
            var choice = ReversiRules.ChooseMove(BuildBoard(), me, 3, rnd);
            if (choice == null) { await Task.CompletedTask; return false; }
            PlaceDiscAt(choice.Value.c, choice.Value.r, player.GetStringAttribute("type"));
            await Task.CompletedTask;
            return true;
        }

        // ------------------------------------------------------------------
        private char[,] BuildBoard()
        {
            var b = new char[8, 8];
            foreach (var d in getItemsByAttribute("disc"))
                b[d.GetIntAttribute("gx"), d.GetIntAttribute("gy")] =
                    d.GetStringAttribute("color") == "black" ? 'b' : 'w';
            return b;
        }

        private ItemData FindDisc(int c, int r)
        {
            foreach (var d in getItemsByAttribute("disc"))
                if (d.GetIntAttribute("gx") == c && d.GetIntAttribute("gy") == r) return d;
            return null;
        }

        private void AddDisc(int c, int r, string colorType)
        {
            addItem(Assets.DISC)
                .SetPosition(COORDS[c], 0.12, COORDS[r])
                .SetScale(0.85)
                .AddAttribute("disc", "1")
                .AddAttribute("color", colorType)
                .AddAttribute("gx", c.ToString())
                .AddAttribute("gy", r.ToString())
                .AddAttribute("tint", colorType == "black" ? BLACK : WHITE);
        }

        private void RebuildMarkers()
        {
            foreach (var m in getItemsByAttribute("moveMarker")) removeItem(m.Id);

            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "black";
            char tc = turn == "black" ? 'b' : 'w';
            var current = getPlayerByAttribute("type", turn);
            var seatIds = GameData.Players
                .Where(p => current?.User != null && p.User?.Id == current.User.Id)
                .Select(p => p.Id).ToList();
            if (seatIds.Count == 0 && current != null) seatIds.Add(current.Id);

            foreach (var (c, r) in ReversiRules.LegalMoves(BuildBoard(), tc))
            {
                var mk = addItem(Assets.DISC)
                    .SetPosition(COORDS[c], 0.08, COORDS[r])
                    .SetScale(0.40)
                    .AddAttribute("moveMarker", "1")
                    .AddAttribute("gx", c.ToString())
                    .AddAttribute("gy", r.ToString())
                    .AddAttribute("tint", "0x66DD66");
                foreach (var sid in seatIds) mk.AddAction(sid, PlaceDisc);
            }
        }

        private void UpdateTurnText()
        {
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "black";
            var board = BuildBoard();
            int nw = ReversiRules.Count(board, 'w');
            int nb = ReversiRules.Count(board, 'b');
            SetBoardText(turn.ToUpper() + " TO MOVE    W:" + nw + " B:" + nb, turn == "black" ? BLACK : WHITE);
        }

        private void SetBoardText(string label, string tint)
        {
            foreach (var t in getItemsByAttribute("turnText")) removeItem(t.Id);
            // Positioned on the wooden frame (grid ends at ±4, frame centre ≈ ±4.5).
            (double x, double z, double roll)[] sides =
            {
                (0, -4.5, 180), (0, 4.5, 0), (-4.5, 0, -90), (4.5, 0, 90),
            };
            foreach (var s in sides)
                addTextItem(Assets.TURN_TEXT).SetText(label)
                    .SetPosition(s.x, 0.12, s.z).SetScale(0.45).SetRotation(-90, 0, s.roll)
                    .AddAttribute("turnText", "1").AddAttribute("tint", tint);
        }
    }
}
