// tests/ClassicUO.UnitTests/DragAnchorRoutingTests.cs
// SPDX-License-Identifier: BSD-2-Clause
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class DragAnchorRoutingTests
    {
        private static PluginAnchorGroupDef Def(int id, bool c, bool s, bool a, bool allied, bool hostile, bool neutral)
            => new PluginAnchorGroupDef { Id = id, DragCtrl = c, DragShift = s, DragAlt = a, DragAllied = allied, DragHostile = hostile, DragNeutral = neutral };

        [Fact]
        public void NoBinding_IsIgnored()
        {
            // modifiers set but no category -> not a binding
            var defs = new List<PluginAnchorGroupDef> { Def(1, true, false, false, false, false, false) };
            Assert.Equal(0, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Hostile, defs));
        }

        [Fact]
        public void ExactModifierMatch_Required()
        {
            var defs = new List<PluginAnchorGroupDef> { Def(1, true, false, false, false, true, false) }; // Ctrl+Hostile
            Assert.Equal(1, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Hostile, defs));
            // Ctrl+Shift held != Ctrl binding
            Assert.Equal(0, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl | DragModifier.Shift, Allegiance.Hostile, defs));
        }

        [Fact]
        public void OneModifier_SplitsByCategory_ToDifferentAnchors()
        {
            var defs = new List<PluginAnchorGroupDef>
            {
                Def(1, true, false, false, false, true, false),  // Ctrl -> Hostile -> group 1
                Def(2, true, false, false, true, false, true),   // Ctrl -> Allied|Neutral -> group 2
            };
            Assert.Equal(1, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Hostile, defs));
            Assert.Equal(2, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Allied, defs));
            Assert.Equal(2, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Neutral, defs));
        }

        [Fact]
        public void NoMatch_ReturnsZero()
        {
            var defs = new List<PluginAnchorGroupDef> { Def(1, true, false, false, false, true, false) };
            Assert.Equal(0, DragAnchorRouting.ResolveDragAnchor(DragModifier.Shift, Allegiance.Hostile, defs)); // wrong modifier
            Assert.Equal(0, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Allied, defs));   // wrong category
        }

        [Fact]
        public void Conflict_SameModifiers_OverlappingCategory()
        {
            var defs = new List<PluginAnchorGroupDef>
            {
                Def(1, true, false, false, true, true, false),   // Ctrl -> Allied|Hostile
                Def(2, true, false, false, false, true, false),  // Ctrl -> Hostile  (overlaps on Hostile)
                Def(3, true, false, false, false, false, true),  // Ctrl -> Neutral  (disjoint, ok)
            };
            var conflicts = DragAnchorRouting.ConflictingGroupIds(defs);
            Assert.Contains(2, conflicts);     // later of the overlapping pair
            Assert.DoesNotContain(1, conflicts);
            Assert.DoesNotContain(3, conflicts);
        }
    }
}
