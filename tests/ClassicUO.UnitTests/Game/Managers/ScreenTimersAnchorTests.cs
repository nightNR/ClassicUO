// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class ScreenTimersAnchorTests
    {
        public ScreenTimersAnchorTests() => ScreenTimers.Reset();

        [Fact]
        public void AddOrUpdate_StoresSerialAnchorFields()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, "poison", true,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000,
                anchorOffsetX: 3, anchorOffsetY: -4, anchorGraceMs: 2000);

            var e = ScreenTimers.Entries[1];
            Assert.Equal(AnchorKind.Serial, e.AnchorKind);
            Assert.Equal((uint)0x4000, e.AnchorSerial);
            Assert.Equal((short)3, e.AnchorOffsetX);
            Assert.Equal((short)-4, e.AnchorOffsetY);
            Assert.Equal(2000, e.AnchorGraceMs);
            Assert.Equal(0, e.MissingSinceTicks);
        }

        [Fact]
        public void AddOrUpdate_SecondSerialTimer_ReplacesFirstOnSameSerial()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000);

            Assert.False(ScreenTimers.Entries.ContainsKey(1)); // purged
            Assert.True(ScreenTimers.Entries.ContainsKey(2));
        }

        [Fact]
        public void AddOrUpdate_SameSerialDifferentEntries_DoNotPurgeDifferentSerial()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x5000);

            Assert.True(ScreenTimers.Entries.ContainsKey(1));
            Assert.True(ScreenTimers.Entries.ContainsKey(2));
        }

        [Fact]
        public void SetMissingSince_UpdatesStoredEntry()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000);
            ScreenTimers.SetMissingSince(1, 1234);
            Assert.Equal(1234, ScreenTimers.Entries[1].MissingSinceTicks);
        }

        [Theory]
        [InlineData(0, 0, 0, 0, 0)]
        [InlineData(1, 0, 0, 22, 22)]
        [InlineData(0, 1, 0, -22, 22)]
        [InlineData(10, 10, 5, 0, 420)] // (10+10)*22 - (5<<2) = 440 - 20 = 420
        public void TileToWorldPixel_MatchesIsoFormula(int x, int y, int z, int wx, int wy)
        {
            var (gx, gy) = ScreenTimers.TileToWorldPixel(x, y, z);
            Assert.Equal(wx, gx);
            Assert.Equal(wy, gy);
        }
    }
}
