// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Plugin-driven "secondary party": a serial→hue set independent of the real
    /// <see cref="PartyManager"/>. Membership policy lives in the Phoenix plugin; the
    /// client only stores it and OR's it into the native party render sites. Coexists
    /// with the real server party (never written from party packets). No size cap — the
    /// relay roster may exceed the real party's 10.
    /// </summary>
    internal sealed class PluginPartyManager
    {
        private readonly Dictionary<uint, ushort> _members = new Dictionary<uint, ushort>();

        public void Set(uint serial, ushort hue) => _members[serial] = hue;

        public void Remove(uint serial) => _members.Remove(serial);

        public void Clear() => _members.Clear();

        public bool Contains(uint serial) => _members.ContainsKey(serial);

        public bool TryGetHue(uint serial, out ushort hue) => _members.TryGetValue(serial, out hue);
    }
}
