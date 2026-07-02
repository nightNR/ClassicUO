// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class PluginBuffsTests
    {
        public PluginBuffsTests() => PluginBuffs.Reset();

        [Fact]
        public void AddOrUpdate_StoresEntry()
        {
            PluginBuffs.AddOrUpdate(7, 0x1234, 5000, BuffDisplayKind.Buff, "hi", now: 1000);
            Assert.True(PluginBuffs.Entries.TryGetValue(7, out var e));
            Assert.Equal((ushort)0x1234, e.Graphic);
            Assert.Equal(BuffDisplayKind.Buff, e.Kind);
            Assert.Equal(6000, e.ExpiryTicks);
        }

        [Fact]
        public void AddOrUpdate_SameId_OverwritesInPlace()
        {
            PluginBuffs.AddOrUpdate(7, 0x1111, 5000, BuffDisplayKind.Buff, "a", now: 0);
            PluginBuffs.AddOrUpdate(7, 0x2222, 1000, BuffDisplayKind.Debuff, "b", now: 100);
            Assert.Single(PluginBuffs.Entries);
            var e = PluginBuffs.Entries[7];
            Assert.Equal((ushort)0x2222, e.Graphic);
            Assert.Equal(BuffDisplayKind.Debuff, e.Kind);
            Assert.Equal(1100, e.ExpiryTicks);
        }

        [Fact]
        public void AddOrUpdate_ZeroDuration_IsInfinite()
        {
            PluginBuffs.AddOrUpdate(7, 0x1234, 0, BuffDisplayKind.None, "", now: 1000);
            Assert.Equal(long.MaxValue, PluginBuffs.Entries[7].ExpiryTicks);
        }

        [Fact]
        public void Remove_DropsEntry_ReturnsTrue()
        {
            PluginBuffs.AddOrUpdate(7, 0x1234, 5000, BuffDisplayKind.Buff, "hi", now: 0);
            Assert.True(PluginBuffs.Remove(7));
            Assert.False(PluginBuffs.Remove(7));
            Assert.Empty(PluginBuffs.Entries);
        }

        [Fact]
        public void CollectExpired_ReturnsOnlyElapsedFiniteBuffs()
        {
            PluginBuffs.AddOrUpdate(1, 0, 1000, BuffDisplayKind.Buff, "", now: 0);   // expires 1000
            PluginBuffs.AddOrUpdate(2, 0, 0, BuffDisplayKind.Buff, "", now: 0);      // infinite
            PluginBuffs.AddOrUpdate(3, 0, 5000, BuffDisplayKind.Buff, "", now: 0);   // expires 5000

            var due = new List<int>();
            PluginBuffs.CollectExpired(now: 1000, due);
            Assert.Equal(new[] { 1 }, due);
        }

        [Fact]
        public void Clear_EmptiesAll()
        {
            PluginBuffs.AddOrUpdate(1, 0, 1000, BuffDisplayKind.Buff, "", now: 0);
            PluginBuffs.Clear();
            Assert.Empty(PluginBuffs.Entries);
        }
    }
}
