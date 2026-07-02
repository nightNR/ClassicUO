// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.Game.UI.Controls
{
    // Pure paged-grid geometry for GridContainerView. No graphics dependencies so
    // it can be unit-tested directly (mirrors CounterBarGridMath).
    internal static class GridContainerLayout
    {
        public const int CELL_SIZE = 50;
        public const int CELL_MARGIN = 4;
        public const int MAX_WIDTH = 300;
        public const int MAX_HEIGHT = 420;

        // Vertical space reserved at the bottom for Prev/Next buttons and the page label.
        public const int RESERVED_BOTTOM = 30;

        private const int STRIDE = CELL_SIZE + CELL_MARGIN;

        public static int Columns()
        {
            int cols = (MAX_WIDTH - CELL_MARGIN) / STRIDE;
            return cols < 1 ? 1 : cols;
        }

        public static int RowsPerPage()
        {
            int rows = (MAX_HEIGHT - RESERVED_BOTTOM - CELL_MARGIN) / STRIDE;
            return rows < 1 ? 1 : rows;
        }

        public static int PerPage()
        {
            return Columns() * RowsPerPage();
        }

        public static int PageCount(int itemCount)
        {
            int perPage = PerPage();
            int pages = (itemCount + perPage - 1) / perPage;
            return pages < 1 ? 1 : pages;
        }

        public static (int x, int y, int page) CellPosition(int index)
        {
            int cols = Columns();
            int perPage = PerPage();

            int page = index / perPage;
            int inPage = index % perPage;

            int col = inPage % cols;
            int row = inPage / cols;

            int x = CELL_MARGIN + col * STRIDE;
            int y = CELL_MARGIN + row * STRIDE;

            return (x, y, page);
        }

        public static int GridWidth()
        {
            return CELL_MARGIN + Columns() * STRIDE;
        }

        public static int GridHeight()
        {
            return CELL_MARGIN + RowsPerPage() * STRIDE;
        }
    }
}
