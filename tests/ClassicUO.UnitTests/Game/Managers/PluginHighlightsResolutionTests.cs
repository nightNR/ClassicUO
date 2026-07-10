// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class PluginHighlightsResolutionTests
    {
        public PluginHighlightsResolutionTests()
        {
            PluginHighlightCharacters.Reset();
            PluginHighlightAreas.Reset();
        }

        [Fact]
        public void TryResolveMobileHue_FallsBackToArea_WhenNoCharacterHighlight()
        {
            // Add(world, id, durationMs, snap, anchorSerial, hue, rangeX, rangeY, objectTypes, x, y, now)
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0077, 5, 5, HighlightObjectTypes.Mobile, 100, 100, 0);

            bool found = PluginHighlights.TryResolveMobileHue(0x1234, statusOverrideActive: false, 100, 100, 0, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0077, hue);
        }

        [Fact]
        public void TryResolveMobileHue_NormalCharacterTier_BeatsArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0077, 5, 5, HighlightObjectTypes.Mobile, 100, 100, 0);
            PluginHighlightCharacters.Set(0x1234, 0x0022, priorityHighlight: false);

            bool found = PluginHighlights.TryResolveMobileHue(0x1234, statusOverrideActive: false, 100, 100, 0, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0022, hue);
        }

        [Fact]
        public void TryResolveMobileHue_StatusOverrideActive_SkipsNormalTierAndArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0077, 5, 5, HighlightObjectTypes.Mobile, 100, 100, 0);
            PluginHighlightCharacters.Set(0x1234, 0x0022, priorityHighlight: false);

            bool found = PluginHighlights.TryResolveMobileHue(0x1234, statusOverrideActive: true, 100, 100, 0, out _);

            Assert.False(found);
        }

        [Fact]
        public void TryResolveMobileHue_PriorityTier_BeatsStatusOverrideAndArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0077, 5, 5, HighlightObjectTypes.Mobile, 100, 100, 0);
            PluginHighlightCharacters.Set(0x1234, 0x0099, priorityHighlight: true);

            bool found = PluginHighlights.TryResolveMobileHue(0x1234, statusOverrideActive: true, 100, 100, 0, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0099, hue);
        }

        [Fact]
        public void TryResolveMobileHue_ReturnsFalse_WhenNothingMatches()
        {
            bool found = PluginHighlights.TryResolveMobileHue(0x1234, statusOverrideActive: false, 100, 100, 0, out ushort hue);

            Assert.False(found);
            Assert.Equal((ushort)0, hue);
        }
    }
}
