using System;
using System.Collections.Generic;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Checkers / draughts on the 8x8 board. Black vs red, black first. Diagonal moves;
    // captures are forced and chain (multi-jump); men crown on the far row. Click a piece
    // to see its legal destinations, click one to move (the whole jump chain applies at once).
    // Material minimax AI.
    public class CheckersGameFlow : BaseGameFlow
    {
        internal class Assets
        {
            // Wooden 8x8 checkerboard (grid + wooden frame) as a flat textured tile. Scale is
            // applied to the board ITEM (a TOKEN renders as a 1x1 tile) → an 8x8 grid inside a
            // 1-unit frame that carries the turn / score text.
            internal static AssetData BOARD = new TokenAssetData("checkers/board.png");
            // A true round disc, reused for men, kings' crowns, and move markers (scale + tint).
            internal static AssetData PIECE = new CylinderAssetData("disc") { Scale = new V3(1) };
            internal static AssetData TURN_TEXT = new Text3dAssetData("turn");
        }

        // Cell centres on a clean 8x8 grid: cell = 1 world unit, board spans -4..+4.
        private static readonly double[] COORDS = { -3.5, -2.5, -1.5, -0.5, 0.5, 1.5, 2.5, 3.5 };
        private const string BLACK = "0x363636"; // charcoal (not pure black) so it reads on the board
        private const string RED = "0xC0392B";

        public CheckersGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.CHECKERS;
        }

        protected override Task Create()
        {
            addAsset(Assets.BOARD);
            addAsset(Assets.PIECE);
            addAsset(Assets.TURN_TEXT);

            GameData.Observer.Position.Set(0, 12, 0);
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "black").SetCameraPosition(0, 9, -9).SetAvatarPosition(0, 2, -8);
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "red").SetCameraPosition(0, 9, 9).SetAvatarPosition(0, 2, 8);

            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        protected override Task StartGame()
        {
            // Board centred at the origin, scaled to a 10-unit tile: 8x8 grid (-4..+4, matching
            // COORDS) inside a 1-unit wooden frame where the turn / score text is drawn.
            addItem(Assets.BOARD).SetPosition(0, 0, 0).SetScale(10, 1, 10);
            GameData.Attributes["turn"] = "black";

            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                    if ((c + r) % 2 == 1)
                    {
                        if (r <= 2) AddPiece(c, r, "black");
                        else if (r >= 5) AddPiece(c, r, "red");
                    }

            RebindSelectable();
            UpdateTurnText();
            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(GameData.Attributes.ContainsKey("over"));

        protected override List<PlayerData> GetGameWinners()
        {
            if (GameData.Attributes.TryGetValue("winnerColor", out var wc) && !string.IsNullOrEmpty(wc))
            {
                var p = getPlayerByAttribute("type", wc);
                if (p != null) return new List<PlayerData> { p };
            }
            return new List<PlayerData>();
        }

        // ------------------------------------------------------------------
        // Select a piece → show its legal destinations (forced captures respected).
        // ------------------------------------------------------------------
        [GameAction]
        public async Task CheckersSelect(ExecuteActionData data)
        {
            if (GameData.Attributes.ContainsKey("over")) { await Task.CompletedTask; return; }
            var clicked = data.Item;
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "black";
            var current = getPlayerByAttribute("type", turn);
            if (clicked == null || current == null || data.Player == null) { await Task.CompletedTask; return; }
            if (current.User != null && data.Player.User?.Id != current.User.Id) { await Task.CompletedTask; return; }
            if (clicked.GetStringAttribute("color") != turn) { await Task.CompletedTask; return; } // only your pieces

            // Toggle off if re-clicking the selected piece.
            if (GameData.Attributes.TryGetValue("selectedItem", out var cur) && cur == clicked.Id)
            { ClearSel(); await Task.CompletedTask; return; }

            ClearSel();
            GameData.Attributes["selectedItem"] = clicked.Id;
            clicked.Attributes["selected"] = "1";

            int fc = clicked.GetIntAttribute("gx"), fr = clicked.GetIntAttribute("gy");
            char tc = turn == "black" ? 'b' : 'w';
            var mine = CheckersRules.LegalMoves(BuildBoard(), tc).Where(m => m.FromC == fc && m.FromR == fr).ToList();
            if (mine.Count == 0) { ClearSel(); await Task.CompletedTask; return; } // no legal move (forced elsewhere)

            var seatIds = ControllingSeatIds(turn);
            foreach (var m in mine)
            {
                var mk = addItem(Assets.PIECE)
                    .SetPosition(COORDS[m.ToC], 0.08, COORDS[m.ToR]).SetScale(0.42)
                    .AddAttribute("moveMarker", "1")
                    .AddAttribute("gx", m.ToC.ToString()).AddAttribute("gy", m.ToR.ToString());
                foreach (var sid in seatIds) mk.AddAction(sid, CheckersMove);
            }
            await Task.CompletedTask;
        }

        [GameAction]
        public async Task CheckersMove(ExecuteActionData data)
        {
            if (GameData.Attributes.ContainsKey("over")) { await Task.CompletedTask; return; }
            if (!GameData.Attributes.TryGetValue("selectedItem", out var selId) || string.IsNullOrEmpty(selId))
            { await Task.CompletedTask; return; }
            var sel = GameData.FindItem(selId);
            var marker = data.Item;
            if (sel == null || marker == null || !marker.HaveAttribute("moveMarker")) { ClearSel(); await Task.CompletedTask; return; }

            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "black";
            char tc = turn == "black" ? 'b' : 'w';
            int fc = sel.GetIntAttribute("gx"), fr = sel.GetIntAttribute("gy");
            int dc = marker.GetIntAttribute("gx"), dr = marker.GetIntAttribute("gy");

            var mv = CheckersRules.LegalMoves(BuildBoard(), tc)
                .FirstOrDefault(m => m.FromC == fc && m.FromR == fr && m.ToC == dc && m.ToR == dr);
            if (mv == null) { ClearSel(); await Task.CompletedTask; return; }

            ApplyCheckersMove(sel, mv, turn);
            await Task.CompletedTask;
        }

        private void ApplyCheckersMove(ItemData piece, CheckersRules.Move mv, string colorType)
        {
            piece.SetPosition(COORDS[mv.ToC], 0.12, COORDS[mv.ToR]);
            piece.Attributes["gx"] = mv.ToC.ToString();
            piece.Attributes["gy"] = mv.ToR.ToString();

            // Crown a man that reached the far row.
            if (!piece.HaveAttribute("king") && ((colorType == "black" && mv.ToR == 7) || (colorType == "red" && mv.ToR == 0)))
                piece.Attributes["king"] = "1";

            foreach (var (cc, cr) in mv.Captured)
            {
                var cap = FindPiece(cc, cr);
                if (cap != null) removeItem(cap.Id);
            }

            RebuildCrowns();
            ClearSel();

            string next = colorType == "black" ? "red" : "black";
            GameData.Attributes["turn"] = next;

            char nc = next == "black" ? 'b' : 'w';
            if (!CheckersRules.HasAnyLegal(BuildBoard(), nc)) { EndCheckers(colorType); return; }

            RebindSelectable();
            UpdateTurnText();
        }

        private void EndCheckers(string winner)
        {
            var wp = getPlayerByAttribute("type", winner);
            string who = wp != null ? PlayerDisplayName(wp) : winner;
            GameData.Attributes["over"] = "1";
            GameData.Attributes["winnerColor"] = winner;
            GameData.Attributes["result"] = Cap(winner) + " (" + who + ") wins!";
            SetBoardText(winner.ToUpper() + " WINS!", winner == "black" ? BLACK : RED);
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
            string myType = player.GetStringAttribute("type");
            char me = myType == "black" ? 'b' : 'w';
            var mv = CheckersRules.ChooseMove(BuildBoard(), me, 6, rnd);
            if (mv == null) { await Task.CompletedTask; return false; }
            var piece = FindPiece(mv.FromC, mv.FromR);
            if (piece == null) { await Task.CompletedTask; return false; }
            ApplyCheckersMove(piece, mv, myType);
            await Task.CompletedTask;
            return true;
        }

        // ------------------------------------------------------------------
        private char[,] BuildBoard()
        {
            var b = new char[8, 8];
            foreach (var p in getItemsByAttribute("piece"))
            {
                bool king = p.HaveAttribute("king");
                char ch = p.GetStringAttribute("color") == "black" ? (king ? 'B' : 'b') : (king ? 'W' : 'w');
                b[p.GetIntAttribute("gx"), p.GetIntAttribute("gy")] = ch;
            }
            return b;
        }

        private ItemData FindPiece(int c, int r)
        {
            foreach (var p in getItemsByAttribute("piece"))
                if (p.GetIntAttribute("gx") == c && p.GetIntAttribute("gy") == r) return p;
            return null;
        }

        private void AddPiece(int c, int r, string colorType)
        {
            addItem(Assets.PIECE)
                .SetPosition(COORDS[c], 0.12, COORDS[r]).SetScale(0.82)
                .AddAttribute("piece", "1")
                .AddAttribute("color", colorType)
                .AddAttribute("gx", c.ToString()).AddAttribute("gy", r.ToString())
                .AddAttribute("tint", colorType == "black" ? BLACK : RED);
        }

        // Gold crown disc drawn on top of each king.
        private void RebuildCrowns()
        {
            foreach (var cr in getItemsByAttribute("crown")) removeItem(cr.Id);
            foreach (var p in getItemsByAttribute("piece"))
                if (p.HaveAttribute("king"))
                    addItem(Assets.PIECE)
                        .SetPosition(COORDS[p.GetIntAttribute("gx")], 0.26, COORDS[p.GetIntAttribute("gy")])
                        .SetScale(0.34)
                        .AddAttribute("crown", "1")
                        .AddAttribute("tint", "0xD4AF37");
        }

        private void RebindSelectable()
        {
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "black";
            var seatIds = ControllingSeatIds(turn);
            foreach (var p in getItemsByAttribute("piece"))
            {
                p.ClickActions = new Dictionary<string, string>();
                if (p.GetStringAttribute("color") == turn)
                    foreach (var sid in seatIds) p.AddAction(sid, CheckersSelect);
            }
        }

        private List<string> ControllingSeatIds(string turn)
        {
            var current = getPlayerByAttribute("type", turn);
            var ids = GameData.Players
                .Where(p => current?.User != null && p.User?.Id == current.User.Id)
                .Select(p => p.Id).ToList();
            if (ids.Count == 0 && current != null) ids.Add(current.Id);
            return ids;
        }

        private void ClearSel()
        {
            if (GameData.Attributes.TryGetValue("selectedItem", out var sid) && !string.IsNullOrEmpty(sid))
            {
                var s = GameData.FindItem(sid);
                if (s != null) s.Attributes.Remove("selected");
            }
            GameData.Attributes.Remove("selectedItem");
            foreach (var m in getItemsByAttribute("moveMarker")) removeItem(m.Id);
        }

        private void UpdateTurnText()
        {
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "black";
            var pieces = getItemsByAttribute("piece");
            int nr = pieces.Count(p => p.GetStringAttribute("color") == "red");
            int nb = pieces.Count(p => p.GetStringAttribute("color") == "black");
            SetBoardText(turn.ToUpper() + " TO MOVE    R:" + nr + " B:" + nb, turn == "black" ? BLACK : RED);
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
                    .SetPosition(s.x, 0.12, s.z).SetScale(0.5).SetRotation(-90, 0, s.roll)
                    .AddAttribute("turnText", "1").AddAttribute("tint", tint);
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
    }
}
