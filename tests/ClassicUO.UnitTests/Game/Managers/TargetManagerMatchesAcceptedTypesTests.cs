// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.PluginApi;
using System;
using System.Reflection;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class TargetManagerMatchesAcceptedTypesTests
    {
        [Fact]
        public void Mobile_MatchesMobileFlag_NotItemFlag()
        {
            var world = new World();
            var mobile = new Mobile(world);

            Assert.True(TargetManager.MatchesAcceptedTypes(mobile, HighlightObjectTypes.Mobile));
            Assert.False(TargetManager.MatchesAcceptedTypes(mobile, HighlightObjectTypes.Item));
        }

        [Fact]
        public void CorpseItem_MatchesCorpseFlag_NotItemFlag()
        {
            var world = new World();
            var corpse = new Item(world) { Graphic = 0x2006 };

            Assert.True(TargetManager.MatchesAcceptedTypes(corpse, HighlightObjectTypes.Corpse));
            Assert.False(TargetManager.MatchesAcceptedTypes(corpse, HighlightObjectTypes.Item));
        }

        [Fact]
        public void PlainItem_MatchesItemFlag_NotCorpseFlag()
        {
            var world = new World();
            var item = new Item(world) { Graphic = 0x0EED };

            Assert.True(TargetManager.MatchesAcceptedTypes(item, HighlightObjectTypes.Item));
            Assert.False(TargetManager.MatchesAcceptedTypes(item, HighlightObjectTypes.Corpse));
        }

        [Fact]
        public void Land_MatchesLandFlag_NotStaticFlag()
        {
            var world = new World();
            var land = CreateTestLand(world);

            Assert.True(TargetManager.MatchesAcceptedTypes(land, HighlightObjectTypes.Land));
            Assert.False(TargetManager.MatchesAcceptedTypes(land, HighlightObjectTypes.Static));
        }

        [Fact]
        public void Static_MatchesStaticFlag()
        {
            var world = new World();
            var stat = new Static(world);

            Assert.True(TargetManager.MatchesAcceptedTypes(stat, HighlightObjectTypes.Static));
        }

        [Fact]
        public void Multi_MatchesMultiFlag()
        {
            var world = new World();
            var multi = new Multi(world);

            Assert.True(TargetManager.MatchesAcceptedTypes(multi, HighlightObjectTypes.Multi));
        }

        [Fact]
        public void ReturnsFalse_WhenMaskExcludesCategory()
        {
            var world = new World();
            var mobile = new Mobile(world);

            Assert.False(TargetManager.MatchesAcceptedTypes(mobile, HighlightObjectTypes.Land | HighlightObjectTypes.Static));
        }

        /// <summary>
        /// Helper to create a Land instance for testing.
        /// Uses reflection to bypass Land.Create's TileData dependency which requires
        /// Client.Game.UO.FileManager initialization not available in unit tests.
        /// </summary>
        private static Land CreateTestLand(World world)
        {
            var ctor = typeof(Land).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(World) },
                null);

            return (Land)ctor!.Invoke(new object[] { world });
        }
    }
}
