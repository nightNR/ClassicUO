// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

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

        // Test seams; when null, events route to the real plugin host.
        internal static Action<int, int> BuffEventSink;
        internal static Action<int, int> TimerEventSink;

        // Wired by BuffGump so an open gump rebuilds after a set change.
        internal static Action GumpRefresh;

        private static readonly List<int> _expiredScratch = new List<int>();

        public static void Update(long now)
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
        }

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
            GumpRefresh = null;
            PluginBuffs.Reset();
            ScreenTimers.Reset();
        }
    }
}
