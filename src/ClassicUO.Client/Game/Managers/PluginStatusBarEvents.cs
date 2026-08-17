// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.PluginAbi;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Emits <see cref="GenericEventIds"/>.StatusBar* events for every status
    /// (health) bar gump — plugin-opened or player-opened alike — and answers
    /// <see cref="GenericOpcodes.ListOpenStatusBars"/>. See
    /// <see cref="BaseHealthBarGump"/> and <see cref="UIManager.Add"/> for the
    /// opened/closed/changed hook sites.
    /// </summary>
    internal static unsafe class PluginStatusBarEvents
    {
        /// <summary>Test-only seam, mirrors PluginTimersManager's *Sink pattern.</summary>
        internal static Action<int, byte[]> EventSink;

        public static void RaiseOpened(uint serial) => Raise(GenericEventIds.StatusBarOpened, BitConverter.GetBytes(serial));
        public static void RaiseClosed(uint serial) => Raise(GenericEventIds.StatusBarClosed, BitConverter.GetBytes(serial));

        public static void RaiseChanged(StatusBarChangedPayload payload)
        {
            var bytes = new byte[sizeof(StatusBarChangedPayload)];
            fixed (byte* dst = bytes)
                *(StatusBarChangedPayload*)dst = payload;
            Raise(GenericEventIds.StatusBarChanged, bytes);
        }

        private static void Raise(int eventId, byte[] payload)
        {
            if (EventSink != null)
                EventSink(eventId, payload);
            else
                Client.Game?.PluginHost?.GenericEvent(eventId, payload);
        }

        /// <summary>
        /// Writes every currently-open status bar's serial (uint) into
        /// <paramref name="outBuf"/>. Returns bytes written, or -2 (writing
        /// nothing) if <paramref name="outCap"/> can't hold all of them.
        /// </summary>
        public static int ListOpen(IntPtr outBuf, int outCap)
        {
            int count = 0;
            foreach (var g in UIManager.Gumps)
            {
                if (g is BaseHealthBarGump)
                    count++;
            }

            int needed = count * sizeof(uint);
            if (outCap < needed)
                return -2;

            uint* dst = (uint*)outBuf;
            int i = 0;
            foreach (var g in UIManager.Gumps)
            {
                if (g is BaseHealthBarGump bar)
                    dst[i++] = bar.LocalSerial;
            }

            return needed;
        }
    }
}
