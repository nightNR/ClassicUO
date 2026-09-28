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
        public void Search_fails_once_total_node_cap_is_exhausted()
        {
            // Unreachable goal on an unbounded field: without a total cap the resumable
            // search keeps returning Running forever and the walk never reports an outcome.
            var s = new AStarSearch();
            s.Begin(0, 0, 0, 1_000_000, 0, 0, OpenField, maxNodes: 50);

            AStarSearch.Status st;
            int calls = 0;
            do
            {
                st = s.Step(10);
                calls++;
            }
            while (st == AStarSearch.Status.Running && calls < 1000);

            Assert.Equal(AStarSearch.Status.Failed, st);
            Assert.True(s.ExpandedNodes <= 50, $"expanded {s.ExpandedNodes} > cap 50");
        }

        // 1-D corridor along y=0. Ground floor (z=0) spans x=0..15; an upper floor (z=20)
        // spans x=3..10, reachable only via a staircase at x=10: stepping west from the
        // ground tile (10,0,0) can land on (9,0,20).
        private static AStarPathSearch.ExpandNeighbors TwoFloors => (x, y, z, into) =>
        {
            int minX = z == 0 ? 0 : 3;
            int maxX = z == 0 ? 15 : 10;

            if (x + 1 <= maxX) into.Add(new AStarPathSearch.Neighbor(x + 1, 0, z, 2, 1));
            if (x - 1 >= minX) into.Add(new AStarPathSearch.Neighbor(x - 1, 0, z, 6, 1));
            if (z == 0 && x == 10) into.Add(new AStarPathSearch.Neighbor(9, 0, 20, 6, 1));
        };

        private static AStarSearch.Status RunToEnd(AStarSearch s)
        {
            AStarSearch.Status st;
            int calls = 0;
            do
            {
                st = s.Step(100);
                calls++;
            }
            while (st == AStarSearch.Status.Running && calls < 10000);

            return st;
        }

        [Fact]
        public void Goal_on_upper_floor_is_reached_on_that_floor_not_the_one_below()
        {
            var s = new AStarSearch();
            s.Begin(0, 0, 0, 5, 0, 0, TwoFloors, goalZ: 20);

            Assert.Equal(AStarSearch.Status.Found, RunToEnd(s));

            var path = new List<AStarPathSearch.Step>();
            s.GetPath(path);
            Assert.Equal(5, path[^1].X);
            Assert.Equal(20, path[^1].Z);
        }

        [Fact]
        public void Unreachable_goal_floor_falls_back_to_xy_match()
        {
            // Caller passed a Z no floor has (e.g. a guessed 0 on a z=40 map area): keep the
            // legacy X/Y-only behaviour instead of failing the walk.
            var s = new AStarSearch();
            s.Begin(0, 0, 0, 5, 0, 0, TwoFloors, goalZ: 100);

            Assert.Equal(AStarSearch.Status.Found, RunToEnd(s));

            var path = new List<AStarPathSearch.Step>();
            s.GetPath(path);
            Assert.Equal(5, path[^1].X);
        }

        [Fact]
        public void Start_on_goal_xy_but_wrong_floor_still_searches()
        {
            var s = new AStarSearch();
            s.Begin(5, 0, 0, 5, 0, 0, TwoFloors, goalZ: 20);

            Assert.Equal(AStarSearch.Status.Found, RunToEnd(s));

            var path = new List<AStarPathSearch.Step>();
            s.GetPath(path);
            Assert.NotEmpty(path);
            Assert.Equal(20, path[^1].Z);
        }

        [Fact]
        public void Without_goal_z_first_xy_match_wins()
        {
            var s = new AStarSearch();
            s.Begin(0, 0, 0, 5, 0, 0, TwoFloors);

            Assert.Equal(AStarSearch.Status.Found, RunToEnd(s));

            var path = new List<AStarPathSearch.Step>();
            s.GetPath(path);
            Assert.Equal(5, path.Count);
            Assert.Equal(0, path[^1].Z);
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
