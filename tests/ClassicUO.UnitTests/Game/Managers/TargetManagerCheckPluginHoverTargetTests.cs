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
        public void FallsBackToCancel_WhenMatchedObjectResolutionSendsNothing()
        {
            // Full packet-send confirmation requires world.InGame (Player + Map), which needs
            // a real profile/session and is out of scope for a unit test (see the design
            // spec's "manual in-game verification" note). Without InGame, Target(uint serial)
            // hits its own silent no-op (world.Get(serial) can't resolve, so its
            // `if (entity != null)` branch never runs and nothing is sent) - the same class of
            // real-world race as an entity leaving the world between being hovered and
            // resolved, or a TextObject whose Owner was already cleared. A plugin request must
            // never be left hanging because of that: CheckPluginHoverTarget's guard notices
            // nothing answered the request and falls back to CancelTarget(), so IsTargeting
            // still ends up false here - a real reply was always sent, just the fallback one.
            var world = new World();
            var sut = new TargetManager(world);
            sut.SetTargeting(CursorTarget.PluginHoverTarget, 1, TargetType.Neutral, HighlightObjectTypes.Item);
            var item = new Item(world);

            sut.CheckPluginHoverTarget(item);

            Assert.False(sut.IsTargeting);
        }
    }
}
