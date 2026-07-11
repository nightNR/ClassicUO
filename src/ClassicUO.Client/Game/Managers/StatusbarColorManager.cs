// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal sealed class StatusbarColorRule
    {
        public ushort Graphic;
        public List<ushort> Hues = new List<ushort>();
        public ushort Color;
    }

    internal sealed class StatusbarColorManager
    {
        private readonly World _world;
        private readonly List<StatusbarColorRule> _rules = new List<StatusbarColorRule>();

        public StatusbarColorManager(World world) { _world = world; }

        public bool Enabled { get; set; } = true;

        public List<StatusbarColorRule> Rules => _rules;

        internal string XmlPathOverride { get; set; }

        private string XmlPath =>
            XmlPathOverride ?? Path.Combine(ProfileManager.ProfilePath, "statusbar_colors.xml");

        public void Add(StatusbarColorRule rule) => _rules.Add(rule);
        public void Remove(StatusbarColorRule rule) => _rules.Remove(rule);

        public bool TryGetColor(ushort graphic, ushort hue, out ushort color)
        {
            color = 0;

            if (!Enabled)
                return false;

            for (int i = 0; i < _rules.Count; i++)
            {
                StatusbarColorRule r = _rules[i];
                if (r.Graphic != graphic)
                    continue;
                if (r.Hues.Count == 0 || r.Hues.Contains(hue))
                {
                    color = r.Color;
                    return true;
                }
            }

            return false;
        }

        public void Initialize()
        {
            _rules.Clear();

            Profile profile = ProfileManager.CurrentProfile;
            if (profile != null)
                Enabled = profile.StatusbarColorsEnabled;

            ReadRules(XmlPath);
        }

        public void Save() => SaveRules(XmlPath);

        internal void ReadRules(string path)
        {
            _rules.Clear();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            XmlDocument doc = new XmlDocument();
            try { doc.Load(path); }
            catch (Exception ex) { Log.Error(ex.ToString()); return; }

            XmlElement root = doc["statusbarcolors"];
            if (root == null)
                return;

            foreach (XmlElement xml in root.GetElementsByTagName("rule"))
            {
                if (!TryParseUShort(xml.GetAttribute("graphic"), out ushort graphic))
                    continue;
                TryParseUShort(xml.GetAttribute("color"), out ushort color);

                _rules.Add(new StatusbarColorRule
                {
                    Graphic = graphic,
                    Color = color,
                    Hues = ParseHues(xml.GetAttribute("hues"))
                });
            }
        }

        internal void SaveRules(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (XmlTextWriter xml = new XmlTextWriter(path, Encoding.UTF8)
            {
                Formatting = Formatting.Indented,
                IndentChar = '\t',
                Indentation = 1
            })
            {
                xml.WriteStartDocument(true);
                xml.WriteStartElement("statusbarcolors");

                foreach (StatusbarColorRule r in _rules)
                {
                    xml.WriteStartElement("rule");
                    xml.WriteAttributeString("graphic", r.Graphic.ToString());
                    xml.WriteAttributeString("hues", FormatHues(r.Hues));
                    xml.WriteAttributeString("color", r.Color.ToString());
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
                xml.WriteEndDocument();
            }
        }

        public static List<ushort> ParseHues(string text)
        {
            var list = new List<ushort>();
            if (string.IsNullOrWhiteSpace(text))
                return list;

            foreach (string part in text.Split('|'))
            {
                if (TryParseUShort(part.Trim(), out ushort h))
                    list.Add(h);
            }

            return list;
        }

        public static string FormatHues(IEnumerable<ushort> hues)
        {
            return string.Join("|", hues.Select(h => "0x" + h.ToString("X")));
        }

        public static bool TryParseUShort(string s, out ushort value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ushort.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

            return ushort.TryParse(s, out value);
        }
    }
}
