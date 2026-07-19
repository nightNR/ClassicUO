// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.Game.UI.Gumps.Login
{
    internal static class LoginReconnectPolicy
    {
        public static bool UseReconnectGump(bool autoLogin, string username, string password, bool forceFullLogin)
        {
            return autoLogin
                && !string.IsNullOrEmpty(username)
                && !string.IsNullOrEmpty(password)
                && !forceFullLogin;
        }

        public static bool IsFatalAuthRejection(byte packetID, byte code)
        {
            return packetID == 0x82 && (code == 0 || code == 2 || code == 3);
        }
    }
}
