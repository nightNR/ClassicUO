// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
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
        internal readonly struct OverlayHues
        {
            public readonly ushort Ring;
            public readonly ushort Background;

            public OverlayHues(ushort ring, ushort background)
            {
                Ring = ring;
                Background = background;
            }
        }

        private static readonly Dictionary<uint, OverlayHues> _overlays = new Dictionary<uint, OverlayHues>();

        public static void Set(uint serial, ushort hue, ushort backgroundHue)
        {
            if (hue == 0 && backgroundHue == 0)
            {
                _overlays.Remove(serial);
                return;
            }

            _overlays[serial] = new OverlayHues(hue, backgroundHue);
        }

        public static ushort Get(uint serial)
        {
            return _overlays.TryGetValue(serial, out OverlayHues hues) ? hues.Ring : (ushort)0;
        }

        public static ushort GetBackground(uint serial)
        {
            return _overlays.TryGetValue(serial, out OverlayHues hues) ? hues.Background : (ushort)0;
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

        // Insertion-ordered live members per group, used to compute the next
        // grid cell (column-major) and its anchor neighbor. Disposed bars are
        // pruned lazily on read.
        private static readonly Dictionary<int, List<BaseHealthBarGump>> _members =
            new Dictionary<int, List<BaseHealthBarGump>>();

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

        /// <summary>
        /// Returns the group's live members in insertion order, pruning any that
        /// have been disposed (closed by the user, out of range, death, etc.).
        /// The returned list is the tracked instance; callers may index it.
        /// </summary>
        public static List<BaseHealthBarGump> GetLiveMembers(int groupId)
        {
            if (!_members.TryGetValue(groupId, out List<BaseHealthBarGump> list))
            {
                return new List<BaseHealthBarGump>();
            }

            list.RemoveAll(b => b == null || b.IsDisposed);

            return list;
        }

        public static void AddMember(int groupId, BaseHealthBarGump bar)
        {
            if (groupId == 0 || bar == null)
            {
                return;
            }

            if (!_members.TryGetValue(groupId, out List<BaseHealthBarGump> list))
            {
                list = new List<BaseHealthBarGump>();
                _members[groupId] = list;
            }

            list.Add(bar);
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
                _members.Remove(id);
            }
        }

        /// <summary>Test-only: drops all tracked groups.</summary>
        public static void Reset()
        {
            _groups.Clear();
            _members.Clear();
        }
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
        public static void SetOverlay(uint serial, ushort hue, ushort backgroundHue)
        {
            PluginStatusOverlays.Set(serial, hue, backgroundHue);
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

        // Defaults mirror Profile.PluginStatusBarMaxRows/Columns; used when no
        // profile is loaded (e.g. unit tests).
        internal const int DefaultMaxRows = 10;
        internal const int DefaultMaxColumns = 1;

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

            // Grouped bars are laid out in a bounded grid; once the group has
            // filled MaxRows*MaxColumns cells, further opens are dropped.
            if (groupId != 0 &&
                IsCapacityReached(
                    PluginStatusBarGroups.GetLiveMembers(groupId).Count,
                    ResolveMaxRows(),
                    ResolveMaxColumns()))
            {
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

        // Lays the group out column-major: the first bar seeds the group at the
        // plugin-supplied position; each subsequent bar stacks below the one
        // above it until a column holds MaxRows bars, then a new column starts to
        // the right. AnchorManager.DropControl slots the bar into the matrix so
        // the whole grid drags as one unit.
        private static void AddToGroup(int groupId, BaseHealthBarGump bar)
        {
            int maxRows = ResolveMaxRows();
            List<BaseHealthBarGump> members = PluginStatusBarGroups.GetLiveMembers(groupId);
            int index = members.Count; // cell this new bar will occupy

            AnchorManager.AnchorGroup group = PluginStatusBarGroups.GetGroup(groupId);

            if (index == 0 || group == null || group.IsEmpty)
            {
                group = new AnchorManager.AnchorGroup(bar);
                UIManager.AnchorManager[bar] = group;
                PluginStatusBarGroups.Track(groupId, group);
                PluginStatusBarGroups.AddMember(groupId, bar);

                return;
            }

            (int _, int row) = GridCell(index, maxRows);

            BaseHealthBarGump neighbor;

            if (row > 0)
            {
                // Stack below the previous bar in this column (GetAnchorDirection → south).
                neighbor = members[index - 1];
                bar.X = neighbor.X;
                bar.Y = neighbor.Y + neighbor.GroupMatrixHeight;
            }
            else
            {
                // Start a new column right of the top bar of the previous column (→ east).
                neighbor = members[index - maxRows];
                bar.X = neighbor.X + neighbor.GroupMatrixWidth;
                bar.Y = neighbor.Y;
            }

            UIManager.AnchorManager.DropControl(bar, neighbor);
            PluginStatusBarGroups.AddMember(groupId, bar);
        }

        // --- Pure layout helpers (unit-tested) ---

        /// <summary>Column-major cell for the given 0-based insertion index.</summary>
        internal static (int column, int row) GridCell(int index, int maxRows)
        {
            if (maxRows < 1)
            {
                maxRows = 1;
            }

            return (index / maxRows, index % maxRows);
        }

        /// <summary>True once a group already holds every cell of its grid.</summary>
        internal static bool IsCapacityReached(int liveCount, int maxRows, int maxColumns)
        {
            return liveCount >= maxRows * maxColumns;
        }

        /// <summary>Clamps a grid dimension to a sane minimum of 1.</summary>
        internal static int NormalizeDimension(int value)
        {
            return value < 1 ? 1 : value;
        }

        internal static int ResolveMaxRows()
        {
            return NormalizeDimension(ProfileManager.CurrentProfile?.PluginStatusBarMaxRows ?? DefaultMaxRows);
        }

        internal static int ResolveMaxColumns()
        {
            return NormalizeDimension(ProfileManager.CurrentProfile?.PluginStatusBarMaxColumns ?? DefaultMaxColumns);
        }
    }
}
