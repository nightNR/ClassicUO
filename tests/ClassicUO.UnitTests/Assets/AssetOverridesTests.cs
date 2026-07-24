// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class AssetOverridesTests
    {
        [Theory]
        [InlineData("1234", true, 1234u)]
        [InlineData("0", true, 0u)]
        [InlineData("0x04D2", true, 1234u)]
        [InlineData("0X04d2", true, 1234u)]
        [InlineData("", false, 0u)]
        [InlineData("abc", false, 0u)]
        [InlineData("12.3", false, 0u)]
        public void TryParseId_ParsesDecimalAndHex(string stem, bool ok, uint expected)
        {
            bool result = AssetOverrides.TryParseId(stem, out uint id);
            Assert.Equal(ok, result);
            if (ok)
                Assert.Equal(expected, id);
        }
    }
}
