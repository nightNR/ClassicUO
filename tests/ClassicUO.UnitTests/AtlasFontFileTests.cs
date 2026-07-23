// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Assets;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class AtlasFontFileTests
    {
        [Fact]
        public void WriteThenRead_RoundTripsHeaderAndGlyph()
        {
            var header = new AtlasFontHeader
            {
                PixelSize = 18, Ascent = 15, Descent = -3, LineHeight = 20,
                FirstChar = 'A', LastChar = 'B'
            };
            var glyphs = new List<AtlasGlyph>
            {
                new AtlasGlyph { OffsetX = 1, OffsetY = -2, Width = 2, Height = 2, Advance = 10,
                                 Coverage = new byte[] { 0, 255, 128, 64 } },
                new AtlasGlyph { OffsetX = 0, OffsetY = 0, Width = 0, Height = 0, Advance = 6,
                                 Coverage = new byte[0] },
            };

            byte[] bytes = AtlasFontFile.Write(header, glyphs);
            AtlasFontFile f = AtlasFontFile.Read(bytes);

            Assert.Equal(18, f.PixelSize);
            Assert.Equal(20, f.LineHeight);
            Assert.True(f.TryGetGlyph('A', out AtlasGlyph a));
            Assert.Equal(2, a.Width);
            Assert.Equal((short)10, a.Advance);
            Assert.Equal(new byte[] { 0, 255, 128, 64 }, a.Coverage);
            Assert.True(f.TryGetGlyph('B', out AtlasGlyph b));
            Assert.Equal(0, b.Width);          // blank glyph
            Assert.False(f.TryGetGlyph('Z', out _)); // out of range
        }
    }
}
