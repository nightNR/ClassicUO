// SPDX-License-Identifier: BSD-2-Clause
using System;
using System.IO;
using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests.Configuration
{
    public class ProfileCatalogTests : IDisposable
    {
        private readonly string _parent = Path.Combine(Path.GetTempPath(), "cuo_profcat_" + Guid.NewGuid().ToString("N"));
        private readonly string _root;

        public ProfileCatalogTests()
        {
            _root = Path.Combine(_parent, "Profiles");
            Directory.CreateDirectory(ProfileCatalog.ProfileDir(_root, "Main"));
            File.WriteAllText(Path.Combine(ProfileCatalog.ProfileDir(_root, "Main"), "macros.xml"), "m");
            File.WriteAllText(Path.Combine(ProfileCatalog.ProfileDir(_root, "Main"), "unrelated.txt"), "x");
        }

        public void Dispose()
        {
            try { Directory.Delete(_parent, true); } catch { }
        }

        private void Assign(string character, string profile)
        {
            var a = ProfileCatalog.LoadAssignments(_root);
            a[ProfileCatalog.AssignmentKey("acc", "srv", character)] = profile;
            ProfileCatalog.SaveAssignments(_root, a);
        }

        [Fact]
        public void Duplicate_CopiesProfileFilesOnly_AndRefusesExistingTarget()
        {
            Assert.True(ProfileCatalog.Duplicate(_root, "Main", "Copy"));
            Assert.Equal("m", File.ReadAllText(Path.Combine(ProfileCatalog.ProfileDir(_root, "Copy"), "macros.xml")));
            Assert.False(File.Exists(Path.Combine(ProfileCatalog.ProfileDir(_root, "Copy"), "unrelated.txt")));
            Assert.False(ProfileCatalog.Duplicate(_root, "Main", "copy")); // case-insensitive clash
            Assert.False(ProfileCatalog.Duplicate(_root, "Missing", "X"));
        }

        [Fact]
        public void Rename_MovesFolder_AndRepointsAssignments()
        {
            Assign("Alice", "Main");
            Assign("Bob", "Other");

            Assert.True(ProfileCatalog.Rename(_root, "Main", "Raid"));

            Assert.False(ProfileCatalog.Exists(_root, "Main"));
            Assert.True(ProfileCatalog.Exists(_root, "Raid"));
            var a = ProfileCatalog.LoadAssignments(_root);
            Assert.Equal("Raid", a[ProfileCatalog.AssignmentKey("acc", "srv", "Alice")]);
            Assert.Equal("Other", a[ProfileCatalog.AssignmentKey("acc", "srv", "Bob")]);
        }

        [Fact]
        public void Rename_CaseOnly_Works_AndTakenNameIsRefused()
        {
            Assert.True(ProfileCatalog.Rename(_root, "Main", "MAIN"));
            Assert.Contains("MAIN", ProfileCatalog.ListProfiles(_root));

            Assert.True(ProfileCatalog.Duplicate(_root, "MAIN", "Second"));
            Assert.False(ProfileCatalog.Rename(_root, "Second", "main"));
        }

        [Fact]
        public void Delete_RefusesAssignedProfile_DeletesUnassigned()
        {
            Assign("Alice", "Main");
            Assert.False(ProfileCatalog.Delete(_root, "Main"));
            Assert.Equal(1, ProfileCatalog.AssignedCount(_root, "Main"));
            Assert.Single(ProfileCatalog.AssignedCharacters(_root, "Main"));

            Assert.True(ProfileCatalog.Duplicate(_root, "Main", "Spare"));
            Assert.True(ProfileCatalog.Delete(_root, "Spare"));
            Assert.False(ProfileCatalog.Exists(_root, "Spare"));
        }
    }
}
