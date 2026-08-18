// SPDX-License-Identifier: BSD-2-Clause

using System.Reflection;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class NameOverHeadManagerIsAllowedTests
    {
        private static void SetCurrentProfile(Profile profile)
        {
            typeof(ProfileManager)
                .GetProperty("CurrentProfile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, profile);
        }

        private static (World world, NameOverHeadManager sut) CreateWorld(NameOverheadTypeAllowed typeAllowed)
        {
            SetCurrentProfile(new Profile { NameOverheadTypeAllowed = typeAllowed });

            var world = new World();

            return (world, new NameOverHeadManager(world));
        }

        private static Item CreateCorpse(World world, uint serial)
        {
            return new Item(world) { Serial = serial, Graphic = 0x2006 };
        }

        [Fact]
        public void OwnCorpse_Shown_WhenOnlyMyCorpseFlagSet()
        {
            var (world, sut) = CreateWorld(NameOverheadTypeAllowed.MyCorpse);
            var corpse = CreateCorpse(world, 0x40000001);
            world.CorpseManager.OwnCorpses.Add(corpse.Serial);

            Assert.True(sut.IsAllowed(corpse));
        }

        [Fact]
        public void OwnCorpse_Shown_WhenOnlyCorpsesFlagSet()
        {
            var (world, sut) = CreateWorld(NameOverheadTypeAllowed.Corpses);
            var corpse = CreateCorpse(world, 0x40000001);
            world.CorpseManager.OwnCorpses.Add(corpse.Serial);

            Assert.True(sut.IsAllowed(corpse));
        }

        [Fact]
        public void OwnCorpse_Hidden_WhenNeitherFlagSet()
        {
            var (world, sut) = CreateWorld(NameOverheadTypeAllowed.Items);
            var corpse = CreateCorpse(world, 0x40000001);
            world.CorpseManager.OwnCorpses.Add(corpse.Serial);

            Assert.False(sut.IsAllowed(corpse));
        }

        [Fact]
        public void StrangerCorpse_NotShown_ByMyCorpseFlagAlone()
        {
            var (world, sut) = CreateWorld(NameOverheadTypeAllowed.MyCorpse);
            var corpse = CreateCorpse(world, 0x40000002);
            // Note: not added to world.CorpseManager.OwnCorpses.

            Assert.False(sut.IsAllowed(corpse));
        }

        [Fact]
        public void All_Flag_IncludesMyCorpse()
        {
            Assert.True(NameOverheadTypeAllowed.All.HasFlag(NameOverheadTypeAllowed.MyCorpse));
        }
    }
}
