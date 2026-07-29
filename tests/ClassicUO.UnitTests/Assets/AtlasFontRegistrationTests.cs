// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class AtlasFontRegistrationTests
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
                RepoRoot(), "Assets", "fonts", "Inter", "static", "Inter_18pt-Regular.ttf"));

        private static FontsLoader NewFonts()
        {
            // The atlas/TTF path never reads UO .mul files, and _unicodeFontAddress
            // is initialized at field declaration (array of nulls), so an unloaded
            // FontsLoader is fine here — no FileManager.Load() / UO data needed.
            var mgr = new UOFileManager(ClientVersion.CV_300, Path.GetTempPath());
            return mgr.Fonts;
        }

        [Fact]
        public void RegisterAtlasFont_Ttf_MarksSlotUsable()
        {
            FontsLoader fonts = NewFonts();
            fonts.RegisterAtlasFont(20, "Inter 22", InterTtf(), 22);

            Assert.True(fonts.IsAtlasFont(20));
            Assert.Equal("Inter 22", fonts.GetFontDisplayName(20));
        }

        [Fact]
        public void RegisterAtlasFont_Ttf_MeasuresAndRastersLazily()
        {
            FontsLoader fonts = NewFonts();
            fonts.RegisterAtlasFont(20, "Inter 22", InterTtf(), 22);

            // Measure triggers GetCharUni -> LoadChar -> lazy fill.
            int width = fonts.GetWidthUnicode(20, "M");
            Assert.True(width > 0);

            // Render produces a glyph with an opaque interior pixel.
            FontsLoader.SingleGlyphInfo g =
                fonts.RenderSingleGlyphUnicode(20, 'M', hasBorder: false, isSolid: false);
            Assert.NotNull(g.Data);
            Assert.True(g.Width > 0 && g.Height > 0);
            // Default charcolor (0) renders as the "neutral white" sentinel
            // 0xFEFFFFFF (see FontsLoader.RenderSingleGlyphUnicode), so a fully
            // covered pixel tops out at alpha 0xFE, not 0xFF — check against that
            // sentinel rather than a hardcoded 0xFF.
            bool anyOpaque = false;
            foreach (uint px in g.Data) if ((px >> 24) >= 0xFE) { anyOpaque = true; break; }
            Assert.True(anyOpaque);
        }

        [Fact]
        public void RegisterAtlasFont_Ttf_MissingCodepoint_NoPixelsNoThrow()
        {
            FontsLoader fonts = NewFonts();
            fonts.RegisterAtlasFont(20, "Inter 22", InterTtf(), 22);

            // A codepoint Inter does not contain (CJK) must measure to no width
            // and must not throw on repeated access (known-missing guard).
            int w1 = fonts.GetWidthUnicode(20, "中");
            int w2 = fonts.GetWidthUnicode(20, "中");
            Assert.Equal(0, w1);
            Assert.Equal(0, w2);
        }

        [Fact]
        public void RegisterAtlasFont_CorruptTtf_SlotNotUsable()
        {
            FontsLoader fonts = NewFonts();
            fonts.RegisterAtlasFont(21, "bad", new byte[] { 1, 2, 3, 4 }, 22);
            Assert.False(fonts.IsAtlasFont(21));
        }
    }
}
