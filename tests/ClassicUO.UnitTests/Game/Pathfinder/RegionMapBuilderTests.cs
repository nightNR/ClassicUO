// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class RegionMapBuilderTests
    {
        private static int Id(int[] ids, int height, int x, int y) => ids[x * height + y];

        [Fact]
        public void Open_field_is_one_region()
        {
            int[] ids = RegionMapBuilder.Build(4, 4, (x, y) => true);

            int first = Id(ids, 4, 0, 0);
            Assert.True(first > 0);
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 4; y++)
                    Assert.Equal(first, Id(ids, 4, x, y));
        }

        [Fact]
        public void Impassable_tiles_get_region_zero()
        {
            int[] ids = RegionMapBuilder.Build(3, 1, (x, y) => x != 1);

            Assert.True(Id(ids, 1, 0, 0) > 0);
            Assert.Equal(0, Id(ids, 1, 1, 0));
            Assert.True(Id(ids, 1, 2, 0) > 0);
        }

        [Fact]
        public void A_full_height_wall_splits_into_two_regions()
        {
            // column x==2 impassable, walls off left (x<2) from right (x>2).
            Func<int, int, bool> passable = (x, y) => x != 2;
            int[] ids = RegionMapBuilder.Build(5, 5, passable);

            int left = Id(ids, 5, 0, 0);
            int right = Id(ids, 5, 4, 0);
            Assert.True(left > 0 && right > 0);
            Assert.NotEqual(left, right);
        }

        [Fact]
        public void Diagonally_touching_cells_are_the_same_region()
        {
            // Only (0,0) and (1,1) passable; they touch diagonally => same region.
            Func<int, int, bool> passable = (x, y) => (x == 0 && y == 0) || (x == 1 && y == 1);
            int[] ids = RegionMapBuilder.Build(2, 2, passable);

            Assert.Equal(Id(ids, 2, 0, 0), Id(ids, 2, 1, 1));
            Assert.True(Id(ids, 2, 0, 0) > 0);
        }
    }
}
