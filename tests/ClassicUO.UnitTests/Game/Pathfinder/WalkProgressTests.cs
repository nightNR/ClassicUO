// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    /// <summary>
    /// Exercises the new auto-walk emit seams without map data:
    /// EndAutoWalk reason mapping, ResetAutoWalk's no-emit teardown, and
    /// StopAutoWalk emitting Stopped. Pathfinder.WalkProgress is a static
    /// event; each test subscribes a local handler and unsubscribes in a
    /// finally so methods (run serially within this class) stay isolated.
    /// </summary>
    public class WalkProgressTests
    {
        private static ClassicUO.Game.Pathfinder NewPathfinder() => new ClassicUO.Game.Pathfinder(new World());

        private static List<WalkState> Capture(System.Action<ClassicUO.Game.Pathfinder> act)
        {
            var states = new List<WalkState>();
            void Handler(WalkState s) => states.Add(s);
            ClassicUO.Game.Pathfinder.WalkProgress += Handler;
            try
            {
                var pf = NewPathfinder();
                pf.AutoWalking = true;
                act(pf);
            }
            finally
            {
                ClassicUO.Game.Pathfinder.WalkProgress -= Handler;
            }
            return states;
        }

        [Fact]
        public void EndAutoWalk_Arrived_emits_Arrived_and_clears_AutoWalking()
        {
            ClassicUO.Game.Pathfinder captured = null;
            var states = new List<WalkState>();
            void Handler(WalkState s) => states.Add(s);
            ClassicUO.Game.Pathfinder.WalkProgress += Handler;
            try
            {
                captured = NewPathfinder();
                captured.AutoWalking = true;
                captured.EndAutoWalk(WalkState.Arrived);
            }
            finally
            {
                ClassicUO.Game.Pathfinder.WalkProgress -= Handler;
            }

            Assert.Equal(new[] { WalkState.Arrived }, states);
            Assert.False(captured.AutoWalking);
        }

        [Fact]
        public void EndAutoWalk_Blocked_emits_Blocked()
        {
            var states = Capture(pf => pf.EndAutoWalk(WalkState.Blocked));
            Assert.Equal(new[] { WalkState.Blocked }, states);
        }

        [Fact]
        public void StopAutoWalk_emits_Stopped_and_clears_AutoWalking()
        {
            ClassicUO.Game.Pathfinder captured = null;
            var states = new List<WalkState>();
            void Handler(WalkState s) => states.Add(s);
            ClassicUO.Game.Pathfinder.WalkProgress += Handler;
            try
            {
                captured = NewPathfinder();
                captured.AutoWalking = true;
                captured.StopAutoWalk();
            }
            finally
            {
                ClassicUO.Game.Pathfinder.WalkProgress -= Handler;
            }

            Assert.Equal(new[] { WalkState.Stopped }, states);
            Assert.False(captured.AutoWalking);
        }

        [Fact]
        public void Server_teleport_mid_walk_stops_the_walk()
        {
            // Teleporter tile: the server never confirms the step onto (2,2), it relocates the
            // player straight to (10,10). Continuing would replan from (10,10) back toward the
            // old goal — possibly walking back through the teleporter.
            var states = Capture(pf => pf.OnServerRelocated(1, 1, 10, 10));
            Assert.Equal(new[] { WalkState.Stopped }, states);
        }

        [Fact]
        public void One_tile_resync_does_not_stop_the_walk()
        {
            // A denied step snaps the player back by at most one tile — ordinary walk jitter.
            var states = Capture(pf => pf.OnServerRelocated(5, 5, 4, 5));
            Assert.Empty(states);
        }

        [Fact]
        public void Relocation_without_active_walk_emits_nothing()
        {
            var states = new List<WalkState>();
            void Handler(WalkState s) => states.Add(s);
            ClassicUO.Game.Pathfinder.WalkProgress += Handler;
            try
            {
                NewPathfinder().OnServerRelocated(1, 1, 10, 10);
            }
            finally
            {
                ClassicUO.Game.Pathfinder.WalkProgress -= Handler;
            }

            Assert.Empty(states);
        }

        [Fact]
        public void ResetAutoWalk_does_not_emit_but_clears_AutoWalking()
        {
            ClassicUO.Game.Pathfinder captured = null;
            var states = new List<WalkState>();
            void Handler(WalkState s) => states.Add(s);
            ClassicUO.Game.Pathfinder.WalkProgress += Handler;
            try
            {
                captured = NewPathfinder();
                captured.AutoWalking = true;
                captured.ResetAutoWalk();
            }
            finally
            {
                ClassicUO.Game.Pathfinder.WalkProgress -= Handler;
            }

            Assert.Empty(states);
            Assert.False(captured.AutoWalking);
        }
    }
}
