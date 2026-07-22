// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Owns the on-screen permanent anchor-group widgets. Rebuilt from
    /// Profile.PluginAnchorGroups on world load and after Options apply.
    /// </summary>
    internal static class PluginAnchorGroupManager
    {
        public static void Rebuild(World world)
        {
            DisposeAll();

            var defs = ProfileManager.CurrentProfile?.PluginAnchorGroups;

            if (defs == null)
            {
                return;
            }

            foreach (PluginAnchorGroupDef def in defs)
            {
                if (def == null || def.Id == 0)
                {
                    continue;
                }

                UIManager.Add(new PluginAnchorGroupGump(world, def));
            }
        }

        public static void DisposeAll()
        {
            List<PluginAnchorGroupGump> existing = UIManager.Gumps.OfType<PluginAnchorGroupGump>().ToList();

            foreach (PluginAnchorGroupGump g in existing)
            {
                g.Dispose();
            }
        }
    }
}
