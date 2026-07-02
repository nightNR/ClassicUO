// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Controls;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class GridContainerLayoutTests
    {
        // cell 50 + margin 4 = 54 stride; MAX_WIDTH 300 -> (300-4)/54 = 5 cols
        [Fact]
        public void Columns_FitWithinMaxWidth()
        {
            Assert.Equal(5, GridContainerLayout.Columns());
        }

        // (MAX_HEIGHT 420 - RESERVED_BOTTOM 30 - margin 4)/54 = 386/54 = 7 rows
        [Fact]
        public void RowsPerPage_FitWithinMaxHeight()
        {
            Assert.Equal(7, GridContainerLayout.RowsPerPage());
        }

        [Fact]
        public void PerPage_IsColumnsTimesRows()
        {
            Assert.Equal(35, GridContainerLayout.PerPage());
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(35, 1)]   // exactly one page
        [InlineData(36, 2)]   // one over -> second page
        [InlineData(70, 2)]
        [InlineData(71, 3)]
        public void PageCount_CountsPages(int itemCount, int expected)
        {
            Assert.Equal(expected, GridContainerLayout.PageCount(itemCount));
        }

        [Fact]
        public void CellPosition_FirstCell_TopLeftPageZero()
        {
            var (x, y, page) = GridContainerLayout.CellPosition(0);
            Assert.Equal(4, x);   // CELL_MARGIN
            Assert.Equal(4, y);
            Assert.Equal(0, page);
        }

        [Fact]
        public void CellPosition_SecondColumn_SameRow()
        {
            var (x, y, page) = GridContainerLayout.CellPosition(1);
            Assert.Equal(4 + 54, x);
            Assert.Equal(4, y);
            Assert.Equal(0, page);
        }

        [Fact]
        public void CellPosition_WrapsToNextRow_AfterLastColumn()
        {
            // index 5 is the 6th cell -> row 1, col 0 (5 columns per row)
            var (x, y, page) = GridContainerLayout.CellPosition(5);
            Assert.Equal(4, x);
            Assert.Equal(4 + 54, y);
            Assert.Equal(0, page);
        }

        [Fact]
        public void CellPosition_WrapsToNextPage_AfterPerPage()
        {
            // index 35 is the 36th cell -> page 1, back to top-left
            var (x, y, page) = GridContainerLayout.CellPosition(35);
            Assert.Equal(4, x);
            Assert.Equal(4, y);
            Assert.Equal(1, page);
        }
    }
}
