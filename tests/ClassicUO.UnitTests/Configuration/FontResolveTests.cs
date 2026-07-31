// SPDX-License-Identifier: BSD-2-Clause
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Resources;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests.Configuration
{
    [Collection("AtlasRegistrySerial")]
    public class FontResolveTests
    {
        private static void AttachFonts()
        {
            FontsLoader fonts = new UOFileManager(ClientVersion.CV_300, Path.GetTempPath()).Fonts;
            AtlasFontRegistry.Attach(fonts);
            AtlasFontRegistry.Reset();
        }

        [Fact]
        public void ResolveFont_DefaultChat_IsClassicFallback()
        {
            AttachFonts();
            var p = new Profile();
            // No LocationFonts entry => Classic => chat's classic (font 1, unicode true).
            (byte slot, bool uni) = p.ResolveFont(FontLocation.Chat);
            Assert.Equal((byte)1, slot);
            Assert.True(uni);
        }

        [Fact]
        public void ResolveFont_TtfChoice_ReturnsDynamicUnicodeSlot()
        {
            AttachFonts();
            var p = new Profile();
            p.LocationFonts[FontLocation.Chat] = new FontChoice { Face = "inter", Size = 18 };

            (byte slot, bool uni) = p.ResolveFont(FontLocation.Chat);
            Assert.True(slot >= AtlasFontRegistry.DynamicSlotFirst);
            Assert.True(uni);
        }

        [Fact]
        public void ResolveFont_UnknownFace_FallsBackToClassic()
        {
            AttachFonts();
            var p = new Profile();
            p.LocationFonts[FontLocation.Tooltip] = new FontChoice { Face = "ghost", Size = 18 };

            (byte slot, bool uni) = p.ResolveFont(FontLocation.Tooltip);
            Assert.Equal((byte)1, slot);   // tooltip classic font
            Assert.True(uni);
        }
    }
}
