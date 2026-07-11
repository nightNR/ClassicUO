// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class RegionMapTests
    {
        private static RegionMap Build(int w, int h, System.Func<int, int, bool> passable)
            => new RegionMap(0, w, h, RegionMapBuilder.Build(w, h, passable));

        [Fact]
        public void Same_component_returns_true()
        {
            var map = Build(4, 4, (x, y) => true);
            Assert.True(map.SameRegion(0, 0, 3, 3));
        }

        [Fact]
        public void Different_components_return_false()
        {
            var map = Build(5, 5, (x, y) => x != 2); // wall at x==2
            Assert.False(map.SameRegion(0, 0, 4, 4));
        }

        [Fact]
        public void Impassable_endpoint_returns_false()
        {
            var map = Build(3, 1, (x, y) => x != 1);
            Assert.Equal(0, map.RegionOf(1, 0));
            Assert.False(map.SameRegion(0, 0, 1, 0));
        }

        [Fact]
        public void Out_of_bounds_returns_zero_and_false()
        {
            var map = Build(2, 2, (x, y) => true);
            Assert.Equal(0, map.RegionOf(-1, 0));
            Assert.Equal(0, map.RegionOf(2, 2));
            Assert.False(map.SameRegion(0, 0, 99, 99));
        }
    }
}
