// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Network.UOAM;

namespace ClassicUO.Game.Managers
{
    internal sealed class UOAMPlayerManager
    {
        private readonly Dictionary<string, UOAMPlayerInfo> _players = new Dictionary<string, UOAMPlayerInfo>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, UOAMPlayerInfo> Players => _players;

        public void UpdateFromList(IEnumerable<UOAMPlayerInfo> players)
        {
            _players.Clear();

            foreach (UOAMPlayerInfo p in players)
            {
                if (!string.IsNullOrEmpty(p.Name))
                {
                    _players[p.Name] = p;
                }
            }
        }

        public void Clear()
        {
            _players.Clear();
        }

        public static bool IsDuplicateOfPartyOrGuild(UOAMPlayerInfo uoamPlayer, string aliasedName, WorldMapEntityManager wmapManager, int toleranceTiles)
        {
            string nameToMatch = string.IsNullOrEmpty(aliasedName) ? uoamPlayer.Name : aliasedName;

            if (string.IsNullOrEmpty(nameToMatch) || wmapManager == null)
            {
                return false;
            }

            foreach (WMapEntity entity in wmapManager.Entities.Values)
            {
                if (string.IsNullOrEmpty(entity.Name))
                {
                    continue;
                }

                if (!string.Equals(entity.Name, nameToMatch, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Math.Abs(entity.X - uoamPlayer.X) <= toleranceTiles && Math.Abs(entity.Y - uoamPlayer.Y) <= toleranceTiles)
                {
                    return true;
                }
            }

            return false;
        }

        // The local player is never a WMapEntity (those only hold party/guild members),
        // so IsDuplicateOfPartyOrGuild alone never suppresses a UOAM marker for "myself" -
        // e.g. when the UOAM login name differs from the character name and an alias maps
        // one to the other, seeing your own dot drawn a second time via UOAM. Check the
        // local player explicitly instead of folding it into the party/guild check, since
        // it isn't one.
        public static bool IsSelf(UOAMPlayerInfo uoamPlayer, string aliasedName, string localPlayerName, int localPlayerX, int localPlayerY, int toleranceTiles)
        {
            string nameToMatch = string.IsNullOrEmpty(aliasedName) ? uoamPlayer.Name : aliasedName;

            if (string.IsNullOrEmpty(nameToMatch) || string.IsNullOrEmpty(localPlayerName))
            {
                return false;
            }

            if (!string.Equals(localPlayerName, nameToMatch, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return Math.Abs(localPlayerX - uoamPlayer.X) <= toleranceTiles && Math.Abs(localPlayerY - uoamPlayer.Y) <= toleranceTiles;
        }
    }
}
