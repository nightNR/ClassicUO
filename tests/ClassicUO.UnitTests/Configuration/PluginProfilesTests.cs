// SPDX-License-Identifier: BSD-2-Clause
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.PluginAbi;
using Xunit;

namespace ClassicUO.UnitTests.Configuration
{
    [Collection("ProfileManagerSerial")]
    public unsafe class PluginProfilesTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cuo_plugprof_" + Guid.NewGuid().ToString("N"), "Profiles");
        private readonly string _oldPath;

        public PluginProfilesTests()
        {
            Directory.CreateDirectory(_root);
            _oldPath = Settings.GlobalSettings.ProfilesPath;
            Settings.GlobalSettings.ProfilesPath = _root;
            ProfileManager.ResetRootCache();
            ProfileManager.OverrideProfileName = null;
            Directory.CreateDirectory(ProfileCatalog.ProfileDir(_root, "Main"));
            Directory.CreateDirectory(ProfileCatalog.ProfileDir(_root, "Raid"));
        }

        public void Dispose()
        {
            PluginProfiles.EventSink = null;
            ProfileManager.OverrideProfileName = null;
            Settings.GlobalSettings.ProfilesPath = _oldPath;
            ProfileManager.ResetRootCache();

            try { Directory.Delete(Path.GetDirectoryName(_root), true); } catch { }
        }

        private static int Invoke(int opcode, byte[] args, byte[] output, out int written)
        {
            fixed (byte* a = args)
            fixed (byte* o = output)
            {
                written = PluginGenericDispatch.Invoke(opcode, (IntPtr)a, args.Length, (IntPtr)o, output.Length);
            }

            return written;
        }

        private static byte[] SwitchArgs(string name, byte flags = 0)
        {
            byte[] n = Encoding.UTF8.GetBytes(name);
            var args = new byte[n.Length + 1];
            args[0] = flags;
            n.CopyTo(args, 1);

            return args;
        }

        [Fact]
        public void SupportedOpcodes_ListProfileOpcodes()
        {
            var buf = new byte[256];
            int n = Invoke(GenericOpcodes.GetSupportedOpcodes, Array.Empty<byte>(), buf, out _);
            var ops = new HashSet<int>();

            for (int i = 0; i < n / 4; i++)
            {
                ops.Add(BitConverter.ToInt32(buf, i * 4));
            }

            Assert.Contains(GenericOpcodes.ListProfiles, ops);
            Assert.Contains(GenericOpcodes.GetActiveProfile, ops);
            Assert.Contains(GenericOpcodes.SwitchProfile, ops);
            Assert.Contains(GenericOpcodes.ClearProfileOverride, ops);
        }

        [Fact]
        public void ListProfiles_ReturnsNewlineSeparatedNames()
        {
            var buf = new byte[256];
            int n = Invoke(GenericOpcodes.ListProfiles, Array.Empty<byte>(), buf, out _);

            Assert.Equal("Main\nRaid", Encoding.UTF8.GetString(buf, 0, n));
        }

        [Fact]
        public void ListProfiles_SmallBuffer_ReturnsMinusTwo()
        {
            Assert.Equal(-2, Invoke(GenericOpcodes.ListProfiles, Array.Empty<byte>(), new byte[3], out _));
        }

        [Fact]
        public void Switch_BeforeLogin_QueuesOverride()
        {
            int r = Invoke(GenericOpcodes.SwitchProfile, SwitchArgs("Raid"), new byte[1], out _);

            Assert.Equal(1, r);
            Assert.Equal("Raid", ProfileManager.OverrideProfileName);

            Assert.Equal(0, Invoke(GenericOpcodes.ClearProfileOverride, Array.Empty<byte>(), new byte[1], out _));
            Assert.Null(ProfileManager.OverrideProfileName);
        }

        [Fact]
        public void Switch_UnknownProfile_AndShortArgs_AreRejected()
        {
            Assert.Equal(-3, Invoke(GenericOpcodes.SwitchProfile, SwitchArgs("Nope"), new byte[1], out _));
            Assert.Equal(-2, Invoke(GenericOpcodes.SwitchProfile, new byte[1], new byte[1], out _));
            Assert.Null(ProfileManager.OverrideProfileName);
        }

        [Fact]
        public void Events_ArePublishedThroughSink()
        {
            var got = new List<(int id, string text)>();
            PluginProfiles.EventSink = (id, payload) => got.Add((id, Encoding.UTF8.GetString(payload)));

            PluginProfiles.RaiseChanged("Raid");
            PluginProfiles.RaiseCharacterSelected("acc", "srv", "Alice");

            Assert.Equal((GenericEventIds.ProfileChanged, "Raid"), got[0]);
            Assert.Equal((GenericEventIds.CharacterSelected, "acc\nsrv\nAlice"), got[1]);
        }
    }
}
