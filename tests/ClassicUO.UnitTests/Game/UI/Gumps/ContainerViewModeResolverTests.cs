// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI.Gumps
{
    public class ContainerViewModeResolverTests
    {
        private const uint Serial = 0x4000_0001;

        [Fact]
        public void Standard_AlwaysFalse()
        {
            Assert.False(ContainerViewModeResolver.Resolve(0, true,
                new Dictionary<uint, bool> { [Serial] = true }, Serial));
        }

        [Fact]
        public void Grid_AlwaysTrue()
        {
            Assert.True(ContainerViewModeResolver.Resolve(1, false,
                new Dictionary<uint, bool> { [Serial] = false }, Serial));
        }

        [Fact]
        public void Toggle_Miss_UsesDefault_False()
        {
            Assert.False(ContainerViewModeResolver.Resolve(2, false,
                new Dictionary<uint, bool>(), Serial));
        }

        [Fact]
        public void Toggle_Miss_UsesDefault_True()
        {
            Assert.True(ContainerViewModeResolver.Resolve(2, true,
                new Dictionary<uint, bool>(), Serial));
        }

        [Fact]
        public void Toggle_Hit_UsesStoredValue_OverDefault()
        {
            Assert.True(ContainerViewModeResolver.Resolve(2, false,
                new Dictionary<uint, bool> { [Serial] = true }, Serial));
            Assert.False(ContainerViewModeResolver.Resolve(2, true,
                new Dictionary<uint, bool> { [Serial] = false }, Serial));
        }

        [Fact]
        public void Resolve_NullDictionary_FallsBackToDefault()
        {
            Assert.True(ContainerViewModeResolver.Resolve(2, true, null, Serial));
            Assert.False(ContainerViewModeResolver.Resolve(2, false, null, Serial));
        }

        [Fact]
        public void ComputeToggleValue_AbsentKey_ReturnsInverseOfDefault()
        {
            Assert.True(ContainerViewModeResolver.ComputeToggleValue(false,
                new Dictionary<uint, bool>(), Serial));
            Assert.False(ContainerViewModeResolver.ComputeToggleValue(true,
                new Dictionary<uint, bool>(), Serial));
        }

        [Fact]
        public void ComputeToggleValue_PresentKey_ReturnsInverseOfStored()
        {
            Assert.False(ContainerViewModeResolver.ComputeToggleValue(false,
                new Dictionary<uint, bool> { [Serial] = true }, Serial));
            Assert.True(ContainerViewModeResolver.ComputeToggleValue(true,
                new Dictionary<uint, bool> { [Serial] = false }, Serial));
        }
    }
}
