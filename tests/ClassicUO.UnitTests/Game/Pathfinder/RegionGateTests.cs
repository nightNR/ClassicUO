// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class RegionGateTests
    {
        private static RegionMap Wall5x5() => new RegionMap(0, 5, 5, RegionMapBuilder.Build(5, 5, (x, y) => x != 2));

        [Fact]
        public void Null_map_never_rejects()
        {
            Assert.False(RegionGate.IsUnreachable(null, 0, 0, 999, 999));
        }

        [Fact]
        public void Different_regions_are_unreachable()
        {
            Assert.True(RegionGate.IsUnreachable(Wall5x5(), 0, 0, 4, 4));
        }

        [Fact]
        public void Same_region_is_reachable()
        {
            Assert.False(RegionGate.IsUnreachable(Wall5x5(), 0, 0, 1, 4));
        }

        [Fact]
        public void Unknown_endpoint_stays_optimistic()
        {
            // goal on the impassable wall (region 0) => do not reject.
            Assert.False(RegionGate.IsUnreachable(Wall5x5(), 0, 0, 2, 2));
        }
    }
}
