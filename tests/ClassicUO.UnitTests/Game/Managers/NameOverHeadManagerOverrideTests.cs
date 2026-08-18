// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class NameOverHeadManagerOverrideTests
    {
        private static NameOverHeadManager CreateSut() => new NameOverHeadManager(new World());

        [Fact]
        public void TryGetOverride_ReturnsFalse_WhenNoneSet()
        {
            var sut = CreateSut();

            var found = sut.TryGetOverride(0x40000001, out bool visible, out byte opacity);

            Assert.False(found);
            Assert.False(visible);
            Assert.Equal(100, opacity);
        }

        [Fact]
        public void SetOverride_VisibleWithOpacity_IsReadBackByTryGetOverride()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: true, opacityPercent: 40);
            var found = sut.TryGetOverride(0x40000001, out bool visible, out byte opacity);

            Assert.True(found);
            Assert.True(visible);
            Assert.Equal(40, opacity);
        }

        [Fact]
        public void SetOverride_Hidden_IsReadBackByTryGetOverride()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: false, opacityPercent: 100);
            var found = sut.TryGetOverride(0x40000001, out bool visible, out _);

            Assert.True(found);
            Assert.False(visible);
        }

        [Fact]
        public void SetOverride_ClampsOpacityAbove100()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: true, opacityPercent: 255);
            sut.TryGetOverride(0x40000001, out _, out byte opacity);

            Assert.Equal(100, opacity);
        }

        [Fact]
        public void SetOverride_ReplacesPreviousOverrideForSameSerial()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: true, opacityPercent: 40);
            sut.SetOverride(0x40000001, visible: false, opacityPercent: 100);
            sut.TryGetOverride(0x40000001, out bool visible, out _);

            Assert.False(visible);
        }

        [Fact]
        public void ClearOverride_RemovesOnlyThatSerial()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: true, opacityPercent: 40);
            sut.SetOverride(0x40000002, visible: true, opacityPercent: 60);
            sut.ClearOverride(0x40000001);

            Assert.False(sut.TryGetOverride(0x40000001, out _, out _));
            Assert.True(sut.TryGetOverride(0x40000002, out _, out _));
        }

        [Fact]
        public void ClearAllOverrides_RemovesEveryOverride()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: true, opacityPercent: 40);
            sut.SetOverride(0x40000002, visible: false, opacityPercent: 100);
            sut.ClearAllOverrides();

            Assert.False(sut.TryGetOverride(0x40000001, out _, out _));
            Assert.False(sut.TryGetOverride(0x40000002, out _, out _));
        }

        [Fact]
        public void ClearOverride_ResetsForceClosedEntity_FromClosedToNone()
        {
            var world = new World();
            var sut = new NameOverHeadManager(world);
            const uint serial = 0x40000001;
            var item = new Item(world) { Serial = serial, Graphic = 0x1234 };
            world.Items[serial] = item;
            item.ObjectHandlesStatus = ObjectHandlesStatus.CLOSED;

            sut.SetOverride(serial, visible: false, opacityPercent: 0);
            sut.ClearOverride(serial);

            Assert.Equal(ObjectHandlesStatus.NONE, item.ObjectHandlesStatus);
        }

        [Fact]
        public void ClearOverride_LeavesNonClosedEntityStatusAlone()
        {
            var world = new World();
            var sut = new NameOverHeadManager(world);
            const uint serial = 0x40000001;
            var item = new Item(world) { Serial = serial, Graphic = 0x1234 };
            world.Items[serial] = item;
            item.ObjectHandlesStatus = ObjectHandlesStatus.DISPLAYING;

            sut.SetOverride(serial, visible: true, opacityPercent: 50);
            sut.ClearOverride(serial);

            Assert.Equal(ObjectHandlesStatus.DISPLAYING, item.ObjectHandlesStatus);
        }

        [Fact]
        public void ClearAllOverrides_ResetsEveryForceClosedEntity_FromClosedToNone()
        {
            var world = new World();
            var sut = new NameOverHeadManager(world);
            const uint serial1 = 0x40000001;
            const uint serial2 = 0x40000002;
            var item1 = new Item(world) { Serial = serial1, Graphic = 0x1234 };
            var item2 = new Item(world) { Serial = serial2, Graphic = 0x1234 };
            world.Items[serial1] = item1;
            world.Items[serial2] = item2;
            item1.ObjectHandlesStatus = ObjectHandlesStatus.CLOSED;
            item2.ObjectHandlesStatus = ObjectHandlesStatus.CLOSED;

            sut.SetOverride(serial1, visible: false, opacityPercent: 0);
            sut.SetOverride(serial2, visible: true, opacityPercent: 30);
            sut.ClearAllOverrides();

            Assert.Equal(ObjectHandlesStatus.NONE, item1.ObjectHandlesStatus);
            Assert.Equal(ObjectHandlesStatus.NONE, item2.ObjectHandlesStatus);
        }

        [Fact]
        public void ClearOverride_UnknownSerial_DoesNotThrow()
        {
            var world = new World();
            var sut = new NameOverHeadManager(world);

            var exception = Record.Exception(() => sut.ClearOverride(0x40000099));

            Assert.Null(exception);
        }
    }
}
