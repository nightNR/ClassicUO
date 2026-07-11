// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using ClassicUO;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal sealed class AliasEntry
    {
        public uint Serial { get; set; }
        public string Alias { get; set; }
        public bool Global { get; set; }
        public string RealName { get; set; }
    }

    internal sealed class AliasManager
    {
        private readonly World _world;
        private readonly Dictionary<uint, string> _global = new Dictionary<uint, string>();
        private readonly Dictionary<uint, string> _profile = new Dictionary<uint, string>();
        private readonly Dictionary<uint, string> _realNames = new Dictionary<uint, string>();

        public AliasManager(World world) { _world = world; }

        public bool Enabled { get; set; } = true;

        internal string GlobalPathOverride { get; set; }

        private string GlobalPath =>
            GlobalPathOverride ?? Path.Combine(CUOEnviroment.ExecutablePath, "Data", "aliases_global.xml");

        public string GetAlias(uint serial)
        {
            if (_profile.TryGetValue(serial, out string p))
                return p;
            if (_global.TryGetValue(serial, out string g))
                return g;
            return null;
        }

        public bool IsGlobal(uint serial) => !_profile.ContainsKey(serial) && _global.ContainsKey(serial);

        public string Resolve(uint serial, string realName)
        {
            if (!Enabled)
                return realName;

            string alias = GetAlias(serial);
            return string.IsNullOrEmpty(alias) ? realName : alias;
        }

        // For OBJECT/name-reply text that may carry extra decoration (e.g. a
        // "[GUILD]" tag): replace only the real-name substring with the alias,
        // preserving the rest. Falls back to leaving the text untouched when the
        // real name can't be located, so decorations are never clobbered.
        public string ResolveObjectText(uint serial, string text)
        {
            if (!Enabled || string.IsNullOrEmpty(text))
                return text;

            string alias = GetAlias(serial);
            if (string.IsNullOrEmpty(alias))
                return text;

            string real = _realNames.TryGetValue(serial, out var rn) && !string.IsNullOrEmpty(rn)
                ? rn
                : _world?.Mobiles.Get(serial)?.Name;

            if (!string.IsNullOrEmpty(real) && text.Contains(real))
                return text.Replace(real, alias);

            return text;
        }

        public void Set(uint serial, string alias, bool global, string realName = null)
        {
            if (string.IsNullOrEmpty(alias))
            {
                Remove(serial);
                return;
            }

            _global.Remove(serial);
            _profile.Remove(serial);

            if (global)
                _global[serial] = alias;
            else
                _profile[serial] = alias;

            if (realName != null)
                _realNames[serial] = realName;

            Persist();
        }

        public void Remove(uint serial)
        {
            _global.Remove(serial);
            _profile.Remove(serial);
            _realNames.Remove(serial);
            Persist();
        }

        public IReadOnlyList<AliasEntry> Entries
        {
            get
            {
                var list = new List<AliasEntry>(_profile.Count + _global.Count);
                foreach (var kv in _profile)
                    list.Add(new AliasEntry { Serial = kv.Key, Alias = kv.Value, Global = false, RealName = _realNames.TryGetValue(kv.Key, out var rn) ? rn : null });
                foreach (var kv in _global)
                    list.Add(new AliasEntry { Serial = kv.Key, Alias = kv.Value, Global = true, RealName = _realNames.TryGetValue(kv.Key, out var rn) ? rn : null });
                return list;
            }
        }

        public void Initialize()
        {
            _global.Clear();
            _profile.Clear();
            _realNames.Clear();

            ReadGlobal(GlobalPath);

            var profile = ProfileManager.CurrentProfile;
            if (profile != null)
            {
                Enabled = profile.AliasesEnabled;
                if (profile.CharacterAliases != null)
                {
                    foreach (var e in profile.CharacterAliases)
                        if (e != null && !string.IsNullOrEmpty(e.Alias))
                        {
                            _profile[e.Serial] = e.Alias;
                            if (!string.IsNullOrEmpty(e.RealName))
                                _realNames[e.Serial] = e.RealName;
                        }
                }
            }

            // profile wins: never keep the same serial in both stores
            foreach (var serial in new List<uint>(_profile.Keys))
                _global.Remove(serial);
        }

        private void Persist()
        {
            SaveGlobal(GlobalPath);

            var profile = ProfileManager.CurrentProfile;
            if (profile != null)
            {
                var list = new List<AliasEntry>(_profile.Count);
                foreach (var kv in _profile)
                    list.Add(new AliasEntry { Serial = kv.Key, Alias = kv.Value, Global = false, RealName = _realNames.TryGetValue(kv.Key, out var rn) ? rn : null });
                profile.CharacterAliases = list;
            }
        }

        internal void ReadGlobal(string path)
        {
            if (!File.Exists(path))
                return;

            XmlDocument doc = new XmlDocument();
            try { doc.Load(path); }
            catch (System.Exception ex) { Log.Error(ex.ToString()); return; }

            XmlElement root = doc["aliases"];
            if (root == null)
                return;

            foreach (XmlElement xml in root.ChildNodes)
            {
                if (xml.Name != "info")
                    continue;

                string serialText = xml.GetAttribute("serial");
                string alias = xml.GetAttribute("alias");
                string realName = xml.GetAttribute("realname");
                if (uint.TryParse(serialText, out uint serial) && !string.IsNullOrEmpty(alias))
                {
                    _global[serial] = alias;
                    if (!string.IsNullOrEmpty(realName))
                        _realNames[serial] = realName;
                }
            }
        }

        internal void SaveGlobal(string path)
        {
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
                xml.WriteStartElement("aliases");

                foreach (var kv in _global)
                {
                    xml.WriteStartElement("info");
                    xml.WriteAttributeString("serial", kv.Key.ToString());
                    xml.WriteAttributeString("alias", kv.Value);
                    xml.WriteAttributeString("realname", _realNames.TryGetValue(kv.Key, out var rn) ? rn ?? "" : "");
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
                xml.WriteEndDocument();
            }
        }
    }
}
