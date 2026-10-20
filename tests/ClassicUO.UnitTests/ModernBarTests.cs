// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class ModernBarTests
    {
        [Fact]
        public void RoundedRect_CornerTransparent_CenterOpaque()
        {
            const int r = 10;
            int size = RoundedRectMath.TextureSize(r);
            byte[] fill = RoundedRectMath.Build(r, 0);

            Assert.Equal(0, fill[3]); // top-left pixel is outside the arc
            Assert.Equal(255, fill[((size / 2) * size + size / 2) * 4 + 3]);
        }

        [Fact]
        public void RoundedRect_RingIsHollow()
        {
            const int r = 10;
            int size = RoundedRectMath.TextureSize(r);
            byte[] ring = RoundedRectMath.Build(r, 2);

            Assert.Equal(0, ring[((size / 2) * size + size / 2) * 4 + 3]);
            Assert.True(ring[((size / 2) * size + 0) * 4 + 3] > 200); // left edge midpoint is on the ring
        }

        [Fact]
        public void RoundedRect_IsPremultipliedWhite()
        {
            byte[] fill = RoundedRectMath.Build(8, 0);

            for (int i = 0; i < fill.Length; i += 4)
            {
                Assert.Equal(fill[i + 3], fill[i]);
                Assert.Equal(fill[i + 3], fill[i + 1]);
                Assert.Equal(fill[i + 3], fill[i + 2]);
            }
        }

        [Theory]
        [InlineData((int)StatGlyph.Heart)]
        [InlineData((int)StatGlyph.Drop)]
        [InlineData((int)StatGlyph.Bolt)]
        [InlineData((int)StatGlyph.ArrowLeft)]
        [InlineData((int)StatGlyph.ArrowRight)]
        public void Glyph_HasCoverageAndTransparentCorner(int glyphId)
        {
            byte[] data = RoundedRectMath.BuildGlyph((StatGlyph)glyphId, 16);
            int covered = 0;

            for (int i = 3; i < data.Length; i += 4)
            {
                if (data[i] > 0)
                {
                    covered++;
                }
            }

            Assert.True(covered > 20);
            Assert.True(covered < 16 * 16);
        }

        [Theory]
        [InlineData(0f, 100, 4, 0)]
        [InlineData(1f, 100, 4, 100)]
        [InlineData(2f, 100, 4, 100)]
        [InlineData(0.5f, 100, 4, 50)]
        [InlineData(0.01f, 100, 4, 4)]
        public void FillWidth_ClampsAndKeepsMinimum(float fraction, int track, int min, int expected)
        {
            Assert.Equal(expected, ModernBarStyle.FillWidth(fraction, track, min));
        }

        [Theory]
        [InlineData(0, 50)]
        [InlineData(100, 100)]
        [InlineData(1000, 300)]
        public void ClampScale_StaysInRange(int input, int expected)
        {
            Assert.Equal(expected, ModernBarStyle.ClampScale(input));
        }

        [Fact]
        public void Layout_ScalesWithProfile()
        {
            var one = new ModernBarStyle(new Profile { ModernBarScale = 100 });
            var two = new ModernBarStyle(new Profile { ModernBarScale = 200 });

            Assert.InRange(two.Width(false), one.Width(false) * 2 - 4, one.Width(false) * 2 + 4);
            Assert.InRange(two.Height(3), one.Height(3) * 2 - 6, one.Height(3) * 2 + 6);
            Assert.True(one.Width(true) > one.Width(false));
            Assert.True(one.Height(3) > one.Height(1));
        }

        [Fact]
        public void Scale_100Percent_IsThreeQuartersOfRawUnits()
        {
            var style = new ModernBarStyle(new Profile { ModernBarScale = 100 });

            Assert.Equal(ModernBarStyle.ScaleBase, style.Scale, 3);
        }

        [Fact]
        public void FontPixels_IsEvenAndBounded()
        {
            for (int pct = ModernBarStyle.MinScalePercent; pct <= ModernBarStyle.MaxScalePercent; pct += 7)
            {
                var style = new ModernBarStyle(new Profile { ModernBarScale = pct });
                byte px = style.FontPixels(ModernBarStyle.NameFontSize);

                Assert.Equal(0, px % 2);
                Assert.InRange(px, 8, 72);
            }
        }

        [Fact]
        public void Rgb_UnpacksRrggbb()
        {
            var v = ModernBarStyle.Rgb(0xFF8000);

            Assert.Equal(1f, v.X);
            Assert.Equal(128 / 255f, v.Y, 3);
            Assert.Equal(0f, v.Z);
        }
    }
}
