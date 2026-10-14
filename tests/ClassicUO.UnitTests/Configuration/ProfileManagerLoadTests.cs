// SPDX-License-Identifier: BSD-2-Clause
using System;
using System.IO;
using System.Linq;
using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests.Configuration
{
    [Collection("ProfileManagerSerial")]
    public class ProfileManagerLoadTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cuo_profload_" + Guid.NewGuid().ToString("N"), "Profiles");
        private readonly string _oldPath;

        public ProfileManagerLoadTests()
        {
            Directory.CreateDirectory(_root);
            _oldPath = Settings.GlobalSettings.ProfilesPath;
            Settings.GlobalSettings.ProfilesPath = _root;
            ProfileManager.ResetRootCache();
            ProfileManager.OverrideProfileName = null;
        }

        public void Dispose()
        {
            ProfileManager.OverrideProfileName = null;
            ProfileManager.UnLoadProfile();
            Settings.GlobalSettings.ProfilesPath = _oldPath;
            ProfileManager.ResetRootCache();

            try { Directory.Delete(Path.GetDirectoryName(_root), true); } catch { }
        }

        private static void SaveNamed(string root, string name, int chatHistoryLength)
        {
            var p = new Profile { ChatHistoryLength = chatHistoryLength };
            ConfigurationResolver.Save(p, Path.Combine(ProfileCatalog.ProfileDir(root, name), "profile.json"), ProfileJsonContext.DefaultToUse.Profile);
        }

        [Fact]
        public void NewCharacter_GetsOwnNamedProfile_AndAssignment()
        {
            ProfileManager.Load("srv", "acc", "Alice");

            Assert.Equal("Alice", ProfileManager.ActiveProfileName);
            Assert.Equal(ProfileCatalog.ProfileDir(_root, "Alice"), ProfileManager.ProfilePath.TrimEnd(Path.DirectorySeparatorChar));
            Assert.Equal("Alice", ProfileCatalog.LoadAssignments(_root)[ProfileCatalog.AssignmentKey("acc", "srv", "Alice")]);
            Assert.Equal("Alice", ProfileManager.CurrentProfile.CharacterName);
        }

        [Fact]
        public void TwoCharacters_CanShareOneProfile_ButKeepOwnCharacterState()
        {
            ProfileManager.Load("srv", "acc", "Alice");
            ProfileManager.CurrentProfile.ChatHistoryLength = 55;
            ProfileManager.CurrentProfile.PinnedContainerSerials.Add(111);
            ProfileManager.Save(ProfileManager.CurrentProfile, ProfileManager.ProfilePath);
            ProfileManager.SaveCharacterState(ProfileManager.CurrentProfile);

            // assign Bob to Alice's profile
            var asg = ProfileCatalog.LoadAssignments(_root);
            asg[ProfileCatalog.AssignmentKey("acc", "srv", "Bob")] = "Alice";
            ProfileCatalog.SaveAssignments(_root, asg);

            ProfileManager.Load("srv", "acc", "Bob");

            Assert.Equal("Alice", ProfileManager.ActiveProfileName);
            Assert.Equal(55, ProfileManager.CurrentProfile.ChatHistoryLength); // shared
            Assert.Empty(ProfileManager.CurrentProfile.PinnedContainerSerials); // Alice's pins are not Bob's
            Assert.Equal("Bob", ProfileManager.CurrentProfile.CharacterName);
        }

        [Fact]
        public void Override_WinsOverAssignment()
        {
            ProfileManager.Load("srv", "acc", "Alice"); // creates profile "Alice" + migration marker
            SaveNamed(_root, "Raid", 77);

            ProfileManager.OverrideProfileName = "Raid";
            ProfileManager.Load("srv", "acc", "Alice");

            Assert.Equal("Raid", ProfileManager.ActiveProfileName);
            Assert.Equal(77, ProfileManager.CurrentProfile.ChatHistoryLength);
        }

        [Fact]
        public void BrokenProfile_Load_Throws_AndKeepsActiveProfile()
        {
            ProfileManager.Load("srv", "acc", "Alice");
            Profile active = ProfileManager.CurrentProfile;
            string activePath = ProfileManager.ProfilePath;

            string brokenDir = ProfileCatalog.ProfileDir(_root, "Broken");
            Directory.CreateDirectory(brokenDir);
            File.WriteAllText(Path.Combine(brokenDir, "profile.json"), "{ nope");

            ProfileManager.OverrideProfileName = "Broken";

            Assert.ThrowsAny<Exception>(() => ProfileManager.Load("srv", "acc", "Alice"));
            Assert.Same(active, ProfileManager.CurrentProfile);
            Assert.Equal(activePath, ProfileManager.ProfilePath);
            Assert.Equal("Alice", ProfileManager.ActiveProfileName);
        }

        [Fact]
        public void LegacyCharacter_IsMigratedOnFirstLoad_AndKeepsItsSettings()
        {
            string dir = Path.Combine(_root, "acc", "srv", "Old");
            Directory.CreateDirectory(dir);
            ConfigurationResolver.Save(new Profile { ChatHistoryLength = 99 }, Path.Combine(dir, "profile.json"), ProfileJsonContext.DefaultToUse.Profile);

            ProfileManager.Load("srv", "acc", "Old");

            Assert.Equal("Old", ProfileManager.ActiveProfileName);
            Assert.Equal(99, ProfileManager.CurrentProfile.ChatHistoryLength);
            Assert.Contains("Old", ProfileCatalog.ListProfiles(_root));
        }
    }
}
