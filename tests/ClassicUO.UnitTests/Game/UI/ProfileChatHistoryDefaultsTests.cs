// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class ProfileChatHistoryDefaultsTests
    {
        [Fact]
        public void NewProfile_HasChatHistoryDefaults()
        {
            var p = new Profile();

            Assert.True(p.ChatUseArrowsForHistory);
            Assert.Equal(20, p.ChatHistoryLength);
            Assert.NotNull(p.ChatHistory);
            Assert.Empty(p.ChatHistory);
        }
    }
}
