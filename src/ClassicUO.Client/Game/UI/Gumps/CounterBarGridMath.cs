// SPDX-License-Identifier: BSD-2-Clause

using System;

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
    }
}
