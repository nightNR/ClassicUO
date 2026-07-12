// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.Managers;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class TargetManagerPluginHoverStateTests
    {
        [Fact]
        public void PluginHoverAcceptedTypes_DefaultsToAll()
        {
            var world = new World();
            var sut = new TargetManager(world);

            Assert.Equal(HighlightObjectTypes.All, sut.PluginHoverAcceptedTypes);
        }

        [Fact]
        public void SetTargeting_StoresAcceptedTypes_ForPluginHoverTarget()
        {
            var world = new World();
            var sut = new TargetManager(world);

            sut.SetTargeting(
                CursorTarget.PluginHoverTarget,
                cursorID: 1,
                TargetType.Neutral,
                HighlightObjectTypes.Mobile | HighlightObjectTypes.Item
            );

            Assert.Equal(CursorTarget.PluginHoverTarget, sut.TargetingState);
            Assert.Equal(HighlightObjectTypes.Mobile | HighlightObjectTypes.Item, sut.PluginHoverAcceptedTypes);
        }

        [Fact]
        public void Reset_RestoresAcceptedTypes_ToAll()
        {
            var world = new World();
            var sut = new TargetManager(world);
            sut.SetTargeting(CursorTarget.PluginHoverTarget, 1, TargetType.Neutral, HighlightObjectTypes.Land);

            sut.Reset();

            Assert.Equal(HighlightObjectTypes.All, sut.PluginHoverAcceptedTypes);
        }
    }
}
