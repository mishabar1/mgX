using System;
using System.Collections.Generic;

namespace MG.Server.GameFlows
{
    // Pure Gomoku (five-in-a-row) engine — no dependency on game entities, so it can be
    // unit-tested in isolation. The board is UNBOUNDED: stones live in a sparse map keyed
    // by (x, y) integer coordinates, so it works on an ever-growing board.
    //
    // Freestyle rules: a line of five OR MORE of one colour wins. Colours are 'b'/'w'.
    public static class GomokuRules
    {
        // The 4 line directions (the opposite direction is checked too).
        static readonly (int dx, int dy)[] DIRS = { (1, 0), (0, 1), (1, 1), (1, -1) };

        static char At(Dictionary<(int, int), char> b, int x, int y)
            => b.TryGetValue((x, y), out var c) ? c : '\0';

        /// <summary>Did placing 'color' at (x,y) complete a line of 5+ (freestyle win)?</summary>
        public static bool IsWinningMove(Dictionary<(int, int), char> b, int x, int y, char color)
        {
            foreach (var (dx, dy) in DIRS)
            {
                int count = 1;
                for (int s = 1; At(b, x + dx * s, y + dy * s) == color; s++) count++;
                for (int s = 1; At(b, x - dx * s, y - dy * s) == color; s++) count++;
                if (count >= 5) return true;
            }
            return false;
        }

        /// <summary>If placing 'color' at (x,y) makes 5+, return the winning run's end
        /// stones ((sx,sy) → (ex,ey)); otherwise null.</summary>
        public static ((int sx, int sy), (int ex, int ey))? WinningLine(
            Dictionary<(int, int), char> b, int x, int y, char color)
        {
            foreach (var (dx, dy) in DIRS)
            {
                int f = 0; while (At(b, x + dx * (f + 1), y + dy * (f + 1)) == color) f++;
                int bk = 0; while (At(b, x - dx * (bk + 1), y - dy * (bk + 1)) == color) bk++;
                if (1 + f + bk >= 5)
                    return ((x - dx * bk, y - dy * bk), (x + dx * f, y + dy * f));
            }
            return null;
        }

        /// <summary>Longest line (in any direction) 'color' would have if placed at (x,y).</summary>
        public static int LongestLineAt(Dictionary<(int, int), char> b, int x, int y, char color)
        {
            int best = 1;
            foreach (var (dx, dy) in DIRS)
            {
                int count = 1;
                for (int s = 1; At(b, x + dx * s, y + dy * s) == color; s++) count++;
                for (int s = 1; At(b, x - dx * s, y - dy * s) == color; s++) count++;
                if (count > best) best = count;
            }
            return best;
        }

        // ---- Heuristic AI --------------------------------------------------

        // Score a run of length `run` for one direction from an empty cell, considering
        // how many ends are open (unblocked). Open threats are far more dangerous.
        static double LineScore(int run, int openEnds)
        {
            if (run >= 5) return 1_000_000;         // makes five → win
            if (openEnds == 0) return 0;            // blocked both ends → useless
            switch (run)
            {
                case 4: return openEnds == 2 ? 100_000 : 12_000; // open four ~ winning; four = strong
                case 3: return openEnds == 2 ? 5_000 : 400;
                case 2: return openEnds == 2 ? 250 : 40;
                default: return openEnds == 2 ? 20 : 5;
            }
        }

        // Value of playing `color` at empty (x,y): sum of its 4-direction line scores.
        static double PlacementValue(Dictionary<(int, int), char> b, int x, int y, char color)
        {
            double total = 0;
            foreach (var (dx, dy) in DIRS)
            {
                int run = 1, open = 0;
                int fwd = 0; while (At(b, x + dx * (fwd + 1), y + dy * (fwd + 1)) == color) fwd++;
                int back = 0; while (At(b, x - dx * (back + 1), y - dy * (back + 1)) == color) back++;
                run += fwd + back;
                // an end is "open" if the cell just past the run is empty
                if (At(b, x + dx * (fwd + 1), y + dy * (fwd + 1)) == '\0') open++;
                if (At(b, x - dx * (back + 1), y - dy * (back + 1)) == '\0') open++;
                total += LineScore(run, open);
            }
            return total;
        }

        public static char Opp(char c) => c == 'b' ? 'w' : 'b';

        /// <summary>Pick the AI's move for `me` given the current stones and the set of
        /// empty candidate cells to consider. Returns null if there are none.</summary>
        public static (int x, int y)? ChooseMove(
            Dictionary<(int, int), char> b, char me, IEnumerable<(int x, int y)> candidates)
        {
            char opp = Opp(me);
            (int x, int y)? best = null;
            double bestScore = double.NegativeInfinity;

            foreach (var (x, y) in candidates)
            {
                if (b.ContainsKey((x, y))) continue; // occupied

                double offense = PlacementValue(b, x, y, me);
                double defense = PlacementValue(b, x, y, opp); // value of blocking here

                // Take our own win first; otherwise weight blocking just under attacking
                // (so we prefer completing our five over blocking, but still block fours).
                double score = offense + 0.9 * defense;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = (x, y);
                }
            }
            return best;
        }
    }
}
