// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class PluginHighlightAreasTests
    {
        public PluginHighlightAreasTests()
        {
            PluginHighlightAreas.Reset();
        }

        [Fact]
        public void GetTimer_ReturnsZero_WhenIdDoesNotExist()
        {
            Assert.Equal(0, PluginHighlightAreas.GetTimer("missing", now: 0));
        }

        [Fact]
        public void GetTimer_ReturnsMaxValue_WhenAreaNeverExpires()
        {
            // Add(world, id, durationMs, snap, anchorSerial, hue, rangeX, rangeY, objectTypes, x, y, now)
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x21, 3, 3, HighlightObjectTypes.All, 100, 100, 0);

            Assert.Equal(int.MaxValue, PluginHighlightAreas.GetTimer("a", 999999));
        }

        [Fact]
        public void GetTimer_ReturnsRemainingMs_ForTimedArea()
        {
            PluginHighlightAreas.Add(null, "a", 5000, HighlightSnap.Position, 0, (ushort)0x21, 3, 3, HighlightObjectTypes.All, 100, 100, 1000);

            Assert.Equal(3000, PluginHighlightAreas.GetTimer("a", 3000));
        }

        [Fact]
        public void Update_RemovesExpiredArea()
        {
            PluginHighlightAreas.Add(null, "a", 1000, HighlightSnap.Position, 0, (ushort)0x21, 3, 3, HighlightObjectTypes.All, 100, 100, 0);

            PluginHighlightAreas.Update(null, 1500);

            Assert.Equal(0, PluginHighlightAreas.GetTimer("a", 1500));
        }

        [Fact]
        public void TryResolve_MatchesPositionSnap_WithinRange()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0099, 3, 3, HighlightObjectTypes.Land, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(102, 101, 0, HighlightObjectTypes.Land, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0099, hue);
        }

        [Fact]
        public void TryResolve_ReturnsFalse_OutsideRange()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0099, 3, 3, HighlightObjectTypes.Land, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(200, 200, 0, HighlightObjectTypes.Land, out _);

            Assert.False(found);
        }

        [Fact]
        public void TryResolve_ReturnsFalse_WhenObjectTypeNotFlagged()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0099, 3, 3, HighlightObjectTypes.Mobile, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.Land, out _);

            Assert.False(found);
        }

        [Fact]
        public void TryResolve_LastAddedWins_OnOverlap()
        {
            PluginHighlightAreas.Add(null, "first", -1, HighlightSnap.Position, 0, (ushort)0x0011, 5, 5, HighlightObjectTypes.Land, 100, 100, 0);
            PluginHighlightAreas.Add(null, "second", -1, HighlightSnap.Position, 0, (ushort)0x0022, 5, 5, HighlightObjectTypes.Land, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.Land, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0022, hue);
        }

        [Fact]
        public void Add_SerialSnap_ResolvesCenterFromSerialResolver()
        {
            PluginHighlightAreas.SerialResolver = serial => serial == 0xAAAA ? (true, 50, 60, (sbyte)0) : (false, 0, 0, (sbyte)0);

            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Serial, 0xAAAA, (ushort)0x0033, 2, 2, HighlightObjectTypes.Mobile, 0, 0, 0);

            bool found = PluginHighlightAreas.TryResolve(51, 60, 0, HighlightObjectTypes.Mobile, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0033, hue);
        }

        [Fact]
        public void Update_SerialSnap_AutoRemovesWhenAnchorLost()
        {
            PluginHighlightAreas.SerialResolver = _ => (true, 50, 60, (sbyte)0);
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Serial, 0xAAAA, (ushort)0x0033, 2, 2, HighlightObjectTypes.Mobile, 0, 0, 0);

            PluginHighlightAreas.SerialResolver = _ => (false, 0, 0, (sbyte)0);
            PluginHighlightAreas.Update(null, 1);

            Assert.Equal(0, PluginHighlightAreas.GetTimer("a", 1));
        }

        [Fact]
        public void Add_MouseSnap_ResolvesCenterFromMouseWorldResolver()
        {
            PluginHighlightAreas.MouseWorldResolver = () => (77, 88, (sbyte)0);

            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Mouse, 0, (ushort)0x0044, 1, 1, HighlightObjectTypes.Static, 0, 0, 0);

            bool found = PluginHighlightAreas.TryResolve(77, 88, 0, HighlightObjectTypes.Static, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0044, hue);
        }

        [Fact]
        public void Remove_DropsArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0011, 5, 5, HighlightObjectTypes.All, 100, 100, 0);

            PluginHighlightAreas.Remove("a");

            Assert.False(PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.All, out _));
        }

        [Fact]
        public void ClearAll_DropsEveryArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0011, 5, 5, HighlightObjectTypes.All, 100, 100, 0);
            PluginHighlightAreas.Add(null, "b", -1, HighlightSnap.Position, 0, (ushort)0x0022, 5, 5, HighlightObjectTypes.All, 200, 200, 0);

            PluginHighlightAreas.ClearAll();

            Assert.False(PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.All, out _));
            Assert.False(PluginHighlightAreas.TryResolve(200, 200, 0, HighlightObjectTypes.All, out _));
        }
    }
}
