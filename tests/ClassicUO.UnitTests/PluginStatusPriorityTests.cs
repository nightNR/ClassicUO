// SPDX-License-Identifier: BSD-2-Clause
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class PluginStatusPriorityTests
    {
        [Fact]
        public void Store_DefaultsToZero_AndResetsByZero()
        {
            PluginStatusPriorities.Reset();
            Assert.Equal(0, PluginStatusPriorities.Get(0x1234));
            PluginStatusPriorities.Set(0x1234, 5);
            Assert.Equal(5, PluginStatusPriorities.Get(0x1234));
            PluginStatusPriorities.Set(0x1234, 0); // reset removes entry
            Assert.Equal(0, PluginStatusPriorities.Get(0x1234));
            PluginStatusPriorities.Clear(0x1234);
            Assert.Equal(0, PluginStatusPriorities.Get(0x1234));
        }

        [Theory]
        // higher priority first; ties keep original (insertion) order
        [InlineData(new[] { 0, 0, 0 }, new[] { 0, 1, 2 })]                 // all equal -> identity
        [InlineData(new[] { 1, 5, 3 }, new[] { 1, 2, 0 })]                 // 5,3,1
        [InlineData(new[] { 5, 5, 1 }, new[] { 0, 1, 2 })]                 // tie 5,5 keeps 0<1
        [InlineData(new[] { -1, 0, -1 }, new[] { 1, 0, 2 })]               // 0 above -1; tie keeps 0<2
        public void OrderByPriority_SortsDescStableTiebreak(int[] priorities, int[] expected)
        {
            Assert.Equal(expected, PluginStatusBars.OrderByPriority(priorities));
        }
    }
}
