// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class CoverageBlendTests
    {
        // pData packs 0xAABBGGRR. The glyph color is baked into src and drawn
        // under premultiplied AlphaBlend, so compositing scales every channel
        // (RGB and alpha) by coverage — a straight src-over LERP toward dst.
        [Fact]
        public void BlendCoverage_ZeroCoverage_KeepsDst()
        {
            Assert.Equal(0xFF102030u, FontsLoader.BlendCoverage(0xFF102030u, 0xFFAABBCCu, 0));
        }

        [Fact]
        public void BlendCoverage_FullCoverage_TakesSrc()
        {
            Assert.Equal(0xFFAABBCCu, FontsLoader.BlendCoverage(0xFF102030u, 0xFFAABBCCu, 255));
        }

        [Fact]
        public void BlendCoverage_HalfCoverage_IsMidpoint()
        {
            // each channel ~ (dst+src)/2, within rounding — premultiplied AA.
            uint r = FontsLoader.BlendCoverage(0xFF000000u, 0xFF0000FFu, 128);
            Assert.InRange(r & 0xFF, 0x7Eu, 0x81u);
        }

        [Fact]
        public void BlendCoverage_PartialOverTransparent_PremultipliesColor()
        {
            // Baked color over an empty pixel: RGB and alpha both scale with
            // coverage (premultiplied), so a half-covered pixel is ~half the
            // src color and ~half alpha — the correct soft AA edge.
            uint r = FontsLoader.BlendCoverage(0u, 0xFF4080C0u, 128);

            Assert.InRange((r >> 24) & 0xFF, 0x7Eu, 0x81u);   // A ~ 0x80
            Assert.InRange((r >> 16) & 0xFF, 0x1Fu, 0x21u);   // B 0x40 -> ~0x20
            Assert.InRange((r >> 8) & 0xFF, 0x3Fu, 0x41u);    // G 0x80 -> ~0x40
            Assert.InRange(r & 0xFF, 0x5Fu, 0x61u);           // R 0xC0 -> ~0x60
        }

        [Fact]
        public void StrengthenCoverage_PreservesEndpoints()
        {
            Assert.Equal(0, FontsLoader.StrengthenCoverage(0));
            Assert.Equal(255, FontsLoader.StrengthenCoverage(255));
        }

        [Fact]
        public void StrengthenCoverage_LiftsMidtones()
        {
            // sub-1 gamma boosts partial coverage (never below the input).
            Assert.True(FontsLoader.StrengthenCoverage(128) >= 128);
            Assert.True(FontsLoader.StrengthenCoverage(64) >= 64);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(127, false)]
        [InlineData(128, true)]
        [InlineData(255, true)]
        public void CoverageIsSet_Thresholds(byte c, bool expected)
        {
            Assert.Equal(expected, FontsLoader.CoverageIsSet(c));
        }

        [Fact]
        public void HasPixels_TrueForCoverageOnlyGlyph()
        {
            var g = new ClassicUO.Assets.FontCharacterDataUnicode
            {
                Width = 3, Height = 4, Coverage = new byte[12]
            };
            Assert.True(g.HasPixels);
            Assert.Null(g.Data);
        }
    }
}
