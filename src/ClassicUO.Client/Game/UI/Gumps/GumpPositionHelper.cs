// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Pure math for the experimental "save gump positions relative to window
    /// center" feature. UI-free so it can be unit-tested without a live window.
    /// </summary>
    internal static class GumpPositionHelper
    {
        /// <summary>
        /// Re-anchors an absolute gump position saved in a window of
        /// <paramref name="saveW"/> x <paramref name="saveH"/> so it keeps the
        /// same offset from the window center in the current
        /// <paramref name="curW"/> x <paramref name="curH"/> window, then clamps
        /// it fully on-screen.
        /// </summary>
        public static (int x, int y) CenterAnchor(int x, int y, int saveW, int saveH, int curW, int curH, int gumpW, int gumpH)
        {
            x += (curW - saveW) / 2;
            y += (curH - saveH) / 2;

            x = Math.Clamp(x, 0, Math.Max(0, curW - gumpW));
            y = Math.Clamp(y, 0, Math.Max(0, curH - gumpH));

            return (x, y);
        }
    }
}
