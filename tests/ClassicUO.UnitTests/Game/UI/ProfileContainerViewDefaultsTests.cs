// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class ProfileContainerViewDefaultsTests
    {
        [Fact]
        public void NewProfile_HasContainerViewDefaults()
        {
            var p = new Profile();

            Assert.Equal(0, p.ContainerViewMode);
            Assert.False(p.ContainerToggleDefaultGrid);
            Assert.NotNull(p.ContainerGridStates);
            Assert.Empty(p.ContainerGridStates);
        }
    }
}
