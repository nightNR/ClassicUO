// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class PluginHighlightCharactersTests
    {
        public PluginHighlightCharactersTests() => PluginHighlightCharacters.Reset();

        [Fact]
        public void TryResolve_ReturnsFalse_WhenNothingSet()
        {
            bool found = PluginHighlightCharacters.TryResolve(0x1234, statusOverrideActive: false, out ushort hue);
            Assert.False(found);
            Assert.Equal((ushort)0, hue);
        }

        [Fact]
        public void TryResolve_NormalTier_WinsWhenNoStatusOverride()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0021, priorityHighlight: false);

            bool found = PluginHighlightCharacters.TryResolve(0x1234, statusOverrideActive: false, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0021, hue);
        }

        [Fact]
        public void TryResolve_NormalTier_LosesToActiveStatusOverride()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0021, priorityHighlight: false);

            bool found = PluginHighlightCharacters.TryResolve(0x1234, statusOverrideActive: true, out ushort hue);

            Assert.False(found);
        }

        [Fact]
        public void TryResolve_PriorityTier_WinsEvenWithActiveStatusOverride()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0055, priorityHighlight: true);

            bool found = PluginHighlightCharacters.TryResolve(0x1234, statusOverrideActive: true, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0055, hue);
        }

        [Fact]
        public void TryResolve_PriorityTier_TakesPrecedenceOverNormalTier()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0021, priorityHighlight: false);
            PluginHighlightCharacters.Set(0x1234, 0x0055, priorityHighlight: true);

            bool found = PluginHighlightCharacters.TryResolve(0x1234, statusOverrideActive: false, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0055, hue);
        }

        [Fact]
        public void Remove_RemovesOnlyMatchingTier()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0021, priorityHighlight: false);
            PluginHighlightCharacters.Set(0x1234, 0x0055, priorityHighlight: true);

            PluginHighlightCharacters.Remove(0x1234, priorityHighlight: false);

            Assert.True(PluginHighlightCharacters.TryResolve(0x1234, false, out ushort hue));
            Assert.Equal((ushort)0x0055, hue); // priority tier survives
        }

        [Fact]
        public void ClearAll_ClearsOnlyMatchingTier()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0021, priorityHighlight: false);
            PluginHighlightCharacters.Set(0x5678, 0x0055, priorityHighlight: true);

            PluginHighlightCharacters.ClearAll(priorityHighlight: false);

            Assert.False(PluginHighlightCharacters.TryResolve(0x1234, false, out _));
            Assert.True(PluginHighlightCharacters.TryResolve(0x5678, false, out ushort hue));
            Assert.Equal((ushort)0x0055, hue);
        }
    }
}
