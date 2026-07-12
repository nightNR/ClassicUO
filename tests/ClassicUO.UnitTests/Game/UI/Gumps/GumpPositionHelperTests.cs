// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI.Gumps
{
    public class GumpPositionHelperTests
    {
        [Fact]
        public void CenterAnchor_SameSize_ReturnsInputUnchanged()
        {
            var (x, y) = GumpPositionHelper.CenterAnchor(100, 50, 800, 600, 800, 600, 40, 30);
            Assert.Equal(100, x);
            Assert.Equal(50, y);
        }

        [Fact]
        public void CenterAnchor_SmallerWindow_ShiftsByHalfDelta()
        {
            // width delta -200 -> x shifts by -100; height delta -100 -> y shifts by -50
            var (x, y) = GumpPositionHelper.CenterAnchor(400, 300, 800, 600, 600, 500, 40, 30);
            Assert.Equal(300, x);
            Assert.Equal(250, y);
        }

        [Fact]
        public void CenterAnchor_ClampsToLeftTopEdge()
        {
            // shift would drive x/y negative; clamp to 0
            var (x, y) = GumpPositionHelper.CenterAnchor(10, 10, 2000, 2000, 400, 400, 40, 30);
            Assert.Equal(0, x);
            Assert.Equal(0, y);
        }

        [Fact]
        public void CenterAnchor_ClampsToRightBottomEdge()
        {
            // gump 40x30 in a 400x400 window: max x = 360, max y = 370
            var (x, y) = GumpPositionHelper.CenterAnchor(5000, 5000, 400, 400, 400, 400, 40, 30);
            Assert.Equal(360, x);
            Assert.Equal(370, y);
        }

        [Fact]
        public void CenterAnchor_GumpWiderThanWindow_ClampsToZero()
        {
            // max(0, curW - gumpW) is negative -> upper bound floored to 0
            var (x, y) = GumpPositionHelper.CenterAnchor(100, 100, 400, 400, 50, 50, 200, 200);
            Assert.Equal(0, x);
            Assert.Equal(0, y);
        }
    }
}
