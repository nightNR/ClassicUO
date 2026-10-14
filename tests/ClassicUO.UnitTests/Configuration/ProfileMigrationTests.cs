// SPDX-License-Identifier: BSD-2-Clause
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests.Configuration
{
    public class ProfileMigrationTests : IDisposable
    {
        private readonly string _parent = Path.Combine(Path.GetTempPath(), "cuo_profmig_" + Guid.NewGuid().ToString("N"));
        private readonly string _root;

        public ProfileMigrationTests()
        {
            _root = Path.Combine(_parent, "Profiles");
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_parent, true); } catch { }
        }

        private string MakeLegacy(string user, string server, string character, Action<Profile> tweak = null, bool withMacros = true)
        {
            string dir = Path.Combine(_root, user, server, character);
            Directory.CreateDirectory(dir);

            var p = new Profile();
            tweak?.Invoke(p);
            ConfigurationResolver.Save(p, Path.Combine(dir, "profile.json"), ProfileJsonContext.DefaultToUse.Profile);

            if (withMacros)
            {
                File.WriteAllText(Path.Combine(dir, "macros.xml"), "<macros>" + character + "</macros>");
            }

            File.WriteAllText(Path.Combine(dir, "gumps.xml"), "<gumps/>");

            return dir;
        }

        [Fact]
        public void EmptyRoot_WritesMarker_NoProfiles()
        {
            Assert.True(ProfileMigration.RunIfNeeded(_root));
            Assert.True(ProfileCatalog.IsMigrated(_root));
            Assert.Empty(ProfileCatalog.ListProfiles(_root));
        }

        [Fact]
        public void Legacy_CharactersBecomeNamedProfiles_WithAssignmentsAndFiles()
        {
            string a = MakeLegacy("acc", "srv", "Alice", p => p.ChatHistoryLength = 33);
            MakeLegacy("acc", "srv", "Bob");

            Assert.True(ProfileMigration.RunIfNeeded(_root));

            Assert.Equal(new[] { "Alice", "Bob" }, ProfileCatalog.ListProfiles(_root));

            Dictionary<string, string> asg = ProfileCatalog.LoadAssignments(_root);
            Assert.Equal("Alice", asg[ProfileCatalog.AssignmentKey("acc", "srv", "Alice")]);
            Assert.Equal("Bob", asg[ProfileCatalog.AssignmentKey("acc", "srv", "Bob")]);

            Profile loaded = ConfigurationResolver.Load(Path.Combine(ProfileCatalog.ProfileDir(_root, "Alice"), "profile.json"), ProfileJsonContext.DefaultToUse.Profile);
            Assert.Equal(33, loaded.ChatHistoryLength);
            Assert.Equal("<macros>Alice</macros>", File.ReadAllText(Path.Combine(ProfileCatalog.ProfileDir(_root, "Alice"), "macros.xml")));

            // originals untouched, gumps.xml stays with the character
            Assert.True(File.Exists(Path.Combine(a, "profile.json")));
            Assert.True(File.Exists(Path.Combine(a, "gumps.xml")));
            Assert.False(File.Exists(Path.Combine(ProfileCatalog.ProfileDir(_root, "Alice"), "gumps.xml")));

            // backup exists
            Assert.NotEmpty(Directory.GetDirectories(_parent, "Profiles_backup_*"));
        }

        [Fact]
        public void Legacy_CharacterBoundData_MovesToCharacterState_AndIsNotInSharedProfile()
        {
            string dir = MakeLegacy("acc", "srv", "Alice", p =>
            {
                p.PinnedContainerSerials = new List<uint> { 1, 2 };
                p.GrabBagSerial = 77;
                p.ContainerGridStates = new Dictionary<uint, bool> { [5] = true };
            });

            // simulate a legacy file that still carries those fields in profile.json
            string json = File.ReadAllText(Path.Combine(dir, "profile.json"));
            Assert.DoesNotContain("pinned_container_serials", json); // new saves never write them
            File.WriteAllText(Path.Combine(dir, "profile.json"),
                json.TrimEnd().TrimEnd('}').TrimEnd() + ",\"pinned_container_serials\":[1,2],\"grab_bag_serial\":77,\"container_grid_states\":{\"5\":true}}");

            Assert.True(ProfileMigration.RunIfNeeded(_root));

            CharacterState state = ConfigurationResolver.Load(Path.Combine(dir, "character_state.json"), ProfileJsonContext.DefaultToUse.CharacterState);
            Assert.Equal(new uint[] { 1, 2 }, state.PinnedContainerSerials);
            Assert.Equal(77u, state.GrabBagSerial);
            Assert.True(state.ContainerGridStates[5]);

            string shared = File.ReadAllText(Path.Combine(ProfileCatalog.ProfileDir(_root, "Alice"), "profile.json"));
            Assert.DoesNotContain("pinned_container_serials", shared);
            Assert.DoesNotContain("grab_bag_serial", shared);
        }

        [Fact]
        public void Legacy_SameCharacterNameOnTwoServers_GetsUniqueProfileNames()
        {
            MakeLegacy("acc", "srvA", "Alice");
            MakeLegacy("acc", "srvB", "Alice");

            Assert.True(ProfileMigration.RunIfNeeded(_root));

            List<string> names = ProfileCatalog.ListProfiles(_root).ToList();
            Assert.Equal(2, names.Count);
            Assert.Contains("Alice", names);
            Assert.Contains("Alice (srvB)", names);
        }

        [Fact]
        public void SecondRun_IsNoOp()
        {
            MakeLegacy("acc", "srv", "Alice");
            Assert.True(ProfileMigration.RunIfNeeded(_root));

            // user edits after migration must survive another start
            string marker = Path.Combine(ProfileCatalog.ProfileDir(_root, "Alice"), "macros.xml");
            File.WriteAllText(marker, "edited");

            Assert.True(ProfileMigration.RunIfNeeded(_root));
            Assert.Equal("edited", File.ReadAllText(marker));
            Assert.Single(Directory.GetDirectories(_parent, "Profiles_backup_*"));
        }

        [Fact]
        public void InterruptedRun_WithoutMarker_IsRepeatedCleanly()
        {
            MakeLegacy("acc", "srv", "Alice");

            // leftovers of an interrupted run
            Directory.CreateDirectory(Path.Combine(ProfileCatalog.NamedRoot(_root), "Stale"));
            Directory.CreateDirectory(Path.Combine(_root, ProfileCatalog.NamedFolder + ".tmp", "Junk"));

            Assert.True(ProfileMigration.RunIfNeeded(_root));
            Assert.Equal(new[] { "Alice" }, ProfileCatalog.ListProfiles(_root));
        }

        [Fact]
        public void CorruptProfile_IsSkipped_OthersMigrate()
        {
            string bad = MakeLegacy("acc", "srv", "Broken");
            File.WriteAllText(Path.Combine(bad, "profile.json"), "{ not json");
            MakeLegacy("acc", "srv", "Good");

            Assert.True(ProfileMigration.RunIfNeeded(_root));
            Assert.Equal(new[] { "Good" }, ProfileCatalog.ListProfiles(_root));
            Assert.False(ProfileCatalog.LoadAssignments(_root).ContainsKey(ProfileCatalog.AssignmentKey("acc", "srv", "Broken")));
        }

        [Fact]
        public void Resolve_PrefersOverride_ThenAssignment_ThenNull()
        {
            MakeLegacy("acc", "srv", "Alice");
            MakeLegacy("acc", "srv", "Bob");
            ProfileMigration.RunIfNeeded(_root);

            Dictionary<string, string> asg = ProfileCatalog.LoadAssignments(_root);

            Assert.Equal(ProfileCatalog.ProfileDir(_root, "Alice"), ProfileCatalog.Resolve(_root, asg, "acc", "srv", "Alice", null, out string n1));
            Assert.Equal("Alice", n1);

            Assert.Equal(ProfileCatalog.ProfileDir(_root, "Bob"), ProfileCatalog.Resolve(_root, asg, "acc", "srv", "Alice", "Bob", out string n2));
            Assert.Equal("Bob", n2);

            // unknown override falls back to the assignment
            Assert.Equal("Alice", ProfileCatalog.Resolve(_root, asg, "acc", "srv", "Alice", "Nope", out string n3) == null ? null : n3);

            Assert.Null(ProfileCatalog.Resolve(_root, asg, "acc", "srv", "Carol", null, out _));
        }

        [Fact]
        public void LegacyProfileJson_LoadsCharacterFieldsThroughShadowProperties()
        {
            string json = "{\"pinned_container_serials\":[9],\"grab_bag_serial\":5}";
            string f = Path.Combine(_parent, "p.json");
            File.WriteAllText(f, json);

            Profile p = ConfigurationResolver.Load(f, ProfileJsonContext.DefaultToUse.Profile);

            Assert.Equal(new uint[] { 9 }, p.PinnedContainerSerials);
            Assert.Equal(5u, p.GrabBagSerial);
        }
    }
}
