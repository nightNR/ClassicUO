// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class AnchorStatusBarLockTests
    {
        // Expected passed as int (not GroupedDragAction) because an internal
        // enum parameter on a public [Theory] trips CS0051 even under
        // InternalsVisibleTo — same reason FillOrder is passed as int in
        // PluginStatusBarLayoutTests.
        [Theory]
        // not in an anchored group -> always fall through, Alt irrelevant
        [InlineData(false, false, false, (int)PluginStatusBars.GroupedDragAction.PassThrough)]
        [InlineData(false, true, false, (int)PluginStatusBars.GroupedDragAction.PassThrough)]
        // in an anchored group, Alt held, normal profile -> eject the bar
        [InlineData(true, true, false, (int)PluginStatusBars.GroupedDragAction.Eject)]
        // in an anchored group, no Alt -> locked, snap back
        [InlineData(true, false, false, (int)PluginStatusBars.GroupedDragAction.SnapBack)]
        // HoldAltToMoveGumps disables the eject modifier -> snap back even with Alt
        [InlineData(true, true, true, (int)PluginStatusBars.GroupedDragAction.SnapBack)]
        [InlineData(true, false, true, (int)PluginStatusBars.GroupedDragAction.SnapBack)]
        public void ResolveGroupedDrag_ClassifiesDrag(bool inAnchoredGroup, bool altHeld, bool holdAlt, int expected)
        {
            var action = PluginStatusBars.ResolveGroupedDrag(inAnchoredGroup, altHeld, holdAlt);
            Assert.Equal(expected, (int)action);
        }
    }
}
