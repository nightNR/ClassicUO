// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using System.Reflection;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class TargetManagerTryResolveObjectTests
    {
        [Fact]
        public void ReturnsFalse_ForNullObject()
        {
            var world = new World();
            var sut = new TargetManager(world);

            Assert.False(sut.TryResolveObject(null));
        }

        [Fact]
        public void ReturnsTrue_ForEntityObject()
        {
            var world = new World();
            var sut = new TargetManager(world);
            var item = new Item(world);

            Assert.True(sut.TryResolveObject(item));
        }

        [Fact(Skip =
            "Land dispatch reads land.TileData.IsWet, which resolves through the " +
            "process-wide Client.Game.UO.FileManager singleton. That singleton is " +
            "only ever populated by the real FNA game bootstrap (Client.Run), which " +
            "requires a live SDL/graphics context and cannot be constructed in this " +
            "unit-test host (GameController derives from FNA's Game and opens a real " +
            "window in its constructor). TargetManagerMatchesAcceptedTypesTests hits " +
            "the same gap at construction time only and works around it by " +
            "reflecting past Land.Create; here the same TileData access happens " +
            "inside TryResolveObject itself, at call time, so no test-side "+
            "construction trick avoids it. Covered instead by manual verification " +
            "and by the pre-existing inline logic this method extracts from " +
            "GameSceneInputHandler, which has shipped unchanged.")]
        public void ReturnsTrue_ForLandObject()
        {
            var world = new World();
            var sut = new TargetManager(world);
            var land = CreateTestLand(world);

            Assert.True(sut.TryResolveObject(land));
        }

        /// <summary>
        /// Helper to create a Land instance for testing.
        /// Uses reflection to bypass Land.Create's TileData dependency, which requires
        /// Client.Game.UO.FileManager initialization not available in unit tests
        /// (same workaround used by TargetManagerMatchesAcceptedTypesTests).
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

        [Fact]
        public void ReturnsTrue_ForStaticObject()
        {
            var world = new World();
            var sut = new TargetManager(world);
            var stat = new Static(world);

            Assert.True(sut.TryResolveObject(stat));
        }
    }
}
