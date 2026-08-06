// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace ClassicUO.Network.UOAM
{
    internal enum UOAMResponseKind
    {
        Unknown,
        HandshakeAck,
        Ack,
        NoChat,
        ChatData,
        PlayerList
    }

    // Wire format reverse-engineered from E:\Projects\uoamhub\src\uoamhub.c (handle_packet,
    // process_position_update, handle_query_list, handle_poll). See
    // docs/superpowers/specs/2026-08-06-uoam-integration-design.md for the full derivation.
    internal static class UOAMProtocol
    {
        public const int HeaderSize = 16;
        private const int PasswordFieldSize = 20;
        private const byte PacketTypeHandshake = 0x0b;
        private const byte PacketTypeData = 0x00;

        private static void WriteHeader(Span<byte> dest, byte type, uint length, uint sequence)
        {
            dest[0] = 0x05;
            dest[1] = 0x00;
            dest[2] = type;
            dest[3] = 0x03;
            dest[4] = 0x10;
            dest[5] = 0;
            dest[6] = 0;
            dest[7] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(8, 4), length);
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(12, 4), sequence);
        }

        private static void WritePassword(Span<byte> dest, string password)
        {
            dest.Clear();

            password ??= string.Empty;
            if (password.Length > PasswordFieldSize - 1)
            {
                password = password.Substring(0, PasswordFieldSize - 1);
            }

            Encoding.ASCII.GetBytes(password, dest);
        }

        public static byte[] BuildHandshake(uint sequence)
        {
            byte[] packet = new byte[24];
            WriteHeader(packet, PacketTypeHandshake, (uint)packet.Length, sequence);
            // offset 16..19 reserved = 0, offset 20..23 masterId = 0 (primary connection)
            return packet;
        }

        private static byte[] BuildDataPacketPrefix(int totalLength, uint sequence, ushort major, ushort minor, string password, out int payloadOffset)
        {
            byte[] packet = new byte[totalLength];
            WriteHeader(packet, PacketTypeData, (uint)totalLength, sequence);
            // offset 16..19 left zero (reserved, unread by server)
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20, 2), major);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(22, 2), minor);
            WritePassword(packet.AsSpan(24, PasswordFieldSize), password);
            payloadOffset = 44;
            return packet;
        }

        public static byte[] BuildPositionUpdate(uint sequence, string password, string name, int x, int y, int z, int map)
        {
            // 44 (prefix) + 4 (player_info.ip, unused/zero) + 92 (noip block) = 140 = 0x8c
            byte[] packet = BuildDataPacketPrefix(140, sequence, 0x0000, 0x0010, password, out int payloadOffset);
            // packet[payloadOffset..payloadOffset+4) is player_info.ip - left zero
            UOAMPlayerInfoCodec.Encode(packet.AsSpan(payloadOffset + 4, UOAMPlayerInfoCodec.Size), name, x, y, z, map);
            return packet;
        }

        public static byte[] BuildQueryListRequest(uint sequence, string password) =>
            BuildDataPacketPrefix(44, sequence, 0x0000, 0x0002, password, out _);

        public static byte[] BuildChatPollRequest(uint sequence, string password) =>
            BuildDataPacketPrefix(44, sequence, 0x0001, 0x0001, password, out _);

        public static byte[] BuildChatSendRequest(uint sequence, string password, string text)
        {
            text ??= string.Empty;
            byte[] textBytes = Encoding.ASCII.GetBytes(text);
            // 44 (prefix) + 8 (reserved, unread) + 1 (subtype) + text + 1 (NUL)
            int totalLength = 44 + 8 + 1 + textBytes.Length + 1;

            byte[] packet = BuildDataPacketPrefix(totalLength, sequence, 0x0001, 0x0000, password, out int payloadOffset);
            // packet[payloadOffset..payloadOffset+8) reserved, left zero
            packet[payloadOffset + 8] = 0x01; // chat sub-sub-type: text
            textBytes.CopyTo(packet, payloadOffset + 9);
            // trailing byte already zero (NUL terminator) from `new byte[]` default-init
            return packet;
        }

        public static bool TryReadFrameLength(ReadOnlySpan<byte> buffer, out uint length)
        {
            if (buffer.Length < 12)
            {
                length = 0;
                return false;
            }

            length = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8, 4));
            return true;
        }

        public static UOAMResponseKind Classify(ReadOnlySpan<byte> data)
        {
            if (data.Length < 16)
            {
                return UOAMResponseKind.Unknown;
            }

            if (data[2] == 0x0c)
            {
                return UOAMResponseKind.HandshakeAck;
            }

            if (data.Length < 24)
            {
                return UOAMResponseKind.Unknown;
            }

            uint marker16 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));
            uint marker20 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));

            if (marker16 == 0x00000004 && marker20 == 0x00000000)
            {
                return UOAMResponseKind.Ack;
            }

            if (marker16 == 0x00000014 && marker20 == 0x00000001)
            {
                return UOAMResponseKind.NoChat;
            }

            if (marker16 == 0xFFFFFFFF && marker20 == 0x00000001)
            {
                return UOAMResponseKind.ChatData;
            }

            if (marker16 == 0xFFFFFFFF && marker20 == 0x00000000)
            {
                return UOAMResponseKind.PlayerList;
            }

            return UOAMResponseKind.Unknown;
        }

        public static bool TryParsePlayerList(ReadOnlySpan<byte> data, List<UOAMPlayerInfo> results)
        {
            if (Classify(data) != UOAMResponseKind.PlayerList || data.Length < 36)
            {
                return false;
            }

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4));
            int offset = 36;

            for (uint i = 0; i < count && offset + UOAMPlayerInfoCodec.Size <= data.Length; i++)
            {
                if (UOAMPlayerInfoCodec.TryDecode(data.Slice(offset, UOAMPlayerInfoCodec.Size), out UOAMPlayerInfo info))
                {
                    results.Add(info);
                }

                offset += UOAMPlayerInfoCodec.Size;
            }

            return true;
        }

        public static bool TryParseChatData(ReadOnlySpan<byte> data, out byte subtype, out string text)
        {
            subtype = 0;
            text = null;

            if (Classify(data) != UOAMResponseKind.ChatData || data.Length < 25)
            {
                return false;
            }

            subtype = data[24];

            if (subtype != 0x01)
            {
                return true; // font/color or unknown sub-sub-type: no text to extract
            }

            ReadOnlySpan<byte> payload = data.Slice(25, data.Length - 25 - 5); // strip 5-byte trailer
            int nul = payload.IndexOf((byte)0);
            text = Encoding.ASCII.GetString(nul >= 0 ? payload.Slice(0, nul) : payload);

            return true;
        }
    }
}
