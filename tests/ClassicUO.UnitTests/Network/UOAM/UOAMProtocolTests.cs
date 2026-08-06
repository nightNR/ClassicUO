// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Buffers.Binary;
using System.Text;
using ClassicUO.Network.UOAM;
using Xunit;

namespace ClassicUO.UnitTests.Network.UOAM
{
    public class UOAMProtocolTests
    {
        private static uint ReadU32(byte[] data, int offset) =>
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

        private static ushort ReadU16(byte[] data, int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

        [Fact]
        public void BuildHandshake_Produces24ByteHandshakePacket()
        {
            byte[] packet = UOAMProtocol.BuildHandshake(7);

            Assert.Equal(24, packet.Length);
            Assert.Equal(0x05, packet[0]);
            Assert.Equal(0x00, packet[1]);
            Assert.Equal(0x0b, packet[2]);
            Assert.Equal(0x03, packet[3]);
            Assert.Equal(0x10, packet[4]);
            Assert.Equal(24u, ReadU32(packet, 8));
            Assert.Equal(7u, ReadU32(packet, 12));
            Assert.Equal(0u, ReadU32(packet, 20)); // masterId = 0 (primary connection)
        }

        [Fact]
        public void BuildPositionUpdate_Produces140BytePacket_WithCorrectSubtypeAndPassword()
        {
            byte[] packet = UOAMProtocol.BuildPositionUpdate(1, "secret", "Bazinka", 100, 200, 0, 1);

            Assert.Equal(140, packet.Length);
            Assert.Equal(0x00, packet[2]);
            Assert.Equal(140u, ReadU32(packet, 8));
            Assert.Equal((ushort)0x0000, ReadU16(packet, 20)); // major
            Assert.Equal((ushort)0x0010, ReadU16(packet, 22)); // minor

            string password = Encoding.ASCII.GetString(packet, 24, 20).TrimEnd('\0');
            Assert.Equal("secret", password);

            // ip[4] at offset 44..47 is zero, noip block starts at 48
            Assert.Equal(0, packet[44]);
            Assert.Equal(0, packet[45]);
            Assert.Equal(0, packet[46]);
            Assert.Equal(0, packet[47]);

            bool ok = UOAMPlayerInfoCodec.TryDecode(packet.AsSpan(48, 92), out UOAMPlayerInfo info);
            Assert.True(ok);
            Assert.Equal("Bazinka", info.Name);
            Assert.Equal(100, info.X);
            Assert.Equal(200, info.Y);
        }

        [Fact]
        public void BuildQueryListRequest_Produces44BytePacket_WithMinorTwo()
        {
            byte[] packet = UOAMProtocol.BuildQueryListRequest(2, "pw");

            Assert.Equal(44, packet.Length);
            Assert.Equal((ushort)0x0000, ReadU16(packet, 20));
            Assert.Equal((ushort)0x0002, ReadU16(packet, 22));
        }

        [Fact]
        public void BuildChatPollRequest_Produces44BytePacket_WithMajorMinorOne()
        {
            byte[] packet = UOAMProtocol.BuildChatPollRequest(3, "pw");

            Assert.Equal(44, packet.Length);
            Assert.Equal((ushort)0x0001, ReadU16(packet, 20));
            Assert.Equal((ushort)0x0001, ReadU16(packet, 22));
        }

        [Fact]
        public void BuildChatSendRequest_EncodesSubtypeAndNulTerminatedText()
        {
            byte[] packet = UOAMProtocol.BuildChatSendRequest(4, "pw", "hello");

            Assert.Equal((ushort)0x0001, ReadU16(packet, 20)); // major
            Assert.Equal((ushort)0x0000, ReadU16(packet, 22)); // minor
            Assert.Equal(0x01, packet[52]); // chat sub-sub-type: text
            Assert.Equal("hello", Encoding.ASCII.GetString(packet, 53, 5));
            Assert.Equal(0, packet[53 + 5]); // NUL terminator
            Assert.Equal((uint)packet.Length, ReadU32(packet, 8));
        }

        [Fact]
        public void TryReadFrameLength_ReadsLengthField()
        {
            byte[] packet = UOAMProtocol.BuildQueryListRequest(1, "pw");

            bool ok = UOAMProtocol.TryReadFrameLength(packet, out uint length);

            Assert.True(ok);
            Assert.Equal(44u, length);
        }

        [Fact]
        public void TryReadFrameLength_ReturnsFalse_WhenBufferTooShort()
        {
            bool ok = UOAMProtocol.TryReadFrameLength(new byte[11], out _);

            Assert.False(ok);
        }
    }
}
