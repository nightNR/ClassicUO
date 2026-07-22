// SPDX-License-Identifier: BSD-2-Clause
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class PluginStatusBarLayoutTests
    {
        [Theory]
        // column-major, rows=3, cols=2 → fill down column 0, then column 1
        [InlineData(0, (int)FillOrder.ColumnMajor, 0, 0)]
        [InlineData(1, (int)FillOrder.ColumnMajor, 0, 1)]
        [InlineData(2, (int)FillOrder.ColumnMajor, 0, 2)]
        [InlineData(3, (int)FillOrder.ColumnMajor, 1, 0)]
        [InlineData(5, (int)FillOrder.ColumnMajor, 1, 2)]
        // row-major, rows=3, cols=2 → fill across row 0, then row 1
        [InlineData(0, (int)FillOrder.RowMajor, 0, 0)]
        [InlineData(1, (int)FillOrder.RowMajor, 1, 0)]
        [InlineData(2, (int)FillOrder.RowMajor, 0, 1)]
        [InlineData(3, (int)FillOrder.RowMajor, 1, 1)]
        [InlineData(5, (int)FillOrder.RowMajor, 1, 2)]
        // NOTE: fill is passed as int, not FillOrder, because FillOrder is
        // `internal` — a public [Theory] method cannot declare an internal
        // parameter type (CS0051) even with InternalsVisibleTo, since that
        // check is purely about signature accessibility, not call-site access.
        public void GridCell_PlacesByFillOrder(int index, int fill, int expCol, int expRow)
        {
            var (col, row) = PluginStatusBars.GridCell(index, rows: 3, cols: 2, (FillOrder)fill);
            Assert.Equal(expCol, col);
            Assert.Equal(expRow, row);
        }

        [Fact]
        public void IsCapacityReached_UsesPerGroupDims()
        {
            // 2x2 = 4 cells
            Assert.False(PluginStatusBars.IsCapacityReached(3, 2, 2));
            Assert.True(PluginStatusBars.IsCapacityReached(4, 2, 2));
        }
    }
}
