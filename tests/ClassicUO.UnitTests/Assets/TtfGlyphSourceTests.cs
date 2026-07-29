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
    }
}
