// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Assets;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class TtfGlyphSourceTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ClassicUO.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("ClassicUO.sln not found");
        }

        private static byte[] InterTtf() =>
            File.ReadAllBytes(Path.Combine(
                RepoRoot(),
                "Assets", "fonts", "Inter", "static", "Inter_18pt-Regular.ttf"));

        [Fact]
        public void Create_ValidTtf_Succeeds()
        {
            TtfGlyphSource src = TtfGlyphSource.Create(InterTtf(), 22);
            Assert.NotNull(src);
            Assert.Equal(22, src.PixelSize);
        }

        [Fact]
        public void Create_GarbageBytes_ReturnsNull()
        {
            Assert.Null(TtfGlyphSource.Create(new byte[] { 1, 2, 3, 4 }, 22));
        }

        [Fact]
        public void TryRasterize_SolidGlyph_HasOpaqueInteriorAndPositiveMetrics()
        {
            TtfGlyphSource src = TtfGlyphSource.Create(InterTtf(), 22);

            bool ok = src.TryRasterize('M', out sbyte ox, out sbyte oy,
                out sbyte w, out sbyte h, out byte[] coverage);

            Assert.True(ok);
            Assert.True(w > 0);
            Assert.True(h > 0);
            Assert.Equal(w * h, coverage.Length);
            // A solid stroke glyph has at least one fully-covered interior pixel.
            byte max = 0;
            foreach (byte b in coverage) if (b > max) max = b;
            Assert.Equal(255, max);
        }

        [Fact]
        public void TryRasterize_Space_ReturnsFalse()
        {
            TtfGlyphSource src = TtfGlyphSource.Create(InterTtf(), 22);
            Assert.False(src.TryRasterize(' ', out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryRasterize_SlovakLetters_Present()
        {
            TtfGlyphSource src = TtfGlyphSource.Create(InterTtf(), 22);
            foreach (char c in new[] { 'č', 'š', 'ž', 'Č', 'Š', 'Ž' })
            {
                Assert.True(src.TryRasterize(c, out _, out _, out sbyte w, out sbyte h, out _),
                    $"missing {c}");
                Assert.True(w > 0 && h > 0);
            }
        }

        [Fact]
        public void Downsample_FullBlocks_AverageTo255AndDivideDims()
        {
            // 6x3 source, factor 3 -> 2x1 output. Left 3x3 block all 255 -> 255.
            // Right 3x3 block all 0 -> 0.
            byte[] src = new byte[6 * 3];
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    src[y * 6 + x] = 255;

            byte[] dst = TtfGlyphSource.Downsample(src, 6, 3, 3, out int dw, out int dh);

            Assert.Equal(2, dw);
            Assert.Equal(1, dh);
            Assert.Equal(255, dst[0]);
            Assert.Equal(0, dst[1]);
        }

        [Fact]
        public void Downsample_HalfCoveredBlock_AveragesToMid()
        {
            // 3x3 all-255 except: make ~half the block 0 -> average ~113-142.
            byte[] src = new byte[3 * 3];
            for (int i = 0; i < src.Length; i++) src[i] = (byte)(i < 4 ? 255 : 0); // 4 of 9 set
            byte[] dst = TtfGlyphSource.Downsample(src, 3, 3, 3, out int dw, out int dh);
            Assert.Equal(1, dw);
            Assert.Equal(1, dh);
            Assert.Equal((byte)((255 * 4 + 4) / 9), dst[0]); // rounded average = 113
        }

        [Fact]
        public void Downsample_EdgeBlockSmallerThanFactor_AveragesOverActualPixels()
        {
            // 4x1 source, factor 3 -> 2x1: block0 = px0..2, block1 = px3 only.
            byte[] src = { 255, 255, 255, 100 };
            byte[] dst = TtfGlyphSource.Downsample(src, 4, 1, 3, out int dw, out int dh);
            Assert.Equal(2, dw);
            Assert.Equal(1, dh);
            Assert.Equal(255, dst[0]);
            Assert.Equal(100, dst[1]); // partial edge block = single pixel
        }

        [Fact]
        public void TryRasterize_Oversampled_HasOpaqueInteriorAndSoftEdge()
        {
            TtfGlyphSource src = TtfGlyphSource.Create(InterTtf(), 22);
            Assert.True(src.TryRasterize('e', out _, out _, out sbyte w, out sbyte h, out byte[] cov));
            Assert.True(w > 0 && h > 0);

            bool hasOpaque = false, hasPartial = false;
            foreach (byte b in cov)
            {
                if (b == 255) hasOpaque = true;
                else if (b > 0) hasPartial = true;
            }
            Assert.True(hasOpaque, "expected fully-opaque interior pixels");
            Assert.True(hasPartial, "expected anti-aliased edge pixels");
        }
    }
}
