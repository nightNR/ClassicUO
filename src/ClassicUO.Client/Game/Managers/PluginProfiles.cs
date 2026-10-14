// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Text;
using ClassicUO.Configuration;
using ClassicUO.Game.Scenes;
using ClassicUO.PluginAbi;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Profile surface of the plugin ABI: <see cref="GenericOpcodes"/>.ListProfiles/GetActiveProfile/SwitchProfile/
    /// ClearProfileOverride and the <see cref="GenericEventIds"/>.ProfileChanged/CharacterSelected events.
    /// </summary>
    internal static unsafe class PluginProfiles
    {
        /// <summary>Test-only seam, mirrors PluginStatusBarEvents.EventSink.</summary>
        internal static Action<int, byte[]> EventSink;

        public static void RaiseChanged(string profileName) => Raise(GenericEventIds.ProfileChanged, Encoding.UTF8.GetBytes(profileName ?? ""));

        public static void RaiseCharacterSelected(string account, string server, string character)
        {
            Raise(GenericEventIds.CharacterSelected, Encoding.UTF8.GetBytes($"{account}\n{server}\n{character}"));
        }

        private static void Raise(int eventId, byte[] payload)
        {
            if (EventSink != null)
            {
                EventSink(eventId, payload);
            }
            else
            {
                Client.Game?.PluginHost?.GenericEvent(eventId, payload);
            }
        }

        public static int List(IntPtr outBuf, int outCap)
        {
            return WriteUtf8(string.Join("\n", ProfileCatalog.ListProfiles(ProfileManager.RootPath)), outBuf, outCap);
        }

        public static int Active(IntPtr outBuf, int outCap)
        {
            return WriteUtf8(ProfileManager.ActiveProfileName ?? "", outBuf, outCap);
        }

        public static int Switch(IntPtr argsPtr, int argsLen)
        {
            if (argsLen < 2)
            {
                return -2;
            }

            byte flags = *(byte*)argsPtr;
            string name = Encoding.UTF8.GetString((byte*)argsPtr + 1, argsLen - 1);

            if (!ProfileCatalog.Exists(ProfileManager.RootPath, name))
            {
                return -3;
            }

            World world = Client.Game?.UO?.World;

            if (world != null && world.InGame && Client.Game.Scene is GameScene scene)
            {
                return scene.SwitchProfile(name, (flags & 1) != 0) ? 0 : -4;
            }

            ProfileManager.OverrideProfileName = ProfileCatalog.SanitizeName(name);

            return 1;
        }

        public static int ClearOverride()
        {
            ProfileManager.OverrideProfileName = null;

            return 0;
        }

        private static int WriteUtf8(string text, IntPtr outBuf, int outCap)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);

            if (outCap < bytes.Length)
            {
                return -2;
            }

            bytes.CopyTo(new Span<byte>((void*)outBuf, bytes.Length));

            return bytes.Length;
        }
    }
}
