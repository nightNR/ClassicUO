// SPDX-License-Identifier: BSD-2-Clause
using ClassicUO.Configuration;
using ClassicUO.Game.UI.Controls;
using Xunit;

namespace ClassicUO.UnitTests.UI
{
    public class FontLocationRowTests
    {
        [Fact(Skip =
            "FontLocationRow's constructor builds Combobox children, whose ResizePic " +
            "and Label immediately resolve through the process-wide Client.Game.UO " +
            "singleton (Gumps.GetGump / FileManager.Fonts). That singleton is only " +
            "ever populated by the real FNA game bootstrap (Client.Run), which " +
            "requires a live SDL/graphics context and cannot be constructed in this " +
            "unit-test host (same class of gap documented on " +
            "TargetManagerTryResolveObjectTests.ReturnsTrue_ForLandObject). The " +
            "face/size mapping this test exercises is covered without constructing " +
            "any Control by FontLocationRowLogicTests; the fully-built row is covered " +
            "by manual verification in Options > Fonts / Options > Tooltip.")]
        public void GetChoice_DefaultClassic_EmptyFace()
        {
            var row = new FontLocationRow(null, 0, 0, new FontChoice { Face = "", Size = 0 });
            FontChoice c = row.GetChoice();
            Assert.True(string.IsNullOrEmpty(c.Face));
        }

        [Fact(Skip =
            "Same Client.Game.UO construction gap as GetChoice_DefaultClassic_EmptyFace " +
            "above (see that test's Skip reason). Covered by FontLocationRowLogicTests.")]
        public void GetChoice_RoundTripsTtfChoice()
        {
            var row = new FontLocationRow(null, 0, 0, new FontChoice { Face = "inter", Size = 18 });
            FontChoice c = row.GetChoice();
            Assert.Equal("inter", c.Face);
            Assert.Equal((byte)18, c.Size);
        }
    }
}
