// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ClassicUO.Game;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Single per-frame driver for both plugin buffs and screen timers. Called
    /// from <see cref="World.Update"/> regardless of whether any gump is open.
    /// Detects expiry, removes entries, and dispatches events back to the plugin
    /// host. Event routing goes through test seams so the pure logic is testable
    /// without a native host.
    /// </summary>
    internal static class PluginTimersManager
    {
        public const int ReasonExpired = 0;
        public const int ReasonRemovedByPlugin = 1;
        public const int ReasonRemovedByUser = 2;
        public const int ReasonAnchorLost = 3;

        // Test seams; when null, events route to the real plugin host.
        internal static Action<int, int> BuffEventSink;
        internal static Action<int, int> TimerEventSink;

        // Test seam; when null, serial resolution uses the live World.
        internal static Func<uint, bool> SerialResolver;

        // Wired by BuffGump so an open gump rebuilds after a set change.
        internal static Action GumpRefresh;

        private static readonly List<int> _expiredScratch = new List<int>();
        private static readonly List<int> _lostScratch = new List<int>();

        public static void Update(long now) => Update(null, now);

        public static void Update(World world, long now)
        {
            _expiredScratch.Clear();
            PluginBuffs.CollectExpired(now, _expiredScratch);
            if (_expiredScratch.Count > 0)
            {
                foreach (int id in _expiredScratch)
                {
                    PluginBuffs.Remove(id);
                    RaiseBuffEvent(id, ReasonExpired);
                }
                GumpRefresh?.Invoke();
            }

            _expiredScratch.Clear();
            ScreenTimers.CollectExpired(now, _expiredScratch);
            foreach (int id in _expiredScratch)
            {
                ScreenTimers.Remove(id);
                RaiseTimerEvent(id, ReasonExpired);
            }

            UpdateAnchors(world, now);
        }

        // Lost-anchor grace for Serial/Self timers. Off-screen is NOT handled here
        // (that is a render-only concern); this only fires when the anchor entity
        // no longer exists in the world. Absolute anchors are never lost.
        private static void UpdateAnchors(World world, long now)
        {
            _lostScratch.Clear();

            foreach (var kv in ScreenTimers.Entries)
            {
                var e = kv.Value;
                if (e.AnchorKind != AnchorKind.Serial && e.AnchorKind != AnchorKind.Self)
                    continue;

                bool resolvable = e.AnchorKind == AnchorKind.Self
                    ? ResolvePlayer(world)
                    : ResolveSerial(world, e.AnchorSerial);

                if (resolvable)
                {
                    if (e.MissingSinceTicks != 0)
                        ScreenTimers.SetMissingSince(e.Id, 0);
                    continue;
                }

                if (e.MissingSinceTicks == 0)
                {
                    ScreenTimers.SetMissingSince(e.Id, now);
                }
                else if (now - e.MissingSinceTicks >= e.AnchorGraceMs)
                {
                    _lostScratch.Add(e.Id);
                }
            }

            foreach (int id in _lostScratch)
            {
                ScreenTimers.Remove(id);
                RaiseTimerEvent(id, ReasonAnchorLost);
            }
        }

        private static bool ResolveSerial(World world, uint serial)
            => SerialResolver != null ? SerialResolver(serial) : world?.Get(serial) != null;

        private static bool ResolvePlayer(World world)
            => SerialResolver != null ? SerialResolver(0) : world?.Player != null;

        public static void RaiseBuffEvent(int id, int reason)
        {
            if (BuffEventSink != null)
                BuffEventSink(id, reason);
            else
                Client.Game?.PluginHost?.BuffEvent(id, reason);
        }

        public static void RaiseTimerEvent(int id, int reason)
        {
            if (TimerEventSink != null)
                TimerEventSink(id, reason);
            else
                Client.Game?.PluginHost?.TimerEvent(id, reason);
        }

        /// <summary>Test-only: clear seams and both stores.</summary>
        public static void Reset()
        {
            BuffEventSink = null;
            TimerEventSink = null;
            SerialResolver = null;
            GumpRefresh = null;
            PluginBuffs.Reset();
            ScreenTimers.Reset();
        }

        private static string PtrToString(IntPtr p) => p == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringAnsi(p) ?? string.Empty);

        // ── Buff commands (bound into ClientBindings) ──
        public static void AddBuff(int id, ushort graphic, int durationMs, int kind, IntPtr textUtf8)
        {
            PluginBuffs.AddOrUpdate(id, graphic, durationMs, (Data.BuffDisplayKind)kind, PtrToString(textUtf8), Time.Ticks);
            GumpRefresh?.Invoke();
        }

        public static void RemoveBuff(int id)
        {
            if (PluginBuffs.Remove(id))
            {
                RaiseBuffEvent(id, ReasonRemovedByPlugin);
                GumpRefresh?.Invoke();
            }
        }

        public static void ClearBuffs()
        {
            var ids = new List<int>(PluginBuffs.Entries.Keys);
            PluginBuffs.Clear();
            foreach (int id in ids)
                RaiseBuffEvent(id, ReasonRemovedByPlugin);
            GumpRefresh?.Invoke();
        }

        // ── Timer commands (bound into ClientBindings) ──
        public static void DefineTimerGroup(int groupId, int x, int y, int direction, int gap)
        {
            ScreenTimers.DefineGroup(groupId, x, y, (StackDirection)direction, gap);
        }

        public static void AddTimer(int id, int shape, int durationMs, ushort hue, int groupId,
                                    int x, int y, int width, int height, IntPtr labelUtf8, byte showTime)
        {
            ScreenTimers.AddOrUpdate(id, (TimerShape)shape, durationMs, hue, groupId,
                x, y, width, height, PtrToString(labelUtf8), showTime != 0, Time.Ticks);
        }

        public static void RemoveTimer(int id)
        {
            if (ScreenTimers.Remove(id))
                RaiseTimerEvent(id, ReasonRemovedByPlugin);
        }

        public static void RemoveTimerGroup(int groupId)
        {
            var removed = new List<int>();
            ScreenTimers.RemoveGroup(groupId, removed);
            foreach (int id in removed)
                RaiseTimerEvent(id, ReasonRemovedByPlugin);
        }

        public static void ClearTimers()
        {
            var ids = new List<int>(ScreenTimers.Entries.Keys);
            ScreenTimers.Clear();
            foreach (int id in ids)
                RaiseTimerEvent(id, ReasonRemovedByPlugin);
        }
    }
}
