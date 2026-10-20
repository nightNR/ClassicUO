// SPDX-License-Identifier: BSD-2-Clause
using System;
using System.IO;
using System.Linq;
using System.Xml;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Configuration
{
    public class GumpScopesTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "cuo_gumpscopes_" + Guid.NewGuid().ToString("N"));

        public GumpScopesTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static string G(GumpType t, uint serial, string extra = "") => $"<gump type=\"{(int) t}\" x=\"1\" y=\"2\" serial=\"{serial}\" {extra}/>";

        [Fact]
        public void IsCharacterBound_ContainersAndOtherMobiles_ButNotOwnGumpsOrLayout()
        {
            Assert.True(GumpScopes.IsCharacterBound(GumpType.Container, 5, 1));
            Assert.True(GumpScopes.IsCharacterBound(GumpType.HealthBar, 99, 1));
            Assert.False(GumpScopes.IsCharacterBound(GumpType.HealthBar, 1, 1));
            Assert.False(GumpScopes.IsCharacterBound(GumpType.Journal, 0, 1));
            Assert.True(GumpScopes.IsSelf(GumpType.PaperDoll, 1, 1));
            Assert.False(GumpScopes.IsSelf(GumpType.Container, 1, 1));
            Assert.False(GumpScopes.IsSelf(GumpType.PaperDoll, 1, 0));
        }

        [Fact]
        public void SplitLayout_KeepsOnlyNonEntityBoundElements_AndLeavesSourceUntouched()
        {
            string src = Path.Combine(_dir, "gumps.xml");
            string dst = Path.Combine(_dir, "layout.xml");
            string xml = $"<gumps save_w=\"800\" save_h=\"600\">{G(GumpType.Journal, 0)}{G(GumpType.Container, 7)}{G(GumpType.HealthBar, 9)}" +
                         $"<anchored_group_gump matrix_w=\"2\" matrix_h=\"1\">{G(GumpType.SpellButton, 0)}</anchored_group_gump>" +
                         $"<anchored_group_gump matrix_w=\"2\" matrix_h=\"1\">{G(GumpType.HealthBar, 9)}</anchored_group_gump></gumps>";
            File.WriteAllText(src, xml);

            Assert.True(GumpScopes.SplitLayout(src, dst));

            var doc = new XmlDocument();
            doc.Load(dst);
            XmlElement root = doc["gumps"];
            Assert.Equal("800", root.GetAttribute("save_w"));
            Assert.Single(root.GetElementsByTagName("anchored_group_gump").Cast<XmlElement>());
            Assert.Equal(2, root.ChildNodes.Count); // journal + spell-button group
            Assert.Equal(xml, File.ReadAllText(src));
        }

        [Fact]
        public void ElementIsEntityBound_SelfMembersDoNotCount()
        {
            var doc = new XmlDocument();
            doc.LoadXml($"<gumps><anchored_group_gump>{G(GumpType.HealthBar, 1, "self=\"1\"")}</anchored_group_gump>{G(GumpType.Container, 3)}</gumps>");

            Assert.False(GumpScopes.ElementIsEntityBound((XmlElement) doc["gumps"].FirstChild));
            Assert.True(GumpScopes.ElementIsEntityBound((XmlElement) doc["gumps"].LastChild));
        }

        [Fact]
        public void Duplicate_CopiesLayoutGumps()
        {
            string root = Path.Combine(_dir, "Profiles");
            string main = ProfileCatalog.ProfileDir(root, "Main");
            Directory.CreateDirectory(main);
            File.WriteAllText(Path.Combine(main, "gumps.xml"), "<gumps/>");

            Assert.True(ProfileCatalog.Duplicate(root, "Main", "Copy"));
            Assert.True(File.Exists(Path.Combine(ProfileCatalog.ProfileDir(root, "Copy"), "gumps.xml")));
        }
    }
}
