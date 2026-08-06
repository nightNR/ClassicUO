// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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

        [Fact]
        public void Classify_RecognizesHandshakeAck()
        {
            byte[] data = new byte[16];
            data[2] = 0x0c;

            Assert.Equal(UOAMResponseKind.HandshakeAck, UOAMProtocol.Classify(data));
        }

        [Fact]
        public void Classify_RecognizesAck()
        {
            byte[] data = BuildResponseHeader(offset16: 0x00000004, offset20: 0x00000000, extraLength: 8);

            Assert.Equal(UOAMResponseKind.Ack, UOAMProtocol.Classify(data));
        }

        [Fact]
        public void Classify_RecognizesNoChat()
        {
            byte[] data = BuildResponseHeader(offset16: 0x00000014, offset20: 0x00000001, extraLength: 8);

            Assert.Equal(UOAMResponseKind.NoChat, UOAMProtocol.Classify(data));
        }

        [Fact]
        public void Classify_RecognizesChatData()
        {
            byte[] data = BuildResponseHeader(offset16: 0xFFFFFFFF, offset20: 0x00000001, extraLength: 8);

            Assert.Equal(UOAMResponseKind.ChatData, UOAMProtocol.Classify(data));
        }

        [Fact]
        public void Classify_RecognizesPlayerList()
        {
            byte[] data = BuildResponseHeader(offset16: 0xFFFFFFFF, offset20: 0x00000000, extraLength: 8);

            Assert.Equal(UOAMResponseKind.PlayerList, UOAMProtocol.Classify(data));
        }

        [Fact]
        public void TryParsePlayerList_ParsesTwoPlayers()
        {
            var p1 = new byte[92];
            UOAMPlayerInfoCodec.Encode(p1, "Alice", 10, 20, 0, 1);
            var p2 = new byte[92];
            UOAMPlayerInfoCodec.Encode(p2, "Bob", 30, 40, 0, 1);

            byte[] data = new byte[36 + 92 + 92 + 4];
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16, 4), 0xFFFFFFFF);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), 0x00000000);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24, 4), 2); // player count
            p1.CopyTo(data, 36);
            p2.CopyTo(data, 36 + 92);

            var results = new List<UOAMPlayerInfo>();
            bool ok = UOAMProtocol.TryParsePlayerList(data, results);

            Assert.True(ok);
            Assert.Equal(2, results.Count);
            Assert.Equal("Alice", results[0].Name);
            Assert.Equal("Bob", results[1].Name);
        }

        [Fact]
        public void TryParseChatData_ParsesTextMessage()
        {
            byte[] textBytes = Encoding.ASCII.GetBytes("hi there");
            byte[] data = new byte[24 + 1 + textBytes.Length + 1 + 5]; // header + subtype + text + NUL + 5 trailer
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16, 4), 0xFFFFFFFF);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), 0x00000001);
            data[24] = 0x01; // text subtype
            textBytes.CopyTo(data, 25);

            bool ok = UOAMProtocol.TryParseChatData(data, out byte subtype, out string text);

            Assert.True(ok);
            Assert.Equal(0x01, subtype);
            Assert.Equal("hi there", text);
        }

        [Fact]
        public void TryParseChatData_ReturnsNullText_ForFontColorSubtype()
        {
            byte[] data = new byte[24 + 1 + 4 + 5];
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16, 4), 0xFFFFFFFF);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), 0x00000001);
            data[24] = 0x03; // font/color subtype

            bool ok = UOAMProtocol.TryParseChatData(data, out byte subtype, out string text);

            Assert.True(ok);
            Assert.Equal(0x03, subtype);
            Assert.Null(text);
        }

        private static byte[] BuildResponseHeader(uint offset16, uint offset20, int extraLength)
        {
            byte[] data = new byte[24 + extraLength];
            data[2] = 0x02;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16, 4), offset16);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), offset20);
            return data;
        }
    }
}
