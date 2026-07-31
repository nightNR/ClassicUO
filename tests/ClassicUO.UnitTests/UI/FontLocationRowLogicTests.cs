// SPDX-License-Identifier: BSD-2-Clause
using ClassicUO.Configuration;
using ClassicUO.Game.UI.Controls;
using Xunit;

namespace ClassicUO.UnitTests.UI
{
    // Exercises the pure face/size mapping FontLocationRow.GetChoice relies on,
    // without constructing any Control (see FontLocationRowTests for why the
    // Control itself can't be built in this unit-test host).
    public class FontLocationRowLogicTests
    {
        [Fact]
        public void GetChoice_DefaultClassic_EmptyFace()
        {
            string[] faceIds = FontLocationRowLogic.BuildFaceIds();
            int faceIndex = FontLocationRowLogic.ResolveFaceIndex(faceIds, "");
            int sizeIndex = FontLocationRowLogic.ResolveSizeIndex(0);

            FontChoice c = FontLocationRowLogic.BuildChoice(faceIndex, sizeIndex, faceIds);

            Assert.True(string.IsNullOrEmpty(c.Face));
        }

        [Fact]
        public void GetChoice_RoundTripsTtfChoice()
        {
            string[] faceIds = FontLocationRowLogic.BuildFaceIds();
            int faceIndex = FontLocationRowLogic.ResolveFaceIndex(faceIds, "inter");
            int sizeIndex = FontLocationRowLogic.ResolveSizeIndex(18);

            FontChoice c = FontLocationRowLogic.BuildChoice(faceIndex, sizeIndex, faceIds);

            Assert.Equal("inter", c.Face);
            Assert.Equal((byte)18, c.Size);
        }

        [Fact]
        public void ResolveFaceIndex_UnknownFace_FallsBackToClassic()
        {
            string[] faceIds = FontLocationRowLogic.BuildFaceIds();
            int faceIndex = FontLocationRowLogic.ResolveFaceIndex(faceIds, "does-not-exist");

            Assert.Equal(0, faceIndex);
        }

        [Fact]
        public void ResolveSizeIndex_ClampsToRange()
        {
            Assert.Equal(0, FontLocationRowLogic.ResolveSizeIndex(0));   // below min -> clamp to SIZE_MIN
            Assert.Equal(0, FontLocationRowLogic.ResolveSizeIndex(8));   // exactly SIZE_MIN
            Assert.Equal(24, FontLocationRowLogic.ResolveSizeIndex(32)); // exactly SIZE_MAX
            Assert.Equal(24, FontLocationRowLogic.ResolveSizeIndex(255)); // above max -> clamp to SIZE_MAX
        }
    }
}
