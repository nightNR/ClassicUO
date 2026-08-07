// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using ClassicUO.Network.UOAM;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class UOAMPlayerManagerTests
    {
        private static UOAMPlayerInfo MakePlayer(string name, int x, int y, int map = 1) =>
            new UOAMPlayerInfo { Name = name, X = x, Y = y, Z = 0, Map = map };

        [Fact]
        public void UpdateFromList_PopulatesPlayersByName()
        {
            var manager = new UOAMPlayerManager();

            manager.UpdateFromList(new[] { MakePlayer("Alice", 1, 2), MakePlayer("Bob", 3, 4) });

            Assert.Equal(2, manager.Players.Count);
            Assert.Equal(1, manager.Players["Alice"].X);
            Assert.Equal(3, manager.Players["Bob"].X);
        }

        [Fact]
        public void UpdateFromList_ReplacesPreviousSet()
        {
            var manager = new UOAMPlayerManager();
            manager.UpdateFromList(new[] { MakePlayer("Alice", 1, 2) });

            manager.UpdateFromList(new[] { MakePlayer("Bob", 3, 4) });

            Assert.Single(manager.Players);
            Assert.False(manager.Players.ContainsKey("Alice"));
            Assert.True(manager.Players.ContainsKey("Bob"));
        }

        [Fact]
        public void Clear_EmptiesPlayers()
        {
            var manager = new UOAMPlayerManager();
            manager.UpdateFromList(new[] { MakePlayer("Alice", 1, 2) });

            manager.Clear();

            Assert.Empty(manager.Players);
        }

        [Fact]
        public void IsDuplicateOfPartyOrGuild_ReturnsTrue_WhenNameAndPositionMatch()
        {
            var wmap = new WorldMapEntityManager(null);
            wmap.Entities[1] = new WMapEntity(1) { Name = "Alice", X = 100, Y = 200, Map = 1 };

            bool result = UOAMPlayerManager.IsDuplicateOfPartyOrGuild(
                MakePlayer("Alice", 102, 199), aliasedName: null, wmap, toleranceTiles: 5);

            Assert.True(result);
        }

        [Fact]
        public void IsDuplicateOfPartyOrGuild_ReturnsFalse_WhenPositionOutsideTolerance()
        {
            var wmap = new WorldMapEntityManager(null);
            wmap.Entities[1] = new WMapEntity(1) { Name = "Alice", X = 100, Y = 200, Map = 1 };

            bool result = UOAMPlayerManager.IsDuplicateOfPartyOrGuild(
                MakePlayer("Alice", 200, 200), aliasedName: null, wmap, toleranceTiles: 5);

            Assert.False(result);
        }

        [Fact]
        public void IsDuplicateOfPartyOrGuild_ReturnsFalse_WhenNoNameMatch()
        {
            var wmap = new WorldMapEntityManager(null);
            wmap.Entities[1] = new WMapEntity(1) { Name = "Alice", X = 100, Y = 200, Map = 1 };

            bool result = UOAMPlayerManager.IsDuplicateOfPartyOrGuild(
                MakePlayer("Charlie", 100, 200), aliasedName: null, wmap, toleranceTiles: 5);

            Assert.False(result);
        }

        [Fact]
        public void IsDuplicateOfPartyOrGuild_UsesAliasedName_WhenProvided()
        {
            var wmap = new WorldMapEntityManager(null);
            wmap.Entities[1] = new WMapEntity(1) { Name = "RealCharName", X = 100, Y = 200, Map = 1 };

            bool result = UOAMPlayerManager.IsDuplicateOfPartyOrGuild(
                MakePlayer("UOAMNick", 100, 200), aliasedName: "RealCharName", wmap, toleranceTiles: 5);

            Assert.True(result);
        }

        [Fact]
        public void IsDuplicateOfPartyOrGuild_IsCaseInsensitive()
        {
            var wmap = new WorldMapEntityManager(null);
            wmap.Entities[1] = new WMapEntity(1) { Name = "Alice", X = 100, Y = 200, Map = 1 };

            bool result = UOAMPlayerManager.IsDuplicateOfPartyOrGuild(
                MakePlayer("alice", 100, 200), aliasedName: null, wmap, toleranceTiles: 5);

            Assert.True(result);
        }

        [Fact]
        public void IsSelf_ReturnsTrue_WhenAliasedNameMatchesLocalPlayerAndPositionCloseEnough()
        {
            bool result = UOAMPlayerManager.IsSelf(
                MakePlayer("UOAMNick", 102, 199), aliasedName: "MyCharName", localPlayerName: "MyCharName", localPlayerX: 100, localPlayerY: 200, toleranceTiles: 5);

            Assert.True(result);
        }

        [Fact]
        public void IsSelf_ReturnsTrue_WhenRawNameMatchesLocalPlayer_NoAlias()
        {
            bool result = UOAMPlayerManager.IsSelf(
                MakePlayer("MyCharName", 100, 200), aliasedName: null, localPlayerName: "MyCharName", localPlayerX: 100, localPlayerY: 200, toleranceTiles: 5);

            Assert.True(result);
        }

        [Fact]
        public void IsSelf_ReturnsFalse_WhenNameDoesNotMatch()
        {
            bool result = UOAMPlayerManager.IsSelf(
                MakePlayer("SomeoneElse", 100, 200), aliasedName: null, localPlayerName: "MyCharName", localPlayerX: 100, localPlayerY: 200, toleranceTiles: 5);

            Assert.False(result);
        }

        [Fact]
        public void IsSelf_ReturnsFalse_WhenPositionOutsideTolerance()
        {
            bool result = UOAMPlayerManager.IsSelf(
                MakePlayer("MyCharName", 300, 400), aliasedName: null, localPlayerName: "MyCharName", localPlayerX: 100, localPlayerY: 200, toleranceTiles: 5);

            Assert.False(result);
        }
    }
}
