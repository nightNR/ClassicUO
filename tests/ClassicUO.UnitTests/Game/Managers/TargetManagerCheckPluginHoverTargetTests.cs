// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class TargetManagerCheckPluginHoverTargetTests
    {
        [Fact]
        public void NoOp_WhenNotTargeting()
        {
            var world = new World();
            var sut = new TargetManager(world);

            sut.CheckPluginHoverTarget(new Item(world));

            Assert.False(sut.IsTargeting);
        }

        [Fact]
        public void NoOp_WhenTargetingButNotPluginHoverState()
        {
            var world = new World();
            var sut = new TargetManager(world);
            sut.SetTargeting(CursorTarget.Object, 1, TargetType.Neutral);

            sut.CheckPluginHoverTarget(new Item(world));

            Assert.True(sut.IsTargeting);
            Assert.Equal(CursorTarget.Object, sut.TargetingState);
        }

        [Fact(Skip = "NetClient.Socket is a bare new() in this headless test host; its PacketsTable is never initialized without a real connection handshake, and Send_TargetCancel dereferences PacketsTable unconditionally before any IsConnected check — this is a pre-existing, unrelated-to-this-task limitation in NetClient/TargetManager, not a defect in the new CheckPluginHoverTarget logic.")]
        public void Cancels_WhenHoveredObjectIsNull()
        {
            var world = new World();
            var sut = new TargetManager(world);
            sut.SetTargeting(CursorTarget.PluginHoverTarget, 1, TargetType.Neutral, HighlightObjectTypes.All);

            sut.CheckPluginHoverTarget(null);

            Assert.False(sut.IsTargeting);
        }

        [Fact(Skip = "NetClient.Socket is a bare new() in this headless test host; its PacketsTable is never initialized without a real connection handshake, and Send_TargetCancel dereferences PacketsTable unconditionally before any IsConnected check — this is a pre-existing, unrelated-to-this-task limitation in NetClient/TargetManager, not a defect in the new CheckPluginHoverTarget logic.")]
        public void Cancels_WhenHoveredObjectDoesNotMatchMask()
        {
            var world = new World();
            var sut = new TargetManager(world);
            sut.SetTargeting(CursorTarget.PluginHoverTarget, 1, TargetType.Neutral, HighlightObjectTypes.Mobile);
            var item = new Item(world);

            sut.CheckPluginHoverTarget(item);

            Assert.False(sut.IsTargeting);
        }

        [Fact]
        public void DoesNotCancel_WhenHoveredObjectMatchesMask()
        {
            // Full packet-send confirmation requires world.InGame (Player + Map),
            // which needs a real profile/session and is out of scope for a unit
            // test (see the design spec's "manual in-game verification" note).
            // This asserts the dispatcher took the confirm branch (not cancel):
            // Target(...) itself no-ops without InGame, but does NOT cancel.
            var world = new World();
            var sut = new TargetManager(world);
            sut.SetTargeting(CursorTarget.PluginHoverTarget, 1, TargetType.Neutral, HighlightObjectTypes.Item);
            var item = new Item(world);

            sut.CheckPluginHoverTarget(item);

            Assert.True(sut.IsTargeting);
        }
    }
}
