// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class CounterBarGridMathTests
    {
        [Fact]
        public void GridPixelSize_UsesColsForWidthRowsForHeight()
        {
            var (w, h) = CounterBarGridMath.GridPixelSize(3, 10, 40, 4);
            Assert.Equal(10 * 40 + 4 * 2, w); // 408
            Assert.Equal(3 * 40 + 4 * 2, h);  // 128
        }

        [Fact]
        public void GridPixelSize_ClampsRowsAndColsToAtLeastOne()
        {
            var (w, h) = CounterBarGridMath.GridPixelSize(0, 0, 40, 4);
            Assert.Equal(1 * 40 + 8, w);
            Assert.Equal(1 * 40 + 8, h);
        }

        [Fact]
        public void GridCapacity_IsRowsTimesCols()
        {
            Assert.Equal(30, CounterBarGridMath.GridCapacity(3, 10));
        }

        [Theory]
        [InlineData(0, 3, 10, 0)]   // no items
        [InlineData(30, 3, 10, 0)]  // exactly full
        [InlineData(31, 3, 10, 1)]  // one over -> one extra row
        [InlineData(50, 3, 10, 2)]  // ceil(50/10)=5 rows, 5-3=2
        public void MaxScroll_CountsOverflowRows(int items, int rows, int cols, int expected)
        {
            Assert.Equal(expected, CounterBarGridMath.MaxScroll(items, rows, cols));
        }
    }
}
