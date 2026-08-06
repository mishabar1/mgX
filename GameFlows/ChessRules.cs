using System;
using System.Collections.Generic;

namespace MG.Server.GameFlows
{
    // Pure chess move-rules engine. Deliberately has NO dependency on game entities
    // (ItemData, V3, …) so it can be unit-tested in isolation.
    //
    // Board is indexed [col, row]:
    //   col 0..7  = files a..h
    //   row 0..7  = ranks 1..8
    // White occupies rows 0 (back rank) and 1 (pawns) and advances toward row 7.
    // Black occupies rows 7 (back rank) and 6 (pawns) and advances toward row 0.
    public static class ChessRules
    {
        public struct Piece
        {
            public char Color; // 'w' or 'b'
            public char Type;  // 'P','N','B','R','Q','K'
            public Piece(char color, char type) { Color = color; Type = type; }
        }

        public class Move
        {
            public int ToC, ToR;
            public bool Capture;
            public bool EnPassant;
            public bool DoublePush;
            public bool Promote;
            public char Castle; // '\0' none, 'K' kingside, 'Q' queenside
        }

        // Which castles are still legally available (king/rook not yet moved).
        public struct Castling
        {
            public bool WhiteK, WhiteQ, BlackK, BlackQ;
        }

        static bool InBounds(int c, int r) => c >= 0 && c < 8 && r >= 0 && r < 8;
        static char Opp(char c) => c == 'w' ? 'b' : 'w';

        // ---- public API -----------------------------------------------------

        /// <summary>All fully-legal moves for the piece at (c,r): pseudo-legal moves
        /// with any that would leave the mover's own king in check removed.</summary>
        public static List<Move> LegalMoves(
            Piece?[,] board, int c, int r,
            (int c, int r)? enPassant, Castling castling)
        {
            var result = new List<Move>();
            var p = board[c, r];
            if (p == null) return result;
            char me = p.Value.Color;

            foreach (var m in Pseudo(board, c, r, enPassant, castling))
            {
                // Castling: king may not be in check now, nor pass through an attacked square.
                if (m.Castle != '\0')
                {
                    if (Attacked(board, c, r, Opp(me))) continue;
                    int midC = m.Castle == 'K' ? c + 1 : c - 1;
                    if (Attacked(board, midC, r, Opp(me))) continue;
                }

                // Simulate and reject if our king ends up attacked.
                var copy = Clone(board);
                ApplyOnCopy(copy, c, r, m);
                var king = FindKing(copy, me);
                if (king == null) continue;
                if (!Attacked(copy, king.Value.c, king.Value.r, Opp(me)))
                    result.Add(m);
            }
            return result;
        }

        /// <summary>True if the given side has at least one legal move (used for
        /// checkmate / stalemate detection).</summary>
        public static bool HasAnyLegalMove(
            Piece?[,] board, char color,
            (int c, int r)? enPassant, Castling castling)
        {
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                    if (board[c, r] != null && board[c, r].Value.Color == color)
                        if (LegalMoves(board, c, r, enPassant, castling).Count > 0)
                            return true;
            return false;
        }

        /// <summary>True if 'color' king is currently in check.</summary>
        public static bool InCheck(Piece?[,] board, char color)
        {
            var k = FindKing(board, color);
            return k != null && Attacked(board, k.Value.c, k.Value.r, Opp(color));
        }

        // ---- pseudo-legal move generation -----------------------------------

        static List<Move> Pseudo(Piece?[,] b, int c, int r, (int c, int r)? ep, Castling cast)
        {
            var moves = new List<Move>();
            var p = b[c, r].Value;
            char me = p.Color;
            char opp = Opp(me);

            switch (p.Type)
            {
                case 'P':
                {
                    int dir = me == 'w' ? 1 : -1;
                    int startRow = me == 'w' ? 1 : 6;
                    int promoRow = me == 'w' ? 7 : 0;

                    // forward one (and two from start)
                    if (InBounds(c, r + dir) && b[c, r + dir] == null)
                    {
                        AddPawn(moves, c, r + dir, false, false, r + dir == promoRow);
                        if (r == startRow && b[c, r + 2 * dir] == null)
                            AddPawn(moves, c, r + 2 * dir, false, true, false);
                    }
                    // diagonal captures + en passant
                    foreach (int dc in new[] { -1, 1 })
                    {
                        int nc = c + dc, nr = r + dir;
                        if (!InBounds(nc, nr)) continue;
                        if (b[nc, nr] != null && b[nc, nr].Value.Color == opp)
                            AddPawn(moves, nc, nr, true, false, nr == promoRow);
                        else if (ep != null && ep.Value.c == nc && ep.Value.r == nr)
                            moves.Add(new Move { ToC = nc, ToR = nr, Capture = true, EnPassant = true });
                    }
                    break;
                }
                case 'N':
                {
                    int[][] o = { new[]{1,2}, new[]{2,1}, new[]{-1,2}, new[]{-2,1},
                                  new[]{1,-2}, new[]{2,-1}, new[]{-1,-2}, new[]{-2,-1} };
                    foreach (var d in o) TryStep(b, moves, c + d[0], r + d[1], me);
                    break;
                }
                case 'B':
                    Slide(b, moves, c, r, me, new int[][] { new[]{1,1}, new[]{1,-1}, new[]{-1,1}, new[]{-1,-1} });
                    break;
                case 'R':
                    Slide(b, moves, c, r, me, new int[][] { new[]{1,0}, new[]{-1,0}, new[]{0,1}, new[]{0,-1} });
                    break;
                case 'Q':
                    Slide(b, moves, c, r, me, new int[][] { new[]{1,0}, new[]{-1,0}, new[]{0,1}, new[]{0,-1},
                                                        new[]{1,1}, new[]{1,-1}, new[]{-1,1}, new[]{-1,-1} });
                    break;
                case 'K':
                {
                    for (int dc = -1; dc <= 1; dc++)
                        for (int dr = -1; dr <= 1; dr++)
                            if (dc != 0 || dr != 0) TryStep(b, moves, c + dc, r + dr, me);

                    int row = me == 'w' ? 0 : 7;
                    bool k = me == 'w' ? cast.WhiteK : cast.BlackK;
                    bool q = me == 'w' ? cast.WhiteQ : cast.BlackQ;
                    if (r == row && c == 4)
                    {
                        if (k && b[5, row] == null && b[6, row] == null &&
                            b[7, row] != null && b[7, row].Value.Type == 'R' && b[7, row].Value.Color == me)
                            moves.Add(new Move { ToC = 6, ToR = row, Castle = 'K' });
                        if (q && b[3, row] == null && b[2, row] == null && b[1, row] == null &&
                            b[0, row] != null && b[0, row].Value.Type == 'R' && b[0, row].Value.Color == me)
                            moves.Add(new Move { ToC = 2, ToR = row, Castle = 'Q' });
                    }
                    break;
                }
            }
            return moves;
        }

        static void AddPawn(List<Move> moves, int c, int r, bool cap, bool dbl, bool promo)
            => moves.Add(new Move { ToC = c, ToR = r, Capture = cap, DoublePush = dbl, Promote = promo });

        static void TryStep(Piece?[,] b, List<Move> moves, int c, int r, char me)
        {
            if (!InBounds(c, r)) return;
            if (b[c, r] == null) moves.Add(new Move { ToC = c, ToR = r });
            else if (b[c, r].Value.Color != me) moves.Add(new Move { ToC = c, ToR = r, Capture = true });
        }

        static void Slide(Piece?[,] b, List<Move> moves, int c, int r, char me, int[][] dirs)
        {
            foreach (var d in dirs)
            {
                int nc = c + d[0], nr = r + d[1];
                while (InBounds(nc, nr))
                {
                    if (b[nc, nr] == null)
                    {
                        moves.Add(new Move { ToC = nc, ToR = nr });
                    }
                    else
                    {
                        if (b[nc, nr].Value.Color != me)
                            moves.Add(new Move { ToC = nc, ToR = nr, Capture = true });
                        break;
                    }
                    nc += d[0]; nr += d[1];
                }
            }
        }

        // ---- attack detection & simulation ----------------------------------

        static (int c, int r)? FindKing(Piece?[,] b, char color)
        {
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                    if (b[c, r] != null && b[c, r].Value.Type == 'K' && b[c, r].Value.Color == color)
                        return (c, r);
            return null;
        }

        /// <summary>Is square (tc,tr) attacked by any piece of color 'by'?</summary>
        public static bool Attacked(Piece?[,] b, int tc, int tr, char by)
        {
            // pawn: a 'by' pawn attacks diagonally forward, so it sits one row "back" from target
            int dir = by == 'w' ? 1 : -1;
            foreach (int dc in new[] { -1, 1 })
            {
                int pc = tc + dc, pr = tr - dir;
                if (InBounds(pc, pr) && b[pc, pr] != null &&
                    b[pc, pr].Value.Color == by && b[pc, pr].Value.Type == 'P') return true;
            }
            // knight
            int[][] no = { new[]{1,2}, new[]{2,1}, new[]{-1,2}, new[]{-2,1},
                           new[]{1,-2}, new[]{2,-1}, new[]{-1,-2}, new[]{-2,-1} };
            foreach (var d in no)
            {
                int c = tc + d[0], r = tr + d[1];
                if (InBounds(c, r) && b[c, r] != null &&
                    b[c, r].Value.Color == by && b[c, r].Value.Type == 'N') return true;
            }
            // king (adjacent)
            for (int dc = -1; dc <= 1; dc++)
                for (int dr = -1; dr <= 1; dr++)
                {
                    if (dc == 0 && dr == 0) continue;
                    int c = tc + dc, r = tr + dr;
                    if (InBounds(c, r) && b[c, r] != null &&
                        b[c, r].Value.Color == by && b[c, r].Value.Type == 'K') return true;
                }
            // sliding: orthogonal → rook/queen, diagonal → bishop/queen
            int[][] orth = { new[]{1,0}, new[]{-1,0}, new[]{0,1}, new[]{0,-1} };
            foreach (var d in orth)
            {
                int c = tc + d[0], r = tr + d[1];
                while (InBounds(c, r))
                {
                    if (b[c, r] != null)
                    {
                        var pc = b[c, r].Value;
                        if (pc.Color == by && (pc.Type == 'R' || pc.Type == 'Q')) return true;
                        break;
                    }
                    c += d[0]; r += d[1];
                }
            }
            int[][] diag = { new[]{1,1}, new[]{1,-1}, new[]{-1,1}, new[]{-1,-1} };
            foreach (var d in diag)
            {
                int c = tc + d[0], r = tr + d[1];
                while (InBounds(c, r))
                {
                    if (b[c, r] != null)
                    {
                        var pc = b[c, r].Value;
                        if (pc.Color == by && (pc.Type == 'B' || pc.Type == 'Q')) return true;
                        break;
                    }
                    c += d[0]; r += d[1];
                }
            }
            return false;
        }

        static Piece?[,] Clone(Piece?[,] b)
        {
            var n = new Piece?[8, 8];
            Array.Copy(b, n, b.Length);
            return n;
        }

        // Apply a move to a board copy (used only for check-testing).
        static void ApplyOnCopy(Piece?[,] b, int fromC, int fromR, Move m)
        {
            var p = b[fromC, fromR];
            b[fromC, fromR] = null;

            if (m.EnPassant)
                b[m.ToC, fromR] = null; // captured pawn sits beside destination, on the from-row

            if (m.Promote && p != null)
                p = new Piece(p.Value.Color, 'Q');

            b[m.ToC, m.ToR] = p;

            if (m.Castle == 'K') { var rook = b[7, fromR]; b[7, fromR] = null; b[5, fromR] = rook; }
            else if (m.Castle == 'Q') { var rook = b[0, fromR]; b[0, fromR] = null; b[3, fromR] = rook; }
        }

        // ---- simple search-based AI (negamax + alpha-beta, material evaluation) ----

        static int PieceValue(char t) => t switch
        {
            'P' => 100, 'N' => 320, 'B' => 330, 'R' => 500, 'Q' => 900, 'K' => 20000, _ => 0
        };

        static int Material(Piece?[,] b, char color)
        {
            int s = 0;
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                    if (b[c, r] != null && b[c, r]!.Value.Color == color) s += PieceValue(b[c, r]!.Value.Type);
            return s;
        }

        static int Evaluate(Piece?[,] b, char color) => Material(b, color) - Material(b, Opp(color));

        // All legal moves for a colour, tagged with their from-square.
        public static List<(int c, int r, Move m)> AllMoves(Piece?[,] b, char color, (int c, int r)? ep, Castling cast)
        {
            var list = new List<(int, int, Move)>();
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                    if (b[c, r] != null && b[c, r]!.Value.Color == color)
                        foreach (var m in LegalMoves(b, c, r, ep, cast))
                            list.Add((c, r, m));
            return list;
        }

        // Board copy with a move applied (for search).
        public static Piece?[,] Make(Piece?[,] b, int fromC, int fromR, Move m)
        {
            var nb = Clone(b);
            ApplyOnCopy(nb, fromC, fromR, m);
            return nb;
        }

        const int MATE = 1_000_000;

        // NOTE: within the search we ignore castling / en-passant (pass none) to keep it
        // manageable — the rare cases barely affect material-based play, and the ROOT still
        // considers them so the AI can actually castle / capture en passant when it's its move.
        static int Negamax(Piece?[,] b, char color, int depth, int alpha, int beta)
        {
            var moves = AllMoves(b, color, null, default);
            if (moves.Count == 0)
                return InCheck(b, color) ? -MATE - depth : 0; // checkmated (bad) or stalemate (draw)
            if (depth == 0) return Evaluate(b, color);

            moves.Sort((a, z) => (z.Item3.Capture ? 1 : 0) - (a.Item3.Capture ? 1 : 0)); // captures first
            int best = int.MinValue + 1;
            foreach (var (c, r, m) in moves)
            {
                int score = -Negamax(Make(b, c, r, m), Opp(color), depth - 1, -beta, -alpha);
                if (score > best) best = score;
                if (best > alpha) alpha = best;
                if (alpha >= beta) break;
            }
            return best;
        }

        /// <summary>Best move for `color` via a `depth`-ply search. Ties broken randomly.</summary>
        public static (int c, int r, Move m)? ChooseBestMove(
            Piece?[,] b, char color, (int c, int r)? ep, Castling cast, int depth, Random rnd)
        {
            var moves = AllMoves(b, color, ep, cast);
            if (moves.Count == 0) return null;
            moves.Sort((a, z) => (z.Item3.Capture ? 1 : 0) - (a.Item3.Capture ? 1 : 0));

            int bestScore = int.MinValue + 1;
            int alpha = int.MinValue + 1;
            var bests = new List<(int, int, Move)>();
            foreach (var (c, r, m) in moves)
            {
                int score = -Negamax(Make(b, c, r, m), Opp(color), depth - 1, int.MinValue + 1, -alpha);
                if (score > bestScore) { bestScore = score; bests.Clear(); bests.Add((c, r, m)); }
                else if (score == bestScore) bests.Add((c, r, m));
                if (bestScore > alpha) alpha = bestScore;
            }
            return bests[rnd.Next(bests.Count)];
        }
    }
}
