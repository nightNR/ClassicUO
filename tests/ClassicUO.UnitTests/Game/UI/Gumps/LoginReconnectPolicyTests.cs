// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps.Login;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI.Gumps
{
    public class LoginReconnectPolicyTests
    {
        [Theory]
        [InlineData(true, "user", "pass", false, true)]
        [InlineData(false, "user", "pass", false, false)]
        [InlineData(true, "", "pass", false, false)]
        [InlineData(true, "user", "", false, false)]
        [InlineData(true, null, "pass", false, false)]
        [InlineData(true, "user", null, false, false)]
        [InlineData(true, "user", "pass", true, false)]
        public void UseReconnectGump_TruthTable(bool autoLogin, string user, string pass, bool force, bool expected)
        {
            Assert.Equal(expected, LoginReconnectPolicy.UseReconnectGump(autoLogin, user, pass, force));
        }

        [Theory]
        [InlineData(0x82, 0, true)]
        [InlineData(0x82, 2, true)]
        [InlineData(0x82, 3, true)]
        [InlineData(0x82, 1, false)]
        [InlineData(0x82, 4, false)]
        [InlineData(0x82, 8, false)]
        [InlineData(0x53, 0, false)]
        [InlineData(0x85, 0, false)]
        public void IsFatalAuthRejection_Classification(byte packetID, byte code, bool expected)
        {
            Assert.Equal(expected, LoginReconnectPolicy.IsFatalAuthRejection(packetID, code));
        }
    }
}
