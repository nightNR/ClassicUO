// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class PluginStatusBarGroupsTests
    {
        public PluginStatusBarGroupsTests() => PluginStatusBarGroups.Reset();

        [Fact]
        public void NewAnchorGroup_IsEmpty()
        {
            var group = new AnchorManager.AnchorGroup();
            Assert.True(group.IsEmpty);
        }

        [Fact]
        public void GetGroup_ReturnsNull_WhenUntracked()
        {
            Assert.Null(PluginStatusBarGroups.GetGroup(7));
        }

        [Fact]
        public void Track_ThenGetGroup_ReturnsSameInstance()
        {
            var group = new AnchorManager.AnchorGroup();
            PluginStatusBarGroups.Track(7, group);
            Assert.Same(group, PluginStatusBarGroups.GetGroup(7));
        }

        [Fact]
        public void PruneEmpty_RemovesEmptyGroups()
        {
            PluginStatusBarGroups.Track(7, new AnchorManager.AnchorGroup());
            PluginStatusBarGroups.PruneEmpty();
            Assert.Null(PluginStatusBarGroups.GetGroup(7));
        }
    }
}
