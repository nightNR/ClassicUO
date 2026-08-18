// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class CorpseManagerOwnCorpsesTests
    {
        [Fact]
        public void OwnCorpses_StartsEmpty_AndIsMutable()
        {
            var world = new World();
            var sut = new CorpseManager(world);

            Assert.Empty(sut.OwnCorpses);

            sut.OwnCorpses.Add(0x40000001);

            Assert.Single(sut.OwnCorpses);
            Assert.Contains(0x40000001u, sut.OwnCorpses);
        }

        [Fact]
        public void Clear_EmptiesOwnCorpses()
        {
            var world = new World();
            var sut = new CorpseManager(world);
            sut.OwnCorpses.Add(0x40000001);

            sut.Clear();

            Assert.Empty(sut.OwnCorpses);
        }
    }
}
