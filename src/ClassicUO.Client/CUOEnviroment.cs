// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace ClassicUO
{
    internal static class CUOEnviroment
    {
        public static Thread GameThread;
        public static float DPIScaleFactor = 1.0f;
        public static bool NoSound;
        public static string[] Args;
        public static string[] Plugins;
        public static bool Debug;
        public static bool IsHighDPI;
        public static uint CurrentRefreshRate;
        public static bool SkipLoginScreen;
        public static bool NoServerPing;

        public static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT || Environment.OSVersion.Platform == PlatformID.Win32Windows || Environment.OSVersion.Platform == PlatformID.Win32S || Environment.OSVersion.Platform == PlatformID.WinCE;
        public static readonly bool IsUnix = !IsWindows;

        public static readonly string Version = Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? "0.0.0.0";
        // Anchor to the directory where the cuo client itself lives (alongside
        // Data/, Overrides/, etc.) rather than the process working directory,
        // which varies by launch method. When cuo is loaded as a plugin by a
        // hosting launcher from a subfolder (e.g. .../client/cuo.dll), the app
        // base points at the HOST, not the client - so prefer this assembly's
        // own location. Assembly.Location is empty under NativeAOT/single-file,
        // where AppContext.BaseDirectory is the correct (and only) fallback.
        public static readonly string ExecutablePath = ResolveExecutablePath();

        private static string ResolveExecutablePath()
        {
            string loc = Assembly.GetExecutingAssembly()?.Location;
            if (!string.IsNullOrEmpty(loc))
            {
                string dir = Path.GetDirectoryName(loc);
                if (!string.IsNullOrEmpty(dir))
                    return dir;
            }

            return AppContext.BaseDirectory;
        }
    }
}