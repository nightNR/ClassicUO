// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Game.Managers;
using ClassicUO.IO;
using ClassicUO.Network;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Network
{
    public class TargetCursorPacketParsingTests
    {
        [Fact]
        public void ReadPluginHoverAcceptedTypes_ReturnsAll_ForRealServerCursorTarget()
        {
            Span<byte> data = new byte[] { 0x00 };
            var reader = new StackDataReader(data);

            HighlightObjectTypes result = PacketHandlers.ReadPluginHoverAcceptedTypes(CursorTarget.Object, ref reader);

            Assert.Equal(HighlightObjectTypes.All, result);
            Assert.Equal(1, reader.Remaining);

            reader.Release();
        }

        [Fact]
        public void ReadPluginHoverAcceptedTypes_ReadsMaskByte_ForPluginHoverTarget()
        {
            Span<byte> data = new byte[] { (byte)(HighlightObjectTypes.Mobile | HighlightObjectTypes.Item) };
            var reader = new StackDataReader(data);

            HighlightObjectTypes result = PacketHandlers.ReadPluginHoverAcceptedTypes(CursorTarget.PluginHoverTarget, ref reader);

            Assert.Equal(HighlightObjectTypes.Mobile | HighlightObjectTypes.Item, result);
            Assert.Equal(0, reader.Remaining);

            reader.Release();
        }
    }
}
