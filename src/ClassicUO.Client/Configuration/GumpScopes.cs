// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Linq;
using System.Xml;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Configuration
{
    /// <summary>
    /// Decides where a saved gump lives. Layout gumps (buttons, journal, map, bars, ...) belong to the shared profile
    /// (Profile/gumps.xml). Gumps bound to a game entity by serial (containers, spellbooks, other mobiles' health
    /// bars/paperdolls/status) belong to the character (Character/gumps.xml). The player's own paperdoll/status/health
    /// bar are layout too, but their serial differs per character, so they are stored with self="1".
    /// </summary>
    internal static class GumpScopes
    {
        public const string SelfAttribute = "self";

        public static bool IsEntityBound(GumpType type)
        {
            return type is GumpType.Container or GumpType.SpellBook or GumpType.PaperDoll or GumpType.HealthBar or GumpType.StatusGump;
        }

        /// <summary>The player's own gump of an entity-bound type: layout, stored in the profile with self="1".</summary>
        public static bool IsSelf(GumpType type, uint serial, uint playerSerial)
        {
            return playerSerial != 0 && serial == playerSerial && type is GumpType.PaperDoll or GumpType.HealthBar or GumpType.StatusGump;
        }

        public static bool IsCharacterBound(GumpType type, uint serial, uint playerSerial)
        {
            return IsEntityBound(type) && !IsSelf(type, serial, playerSerial);
        }

        private static bool TryType(XmlElement gump, out GumpType type)
        {
            type = GumpType.None;

            if (!int.TryParse(gump.GetAttribute("type"), out int t) || !Enum.IsDefined(typeof(GumpType), t))
            {
                return false;
            }

            type = (GumpType) t;

            return true;
        }

        /// <summary>True when a saved element (gump or anchored group) is serial-bound, i.e. belongs to the character file.</summary>
        public static bool ElementIsEntityBound(XmlElement element)
        {
            if (element.Name == "anchored_group_gump")
            {
                return element.GetElementsByTagName("gump").Cast<XmlElement>().Any(g => g.GetAttribute(SelfAttribute) != "1" && TryType(g, out GumpType t) && IsEntityBound(t));
            }

            return element.Name == "gump" && element.GetAttribute(SelfAttribute) != "1" && TryType(element, out GumpType type) && IsEntityBound(type);
        }

        /// <summary>
        /// Splits a legacy all-in-one gumps.xml: everything that is not serial-bound goes to the profile layout file.
        /// The source is not modified (the character file keeps being read with a filter, see ElementIsEntityBound).
        /// </summary>
        public static bool SplitLayout(string legacyGumpsXml, string profileGumpsXml)
        {
            var doc = new XmlDocument();
            doc.Load(legacyGumpsXml);

            XmlElement root = doc["gumps"];

            if (root == null)
            {
                return false;
            }

            var layout = new XmlDocument();
            XmlElement newRoot = layout.CreateElement("gumps");

            foreach (XmlAttribute a in root.Attributes)
            {
                newRoot.SetAttribute(a.Name, a.Value);
            }

            layout.AppendChild(newRoot);

            foreach (XmlElement child in root.ChildNodes.OfType<XmlElement>())
            {
                if ((child.Name == "gump" || child.Name == "anchored_group_gump") && !ElementIsEntityBound(child))
                {
                    newRoot.AppendChild(layout.ImportNode(child, true));
                }
            }

            layout.Save(profileGumpsXml);

            return true;
        }
    }
}
