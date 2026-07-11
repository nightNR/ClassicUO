// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class PluginTimersManagerTests
    {
        public PluginTimersManagerTests()
        {
            PluginTimersManager.Reset();
            PluginTimersManager.SerialResolver = null;
        }

        [Fact]
        public void Update_ExpiresBuff_RemovesAndDispatchesExpired()
        {
            var events = new List<(int id, int reason)>();
            PluginTimersManager.BuffEventSink = (id, reason) => events.Add((id, reason));

            PluginBuffs.AddOrUpdate(5, 0x10, 1000, BuffDisplayKind.Buff, "", now: 0);
            PluginTimersManager.Update(now: 1000);

            Assert.False(PluginBuffs.Entries.ContainsKey(5));
            Assert.Equal(new[] { (5, PluginTimersManager.ReasonExpired) }, events);
        }

        [Fact]
        public void Update_ExpiresTimer_RemovesAndDispatchesExpired()
        {
            var events = new List<(int id, int reason)>();
            PluginTimersManager.TimerEventSink = (id, reason) => events.Add((id, reason));

            ScreenTimers.AddOrUpdate(3, TimerShape.Bar, 1000, 0, 0, 0, 0, 0, 0, null, false, now: 0);
            PluginTimersManager.Update(now: 1000);

            Assert.False(ScreenTimers.Entries.ContainsKey(3));
            Assert.Equal(new[] { (3, PluginTimersManager.ReasonExpired) }, events);
        }

        [Fact]
        public void Update_LeavesLiveEntriesUntouched()
        {
            var buffEvents = new List<(int, int)>();
            PluginTimersManager.BuffEventSink = (id, reason) => buffEvents.Add((id, reason));
            PluginBuffs.AddOrUpdate(5, 0x10, 5000, BuffDisplayKind.Buff, "", now: 0);

            PluginTimersManager.Update(now: 1000);

            Assert.True(PluginBuffs.Entries.ContainsKey(5));
            Assert.Empty(buffEvents);
        }

        [Fact]
        public void RaiseBuffEvent_UsesSinkWhenSet()
        {
            var events = new List<(int, int)>();
            PluginTimersManager.BuffEventSink = (id, reason) => events.Add((id, reason));
            PluginTimersManager.RaiseBuffEvent(9, PluginTimersManager.ReasonRemovedByPlugin);
            Assert.Equal(new[] { (9, PluginTimersManager.ReasonRemovedByPlugin) }, events);
        }

        [Fact]
        public void Update_SerialAnchorMissingBeyondGrace_RemovesWithAnchorLost()
        {
            var events = new List<(int id, int reason)>();
            PluginTimersManager.TimerEventSink = (id, reason) => events.Add((id, reason));
            PluginTimersManager.SerialResolver = _ => false; // always missing

            ScreenTimers.AddOrUpdate(7, TimerShape.Bar, 60000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000, anchorGraceMs: 1000);

            PluginTimersManager.Update(now: 500);   // within grace: still alive
            Assert.True(ScreenTimers.Entries.ContainsKey(7));
            Assert.Empty(events);

            PluginTimersManager.Update(now: 1500);  // grace elapsed: removed
            Assert.False(ScreenTimers.Entries.ContainsKey(7));
            Assert.Equal(new[] { (7, PluginTimersManager.ReasonAnchorLost) }, events);
        }

        [Fact]
        public void Update_SerialAnchorReappearsWithinGrace_ResetsAndKeeps()
        {
            var events = new List<(int id, int reason)>();
            PluginTimersManager.TimerEventSink = (id, reason) => events.Add((id, reason));

            ScreenTimers.AddOrUpdate(7, TimerShape.Bar, 60000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000, anchorGraceMs: 1000);

            PluginTimersManager.SerialResolver = _ => false;
            PluginTimersManager.Update(now: 500);   // marks missing
            Assert.Equal(500, ScreenTimers.Entries[7].MissingSinceTicks);

            PluginTimersManager.SerialResolver = _ => true; // back
            PluginTimersManager.Update(now: 700);
            Assert.Equal(0, ScreenTimers.Entries[7].MissingSinceTicks);
            Assert.True(ScreenTimers.Entries.ContainsKey(7));
            Assert.Empty(events);
        }

        [Fact]
        public void Update_AbsoluteAnchor_NeverLost()
        {
            var events = new List<(int id, int reason)>();
            PluginTimersManager.TimerEventSink = (id, reason) => events.Add((id, reason));
            PluginTimersManager.SerialResolver = _ => false;

            ScreenTimers.AddOrUpdate(8, TimerShape.Bar, 60000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Absolute, anchorX: 100, anchorY: 100, anchorGraceMs: 1000);

            PluginTimersManager.Update(now: 5000);
            Assert.True(ScreenTimers.Entries.ContainsKey(8));
            Assert.Empty(events);
        }
    }
}
