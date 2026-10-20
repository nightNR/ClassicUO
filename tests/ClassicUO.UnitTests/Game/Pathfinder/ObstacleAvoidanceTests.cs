// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    /// <summary>
    /// Exercises <see cref="ObstacleAvoidance.Steer"/> on synthetic grids.
    /// Direction encoding matches Pathfinder.GetNewXY: 0=N(-y),1=NE,2=E(+x),
    /// 3=SE,4=S,5=SW,6=W,7=NW; odd = diagonal.
    /// </summary>
    public class ObstacleAvoidanceTests
    {
        private static readonly int[] OffX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] OffY = { -1, -1, 0, 1, 1, 1, 0, -1 };

        private static ObstacleAvoidance.WalkProbe Grid(params (int x, int y)[] blocked)
        {
            var set = new HashSet<(int, int)>(blocked);

            return (Direction dir, int x, int y, sbyte z, out int nx, out int ny, out sbyte nz) =>
            {
                int d = (int) dir & 7;
                nx = x + OffX[d];
                ny = y + OffY[d];
                nz = z;

                return !set.Contains((nx, ny));
            };
        }

        // Cursor straight ahead of `d` in screen space (screen = (ox-oy, ox+oy) * 100).
        private static (int, int) Ahead(int d) => ((OffX[d] - OffY[d]) * 100, (OffX[d] + OffY[d]) * 100);

        [Fact]
        public void FreePath_ReturnsWant()
        {
            int last = 0;
            var (cx, cy) = Ahead(2);

            Direction r = ObstacleAvoidance.Steer(Direction.East, 0, 0, 0, cx, cy, ref last, Grid());

            Assert.Equal(Direction.East, r);
        }

        [Fact]
        public void ObstacleTwoTilesAhead_StepsDiagonalEarly()
        {
            int last = 0;
            var (cx, cy) = Ahead(2);

            // East heading from (0,0); tree at (2,0). Lookahead 2 sees it.
            Direction r = ObstacleAvoidance.Steer(Direction.East, 0, 0, 0, cx, cy, ref last, Grid((2, 0)));

            Assert.True(r == Direction.Right || r == Direction.Down);
        }

        [Fact]
        public void ObstacleOneTileAhead_StepsDiagonal()
        {
            int last = 0;
            var (cx, cy) = Ahead(2);

            Direction r = ObstacleAvoidance.Steer(Direction.East, 0, 0, 0, cx, cy, ref last, Grid((1, 0)));

            Assert.True(r == Direction.Right || r == Direction.Down);
        }

        [Fact]
        public void SideFollowsCursor()
        {
            int last = 0;
            // Cursor leaning toward SE (tile +y) of East heading.
            int cx = (OffX[2] - OffY[2]) * 100 + (OffX[3] - OffY[3]) * 30;
            int cy = (OffX[2] + OffY[2]) * 100 + (OffX[3] + OffY[3]) * 30;

            Direction r = ObstacleAvoidance.Steer(Direction.East, 0, 0, 0, cx, cy, ref last, Grid((2, 0)));

            Assert.Equal(Direction.Down, r);
            Assert.Equal(1, last);
        }

        [Fact]
        public void Tie_UsesLastSide()
        {
            var (cx, cy) = Ahead(2);

            int last = -1;
            Direction r1 = ObstacleAvoidance.Steer(Direction.East, 0, 0, 0, cx, cy, ref last, Grid((2, 0)));

            last = 1;
            Direction r2 = ObstacleAvoidance.Steer(Direction.East, 0, 0, 0, cx, cy, ref last, Grid((2, 0)));

            Assert.Equal(Direction.Right, r1); // side -1 = want-1
            Assert.Equal(Direction.Down, r2); // side +1 = want+1
        }

        [Fact]
        public void OneSideBlocked_PicksOtherSide()
        {
            int last = 0;
            var (cx, cy) = Ahead(2);

            // Tree ahead and NE blocked too: only SE works.
            Direction r = ObstacleAvoidance.Steer(Direction.East, 0, 0, 0, cx, cy, ref last, Grid((2, 0), (1, -1)));

            Assert.Equal(Direction.Down, r);
        }

        [Fact]
        public void Wall_ReturnsWant()
        {
            int last = 0;
            var (cx, cy) = Ahead(2);
            var wall = new List<(int, int)>();

            for (int y = -5; y <= 5; y++)
            {
                wall.Add((2, y));
            }

            Direction r = ObstacleAvoidance.Steer(Direction.East, 0, 0, 0, cx, cy, ref last, Grid(wall.ToArray()));

            Assert.Equal(Direction.East, r);
        }

        [Fact]
        public void DiagonalHeading_ObstacleAhead_SteersToCardinal()
        {
            int last = 0;
            var (cx, cy) = Ahead(1);

            // NE heading from (0,0); rock at (2,-2) (lookahead 2).
            Direction r = ObstacleAvoidance.Steer(Direction.Right, 0, 0, 0, cx, cy, ref last, Grid((2, -2)));

            Assert.True(r == Direction.North || r == Direction.East);
        }
    }
}
