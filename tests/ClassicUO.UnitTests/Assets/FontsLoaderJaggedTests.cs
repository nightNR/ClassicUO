// SPDX-License-Identifier: BSD-2-Clause
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests.Assets
{
    public class FontsLoaderJaggedTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ClassicUO.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new System.InvalidOperationException("ClassicUO.sln not found");
        }

        private static byte[] InterTtf() =>
            File.ReadAllBytes(Path.Combine(RepoRoot(), "Assets", "fonts", "Inter", "static", "Inter_18pt-Regular.ttf"));

        private static FontsLoader NewFonts() =>
            new UOFileManager(ClientVersion.CV_300, Path.GetTempPath()).Fonts;

        [Fact]
        public void Cap_IsNinetySix()
        {
            Assert.Equal(96, FontsLoader.MAX_UNICODE_FONTS);
        }

        [Fact]
        public void HighDynamicSlot_RastersAndReset_FreesSlot()
        {
            FontsLoader fonts = NewFonts();
            fonts.RegisterAtlasFont(95, "Inter 30", InterTtf(), 30);
            Assert.True(fonts.IsAtlasFont(95));
            Assert.True(fonts.GetWidthUnicode(95, "M") > 0);   // forces lazy row alloc on slot 95

            fonts.ResetAtlasFont(95);
            Assert.False(fonts.IsAtlasFont(95));
        }
    }
}
