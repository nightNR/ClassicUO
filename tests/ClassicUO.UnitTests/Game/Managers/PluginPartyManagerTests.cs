using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class PluginPartyManagerTests
    {
        [Fact]
        public void Set_then_Contains_and_TryGetHue_roundtrip()
        {
            var m = new PluginPartyManager();
            m.Set(0x1234, 0x0044);

            Assert.True(m.Contains(0x1234));
            Assert.True(m.TryGetHue(0x1234, out ushort hue));
            Assert.Equal((ushort)0x0044, hue);
        }

        [Fact]
        public void Remove_drops_member()
        {
            var m = new PluginPartyManager();
            m.Set(0x1234, 0x0044);
            m.Remove(0x1234);

            Assert.False(m.Contains(0x1234));
            Assert.False(m.TryGetHue(0x1234, out _));
        }

        [Fact]
        public void Clear_drops_all()
        {
            var m = new PluginPartyManager();
            m.Set(1, 10);
            m.Set(2, 20);
            m.Clear();

            Assert.False(m.Contains(1));
            Assert.False(m.Contains(2));
        }

        [Fact]
        public void Set_same_serial_updates_hue()
        {
            var m = new PluginPartyManager();
            m.Set(7, 100);
            m.Set(7, 200);

            m.TryGetHue(7, out ushort hue);
            Assert.Equal((ushort)200, hue);
        }
    }
}
