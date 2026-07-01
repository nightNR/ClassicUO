// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI.Gumps
{
    public class HealthBarFactoryTests
    {
        [Fact]
        public void ShouldUseCustomBar_True_WhenToggleOn()
        {
            var profile = new Profile { CustomBarsToggled = true };
            Assert.True(HealthBarFactory.ShouldUseCustomBar(profile));
        }

        [Fact]
        public void ShouldUseCustomBar_False_WhenToggleOff()
        {
            var profile = new Profile { CustomBarsToggled = false };
            Assert.False(HealthBarFactory.ShouldUseCustomBar(profile));
        }

        [Fact]
        public void ShouldUseCustomBar_False_WhenProfileNull()
        {
            Assert.False(HealthBarFactory.ShouldUseCustomBar(null));
        }
    }
}
