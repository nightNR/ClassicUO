// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.Linq;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Plugin-driven priority-highlight hues keyed by mobile serial. Policy
    /// (which serial gets which hue) lives entirely in the plugin; the client
    /// only stores and renders. A hue of 0 means "no overlay".
    /// </summary>
    internal static class PluginStatusOverlays
    {
        private static readonly Dictionary<uint, ushort> _overlays = new Dictionary<uint, ushort>();

        public static void Set(uint serial, ushort hue)
        {
            if (hue == 0)
            {
                _overlays.Remove(serial);
                return;
            }

            _overlays[serial] = hue;
        }

        public static ushort Get(uint serial)
        {
            return _overlays.TryGetValue(serial, out ushort hue) ? hue : (ushort)0;
        }

        public static void Clear(uint serial) => _overlays.Remove(serial);

        /// <summary>Test-only: drops every overlay so tests start clean.</summary>
        public static void Reset() => _overlays.Clear();
    }

    /// <summary>
    /// Maps plugin-supplied group ids to the existing anchor-system
    /// <see cref="AnchorManager.AnchorGroup"/> objects so plugins can snap status
    /// bars into a shared, drag-as-a-unit group. The anchor matrix machinery is
    /// reused unchanged; this only tracks id ownership and prunes dead groups.
    /// </summary>
    internal static class PluginStatusBarGroups
    {
        private static readonly Dictionary<int, AnchorManager.AnchorGroup> _groups =
            new Dictionary<int, AnchorManager.AnchorGroup>();

        public static void Track(int groupId, AnchorManager.AnchorGroup group)
        {
            if (groupId == 0 || group == null)
            {
                return;
            }

            _groups[groupId] = group;
        }

        public static AnchorManager.AnchorGroup GetGroup(int groupId)
        {
            return _groups.TryGetValue(groupId, out AnchorManager.AnchorGroup group) ? group : null;
        }

        public static void PruneEmpty()
        {
            List<int> dead = _groups
                .Where(kv => kv.Value == null || kv.Value.IsEmpty)
                .Select(kv => kv.Key)
                .ToList();

            foreach (int id in dead)
            {
                _groups.Remove(id);
            }
        }

        /// <summary>Test-only: drops all tracked groups.</summary>
        public static void Reset() => _groups.Clear();
    }

    /// <summary>
    /// Static client-side targets for the three plugin->cuo status-bar
    /// primitives. Bound into <c>ClientBindings</c> in PluginHost.cs and called
    /// by the host's StatusBarsImpl. All run on the game thread (the host
    /// marshals before calling). Mirrors GameActions.CastSpell as a static
    /// binding target.
    /// </summary>
    internal static class PluginStatusBars
    {
        public static void SetOverlay(uint serial, ushort hue)
        {
            PluginStatusOverlays.Set(serial, hue);
        }

        public static void CloseStatusBar(uint serial)
        {
            BaseHealthBarGump bar = UIManager.GetGump<BaseHealthBarGump>(serial);

            if (bar == null)
            {
                return;
            }

            UIManager.AnchorManager.DetachControl(bar);
            bar.Dispose();

            PluginStatusBarGroups.PruneEmpty();
        }

        public static void OpenStatusBar(uint serial, int x, int y, byte moveIfExists, int groupId)
        {
            World world = Client.Game?.UO?.World;

            if (world == null)
            {
                return;
            }

            BaseHealthBarGump existing = UIManager.GetGump<BaseHealthBarGump>(serial);

            if (existing != null)
            {
                if (moveIfExists != 0)
                {
                    existing.X = x;
                    existing.Y = y;
                }

                return;
            }

            // Plugin-opened bars are always the custom bar so priority-overlay tint works.
            HealthBarGumpCustom bar = new HealthBarGumpCustom(world, serial)
            {
                X = x,
                Y = y
            };

            UIManager.Add(bar);

            if (groupId != 0)
            {
                AddToGroup(groupId, bar);
            }
        }

        // Seeds a new AnchorGroup for the first bar of a groupId; for later bars
        // it positions the new bar to the right of an existing member and reuses
        // AnchorManager.DropControl to slot it into the matrix.
        private static void AddToGroup(int groupId, BaseHealthBarGump bar)
        {
            AnchorManager.AnchorGroup group = PluginStatusBarGroups.GetGroup(groupId);

            if (group == null || group.IsEmpty)
            {
                group = new AnchorManager.AnchorGroup(bar);
                UIManager.AnchorManager[bar] = group;
                PluginStatusBarGroups.Track(groupId, group);

                return;
            }

            BaseHealthBarGump host = FindHost(group);

            if (host == null)
            {
                group = new AnchorManager.AnchorGroup(bar);
                UIManager.AnchorManager[bar] = group;
                PluginStatusBarGroups.Track(groupId, group);

                return;
            }

            // Place to the right of the host so GetAnchorDirection slots it east.
            bar.X = host.X + host.GroupMatrixWidth;
            bar.Y = host.Y;

            UIManager.AnchorManager.DropControl(bar, host);
        }

        private static BaseHealthBarGump FindHost(AnchorManager.AnchorGroup group)
        {
            foreach (Gump gump in UIManager.Gumps)
            {
                if (gump is BaseHealthBarGump bar && !bar.IsDisposed && UIManager.AnchorManager[bar] == group)
                {
                    return bar;
                }
            }

            return null;
        }
    }
}
