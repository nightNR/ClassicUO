// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    /// <summary>
    /// Pure grid-layout math for plugin-opened, grouped status bars: column-major
    /// cell placement, capacity limits, and dimension clamping.
    /// </summary>
    public class PluginStatusBarsGridTests
    {
        [Theory]
        [InlineData(0, 10, 0, 0)]   // first cell
        [InlineData(9, 10, 0, 9)]   // bottom of first column
        [InlineData(10, 10, 1, 0)]  // wraps to top of second column
        [InlineData(23, 10, 2, 3)]  // third column, fourth row
        [InlineData(0, 1, 0, 0)]    // single-row columns
        [InlineData(1, 1, 1, 0)]    // single-row columns wrap immediately
        public void GridCell_IsColumnMajor(int index, int maxRows, int expectedColumn, int expectedRow)
        {
            (int column, int row) = PluginStatusBars.GridCell(index, maxRows);

            Assert.Equal(expectedColumn, column);
            Assert.Equal(expectedRow, row);
        }

        [Fact]
        public void GridCell_TreatsSubOneMaxRowsAsOne()
        {
            (int column, int row) = PluginStatusBars.GridCell(3, 0);

            Assert.Equal(3, column);
            Assert.Equal(0, row);
        }

        [Theory]
        [InlineData(0, 10, 1, false)]   // empty group
        [InlineData(9, 10, 1, false)]   // one slot left
        [InlineData(10, 10, 1, true)]   // full single column
        [InlineData(11, 10, 1, true)]   // over capacity
        [InlineData(19, 10, 2, false)]  // room in second column
        [InlineData(20, 10, 2, true)]   // both columns full
        public void IsCapacityReached_ComparesAgainstRowsTimesColumns(int liveCount, int maxRows, int maxColumns, bool expected)
        {
            Assert.Equal(expected, PluginStatusBars.IsCapacityReached(liveCount, maxRows, maxColumns));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-5, 1)]
        [InlineData(1, 1)]
        [InlineData(7, 7)]
        public void NormalizeDimension_ClampsToMinimumOfOne(int value, int expected)
        {
            Assert.Equal(expected, PluginStatusBars.NormalizeDimension(value));
        }

        [Fact]
        public void ResolveMaxRows_FallsBackToDefault_WhenNoProfile()
        {
            Assert.Equal(PluginStatusBars.DefaultMaxRows, PluginStatusBars.ResolveMaxRows());
        }

        [Fact]
        public void ResolveMaxColumns_FallsBackToDefault_WhenNoProfile()
        {
            Assert.Equal(PluginStatusBars.DefaultMaxColumns, PluginStatusBars.ResolveMaxColumns());
        }
    }
}
