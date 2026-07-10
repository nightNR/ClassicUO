// SPDX-License-Identifier: BSD-2-Clause

using System.IO;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class AliasManagerTests
    {
        private static int _seq;

        private static AliasManager New()
        {
            var m = new AliasManager(null);
            m.GlobalPathOverride = Path.Combine(Path.GetTempPath(), $"cuo_alias_unit_{System.Threading.Interlocked.Increment(ref _seq)}.xml");
            if (File.Exists(m.GlobalPathOverride)) File.Delete(m.GlobalPathOverride);
            return m;
        }

        [Fact]
        public void Resolve_ReturnsRealName_WhenNoAlias()
        {
            var m = New();
            Assert.Equal("Bazinka", m.Resolve(0xA0, "Bazinka"));
        }

        [Fact]
        public void Resolve_ReturnsAlias_WhenSet()
        {
            var m = New();
            m.Set(0xA0, "Ducky", global: false);
            Assert.Equal("Ducky", m.Resolve(0xA0, "Bazinka"));
        }

        [Fact]
        public void Resolve_ReturnsRealName_WhenDisabled()
        {
            var m = New();
            m.Set(0xA0, "Ducky", global: false);
            m.Enabled = false;
            Assert.Equal("Bazinka", m.Resolve(0xA0, "Bazinka"));
        }

        [Fact]
        public void ProfileAlias_BeatsGlobalAlias_ForSameSerial()
        {
            var m = New();
            m.Set(0xA0, "GlobalName", global: true);
            m.Set(0xA0, "ProfileName", global: false);
            Assert.Equal("ProfileName", m.Resolve(0xA0, "Bazinka"));
            Assert.False(m.IsGlobal(0xA0));
        }

        [Fact]
        public void Set_MovesEntry_BetweenStores()
        {
            var m = New();
            m.Set(0xA0, "Ducky", global: false);
            Assert.False(m.IsGlobal(0xA0));
            m.Set(0xA0, "Ducky", global: true);
            Assert.True(m.IsGlobal(0xA0));
            Assert.Single(m.Entries);
        }

        [Fact]
        public void Remove_ClearsAlias()
        {
            var m = New();
            m.Set(0xA0, "Ducky", global: true);
            m.Remove(0xA0);
            Assert.Equal("Bazinka", m.Resolve(0xA0, "Bazinka"));
            Assert.Empty(m.Entries);
        }

        [Fact]
        public void GlobalStore_RoundTrips_ThroughXml()
        {
            string path = Path.Combine(Path.GetTempPath(), "cuo_alias_test.xml");
            if (File.Exists(path)) File.Delete(path);

            var a = New();
            a.Set(0xA0, "Ducky", global: true);
            a.Set(0xB1, "Piggy", global: true);
            a.SaveGlobal(path);

            var b = New();
            b.ReadGlobal(path);
            Assert.Equal("Ducky", b.Resolve(0xA0, "x"));
            Assert.Equal("Piggy", b.Resolve(0xB1, "y"));
            Assert.True(b.IsGlobal(0xA0));

            File.Delete(path);
        }
    }
}
