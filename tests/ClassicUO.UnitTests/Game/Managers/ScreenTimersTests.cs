// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class ScreenTimersTests
    {
        public ScreenTimersTests() => ScreenTimers.Reset();

        [Fact]
        public void AddOrUpdate_StoresEntry_WithStartAndOrder()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 33, groupId: 0,
                x: 10, y: 20, width: 0, height: 0, label: "L", showTime: true, now: 1000);

            Assert.True(ScreenTimers.Entries.TryGetValue(1, out var e));
            Assert.Equal(TimerShape.Bar, e.Shape);
            Assert.Equal(1000, e.StartTicks);
            Assert.Equal(0, e.Order);
        }

        [Fact]
        public void AddOrUpdate_SameId_RestartsTimer_KeepsOrder()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 0, 5, 0, 0, 0, 0, null, false, now: 0);
            int order0 = ScreenTimers.Entries[1].Order;
            ScreenTimers.AddOrUpdate(1, TimerShape.Circle, 2000, 0, 5, 0, 0, 0, 0, null, false, now: 500);

            var e = ScreenTimers.Entries[1];
            Assert.Equal(TimerShape.Circle, e.Shape);
            Assert.Equal(500, e.StartTicks);      // restarted
            Assert.Equal(2000, e.DurationMs);
            Assert.Equal(order0, e.Order);        // order preserved
        }

        [Fact]
        public void AddOrUpdate_AssignsIncreasingOrderWithinGroup()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, groupId: 9, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 1000, 0, groupId: 9, 0, 0, 0, 0, null, false, now: 0);
            Assert.Equal(0, ScreenTimers.Entries[1].Order);
            Assert.Equal(1, ScreenTimers.Entries[2].Order);
        }

        [Fact]
        public void RemainingFraction_IsClampedZeroToOne()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, 0, 0, 0, 0, 0, null, false, now: 0);
            var e = ScreenTimers.Entries[1];
            Assert.Equal(1f, ScreenTimers.RemainingFraction(e, 0));
            Assert.Equal(0.5f, ScreenTimers.RemainingFraction(e, 500), 3);
            Assert.Equal(0f, ScreenTimers.RemainingFraction(e, 1000));
            Assert.Equal(0f, ScreenTimers.RemainingFraction(e, 5000)); // past expiry clamps
        }

        [Fact]
        public void CollectExpired_ReturnsElapsedTimers()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, 0, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false, now: 0);
            var due = new List<int>();
            ScreenTimers.CollectExpired(1000, due);
            Assert.Equal(new[] { 1 }, due);
        }

        [Fact]
        public void ComputePosition_Lone_UsesEntryXY()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, groupId: 0, x: 40, y: 60, 0, 0, null, false, now: 0);
            var e = ScreenTimers.Entries[1];
            var group = default(TimerGroup);
            var (x, y) = ScreenTimers.ComputePosition(e, group, extent: 20);
            Assert.Equal((40, 60), (x, y));
        }

        [Fact]
        public void ComputePosition_GroupDown_OffsetsByOrderTimesExtentPlusGap()
        {
            ScreenTimers.DefineGroup(9, x: 100, y: 200, StackDirection.Down, gap: 5);
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0); // order 0
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0); // order 1

            Assert.True(ScreenTimers.TryGetGroup(9, out var g));
            var p0 = ScreenTimers.ComputePosition(ScreenTimers.Entries[1], g, extent: 20);
            var p1 = ScreenTimers.ComputePosition(ScreenTimers.Entries[2], g, extent: 20);
            Assert.Equal((100, 200), p0);
            Assert.Equal((100, 200 + 1 * (20 + 5)), p1);
        }

        [Fact]
        public void ComputePosition_GroupUpAndLeft_UseNegativeOffsets()
        {
            ScreenTimers.DefineGroup(9, 100, 200, StackDirection.Up, gap: 5);
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.TryGetGroup(9, out var gUp);
            Assert.Equal((100, 200 - 1 * (20 + 5)), ScreenTimers.ComputePosition(ScreenTimers.Entries[2], gUp, 20));

            ScreenTimers.DefineGroup(8, 300, 400, StackDirection.Left, gap: 2);
            ScreenTimers.AddOrUpdate(3, TimerShape.Bar, 1000, 0, 8, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(4, TimerShape.Bar, 1000, 0, 8, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.TryGetGroup(8, out var gLeft);
            Assert.Equal((300 - 1 * (30 + 2), 400), ScreenTimers.ComputePosition(ScreenTimers.Entries[4], gLeft, 30));
        }

        [Fact]
        public void RemoveGroup_RemovesMembersAndGroup()
        {
            ScreenTimers.DefineGroup(9, 0, 0, StackDirection.Down, 0);
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(3, TimerShape.Bar, 1000, 0, 0, 0, 0, 0, 0, null, false, now: 0); // lone

            var removed = new List<int>();
            ScreenTimers.RemoveGroup(9, removed);

            Assert.Equal(new[] { 1, 2 }, removed);
            Assert.False(ScreenTimers.TryGetGroup(9, out _));
            Assert.True(ScreenTimers.Entries.ContainsKey(3));
        }
    }
}
