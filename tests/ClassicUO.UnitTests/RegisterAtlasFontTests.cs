// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Assets;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class RegisterAtlasFontTests
    {
        [Fact]
        public void RegisterAtlasFont_MakesSlotExistWithMetrics()
        {
            var header = new AtlasFontHeader { PixelSize = 18, Ascent = 15, Descent = -3, LineHeight = 20, FirstChar = 'A', LastChar = 'A' };
            var glyphs = new List<AtlasGlyph> { new AtlasGlyph { OffsetX = 0, OffsetY = 0, Width = 2, Height = 2, Advance = 9, Coverage = new byte[] { 10, 20, 30, 40 } } };
            byte[] bytes = AtlasFontFile.Write(header, glyphs);

            var loader = new FontsLoader(new UOFileManager(ClientVersion.CV_70160, string.Empty));
            loader.RegisterAtlasFont(20, "Test 18", bytes);

            Assert.True(loader.UnicodeFontExists(20));
            Assert.True(loader.IsAtlasFont(20));
            Assert.Equal("Test 18", loader.GetFontDisplayName(20));
            // Advance model: OffsetX(0) + Width(2) + 1 = 3
            Assert.Equal(3, loader.GetWidthUnicode(20, "A"));
        }
    }
}
