// SPDX-License-Identifier: BSD-2-Clause
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Resources;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests.Resources
{
    [Collection("AtlasRegistrySerial")]  // static state: do not parallelize
    public class AtlasFontRegistryTests
    {
        private static FontsLoader NewFonts() =>
            new UOFileManager(ClientVersion.CV_300, Path.GetTempPath()).Fonts;

        [Fact]
        public void Resolve_SameFaceSize_DedupsToSameSlot()
        {
            FontsLoader fonts = NewFonts();
            AtlasFontRegistry.Attach(fonts);
            AtlasFontRegistry.Reset();

            Assert.True(AtlasFontRegistry.TryResolve("inter", 16, out byte a));
            Assert.True(AtlasFontRegistry.TryResolve("inter", 16, out byte b));
            Assert.Equal(a, b);
            Assert.True(fonts.IsAtlasFont(a));
        }

        [Fact]
        public void Resolve_DifferentCombos_DistinctSlots()
        {
            FontsLoader fonts = NewFonts();
            AtlasFontRegistry.Attach(fonts);
            AtlasFontRegistry.Reset();

            Assert.True(AtlasFontRegistry.TryResolve("inter", 16, out byte a));
            Assert.True(AtlasFontRegistry.TryResolve("inter", 20, out byte b));
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Resolve_UnknownFace_False()
        {
            FontsLoader fonts = NewFonts();
            AtlasFontRegistry.Attach(fonts);
            AtlasFontRegistry.Reset();

            Assert.False(AtlasFontRegistry.TryResolve("nope", 16, out _));
        }

        [Fact]
        public void Reset_FreesSlotsForReuse()
        {
            FontsLoader fonts = NewFonts();
            AtlasFontRegistry.Attach(fonts);
            AtlasFontRegistry.Reset();

            Assert.True(AtlasFontRegistry.TryResolve("inter", 16, out byte first));
            AtlasFontRegistry.Reset();
            Assert.False(fonts.IsAtlasFont(first));

            // Next allocation reuses the first dynamic slot.
            Assert.True(AtlasFontRegistry.TryResolve("cinzel", 22, out byte again));
            Assert.Equal(AtlasFontRegistry.DynamicSlotFirst, again);
        }

        [Fact]
        public void LegacySlotToFaceSize_MapsOldFixedSlots()
        {
            Assert.Equal(("cinzel", (byte)14), AtlasFontRegistry.LegacySlotToFaceSize(20));
            Assert.Null(AtlasFontRegistry.LegacySlotToFaceSize(5));
        }
    }
}
