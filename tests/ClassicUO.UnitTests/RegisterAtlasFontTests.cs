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

        [Fact]
        public void RegisterAtlasFont_BlankGlyphSpaceMeasuresAsUnicodeSpaceWidth()
        {
            // Range covers ' ' (0x20) .. 'A' (0x41). The space is baked as a
            // zero-area glyph (Width=0, Height=0, Coverage=empty array, as a
            // real TTF atlas baker would emit for a blank glyph) and every
            // other slot in between is likewise blank except 'A', which is a
            // real glyph.
            var header = new AtlasFontHeader { PixelSize = 18, Ascent = 15, Descent = -3, LineHeight = 20, FirstChar = ' ', LastChar = 'A' };
            var glyphs = new List<AtlasGlyph>();

            for (char c = ' '; c <= 'A'; c++)
            {
                if (c == 'A')
                {
                    glyphs.Add(new AtlasGlyph { OffsetX = 0, OffsetY = 0, Width = 2, Height = 2, Advance = 9, Coverage = new byte[] { 10, 20, 30, 40 } });
                }
                else
                {
                    glyphs.Add(new AtlasGlyph { OffsetX = 0, OffsetY = 0, Width = 0, Height = 0, Advance = 0, Coverage = System.Array.Empty<byte>() });
                }
            }

            byte[] bytes = AtlasFontFile.Write(header, glyphs);

            var loader = new FontsLoader(new UOFileManager(ClientVersion.CV_70160, string.Empty));
            loader.RegisterAtlasFont(21, "Test Space", bytes);

            // A blank atlas glyph (zero area, non-null empty Coverage) must
            // report HasPixels==false, so the space falls into the
            // `else if (c == ' ')` branch and measures UNICODE_SPACE_WIDTH.
            // UNICODE_SPACE_WIDTH is `private const int ... = 8` in
            // FontsLoader (not internal, so InternalsVisibleTo doesn't reach
            // it) — asserting the literal 8 here, same value as the const.
            Assert.Equal(8, loader.GetWidthUnicode(21, " "));
        }
    }
}
