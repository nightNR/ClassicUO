// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    /// <summary>
    /// Unit-level guards on the static passability predicate. These need no UO
    /// data: they construct <see cref="StaticTiles"/> with explicit flags. The
    /// predicate must match what the live walker (Pathfinder.CreateItemList)
    /// treats as a walkable surface — crucially, BRIDGE tiles (stairs, plank
    /// bridges over water) are walkable even when not flagged SURFACE.
    /// </summary>
    public class StaticMapPassabilityTests
    {
        private static StaticTiles Static(TileFlag flags) =>
            new StaticTiles((ulong)flags, 0, 0, 0, 0, 0, 0, 0, null);

        [Fact]
        public void Surface_static_is_passable()
        {
            Assert.True(StaticMapPassability.IsPassableStatic(Static(TileFlag.Surface)));
        }

        [Fact]
        public void Bridge_static_is_passable_even_without_surface_flag()
        {
            // Regression: a plank bridge / stairs flagged BRIDGE but not SURFACE
            // used to be treated as impassable, walling the two banks into
            // separate reachability regions and making WalkTo reject any route
            // crossing the bridge.
            Assert.True(StaticMapPassability.IsPassableStatic(Static(TileFlag.Bridge)));
        }

        [Fact]
        public void Impassable_bridge_is_not_passable()
        {
            Assert.False(StaticMapPassability.IsPassableStatic(Static(TileFlag.Bridge | TileFlag.Impassable)));
        }

        [Fact]
        public void Impassable_surface_is_not_passable()
        {
            Assert.False(StaticMapPassability.IsPassableStatic(Static(TileFlag.Surface | TileFlag.Impassable)));
        }

        [Fact]
        public void Plain_static_that_is_neither_surface_nor_bridge_is_not_passable()
        {
            Assert.False(StaticMapPassability.IsPassableStatic(Static(TileFlag.Wall)));
        }
    }
}
