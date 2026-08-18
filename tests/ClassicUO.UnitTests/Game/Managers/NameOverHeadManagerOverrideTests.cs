// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
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
    }
}
