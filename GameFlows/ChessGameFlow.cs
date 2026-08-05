using System;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Real chess: pieces move by the rules. Selecting a piece shows ONLY its legal
    // destinations (blocking, captures, castling, en passant, promotion, and moves
    // that would leave your own king in check are excluded). Turns alternate
    // white → black. Move legality is computed by the pure ChessRules engine.
    public class ChessGameFlow : BaseGameFlow
    {
        internal class Assets
        {
            // The client normalizes every model so max(width,depth) == asset.scale.
            internal static AssetData BOARD = new ObjectAssetData("chess/board.glb") { Scale = new V3(8) };

            internal static AssetData KING_W = new ObjectAssetData("chess/king_w.gltf");
            internal static AssetData QUEEN_W = new ObjectAssetData("chess/queen_w.gltf");
            internal static AssetData ROOK_W = new ObjectAssetData("chess/rook_w.gltf");
            internal static AssetData BISHOP_W = new ObjectAssetData("chess/bishop_w.gltf");
            internal static AssetData KNIGHT_W = new ObjectAssetData("chess/knight_white.glb"); // only .glb exists for white knight
            internal static AssetData PAWN_W = new ObjectAssetData("chess/pawn_w.gltf");

            internal static AssetData KING_B = new ObjectAssetData("chess/king_b.gltf");
            internal static AssetData QUEEN_B = new ObjectAssetData("chess/queen_b.gltf");
            internal static AssetData ROOK_B = new ObjectAssetData("chess/rook_b.gltf");
            internal static AssetData BISHOP_B = new ObjectAssetData("chess/bishop_b.gltf");
            internal static AssetData KNIGHT_B = new ObjectAssetData("chess/knight_black.gltf");
            internal static AssetData PAWN_B = new ObjectAssetData("chess/pawn_b.gltf");

            // yellow move-target marker (reused from tic-tac-toe), shown on a piece's legal squares
            internal static AssetData MARKER = new ObjectAssetData("ticktacktoe/hover.gltf") { Scale = new V3(0.6) };
        }

        // Square centers along x and z, measured from the live board (8 squares over ~[-3.63..3.64]).
        private static readonly double[] COORDS = { -3.18, -2.27, -1.36, -0.45, 0.45, 1.36, 2.27, 3.18 };
        private const double PIECE_SCALE = 0.85;

        public ChessGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.CHESS;
        }

        protected override Task Create()
        {
            addAsset(Assets.BOARD);
            addAsset(Assets.KING_W); addAsset(Assets.QUEEN_W); addAsset(Assets.ROOK_W);
            addAsset(Assets.BISHOP_W); addAsset(Assets.KNIGHT_W); addAsset(Assets.PAWN_W);
            addAsset(Assets.KING_B); addAsset(Assets.QUEEN_B); addAsset(Assets.ROOK_B);
            addAsset(Assets.BISHOP_B); addAsset(Assets.KNIGHT_B); addAsset(Assets.PAWN_B);
            addAsset(Assets.MARKER);

            GameData.Observer.Position.Set(0, 10, 0);

            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "white")
                .SetCameraPosition(0, 5, -6)
                .SetAvatarPosition(0, 2, -5);

            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "black")
                .SetCameraPosition(0, 5, 6)
                .SetAvatarPosition(0, 2, 5);

            return Task.CompletedTask;
        }

        protected override Task Setup()
        {
            return Task.CompletedTask;
        }

        protected override Task StartGame()
        {
            // board.glb's model origin is at a CORNER, so recenter it on the origin.
            addItem(Assets.BOARD).SetPosition(-3.17, 0, -3.14); // static surface (no free-move action)

            GameData.Attributes["turn"] = "white"; // white moves first
            GameData.Attributes.Remove("ep");      // no en-passant target yet

            AssetData[] whiteBack = { Assets.ROOK_W, Assets.KNIGHT_W, Assets.BISHOP_W, Assets.QUEEN_W, Assets.KING_W, Assets.BISHOP_W, Assets.KNIGHT_W, Assets.ROOK_W };
            AssetData[] blackBack = { Assets.ROOK_B, Assets.KNIGHT_B, Assets.BISHOP_B, Assets.QUEEN_B, Assets.KING_B, Assets.BISHOP_B, Assets.KNIGHT_B, Assets.ROOK_B };
            string[] backTypes = { "rook", "knight", "bishop", "queen", "king", "bishop", "knight", "rook" };

            for (int i = 0; i < 8; i++)
            {
                makeMovable(addItem(whiteBack[i]).SetPosition(COORDS[i], 0, COORDS[0]).SetScale(PIECE_SCALE).AddAttribute("color", "white").AddAttribute("piece", backTypes[i]));
                makeMovable(addItem(Assets.PAWN_W).SetPosition(COORDS[i], 0, COORDS[1]).SetScale(PIECE_SCALE).AddAttribute("color", "white").AddAttribute("piece", "pawn"));
                makeMovable(addItem(blackBack[i]).SetPosition(COORDS[i], 0, COORDS[7]).SetScale(PIECE_SCALE).AddAttribute("color", "black").AddAttribute("piece", backTypes[i]));
                makeMovable(addItem(Assets.PAWN_B).SetPosition(COORDS[i], 0, COORDS[6]).SetScale(PIECE_SCALE).AddAttribute("color", "black").AddAttribute("piece", "pawn"));
            }

            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(false);
        protected override List<PlayerData> GetGameWinners() => new List<PlayerData>();

        // ------------------------------------------------------------------
        // Selecting a piece → show only its legal moves as yellow markers.
        // ------------------------------------------------------------------
        protected override void OnPieceSelected(ItemData? piece)
        {
            if (piece == null) { CancelSelection(piece); return; }

            var (board, items) = BuildBoard();
            int c = ToIndex(piece.Position.X), r = ToIndex(piece.Position.Z);
            if (board[c, r] == null) { CancelSelection(piece); return; }

            char me = board[c, r]!.Value.Color;
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "white";
            if ((me == 'w' ? "white" : "black") != turn) { CancelSelection(piece); return; } // not your turn

            var moves = ChessRules.LegalMoves(board, c, r, GetEnPassant(), GetCastling(items));
            if (moves.Count == 0) { CancelSelection(piece); return; } // pinned / no legal moves

            foreach (var m in moves)
            {
                var marker = addItem(Assets.MARKER)
                    .SetPosition(ToCoord(m.ToC), 0.02, ToCoord(m.ToR))
                    .AddAttribute("moveMarker", "1")
                    .AddAttribute("tc", m.ToC.ToString())
                    .AddAttribute("tr", m.ToR.ToString());
                if (m.Castle != '\0') marker.AddAttribute("castle", m.Castle.ToString());
                if (m.EnPassant) marker.AddAttribute("ep", "1");
                if (m.Promote) marker.AddAttribute("promote", "1");
                if (m.DoublePush) marker.AddAttribute("dbl", "1");
                marker.AddAction(ChessMove);
            }
        }

        protected override void OnMarkersClear()
        {
            foreach (var m in getItemsByAttribute("moveMarker"))
                removeItem(m.Id);
        }

        // ------------------------------------------------------------------
        // Executing a legal move (clicking a yellow marker).
        // ------------------------------------------------------------------
        [GameAction]
        public async Task ChessMove(ExecuteActionData data)
        {
            var marker = data.Item;
            if (marker == null || !marker.HaveAttribute("moveMarker")) { await Task.CompletedTask; return; }

            if (!GameData.Attributes.TryGetValue("selectedItem", out var selId) || string.IsNullOrEmpty(selId))
            { ClearSelection(); await Task.CompletedTask; return; }

            var piece = GameData.FindItem(selId);
            if (piece == null) { ClearSelection(); await Task.CompletedTask; return; }

            int tc = int.Parse(marker.GetStringAttribute("tc"));
            int tr = int.Parse(marker.GetStringAttribute("tr"));
            int fromR = ToIndex(piece.Position.Z);

            var (_, items) = BuildBoard();

            // Normal capture: remove any enemy piece standing on the destination.
            var occ = items[tc, tr];
            if (occ != null && occ.Id != piece.Id) removeItem(occ.Id);

            // En passant: the captured pawn sits beside the destination, on the from-row.
            if (marker.HaveAttribute("ep"))
            {
                var cap = items[tc, fromR];
                if (cap != null) removeItem(cap.Id);
            }

            // Move the piece and remember that it has moved (for castling rights).
            piece.SetPosition(ToCoord(tc), 0, ToCoord(tr));
            piece.Attributes["moved"] = "1";

            // Castling: slide the corresponding rook next to the king.
            if (marker.HaveAttribute("castle"))
            {
                bool kingside = marker.GetStringAttribute("castle") == "K";
                var rook = items[kingside ? 7 : 0, fromR];
                if (rook != null)
                {
                    rook.SetPosition(ToCoord(kingside ? 5 : 3), 0, ToCoord(fromR));
                    rook.Attributes["moved"] = "1";
                }
            }

            // Promotion: swap the pawn for a queen of the same color (auto-queen).
            if (marker.HaveAttribute("promote"))
            {
                string color = piece.GetStringAttribute("color");
                removeItem(piece.Id);
                makeMovable(addItem(color == "white" ? Assets.QUEEN_W : Assets.QUEEN_B)
                    .SetPosition(ToCoord(tc), 0, ToCoord(tr))
                    .SetScale(PIECE_SCALE)
                    .AddAttribute("color", color)
                    .AddAttribute("piece", "queen")
                    .AddAttribute("moved", "1"));
            }

            // Set / clear the en-passant target square for the opponent's next move.
            GameData.Attributes.Remove("ep");
            if (marker.HaveAttribute("dbl"))
                GameData.Attributes["ep"] = tc + "," + (fromR + tr) / 2;

            // Alternate turns.
            string turn = GameData.Attributes.TryGetValue("turn", out var tv) ? tv : "white";
            GameData.Attributes["turn"] = turn == "white" ? "black" : "white";

            ClearSelection(); // un-highlight + remove markers
            await Task.CompletedTask;
        }

        // ------------------------------------------------------------------
        // Helpers: bridge between the 3D scene and the pure ChessRules engine.
        // ------------------------------------------------------------------

        // Build the engine board + a parallel map of the ItemData at each square.
        private (ChessRules.Piece?[,], ItemData?[,]) BuildBoard()
        {
            var board = new ChessRules.Piece?[8, 8];
            var items = new ItemData?[8, 8];
            foreach (var it in getItemsByAttribute("color"))
            {
                if (!it.HaveAttribute("piece")) continue;
                int c = ToIndex(it.Position.X), r = ToIndex(it.Position.Z);
                char color = it.GetStringAttribute("color") == "white" ? 'w' : 'b';
                board[c, r] = new ChessRules.Piece(color, TypeChar(it.GetStringAttribute("piece")));
                items[c, r] = it;
            }
            return (board, items);
        }

        private (int, int)? GetEnPassant()
        {
            if (GameData.Attributes.TryGetValue("ep", out var s) && !string.IsNullOrEmpty(s))
            {
                var parts = s.Split(',');
                return (int.Parse(parts[0]), int.Parse(parts[1]));
            }
            return null;
        }

        private ChessRules.Castling GetCastling(ItemData?[,] items)
        {
            return new ChessRules.Castling
            {
                WhiteK = KingUnmoved('w') && RookUnmoved(items, 7, 0, 'w'),
                WhiteQ = KingUnmoved('w') && RookUnmoved(items, 0, 0, 'w'),
                BlackK = KingUnmoved('b') && RookUnmoved(items, 7, 7, 'b'),
                BlackQ = KingUnmoved('b') && RookUnmoved(items, 0, 7, 'b'),
            };
        }

        private bool KingUnmoved(char color)
        {
            foreach (var it in getItemsByAttribute("color"))
            {
                if (!it.HaveAttribute("piece")) continue;
                if (TypeChar(it.GetStringAttribute("piece")) == 'K' &&
                    (it.GetStringAttribute("color") == "white" ? 'w' : 'b') == color)
                    return !it.HaveAttribute("moved");
            }
            return false;
        }

        private bool RookUnmoved(ItemData?[,] items, int col, int row, char color)
        {
            var it = items[col, row];
            return it != null && it.HaveAttribute("piece") &&
                   TypeChar(it.GetStringAttribute("piece")) == 'R' &&
                   (it.GetStringAttribute("color") == "white" ? 'w' : 'b') == color &&
                   !it.HaveAttribute("moved");
        }

        private void CancelSelection(ItemData? piece)
        {
            if (piece != null) piece.Attributes.Remove("selected");
            GameData.Attributes.Remove("selectedItem");
            OnMarkersClear();
        }

        private static char TypeChar(string p) => p switch
        {
            "pawn" => 'P',
            "knight" => 'N',
            "bishop" => 'B',
            "rook" => 'R',
            "queen" => 'Q',
            "king" => 'K',
            _ => '?'
        };

        private static int ToIndex(double v)
        {
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                double d = Math.Abs(COORDS[i] - v);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        private static double ToCoord(int i) => COORDS[i];
    }
}
