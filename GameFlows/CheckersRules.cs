using System;
using System.Collections.Generic;

namespace MG.Server.GameFlows
{
    // Pure checkers/draughts engine (American rules, 8x8), no game-entity deps.
    // Board is [col, row] 0..7. Pieces: 'b'/'w' men, 'B'/'W' kings, '\0' empty.
    // 'b' starts on rows 0..2 and advances toward row 7; 'w' on rows 5..7 toward row 0.
    // Kings move both ways. Captures are FORCED; multi-jumps chain; men crown on the last row.
    public static class CheckersRules
    {
        public class Move
        {
            public int FromC, FromR, ToC, ToR;
            public List<(int c, int r)> Captured = new();
            public bool IsCapture => Captured.Count > 0;
        }

        public static char Color(char p) => (p == 'b' || p == 'B') ? 'b' : (p == 'w' || p == 'W') ? 'w' : '\0';
        static bool IsKing(char p) => p == 'B' || p == 'W';
        static char Opp(char c) => c == 'b' ? 'w' : 'b';
        static bool In(int c, int r) => c >= 0 && c < 8 && r >= 0 && r < 8;
        static bool KingRow(char color, int r) => (color == 'b' && r == 7) || (color == 'w' && r == 0);
        static char ToKing(char p) => p == 'b' ? 'B' : p == 'w' ? 'W' : p;

        static (int dx, int dy)[] Dirs(char piece)
        {
            if (IsKing(piece)) return new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) };
            return Color(piece) == 'b'
                ? new[] { (-1, 1), (1, 1) }    // black advances +row
                : new[] { (-1, -1), (1, -1) }; // white advances -row
        }

        static char[,] Clone(char[,] b) { var n = new char[8, 8]; Array.Copy(b, n, b.Length); return n; }

        public static List<Move> LegalMoves(char[,] b, char color)
        {
            var caps = new List<Move>();
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                    if (Color(b[c, r]) == color) Chains(b, c, r, b[c, r], c, r, new List<(int, int)>(), caps);
            if (caps.Count > 0) return caps; // captures are mandatory

            var simple = new List<Move>();
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                    if (Color(b[c, r]) == color)
                        foreach (var (dx, dy) in Dirs(b[c, r]))
                        {
                            int nc = c + dx, nr = r + dy;
                            if (In(nc, nr) && b[nc, nr] == '\0')
                                simple.Add(new Move { FromC = c, FromR = r, ToC = nc, ToR = nr });
                        }
            return simple;
        }

        // Enumerate maximal capture chains for a piece; emits into `results`.
        static void Chains(char[,] work, int c, int r, char piece, int startC, int startR,
                           List<(int, int)> captured, List<Move> results)
        {
            char color = Color(piece);
            bool extended = false;
            foreach (var (dx, dy) in Dirs(piece))
            {
                int ec = c + dx, er = r + dy, lc = c + 2 * dx, lr = r + 2 * dy;
                if (!In(lc, lr)) continue;
                if (Color(work[ec, er]) != Opp(color)) continue;
                if (work[lc, lr] != '\0') continue;

                extended = true;
                var w2 = Clone(work);
                w2[c, r] = '\0';
                w2[ec, er] = '\0';
                bool crown = !IsKing(piece) && KingRow(color, lr);
                char placed = crown ? ToKing(piece) : piece;
                w2[lc, lr] = placed;

                var cap2 = new List<(int, int)>(captured) { (ec, er) };
                if (crown)
                {
                    // Crowning ends the move (American rules).
                    results.Add(new Move { FromC = startC, FromR = startR, ToC = lc, ToR = lr, Captured = cap2 });
                }
                else
                {
                    int before = results.Count;
                    Chains(w2, lc, lr, placed, startC, startR, cap2, results);
                    if (results.Count == before) // no further jump → this chain ends here
                        results.Add(new Move { FromC = startC, FromR = startR, ToC = lc, ToR = lr, Captured = cap2 });
                }
            }
            _ = extended;
        }

        public static char[,] Apply(char[,] b, Move m, char color)
        {
            var nb = Clone(b);
            char piece = nb[m.FromC, m.FromR];
            nb[m.FromC, m.FromR] = '\0';
            foreach (var (cc, cr) in m.Captured) nb[cc, cr] = '\0';
            if (!IsKing(piece) && KingRow(color, m.ToR)) piece = ToKing(piece);
            nb[m.ToC, m.ToR] = piece;
            return nb;
        }

        public static bool HasAnyLegal(char[,] b, char color) => LegalMoves(b, color).Count > 0;

        public static int Count(char[,] b, char color)
        {
            int n = 0;
            for (int c = 0; c < 8; c++) for (int r = 0; r < 8; r++) if (Color(b[c, r]) == color) n++;
            return n;
        }

        public static char[,] StartBoard()
        {
            var b = new char[8, 8];
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                    if ((c + r) % 2 == 1) // dark squares
                    {
                        if (r <= 2) b[c, r] = 'b';
                        else if (r >= 5) b[c, r] = 'w';
                    }
            return b;
        }

        // ---- AI: material minimax ----
        static int Value(char p) => (p == 'B' || p == 'W') ? 175 : 100;

        static int Evaluate(char[,] b, char me)
        {
            char opp = Opp(me);
            int s = 0;
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                {
                    char p = b[c, r]; if (p == '\0') continue;
                    int v = Value(p);
                    // small bonus for advancing men
                    if (!IsKing(p)) v += (Color(p) == 'b' ? r : 7 - r) * 2;
                    s += Color(p) == me ? v : -v;
                }
            return s;
        }

        const int WIN = 1_000_000;

        static int Negamax(char[,] b, char color, int depth, int alpha, int beta)
        {
            var moves = LegalMoves(b, color);
            if (moves.Count == 0) return -WIN - depth; // no moves → this side loses
            if (depth == 0) return Evaluate(b, color);
            moves.Sort((a, z) => z.Captured.Count - a.Captured.Count); // captures first
            int best = int.MinValue + 1;
            foreach (var m in moves)
            {
                int score = -Negamax(Apply(b, m, color), Opp(color), depth - 1, -beta, -alpha);
                if (score > best) best = score;
                if (best > alpha) alpha = best;
                if (alpha >= beta) break;
            }
            return best;
        }

        public static Move ChooseMove(char[,] b, char color, int depth, Random rnd)
        {
            var moves = LegalMoves(b, color);
            if (moves.Count == 0) return null;
            moves.Sort((a, z) => z.Captured.Count - a.Captured.Count);
            int bestScore = int.MinValue + 1, alpha = int.MinValue + 1;
            var bests = new List<Move>();
            foreach (var m in moves)
            {
                int score = -Negamax(Apply(b, m, color), Opp(color), depth - 1, int.MinValue + 1, -alpha);
                if (score > bestScore) { bestScore = score; bests.Clear(); bests.Add(m); }
                else if (score == bestScore) bests.Add(m);
                if (bestScore > alpha) alpha = bestScore;
            }
            return bests[rnd.Next(bests.Count)];
        }
    }
}
