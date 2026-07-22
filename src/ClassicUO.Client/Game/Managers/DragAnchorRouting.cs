// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;

namespace ClassicUO.Game.Managers
{
    internal enum Allegiance
    {
        Neutral = 0,
        Allied = 1,
        Hostile = 2
    }

    [Flags]
    internal enum DragModifier
    {
        None = 0,
        Ctrl = 1,
        Shift = 2,
        Alt = 4
    }

    /// <summary>
    /// Pure routing of a drag-selected mobile to an anchor group id, keyed by the
    /// held modifier set and the mobile's allegiance category. No UI/game state.
    /// </summary>
    internal static class DragAnchorRouting
    {
        public static DragModifier ModifiersOf(PluginAnchorGroupDef def)
        {
            DragModifier m = DragModifier.None;
            if (def.DragCtrl) m |= DragModifier.Ctrl;
            if (def.DragShift) m |= DragModifier.Shift;
            if (def.DragAlt) m |= DragModifier.Alt;
            return m;
        }

        public static bool HasCategory(PluginAnchorGroupDef def, Allegiance a)
        {
            switch (a)
            {
                case Allegiance.Allied: return def.DragAllied;
                case Allegiance.Hostile: return def.DragHostile;
                default: return def.DragNeutral;
            }
        }

        public static bool HasBinding(PluginAnchorGroupDef def)
        {
            return ModifiersOf(def) != DragModifier.None && (def.DragAllied || def.DragHostile || def.DragNeutral);
        }

        public static int ResolveDragAnchor(DragModifier held, Allegiance cat, IReadOnlyList<PluginAnchorGroupDef> defs)
        {
            if (defs == null)
            {
                return 0;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                PluginAnchorGroupDef d = defs[i];
                if (d != null && d.Id != 0 && HasBinding(d) && ModifiersOf(d) == held && HasCategory(d, cat))
                {
                    return d.Id;
                }
            }

            return 0;
        }

        public static List<int> ConflictingGroupIds(IReadOnlyList<PluginAnchorGroupDef> defs)
        {
            var conflicts = new List<int>();
            if (defs == null)
            {
                return conflicts;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                PluginAnchorGroupDef a = defs[i];
                if (a == null || !HasBinding(a))
                {
                    continue;
                }

                for (int j = 0; j < i; j++)
                {
                    PluginAnchorGroupDef b = defs[j];
                    if (b == null || !HasBinding(b) || ModifiersOf(b) != ModifiersOf(a))
                    {
                        continue;
                    }

                    bool overlap = (a.DragAllied && b.DragAllied) || (a.DragHostile && b.DragHostile) || (a.DragNeutral && b.DragNeutral);
                    if (overlap)
                    {
                        conflicts.Add(a.Id); // the later def loses
                        break;
                    }
                }
            }

            return conflicts;
        }
    }
}
