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

        [Fact]
        public void Layout_NoSpans_FillsRowByRow()
        {
            var placed = CounterBarGridMath.Layout(new[] { (1, 1), (1, 1), (1, 1) }, 2);
            Assert.Equal((0, 0), (placed[0].col, placed[0].row));
            Assert.Equal((1, 0), (placed[1].col, placed[1].row));
            Assert.Equal((0, 1), (placed[2].col, placed[2].row));
        }

        [Fact]
        public void Layout_TwoByTwoFirstItem_PushesOthersAround()
        {
            var placed = CounterBarGridMath.Layout(new[] { (2, 2), (1, 1), (1, 1), (1, 1) }, 4);
            Assert.Equal((0, 0, 2, 2), placed[0]);
            Assert.Equal((2, 0), (placed[1].col, placed[1].row));
            Assert.Equal((3, 0), (placed[2].col, placed[2].row));
            Assert.Equal((2, 1), (placed[3].col, placed[3].row));
            Assert.Equal(2, CounterBarGridMath.TotalRows(placed));
        }

        [Fact]
        public void Layout_ColSpanWiderThanGrid_IsClamped()
        {
            var placed = CounterBarGridMath.Layout(new[] { (5, 1), (1, 1) }, 3);
            Assert.Equal((0, 0, 3, 1), placed[0]);
            Assert.Equal((0, 1), (placed[1].col, placed[1].row));
        }

        [Fact]
        public void Layout_SpanInMiddle_WrapsToNextRowWhenItDoesNotFit()
        {
            var placed = CounterBarGridMath.Layout(new[] { (1, 1), (1, 1), (2, 1) }, 3);
            Assert.Equal((0, 1), (placed[2].col, placed[2].row));
        }
    }
}
