// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Game.GameObjects;
using ClassicUO.PluginApi;

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

    internal sealed class AreaEntry
    {
        public string Id;
        public HighlightSnap Snap;
        public uint AnchorSerial;
        public ushort Hue;
        public int RangeX;
        public int RangeY;
        public HighlightObjectTypes ObjectTypes;
        public long ExpireAtTicks; // -1 = never
        public int CenterX;
        public int CenterY;
        public sbyte CenterZ;
        public bool AnchorResolved;
        public int InsertionSeq;
    }

    /// <summary>
    /// Plugin-driven world-space area highlights, keyed by plugin-chosen id.
    /// No spatial index: membership is a linear scan over the active-area
    /// dictionary, which is cheap because both the on-screen tile count
    /// (isometric view, ~24x24) and the realistic active-area count (tens)
    /// stay small. Last-added-wins on overlap, tracked via a monotonic
    /// insertion sequence rather than dictionary iteration order.
    /// </summary>
    internal static class PluginHighlightAreas
    {
        private static readonly Dictionary<string, AreaEntry> _areas = new Dictionary<string, AreaEntry>();
        private static readonly List<string> _expiredScratch = new List<string>();
        private static int _nextSeq;

        // Test seams; when null, resolution uses the live World / SelectedObject.
        internal static Func<uint, (bool found, int x, int y, sbyte z)> SerialResolver;
        internal static Func<(int x, int y, sbyte z)> MouseWorldResolver;

        public static void Add(
            World world,
            string id,
            int durationMs,
            HighlightSnap snap,
            uint anchorSerial,
            ushort hue,
            int rangeX,
            int rangeY,
            HighlightObjectTypes objectTypes,
            int x,
            int y,
            long now
        )
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            var entry = new AreaEntry
            {
                Id = id,
                Snap = snap,
                AnchorSerial = anchorSerial,
                Hue = hue,
                RangeX = rangeX,
                RangeY = rangeY,
                ObjectTypes = objectTypes,
                ExpireAtTicks = durationMs < 0 ? -1 : now + durationMs,
                CenterX = x,
                CenterY = y,
                CenterZ = 0,
                InsertionSeq = _nextSeq++
            };

            _areas[id] = entry;
            ResolveCenter(world, entry);
        }

        public static void Remove(string id) => _areas.Remove(id);

        public static void ClearAll() => _areas.Clear();

        public static int GetTimer(string id, long now)
        {
            if (!_areas.TryGetValue(id, out AreaEntry e))
            {
                return 0;
            }

            if (e.ExpireAtTicks < 0)
            {
                return int.MaxValue;
            }

            long remaining = e.ExpireAtTicks - now;
            return remaining > 0 ? (int)remaining : 0;
        }

        public static bool TryResolve(int x, int y, sbyte z, HighlightObjectTypes type, out ushort hue)
        {
            AreaEntry best = null;

            foreach (KeyValuePair<string, AreaEntry> kv in _areas)
            {
                AreaEntry e = kv.Value;

                if ((e.ObjectTypes & type) == 0 || !e.AnchorResolved)
                {
                    continue;
                }

                if (Math.Abs(x - e.CenterX) > e.RangeX || Math.Abs(y - e.CenterY) > e.RangeY)
                {
                    continue;
                }

                if (best == null || e.InsertionSeq > best.InsertionSeq)
                {
                    best = e;
                }
            }

            if (best == null)
            {
                hue = 0;
                return false;
            }

            hue = best.Hue;
            return true;
        }

        /// <summary>Per-frame maintenance: expire timed areas, re-resolve Mouse/Serial centers.</summary>
        public static void Update(World world, long now)
        {
            _expiredScratch.Clear();

            foreach (KeyValuePair<string, AreaEntry> kv in _areas)
            {
                AreaEntry e = kv.Value;

                if (e.ExpireAtTicks >= 0 && now >= e.ExpireAtTicks)
                {
                    _expiredScratch.Add(kv.Key);
                    continue;
                }

                if (e.Snap == HighlightSnap.Mouse || e.Snap == HighlightSnap.Serial)
                {
                    ResolveCenter(world, e);

                    if (e.Snap == HighlightSnap.Serial && !e.AnchorResolved)
                    {
                        _expiredScratch.Add(kv.Key);
                    }
                }
            }

            foreach (string id in _expiredScratch)
            {
                _areas.Remove(id);
            }
        }

        private static void ResolveCenter(World world, AreaEntry e)
        {
            switch (e.Snap)
            {
                case HighlightSnap.Position:
                    e.AnchorResolved = true;
                    break;

                case HighlightSnap.Mouse:
                    (int mx, int my, sbyte mz) = MouseWorldResolver != null ? MouseWorldResolver() : DefaultMouseWorld(world);
                    e.CenterX = mx;
                    e.CenterY = my;
                    e.CenterZ = mz;
                    e.AnchorResolved = true;
                    break;

                case HighlightSnap.Serial:
                    (bool found, int sx, int sy, sbyte sz) = SerialResolver != null
                        ? SerialResolver(e.AnchorSerial)
                        : DefaultSerialWorld(world, e.AnchorSerial);
                    e.AnchorResolved = found;
                    if (found)
                    {
                        e.CenterX = sx;
                        e.CenterY = sy;
                        e.CenterZ = sz;
                    }
                    break;
            }
        }

        private static (int x, int y, sbyte z) DefaultMouseWorld(World world)
        {
            if (SelectedObject.Object is GameObject g)
            {
                return (g.X, g.Y, g.Z);
            }

            if (world?.Player != null)
            {
                return (world.Player.X, world.Player.Y, world.Player.Z);
            }

            return (0, 0, 0);
        }

        private static (bool found, int x, int y, sbyte z) DefaultSerialWorld(World world, uint serial)
        {
            Entity entity = world?.Get(serial);

            if (entity == null)
            {
                return (false, 0, 0, 0);
            }

            return (true, entity.X, entity.Y, entity.Z);
        }

        /// <summary>Test-only: drops every area and test seam so tests start clean.</summary>
        public static void Reset()
        {
            _areas.Clear();
            SerialResolver = null;
            MouseWorldResolver = null;
            _nextSeq = 0;
        }
    }
}
