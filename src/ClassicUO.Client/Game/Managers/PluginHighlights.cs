// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Plugin-driven per-serial highlight hues, two priority tiers. Policy
    /// (which serial gets which hue, and whether it should override the
    /// client's own status coloring) lives entirely in the plugin; the client
    /// only stores and resolves. Mirrors <see cref="PluginStatusOverlays"/>.
    /// </summary>
    internal static class PluginHighlightCharacters
    {
        private static readonly Dictionary<uint, ushort> _priority = new Dictionary<uint, ushort>();
        private static readonly Dictionary<uint, ushort> _normal = new Dictionary<uint, ushort>();

        public static void Set(uint serial, ushort hue, bool priorityHighlight)
        {
            if (priorityHighlight)
            {
                _priority[serial] = hue;
            }
            else
            {
                _normal[serial] = hue;
            }
        }

        public static void Remove(uint serial, bool priorityHighlight)
        {
            if (priorityHighlight)
            {
                _priority.Remove(serial);
            }
            else
            {
                _normal.Remove(serial);
            }
        }

        public static void ClearAll(bool priorityHighlight)
        {
            if (priorityHighlight)
            {
                _priority.Clear();
            }
            else
            {
                _normal.Clear();
            }
        }

        /// <summary>
        /// Resolves the highlight hue for <paramref name="serial"/>. The
        /// priority tier always wins. The normal tier only applies when
        /// <paramref name="statusOverrideActive"/> is false (the client's own
        /// status/notoriety coloring did not already set an override hue).
        /// </summary>
        public static bool TryResolve(uint serial, bool statusOverrideActive, out ushort hue)
        {
            if (_priority.TryGetValue(serial, out hue))
            {
                return true;
            }

            if (statusOverrideActive)
            {
                hue = 0;
                return false;
            }

            if (_normal.TryGetValue(serial, out hue))
            {
                return true;
            }

            hue = 0;
            return false;
        }

        /// <summary>Test-only: drops both tiers so tests start clean.</summary>
        public static void Reset()
        {
            _priority.Clear();
            _normal.Clear();
        }
    }
}
