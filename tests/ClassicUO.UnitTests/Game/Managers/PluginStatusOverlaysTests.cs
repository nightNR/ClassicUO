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
            PluginStatusOverlays.Set(0x1234, 0x0021);
            Assert.Equal((ushort)0x0021, PluginStatusOverlays.Get(0x1234));
        }

        [Fact]
        public void Set_WithZeroHue_ClearsExistingOverlay()
        {
            PluginStatusOverlays.Set(0x1234, 0x0021);
            PluginStatusOverlays.Set(0x1234, 0);
            Assert.Equal((ushort)0, PluginStatusOverlays.Get(0x1234));
        }

        [Fact]
        public void Clear_RemovesOverlay()
        {
            PluginStatusOverlays.Set(0x1234, 0x0021);
            PluginStatusOverlays.Clear(0x1234);
            Assert.Equal((ushort)0, PluginStatusOverlays.Get(0x1234));
        }
    }
}
