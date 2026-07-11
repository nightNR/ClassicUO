// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    /// <summary>
    /// Exercises the pure A* core (heap + hashset) on synthetic grids, with no
    /// UO map data. Walkability is injected via an <see cref="AStarPathSearch.ExpandNeighbors"/>
    /// delegate, so these tests pin down search correctness independent of
    /// CanWalk/CalculateNewZ. Direction encoding matches Pathfinder.GetNewXY:
    /// 0=N,1=NE,2=E,3=SE,4=S,5=SW,6=W,7=NW; odd = diagonal.
    /// </summary>
    public class AStarPathSearchTests
    {
        private static readonly int[] OffX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] OffY = { -1, -1, 0, 1, 1, 1, 0, -1 };

        /// <summary>Eight-connected grid. blocked(x,y) => cell is impassable.
        /// Cardinal step cost 1, diagonal cost 2 (matching Pathfinder).</summary>
        private static AStarPathSearch.ExpandNeighbors Grid(Func<int, int, bool> blocked)
        {
            return (x, y, z, into) =>
            {
                for (int dir = 0; dir < 8; dir++)
                {
                    int nx = x + OffX[dir];
                    int ny = y + OffY[dir];

                    if (blocked(nx, ny))
                    {
                        continue;
                    }

                    int cost = (dir % 2 != 0) ? 2 : 1;
                    into.Add(new AStarPathSearch.Neighbor(nx, ny, 0, dir, cost));
                }
            };
        }

        private static Func<int, int, bool> OpenField => (x, y) => false;

        private static void AssertContiguous(IReadOnlyList<AStarPathSearch.Step> path, int startX, int startY)
        {
            int px = startX, py = startY;
            foreach (var s in path)
            {
                int dx = Math.Abs(s.X - px);
                int dy = Math.Abs(s.Y - py);
                Assert.True(dx <= 1 && dy <= 1 && (dx + dy) > 0, $"non-adjacent step to ({s.X},{s.Y}) from ({px},{py})");
                px = s.X;
                py = s.Y;
            }
        }

        [Fact]
        public void Straight_line_on_open_field_returns_shortest_path()
        {
            var path = new List<AStarPathSearch.Step>();
            bool found = AStarPathSearch.TryFindPath(0, 0, 0, 5, 0, 0, 100_000, Grid(OpenField), path);

            Assert.True(found);
            Assert.Equal(5, path.Count);
            Assert.Equal(5, path[^1].X);
            Assert.Equal(0, path[^1].Y);
            AssertContiguous(path, 0, 0);
        }

        [Fact]
        public void Diagonal_target_uses_diagonal_steps()
        {
            var path = new List<AStarPathSearch.Step>();
            bool found = AStarPathSearch.TryFindPath(0, 0, 0, 5, 5, 0, 100_000, Grid(OpenField), path);

            Assert.True(found);
            // Chebyshev distance 5 => 5 diagonal steps, optimal.
            Assert.Equal(5, path.Count);
            Assert.Equal(5, path[^1].X);
            Assert.Equal(5, path[^1].Y);
            AssertContiguous(path, 0, 0);
        }

        [Fact]
        public void Stops_within_distance_tolerance()
        {
            var path = new List<AStarPathSearch.Step>();
            bool found = AStarPathSearch.TryFindPath(0, 0, 0, 5, 0, 2, 100_000, Grid(OpenField), path);

            Assert.True(found);
            // Reaches within 2 tiles of goal => (3,0), 3 steps.
            Assert.Equal(3, path.Count);
            int cheb = Math.Max(Math.Abs(path[^1].X - 5), Math.Abs(path[^1].Y - 0));
            Assert.True(cheb <= 2);
        }

        [Fact]
        public void Start_already_within_distance_returns_true_empty_path()
        {
            var path = new List<AStarPathSearch.Step>();
            bool found = AStarPathSearch.TryFindPath(3, 3, 0, 3, 3, 0, 100_000, Grid(OpenField), path);

            Assert.True(found);
            Assert.Empty(path);
        }

        [Fact]
        public void Routes_around_a_wall()
        {
            // Vertical wall at x==2 for y in 0..4, with a gap at y==5 (open).
            Func<int, int, bool> wall = (x, y) => x == 2 && y >= 0 && y <= 4;

            var path = new List<AStarPathSearch.Step>();
            bool found = AStarPathSearch.TryFindPath(0, 0, 0, 4, 0, 0, 100_000, Grid(wall), path);

            Assert.True(found);
            Assert.Equal(4, path[^1].X);
            Assert.Equal(0, path[^1].Y);
            AssertContiguous(path, 0, 0);
            foreach (var s in path)
            {
                Assert.False(wall(s.X, s.Y), $"path crosses wall at ({s.X},{s.Y})");
            }
        }

        [Fact]
        public void Unreachable_goal_returns_false()
        {
            // Box the goal in completely.
            Func<int, int, bool> box = (x, y) =>
            {
                // wall ring around goal (10,10)
                return (Math.Abs(x - 10) == 1 && Math.Abs(y - 10) <= 1) ||
                       (Math.Abs(y - 10) == 1 && Math.Abs(x - 10) <= 1);
            };

            var path = new List<AStarPathSearch.Step>();
            bool found = AStarPathSearch.TryFindPath(0, 0, 0, 10, 10, 0, 100_000, Grid(box), path);

            Assert.False(found);
            Assert.Empty(path);
        }

        [Fact]
        public void Exceeding_maxNodes_returns_false()
        {
            var path = new List<AStarPathSearch.Step>();
            // Far goal on open field but a tiny node budget cannot reach it.
            bool found = AStarPathSearch.TryFindPath(0, 0, 0, 500, 500, 0, 10, Grid(OpenField), path);

            Assert.False(found);
        }

        [Fact]
        public void Long_distance_beyond_legacy_10000_cap_succeeds()
        {
            var path = new List<AStarPathSearch.Step>();
            // 200 tiles straight: legacy 10000-node cap would explore the whole
            // frontier and fail; the new search reaches it with a generous budget.
            bool found = AStarPathSearch.TryFindPath(0, 0, 0, 200, 0, 0, 1_000_000, Grid(OpenField), path);

            Assert.True(found);
            Assert.Equal(200, path.Count);
            Assert.Equal(200, path[^1].X);
        }
    }
}
