// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Static client-side targets for the three plugin→cuo secondary-party primitives.
    /// Bound into <c>ClientBindings</c> in PluginHost.cs and called by the host after it
    /// marshals onto the game thread. Set also feeds the world-map entity manager so the
    /// member appears as a radar dot without the server 0xF0 party-position query.
    /// Mirrors <see cref="PluginStatusBars"/> as a static binding target.
    /// </summary>
    internal static class PluginPartyBridge
    {
        public static void SetPluginPartyMember(uint serial, ushort hue, int x, int y, int hp, int map)
        {
            World world = Client.Game?.UO?.World;
            if (world == null)
            {
                return;
            }

            world.PluginParty.Set(serial, hue);

            // Feed the radar/minimap directly; from_packet: true keeps the recv window
            // alive so the dot is not pruned. A plugin-party member is neither a real
            // World.Party.Members[] entry nor a WMapManager guild member of its own
            // accord, so isguild: true is required — the guild-dot draw loop in
            // WorldMapGump (wme.IsGuild && !World.Party.Contains(serial)) is the only
            // render path available to a non-real-party map entity. If the member also
            // happens to be in the real party, that loop's !Contains guard skips it, so
            // there is no double-draw.
            world.WMapManager.AddOrUpdate(serial, x, y, hp, map, true, name: null, from_packet: true);

            // An already-open status bar caches its single-vs-party layout at build time
            // (BaseHealthBarGump keys the multi-bar mana/stam rows on party membership). Joining
            // the plugin party mid-session must rebuild it, exactly as the real PartyManager does
            // on a 0xBF 0x06 party-list packet.
            UIManager.GetGump<BaseHealthBarGump>(serial)?.RequestUpdateContents();
        }

        public static void RemovePluginPartyMember(uint serial)
        {
            World world = Client.Game?.UO?.World;
            if (world == null)
            {
                return;
            }

            world.PluginParty.Remove(serial);
            world.WMapManager.Remove(serial);

            // Leaving the plugin party must rebuild an open bar back to the single-bar layout.
            UIManager.GetGump<BaseHealthBarGump>(serial)?.RequestUpdateContents();
        }

        public static void ClearPluginParty()
        {
            World world = Client.Game?.UO?.World;
            if (world == null)
            {
                return;
            }

            world.PluginParty.Clear();
        }
    }
}
