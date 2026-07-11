// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Data;
using Xunit;

namespace ClassicUO.UnitTests.Game.Data
{
    public class BuffIconKindTests
    {
        [Fact]
        public void ServerConstructor_DefaultsKindToNone()
        {
            var icon = new BuffIcon(BuffIconType.NightSight, 0x0000, 0, "text");
            Assert.Equal(BuffDisplayKind.None, icon.Kind);
        }

        [Fact]
        public void Kind_EnumValues_MatchPluginApiOrder()
        {
            Assert.Equal(0, (int)BuffDisplayKind.None);
            Assert.Equal(1, (int)BuffDisplayKind.Buff);
            Assert.Equal(2, (int)BuffDisplayKind.Debuff);
        }
    }
}
