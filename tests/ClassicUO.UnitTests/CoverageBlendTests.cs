// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class CoverageBlendTests
    {
        // pData packs 0xAABBGGRR (little-endian RGBA); blend is per-channel so
        // the exact channel order does not matter to these endpoint assertions.
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
            // each channel ~ (dst+src)/2, within rounding
            uint r = FontsLoader.BlendCoverage(0xFF000000u, 0xFF0000FFu, 128);
            Assert.InRange(r & 0xFF, 0x7Eu, 0x81u);
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
