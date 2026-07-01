// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class AStarSearchTests
    {
        private static readonly int[] OffX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] OffY = { -1, -1, 0, 1, 1, 1, 0, -1 };

        private static AStarPathSearch.ExpandNeighbors OpenField => (x, y, z, into) =>
        {
            for (int dir = 0; dir < 8; dir++)
            {
                int cost = (dir % 2 != 0) ? 2 : 1;
                into.Add(new AStarPathSearch.Neighbor(x + OffX[dir], y + OffY[dir], 0, dir, cost));
            }
        };

        [Fact]
        public void Stepping_in_small_budgets_finds_same_path_as_one_shot()
        {
            var oneShot = new List<AStarPathSearch.Step>();
            Assert.True(AStarPathSearch.TryFindPath(0, 0, 0, 10, 0, 0, 1_000_000, OpenField, oneShot));

            var s = new AStarSearch();
            s.Begin(0, 0, 0, 10, 0, 0, OpenField);
            AStarSearch.Status st;
            do
            {
                st = s.Step(3);
            }
            while (st == AStarSearch.Status.Running);

            Assert.Equal(AStarSearch.Status.Found, st);

            var resumed = new List<AStarPathSearch.Step>();
            s.GetPath(resumed);
            Assert.Equal(oneShot.Count, resumed.Count);
            Assert.Equal(oneShot[^1].X, resumed[^1].X);
            Assert.Equal(oneShot[^1].Y, resumed[^1].Y);
        }

        [Fact]
        public void Step_respects_the_node_budget()
        {
            var s = new AStarSearch();
            s.Begin(0, 0, 0, 500, 0, 0, OpenField);

            AStarSearch.Status st = s.Step(5);

            Assert.Equal(AStarSearch.Status.Running, st);
            Assert.True(s.ExpandedNodes <= 5, $"expanded {s.ExpandedNodes} > budget 5");
        }

        [Fact]
        public void Start_within_tolerance_returns_found_empty_path()
        {
            var s = new AStarSearch();
            s.Begin(3, 3, 0, 3, 3, 0, OpenField);

            Assert.Equal(AStarSearch.Status.Found, s.Step(100));

            var path = new List<AStarPathSearch.Step>();
            s.GetPath(path);
            Assert.Empty(path);
        }
    }
}
