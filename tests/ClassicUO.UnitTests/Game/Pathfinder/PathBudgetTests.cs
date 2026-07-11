// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class PathBudgetTests
    {
        [Fact]
        public void First_call_budget_is_larger_than_per_tick_budget()
        {
            Assert.True(ClassicUO.Game.Pathfinder.FrameBudget(true) > ClassicUO.Game.Pathfinder.FrameBudget(false));
        }

        [Fact]
        public void First_call_budget_covers_a_typical_in_view_walk()
        {
            // A ~30-tile in-view walk expands well under the first-call budget,
            // so WalkTo still completes synchronously (no behaviour change).
            Assert.True(ClassicUO.Game.Pathfinder.FrameBudget(true) >= 8000);
        }
    }
}
