using System;
using System.Collections.Generic;

namespace MG.Server.GameFlows
{
    // Pure Reversi / Othello engine (no game-entity deps → unit-testable).
    // Board is [col, row] 0..7, cells 'b' / 'w' / '\0' (empty). Black moves first.
    public static class ReversiRules
    {
        static readonly (int dx, int dy)[] DIR8 =
        {
            (1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)
        };

        // Standard Othello positional weights (corners great, X/C squares bad).
        static readonly int[,] W =
        {
            {120,-20, 20,  5,  5, 20,-20,120},
            {-20,-40, -5, -5, -5, -5,-40,-20},
            { 20, -5, 15,  3,  3, 15, -5, 20},
            {  5, -5,  3,  3,  3,  3, -5,  5},
            {  5, -5,  3,  3,  3,  3, -5,  5},
            { 20, -5, 15,  3,  3, 15, -5, 20},
            {-20,-40, -5, -5, -5, -5,-40,-20},
            {120,-20, 20,  5,  5, 20,-20,120},
        };

        public static char Opp(char c) => c == 'b' ? 'w' : 'b';
        static bool In(int c, int r) => c >= 0 && c < 8 && r >= 0 && r < 8;

        /// <summary>Discs that would flip if `color` plays at (c,r); empty ⇒ illegal move.</summary>
        public static List<(int c, int r)> Flips(char[,] b, int c, int r, char color)
        {
            var flips = new List<(int, int)>();
            if (!In(c, r) || b[c, r] != '\0') return flips;
            char opp = Opp(color);
            foreach (var (dx, dy) in DIR8)
            {
                var run = new List<(int, int)>();
                int x = c + dx, y = r + dy;
                while (In(x, y) && b[x, y] == opp) { run.Add((x, y)); x += dx; y += dy; }
                if (run.Count > 0 && In(x, y) && b[x, y] == color) flips.AddRange(run);
            }
            return flips;
        }

        public static List<(int c, int r)> LegalMoves(char[,] b, char color)
        {
            var moves = new List<(int, int)>();
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                    if (b[c, r] == '\0' && Flips(b, c, r, color).Count > 0) moves.Add((c, r));
            return moves;
        }

        public static bool HasMove(char[,] b, char color) => LegalMoves(b, color).Count > 0;

        public static int Count(char[,] b, char color)
        {
            int n = 0;
            for (int c = 0; c < 8; c++) for (int r = 0; r < 8; r++) if (b[c, r] == color) n++;
            return n;
        }

        // Apply a move on a COPY (place + flip) and return the new board.
        public static char[,] Apply(char[,] b, int c, int r, char color)
        {
            var nb = (char[,])b.Clone();
            var flips = Flips(b, c, r, color);
            nb[c, r] = color;
            foreach (var (fc, fr) in flips) nb[fc, fr] = color;
            return nb;
        }

        public static char[,] StartBoard()
        {
            var b = new char[8, 8];
            b[3, 3] = 'w'; b[4, 4] = 'w';
            b[3, 4] = 'b'; b[4, 3] = 'b';
            return b;
        }

        // ---- AI: minimax with positional evaluation ----

        static int Evaluate(char[,] b, char me)
        {
            char opp = Opp(me);
            int pos = 0, my = 0, op = 0;
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++)
                {
                    if (b[c, r] == me) { pos += W[c, r]; my++; }
                    else if (b[c, r] == opp) { pos -= W[c, r]; op++; }
                }
            int mob = LegalMoves(b, me).Count - LegalMoves(b, opp).Count;
            int total = my + op;
            // Late in the game disc-count matters more; early, position + mobility.
            if (total >= 58) return (my - op) * 100;
            return pos + 8 * mob;
        }

        static int Negamax(char[,] b, char me, int depth, int alpha, int beta)
        {
            if (depth == 0) return Evaluate(b, me);
            var moves = LegalMoves(b, me);
            if (moves.Count == 0)
            {
                if (!HasMove(b, Opp(me)))
                    return (Count(b, me) - Count(b, Opp(me))) * 1000; // both stuck → game over
                return -Negamax(b, Opp(me), depth - 1, -beta, -alpha); // pass
            }
            int best = int.MinValue + 1;
            foreach (var (c, r) in moves)
            {
                int score = -Negamax(Apply(b, c, r, me), Opp(me), depth - 1, -beta, -alpha);
                if (score > best) best = score;
                if (best > alpha) alpha = best;
                if (alpha >= beta) break;
            }
            return best;
        }

        public static (int c, int r)? ChooseMove(char[,] b, char me, int depth, Random rnd)
        {
            var moves = LegalMoves(b, me);
            if (moves.Count == 0) return null;
            int bestScore = int.MinValue + 1, alpha = int.MinValue + 1;
            var bests = new List<(int, int)>();
            foreach (var (c, r) in moves)
            {
                int score = -Negamax(Apply(b, c, r, me), Opp(me), depth - 1, int.MinValue + 1, -alpha);
                if (score > bestScore) { bestScore = score; bests.Clear(); bests.Add((c, r)); }
                else if (score == bestScore) bests.Add((c, r));
                if (bestScore > alpha) alpha = bestScore;
            }
            return bests[rnd.Next(bests.Count)];
        }
    }
}
