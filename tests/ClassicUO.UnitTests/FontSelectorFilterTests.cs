// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class FontSelectorFilterTests
    {
        [Theory]
        [InlineData(0, true)]
        [InlineData(6, true)]
        [InlineData(7, false)]   // rune range start
        [InlineData(10, false)]
        [InlineData(12, false)]  // rune range end
        [InlineData(13, true)]
        [InlineData(20, true)]   // future TTF slot
        public void IsSelectableUnicodeSlot_ExcludesRunes(int slot, bool expected)
        {
            Assert.Equal(expected, OptionsGump.FontSelector.IsSelectableUnicodeSlot(slot));
        }
    }
}
