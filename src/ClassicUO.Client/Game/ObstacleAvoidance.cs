// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Data;

namespace ClassicUO.Game
{
    /// <summary>
    /// Steers a mouse-walk heading around small obstacles one tile early.
    /// Pure logic: walkability is injected through <see cref="WalkProbe"/> so it
    /// can be tested on synthetic grids.
    /// </summary>
    internal static class ObstacleAvoidance
    {
        private static readonly int[] OffsetX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] OffsetY = { -1, -1, 0, 1, 1, 1, 0, -1 };

        /// <summary>
        /// One-step probe. Returns true only for a "clean" step: it succeeds, keeps
        /// <paramref name="dir"/> and lands on the geometrically expected tile
        /// (no corner-slide substitution).
        /// </summary>
        public delegate bool WalkProbe(Direction dir, int x, int y, sbyte z, out int nx, out int ny, out sbyte nz);

        /// <summary>
        /// Returns the direction to walk. <paramref name="cursorDx"/>/<paramref name="cursorDy"/>
        /// is the cursor offset from the screen center and picks the avoidance side;
        /// on a tie <paramref name="lastSide"/> (+1 = want+1, -1 = want-1) wins and
        /// is updated whenever a side is chosen.
        /// </summary>
        public static Direction Steer(Direction want, int x, int y, sbyte z, int cursorDx, int cursorDy, ref int lastSide, WalkProbe probe)
        {
            int w = (int) want & 7;

            // Clear ahead for two tiles: nothing to avoid.
            if (probe((Direction) w, x, y, z, out int x1, out int y1, out sbyte z1))
            {
                if (probe((Direction) w, x1, y1, z1, out _, out _, out _))
                {
                    return want;
                }
            }

            int plus = (w + 1) & 7;
            int minus = (w + 7) & 7;

            long dotPlus = Dot(plus, cursorDx, cursorDy);
            long dotMinus = Dot(minus, cursorDx, cursorDy);

            int first = plus;
            int second = minus;

            if (dotMinus > dotPlus || dotMinus == dotPlus && lastSide < 0)
            {
                first = minus;
                second = plus;
            }

            if (TryCandidate(first, w, x, y, z, probe))
            {
                lastSide = first == plus ? 1 : -1;

                return (Direction) first;
            }

            if (TryCandidate(second, w, x, y, z, probe))
            {
                lastSide = second == plus ? 1 : -1;

                return (Direction) second;
            }

            return want;
        }

        // Candidate must step cleanly and still allow resuming the original heading.
        private static bool TryCandidate(int c, int w, int x, int y, sbyte z, WalkProbe probe)
        {
            return probe((Direction) c, x, y, z, out int cx, out int cy, out sbyte cz)
                && probe((Direction) w, cx, cy, cz, out _, out _, out _);
        }

        // Screen vector of a tile direction is (ox - oy, ox + oy).
        private static long Dot(int dir, int cursorDx, int cursorDy)
        {
            return (long) (OffsetX[dir] - OffsetY[dir]) * cursorDx + (long) (OffsetX[dir] + OffsetY[dir]) * cursorDy;
        }
    }
}
