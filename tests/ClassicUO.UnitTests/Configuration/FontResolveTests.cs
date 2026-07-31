// SPDX-License-Identifier: BSD-2-Clause
using System.IO;
using System.Text.Json;
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
        private static FontsLoader AttachFonts()
        {
            FontsLoader fonts = new UOFileManager(ClientVersion.CV_300, Path.GetTempPath()).Fonts;
            AtlasFontRegistry.Attach(fonts);
            AtlasFontRegistry.Reset();
            return fonts;
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
        public void ResolveFont_DefaultJournalControls_IsClassicFallback()
        {
            AttachFonts();
            var p = new Profile();
            // No LocationFonts entry => Classic => journal-controls' classic
            // (font 6, unicode false — the JournalGump filter-checkbox literal).
            (byte slot, bool uni) = p.ResolveFont(FontLocation.JournalControls);
            Assert.Equal((byte)6, slot);
            Assert.False(uni);
        }

        [Fact]
        public void ResolveFont_JournalControlsTtfChoice_ReturnsDynamicUnicodeSlot()
        {
            AttachFonts();
            var p = new Profile();
            p.LocationFonts[FontLocation.JournalControls] = new FontChoice { Face = "inter", Size = 18 };

            (byte slot, bool uni) = p.ResolveFont(FontLocation.JournalControls);
            Assert.True(slot >= AtlasFontRegistry.DynamicSlotFirst);
            Assert.True(uni);
        }

        [Fact]
        public void ResolveHue_NoOverride_ReturnsFallback()
        {
            var p = new Profile();
            // No entry / Hue==0 => the caller's own hue (zero change).
            Assert.Equal((ushort)0x0386, p.ResolveHue(FontLocation.GumpLabels, 0x0386));
            p.LocationFonts[FontLocation.GumpLabels] = new FontChoice { Face = "inter", Size = 18, Hue = 0 };
            Assert.Equal((ushort)0x0386, p.ResolveHue(FontLocation.GumpLabels, 0x0386));
        }

        [Fact]
        public void ResolveHue_Override_ReturnsChosenHue()
        {
            var p = new Profile();
            p.LocationFonts[FontLocation.GumpLabels] = new FontChoice { Face = "", Size = 0, Hue = 0x0044 };
            // A non-zero picked hue overrides the caller's fallback for both Classic and TTF.
            Assert.Equal((ushort)0x0044, p.ResolveHue(FontLocation.GumpLabels, 0x0386));
        }

        [Fact]
        public void ResolveFont_PerSiteFallback_ReturnsCallerClassicWhenNoChoice()
        {
            AttachFonts();
            var p = new Profile();
            // No choice for a broad location => returns the caller-supplied classic
            // fallback (font 3 here), NOT the LocationDef's nominal font — this is
            // what lets many differing call sites each stay a zero visual change.
            (byte slot, bool uni) = p.ResolveFont(FontLocation.StatusBars, 3, true);
            Assert.Equal((byte)3, slot);
            Assert.True(uni);
        }

        [Fact]
        public void ResolveFont_PerSiteFallback_TtfChoiceOverridesCallerClassic()
        {
            AttachFonts();
            var p = new Profile();
            p.LocationFonts[FontLocation.StatusBars] = new FontChoice { Face = "inter", Size = 18 };
            // A TTF choice overrides every site regardless of its own classic font.
            (byte slot, bool uni) = p.ResolveFont(FontLocation.StatusBars, 3, true);
            Assert.True(slot >= AtlasFontRegistry.DynamicSlotFirst);
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

        [Fact]
        public void Profile_LocationFonts_SurvivesSourceGenRoundTrip()
        {
            var p = new Profile();
            p.LocationFonts[FontLocation.Chat] = new FontChoice { Face = "inter", Size = 18 };

            // Exercise the source-gen JsonTypeInfo<Profile> directly (not a bare
            // JsonSerializer.Serialize(obj) call) so this fails if either
            // [JsonSerializable(typeof(FontChoice))] or
            // [JsonSerializable(typeof(Dictionary<string, FontChoice>))] is ever removed
            // from ProfileJsonContext.
            ProfileJsonContext ctx = ProfileJsonContext.DefaultToUse;
            string json = JsonSerializer.Serialize(p, ctx.Profile);
            Profile back = (Profile)JsonSerializer.Deserialize(json, ctx.Profile);

            Assert.True(back.LocationFonts.TryGetValue(FontLocation.Chat, out FontChoice c));
            Assert.Equal("inter", c.Face);
            Assert.Equal((byte)18, c.Size);
        }

        [Fact]
        public void TtfChoice_SurvivesResetCycle_LegacyBytePointsAtLiveSlot()
        {
            FontsLoader fonts = AttachFonts();
            var p = new Profile();
            p.LocationFonts[FontLocation.Chat] = new FontChoice { Face = "inter", Size = 18 };

            // First "session": resolve once so ChatFont gets persisted as a dynamic
            // slot, exactly as picking a TTF face in Options would do.
            p.ChatFont = p.ResolveFont(FontLocation.Chat).slot;
            Assert.True(fonts.IsAtlasFont(p.ChatFont));

            // Next boot / profile (re)load: ProfileManager.Load calls Reset() on every
            // load, which frees every dynamic slot. Without a re-sync, the persisted
            // legacy byte is now stale/dangling — this is the shipped bug, since
            // renderers (SystemChatControl, MessageManager, JournalManager, Tooltip,
            // ...) read Profile.ChatFont/TooltipFont directly and never call
            // ResolveFont themselves.
            AtlasFontRegistry.Reset();
            Assert.False(fonts.IsAtlasFont(p.ChatFont)); // stale byte, now dangling

            // The fix: ProfileManager.Load re-syncs the legacy byte from ResolveFont
            // right after Reset()+migration, which re-registers the slot via
            // TryResolve so the byte a renderer reads is live again.
            p.ChatFont = p.ResolveFont(FontLocation.Chat).slot;
            Assert.True(fonts.IsAtlasFont(p.ChatFont));
        }
    }
}
