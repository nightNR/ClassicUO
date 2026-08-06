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
    }
}
