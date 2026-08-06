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

            // 3D text used for the "whose turn" labels around the board edges.
            internal static AssetData TURN_TEXT = new Text3dAssetData("turn");
        }

        // Square centers along x and z, measured from the live board (8 squares over ~[-3.63..3.64]).
        private static readonly double[] COORDS = { -3.18, -2.27, -1.36, -0.45, 0.45, 1.36, 2.27, 3.18 };
        private const double PIECE_SCALE = 0.85;

        // Piece tints (the raw models are harsh pure-white / purple). Charcoal (not pure
        // black) keeps the black pieces' shape readable under lighting.
        private const string WHITE_TINT = "0xE3D5B8";
        private const string BLACK_TINT = "0x2B2B2B";

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
            addAsset(Assets.TURN_TEXT);

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
                makeChessMovable(addItem(whiteBack[i]).SetPosition(COORDS[i], 0, COORDS[0]).SetScale(PIECE_SCALE).AddAttribute("color", "white").AddAttribute("piece", backTypes[i]).AddAttribute("tint", WHITE_TINT));
                makeChessMovable(addItem(Assets.PAWN_W).SetPosition(COORDS[i], 0, COORDS[1]).SetScale(PIECE_SCALE).AddAttribute("color", "white").AddAttribute("piece", "pawn").AddAttribute("tint", WHITE_TINT));
                makeChessMovable(addItem(blackBack[i]).SetPosition(COORDS[i], 0, COORDS[7]).SetScale(PIECE_SCALE).AddAttribute("color", "black").AddAttribute("piece", backTypes[i]).AddAttribute("tint", BLACK_TINT));
                makeChessMovable(addItem(Assets.PAWN_B).SetPosition(COORDS[i], 0, COORDS[6]).SetScale(PIECE_SCALE).AddAttribute("color", "black").AddAttribute("piece", "pawn").AddAttribute("tint", BLACK_TINT));
            }

            UpdateTurnText();
            return Task.CompletedTask;
        }

        // Place a flat "whose turn" label on each of the board's 4 edges (readable from
        // any side, low profile so it doesn't block the pieces). Rebuilt each move.
        private void UpdateTurnText()
        {
            string turn = GameData.Attributes.TryGetValue("turn", out var tv) ? tv : "white";
            SetBoardText((turn == "white" ? "WHITE" : "BLACK") + " TO MOVE",
                         turn == "white" ? "0xF2F2F2" : "0x151515"); // colour follows the side to move
        }

        // Place the same label flat on all 4 board edges (readable from any side).
        // Text is laid FLAT with a -90° X tilt; the in-plane facing must then be a
        // ROLL about Z (using Y tilted them upright — the "standing" labels bug).
        private void SetBoardText(string label, string tint)
        {
            foreach (var t in getItemsByAttribute("turnText")) removeItem(t.Id);

            // (x, z, rollZ°) for the south, north, west and east edges.
            (double x, double z, double roll)[] sides =
            {
                (0, -3.9, 180),  // south (white's side)
                (0,  3.9, 0),    // north (black's side)
                (-3.9, 0, -90),  // west
                ( 3.9, 0,  90),  // east
            };
            foreach (var s in sides)
            {
                addTextItem(Assets.TURN_TEXT)
                    .SetText(label)
                    .SetPosition(s.x, 0.12, s.z)   // just above the board surface
                    .SetScale(0.5)
                    .SetRotation(-90, 0, s.roll)   // lay flat (X), then face its edge (Z)
                    .AddAttribute("turnText", "1")
                    .AddAttribute("tint", tint);
            }
        }

        protected override Task EndGame() => Task.CompletedTask;

        // The game ends when the side to move has no legal moves: checkmate if it is
        // in check (the other side wins), otherwise stalemate (a draw).
        protected override Task<bool> IsEndGame()
        {
            var (board, items) = BuildBoard();
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "white";
            char side = turn == "white" ? 'w' : 'b';

            if (ChessRules.HasAnyLegalMove(board, side, GetEnPassant(), GetCastling(items)))
                return Task.FromResult(false);

            if (ChessRules.InCheck(board, side))
            {
                string winner = turn == "white" ? "black" : "white"; // the side that just moved
                var wp = getPlayerByAttribute("type", winner);
                string who = wp != null ? PlayerDisplayName(wp) : winner;
                GameData.Attributes["result"] = "Checkmate — " + Cap(winner) + " (" + who + ") wins!";
                GameData.Attributes["winnerColor"] = winner;
                SetBoardText(winner.ToUpper() + " WINS!", winner == "white" ? "0xF2F2F2" : "0x151515");
            }
            else
            {
                GameData.Attributes["result"] = "Stalemate — it's a draw.";
                GameData.Attributes.Remove("winnerColor");
                SetBoardText("DRAW", "0x888888");
            }
            return Task.FromResult(true);
        }

        protected override List<PlayerData> GetGameWinners()
        {
            if (GameData.Attributes.TryGetValue("winnerColor", out var wc) && !string.IsNullOrEmpty(wc))
            {
                var p = getPlayerByAttribute("type", wc); // the seat whose "type" is white/black
                if (p != null) return new List<PlayerData> { p };
            }
            return new List<PlayerData>();
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

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
                var occ = items[m.ToC, m.ToR];

                // Capturing an enemy piece → highlight THAT piece yellow (a flat marker
                // would just hide under the tall model). Clicking it performs the
                // capture via ChessSelect.
                if (m.Capture && !m.EnPassant && occ != null)
                {
                    occ.Attributes["captureTarget"] = "1";
                    continue;
                }

                // Otherwise (empty square, or en passant onto an empty square) → yellow marker.
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
            foreach (var p in getItemsByAttribute("captureTarget"))
                p.Attributes.Remove("captureTarget"); // un-highlight capturable enemies
        }

        // ------------------------------------------------------------------
        // Executing a legal move (clicking a yellow marker).
        // ------------------------------------------------------------------
        // Chess pieces use this instead of the generic SelectPiece. If a piece is
        // already selected and you click an enemy piece that sits on one of its
        // legal target squares, that's a capture — do the move (the yellow marker is
        // hidden under the tall enemy model, so clicking the piece must work too).
        // Otherwise fall back to normal selection (highlight + show legal markers).
        [GameAction]
        public async Task ChessSelect(ExecuteActionData data)
        {
            var clicked = data.Item;

            // Clicking the already-selected piece deselects it (toggle off).
            if (clicked != null
                && GameData.Attributes.TryGetValue("selectedItem", out var curSel)
                && curSel == clicked.Id)
            {
                ClearSelection();
                await Task.CompletedTask;
                return;
            }

            if (clicked != null
                && GameData.Attributes.TryGetValue("selectedItem", out var selId)
                && !string.IsNullOrEmpty(selId) && selId != clicked.Id)
            {
                var selected = GameData.FindItem(selId);
                if (selected != null)
                {
                    var (board, items) = BuildBoard();
                    int fc = ToIndex(selected.Position.X), fr = ToIndex(selected.Position.Z);
                    if (board[fc, fr] != null)
                    {
                        int tc = ToIndex(clicked.Position.X), tr = ToIndex(clicked.Position.Z);
                        var moves = ChessRules.LegalMoves(board, fc, fr, GetEnPassant(), GetCastling(items));
                        var hit = moves.Find(m => m.ToC == tc && m.ToR == tr);
                        if (hit != null)
                        {
                            ApplyMove(selected, fc, fr, hit); // capture this piece
                            return;
                        }
                    }
                }
            }

            // Not a capture of the selected piece → normal selection.
            await SelectPiece(data);
        }

        [GameAction]
        public async Task ChessMove(ExecuteActionData data)
        {
            var marker = data.Item;
            if (marker == null || !marker.HaveAttribute("moveMarker")) { await Task.CompletedTask; return; }

            if (!GameData.Attributes.TryGetValue("selectedItem", out var selId) || string.IsNullOrEmpty(selId))
            { ClearSelection(); await Task.CompletedTask; return; }

            var piece = GameData.FindItem(selId);
            if (piece == null) { ClearSelection(); await Task.CompletedTask; return; }

            int fromC = ToIndex(piece.Position.X), fromR = ToIndex(piece.Position.Z);

            // Reconstruct the move the clicked marker represents, then apply it.
            var m = new ChessRules.Move
            {
                ToC = int.Parse(marker.GetStringAttribute("tc")),
                ToR = int.Parse(marker.GetStringAttribute("tr")),
                Castle = marker.HaveAttribute("castle") ? marker.GetStringAttribute("castle")[0] : '\0',
                EnPassant = marker.HaveAttribute("ep"),
                Promote = marker.HaveAttribute("promote"),
                DoublePush = marker.HaveAttribute("dbl")
            };
            ApplyMove(piece, fromC, fromR, m);
            await Task.CompletedTask;
        }

        // Apply a legal move to the scene: capture, castle, en passant, promotion,
        // update the en-passant target, flip the turn, clear selection/markers.
        // Shared by human moves (ChessMove) and the AI (PlayAI).
        private void ApplyMove(ItemData piece, int fromC, int fromR, ChessRules.Move m)
        {
            var (_, items) = BuildBoard();
            int tc = m.ToC, tr = m.ToR;

            // Capture the move details up front (for the move-history log below).
            string moverColor = piece.GetStringAttribute("color");
            string moverPiece = piece.HaveAttribute("piece") ? piece.GetStringAttribute("piece") : "piece";

            // Normal capture: remove any enemy piece on the destination.
            var occ = items[tc, tr];
            bool captured = (occ != null && occ.Id != piece.Id) || m.EnPassant;
            if (occ != null && occ.Id != piece.Id) removeItem(occ.Id);

            // En passant: the captured pawn sits beside the destination, on the from-row.
            if (m.EnPassant)
            {
                var cap = items[tc, fromR];
                if (cap != null) removeItem(cap.Id);
            }

            piece.SetPosition(ToCoord(tc), 0, ToCoord(tr));
            piece.Attributes["moved"] = "1";

            // Castling: slide the rook next to the king.
            if (m.Castle == 'K' || m.Castle == 'Q')
            {
                bool kingside = m.Castle == 'K';
                var rook = items[kingside ? 7 : 0, fromR];
                if (rook != null)
                {
                    rook.SetPosition(ToCoord(kingside ? 5 : 3), 0, ToCoord(fromR));
                    rook.Attributes["moved"] = "1";
                }
            }

            // Promotion: swap the pawn for a queen of the same color (auto-queen).
            if (m.Promote)
            {
                string color = piece.GetStringAttribute("color");
                removeItem(piece.Id);
                var promoted = addItem(color == "white" ? Assets.QUEEN_W : Assets.QUEEN_B)
                    .SetPosition(ToCoord(tc), 0, ToCoord(tr))
                    .SetScale(PIECE_SCALE)
                    .AddAttribute("color", color)
                    .AddAttribute("piece", "queen")
                    .AddAttribute("moved", "1");
                promoted.AddAttribute("tint", color == "white" ? WHITE_TINT : BLACK_TINT);
                makeChessMovable(promoted);
            }

            // Set / clear the en-passant target square for the opponent's next move.
            GameData.Attributes.Remove("ep");
            if (m.DoublePush)
                GameData.Attributes["ep"] = tc + "," + (fromR + tr) / 2;

            // Alternate turns.
            string turn = GameData.Attributes.TryGetValue("turn", out var tv) ? tv : "white";
            GameData.Attributes["turn"] = turn == "white" ? "black" : "white";

            // Move-history log, e.g. "CHESS: white knight g1->f3  |  black to move".
            string tag = m.Castle == 'K' ? " O-O" : m.Castle == 'Q' ? " O-O-O" : "";
            if (captured) tag += m.EnPassant ? " x(e.p.)" : " x";
            if (m.Promote) tag += "=Queen";
            Console.WriteLine($"CHESS: {moverColor} {moverPiece} {Square(fromC, fromR)}->{Square(tc, tr)}{tag}  |  {GameData.Attributes["turn"]} to move");

            UpdateTurnText();  // refresh the board-edge "whose turn" labels
            ClearSelection(); // un-highlight + remove markers
        }

        // Board coordinate → algebraic square name, e.g. (4,0) → "e1".
        private static string Square(int c, int r) => $"{(char)('a' + c)}{r + 1}";

        // ------------------------------------------------------------------
        // AI: on its turn, pick a random piece that has at least one legal move,
        // then pick a random one of that piece's legal moves.
        // ------------------------------------------------------------------
        public override bool IsAITurn(PlayerData player)
        {
            string turn = GameData.Attributes.TryGetValue("turn", out var t) ? t : "white";
            return player.GetStringAttribute("type") == turn; // seat's "type" is "white"/"black"
        }

        public override async Task<bool> PlayAI(PlayerData player, Random rnd)
        {
            var (board, items) = BuildBoard();
            char me = player.GetStringAttribute("type") == "white" ? 'w' : 'b';
            var ep = GetEnPassant();
            var cast = GetCastling(items);

            // Every piece of my color that has at least one legal move.
            var movable = new List<(int c, int r, List<ChessRules.Move> moves)>();
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                {
                    var sq = board[c, r];
                    if (sq == null || sq.Value.Color != me) continue;
                    var mv = ChessRules.LegalMoves(board, c, r, ep, cast);
                    if (mv.Count > 0) movable.Add((c, r, mv));
                }

            if (movable.Count == 0) return false; // checkmate / stalemate — nothing to do

            var pick = movable[rnd.Next(movable.Count)];        // random piece
            var move = pick.moves[rnd.Next(pick.moves.Count)];  // random legal move
            var pieceItem = items[pick.c, pick.r];
            if (pieceItem == null) return false;

            ApplyMove(pieceItem, pick.c, pick.r, move);
            await Task.CompletedTask;
            return true;
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

        // Chess pieces select via ChessSelect (which also handles capture-by-click).
        private void makeChessMovable(ItemData piece) => piece.AddAction(ChessSelect);
    }
}
