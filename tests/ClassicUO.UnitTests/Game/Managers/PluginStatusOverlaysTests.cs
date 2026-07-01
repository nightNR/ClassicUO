// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class PluginStatusOverlaysTests
    {
        public PluginStatusOverlaysTests() => PluginStatusOverlays.Reset();

        [Fact]
        public void Get_ReturnsZero_WhenNoOverlaySet()
        {
            Assert.Equal((ushort)0, PluginStatusOverlays.Get(0x1234));
        }

        [Fact]
        public void Set_StoresHue_RetrievableByGet()
        {
            PluginStatusOverlays.Set(0x1234, 0x0021, 0);
            Assert.Equal((ushort)0x0021, PluginStatusOverlays.Get(0x1234));
        }

        [Fact]
        public void Set_StoresBackgroundHue_RetrievableByGetBackground()
        {
            PluginStatusOverlays.Set(0x1234, 0x0021, 0x0044);
            Assert.Equal((ushort)0x0044, PluginStatusOverlays.GetBackground(0x1234));
        }

        [Fact]
        public void Set_BackgroundOnly_StoresBackground_RingZero()
        {
            PluginStatusOverlays.Set(0x1234, 0, 0x0044);
            Assert.Equal((ushort)0, PluginStatusOverlays.Get(0x1234));
            Assert.Equal((ushort)0x0044, PluginStatusOverlays.GetBackground(0x1234));
        }

        [Fact]
        public void Set_WithBothZero_ClearsExistingOverlay()
        {
            PluginStatusOverlays.Set(0x1234, 0x0021, 0x0044);
            PluginStatusOverlays.Set(0x1234, 0, 0);
            Assert.Equal((ushort)0, PluginStatusOverlays.Get(0x1234));
            Assert.Equal((ushort)0, PluginStatusOverlays.GetBackground(0x1234));
        }

        [Fact]
        public void Clear_RemovesOverlay()
        {
            PluginStatusOverlays.Set(0x1234, 0x0021, 0x0044);
            PluginStatusOverlays.Clear(0x1234);
            Assert.Equal((ushort)0, PluginStatusOverlays.Get(0x1234));
            Assert.Equal((ushort)0, PluginStatusOverlays.GetBackground(0x1234));
        }
    }
}
