// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Pure grid math for the fixed-grid counter bar. UI-free so it can be
    /// unit-tested without instantiating gumps.
    /// </summary>
    internal static class CounterBarGridMath
    {
        public static (int width, int height) GridPixelSize(int rows, int cols, int cellSize, int border)
        {
            rows = Math.Max(1, rows);
            cols = Math.Max(1, cols);
            return (cols * cellSize + border * 2, rows * cellSize + border * 2);
        }

        public static int GridCapacity(int rows, int cols)
        {
            return Math.Max(1, rows) * Math.Max(1, cols);
        }

        public static int MaxScroll(int itemCount, int rows, int cols)
        {
            rows = Math.Max(1, rows);
            cols = Math.Max(1, cols);
            int totalRows = (Math.Max(0, itemCount) + cols - 1) / cols; // ceil
            return Math.Max(0, totalRows - rows);
        }

        /// <summary>
        /// Places items (in order) onto a grid <paramref name="cols"/> wide. Each item takes
        /// colSpan x rowSpan cells and goes to the first free spot scanning row by row, so a
        /// spanning item pushes the following items around it. Spans are clamped to 1..cols.
        /// </summary>
        public static (int col, int row, int colSpan, int rowSpan)[] Layout(IReadOnlyList<(int colSpan, int rowSpan)> spans, int cols)
        {
            cols = Math.Max(1, cols);
            var result = new (int col, int row, int colSpan, int rowSpan)[spans.Count];
            var occupied = new List<bool[]>();

            for (int i = 0; i < spans.Count; i++)
            {
                int cs = Math.Clamp(spans[i].colSpan, 1, cols);
                int rs = Math.Max(1, spans[i].rowSpan);
                int row = 0;

                while (true)
                {
                    int col = FindFreeColumn(occupied, row, cs, rs, cols);
                    if (col >= 0)
                    {
                        while (occupied.Count < row + rs)
                        {
                            occupied.Add(new bool[cols]);
                        }

                        for (int r = row; r < row + rs; r++)
                        {
                            for (int c = col; c < col + cs; c++)
                            {
                                occupied[r][c] = true;
                            }
                        }

                        result[i] = (col, row, cs, rs);
                        break;
                    }

                    row++;
                }
            }

            return result;
        }

        public static int TotalRows((int col, int row, int colSpan, int rowSpan)[] placed)
        {
            int total = 0;
            foreach (var p in placed)
            {
                total = Math.Max(total, p.row + p.rowSpan);
            }

            return total;
        }

        private static int FindFreeColumn(List<bool[]> occupied, int row, int cs, int rs, int cols)
        {
            for (int col = 0; col + cs <= cols; col++)
            {
                bool free = true;
                for (int r = row; r < row + rs && free; r++)
                {
                    if (r >= occupied.Count)
                    {
                        break;
                    }

                    for (int c = col; c < col + cs; c++)
                    {
                        if (occupied[r][c])
                        {
                            free = false;
                            break;
                        }
                    }
                }

                if (free)
                {
                    return col;
                }
            }

            return -1;
        }
    }
}
