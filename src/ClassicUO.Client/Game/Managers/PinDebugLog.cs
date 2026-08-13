// SPDX-License-Identifier: BSD-2-Clause
// TEMPORARY diagnostic helper — remove once the backpack-closes-on-death investigation is done.

using System;
using System.IO;

namespace ClassicUO.Game.Managers
{
    internal static class PinDebugLog
    {
        private static readonly string Path = System.IO.Path.Combine(CUOEnviroment.ExecutablePath, "pin_debug.log");

        public static void Write(string msg)
        {
            try
            {
                File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
            }
            catch
            {
                // best-effort diagnostic only
            }
        }
    }
}
