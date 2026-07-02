// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class PluginTimersManagerTests
    {
        public PluginTimersManagerTests() => PluginTimersManager.Reset();

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
    }
}
