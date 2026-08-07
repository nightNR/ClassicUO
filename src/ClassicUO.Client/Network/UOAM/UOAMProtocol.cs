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

    // Wire format verified against a real hub session capture (map.knuckleheads.dk:2000,
    // 2026-08-07) - NOT uoamhub.c alone. uoamhub is a *server* reimplementation that only
    // needed to emit a few canned response templates; it never had to construct a valid
    // client request, so its source told us nothing about the actual RPC-style bind
    // handshake or the extra fields real "data" packets carry. The response-side framing
    // (ack/no-chat/list/chat markers) does still match uoamhub's respond() logic once its
    // per-packet-type offset16-19 rewrite (`response_length - 24` for every type-0x02
    // reply) is accounted for - that rewrite is why offset16-19 can't be used to tell
    // response subtypes apart; only offset20-23 + total length can.
    internal static class UOAMProtocol
    {
        public const int HeaderSize = 16;
        private const int PasswordFieldSize = 20;
        private const byte PacketTypeHandshake = 0x0b;
        private const byte PacketTypeData = 0x00;

        // Fixed 116-byte RPC bind PDU captured verbatim from a real UOAM client's primary
        // connection handshake. Only the sequence field (offset 12-15) is dynamic - master
        // client id at offset 20-23 stays 0 (primary connection), everything else (the two
        // embedded GUIDs - an interface UUID and the well-known NDR transfer syntax UUID -
        // and the surrounding structure) is a fixed protocol constant, not per-session data.
        private static readonly byte[] HandshakeTemplate =
        {
            0x05, 0x00, 0x0b, 0x03, 0x10, 0x00, 0x00, 0x00, 0x74, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xd0, 0x16, 0xd0, 0x16, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
            0x10, 0x66, 0x15, 0x9e, 0x5c, 0x7b, 0xd2, 0x11, 0xb8, 0xcf, 0x00, 0x80, 0xc7, 0x97, 0x1b, 0xe1,
            0x01, 0x00, 0x00, 0x00, 0x04, 0x5d, 0x88, 0x8a, 0xeb, 0x1c, 0xc9, 0x11, 0x9f, 0xe8, 0x08, 0x00,
            0x2b, 0x10, 0x48, 0x60, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x10, 0x66, 0x15, 0x9e,
            0x5c, 0x7b, 0xd2, 0x11, 0xb8, 0xcf, 0x00, 0x80, 0xc7, 0x97, 0x1b, 0xe1, 0x01, 0x00, 0x00, 0x00,
            0x2c, 0x1c, 0xb7, 0x6c, 0x12, 0x98, 0x40, 0x45, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00
        };

        // Fixed 72-byte follow-up packet (type 0x0e) a real client always sends right after
        // the handshake ack, before any position/list/chat traffic. uoamhub.c responds to
        // this ("no idea what this is, but we respond something") without ever explaining
        // what it's for; skipping it against a real (non-uoamhub) server leaves the
        // connection in a state where subsequent data packets get silently dropped.
        // Offset 20-23 (the assigned client/session id from the handshake ack) and the
        // random-looking GUID at offset 32-47 (a per-call RPC activity id, not a fixed
        // protocol constant - real captures show high-entropy bytes there) are patched in.
        private static readonly byte[] MysteryPacketTemplate =
        {
            0x05, 0x00, 0x0e, 0x03, 0x10, 0x00, 0x00, 0x00, 0x48, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xd0, 0x16, 0xd0, 0x16, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00, 0x04, 0x5d, 0x88, 0x8a, 0xeb, 0x1c, 0xc9, 0x11, 0x9f, 0xe8, 0x08, 0x00,
            0x2b, 0x10, 0x48, 0x60, 0x02, 0x00, 0x00, 0x00
        };

        // Fixed 4-byte field observed immediately after the password in every real
        // client "data" (type 0x00) packet. uoamhub.c's `struct player_info.ip[4]`
        // comment ("the internal client's IP address, not affected by NAT") matches its
        // position, but since the server never validates it, the client's actual local
        // IPv4 address is used only where the caller supplies one; a zeroed marker is a
        // safe default.
        private static readonly byte[] ZeroIpMarker = { 0x00, 0x00, 0x00, 0x00 };

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

        // NDR conformant-varying string: max_count, offset(=0), actual_count (all uint32
        // LE, actual_count including the NUL terminator), followed by the ASCII bytes
        // including that NUL. Matches the encoding seen for the player name embedded in
        // real chat-poll/list-response packets.
        private static int WriteNdrString(Span<byte> dest, string value)
        {
            value ??= string.Empty;
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            uint count = (uint)bytes.Length + 1; // + NUL

            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(0, 4), count);
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(4, 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(8, 4), count);
            bytes.CopyTo(dest.Slice(12));
            dest[12 + bytes.Length] = 0;

            return 12 + bytes.Length + 1;
        }

        public static byte[] BuildHandshake(uint sequence)
        {
            byte[] packet = (byte[])HandshakeTemplate.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12, 4), sequence);
            return packet;
        }

        public static byte[] BuildMysteryFollowup(uint sequence, uint clientId, ReadOnlySpan<byte> activityGuid)
        {
            byte[] packet = (byte[])MysteryPacketTemplate.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12, 4), sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(20, 4), clientId);

            if (activityGuid.Length == 16)
            {
                activityGuid.CopyTo(packet.AsSpan(32, 16));
            }

            return packet;
        }

        public static bool TryParseHandshakeAck(ReadOnlySpan<byte> data, out uint clientId)
        {
            clientId = 0;

            if (data.Length < 24 || data[2] != 0x0c)
            {
                return false;
            }

            clientId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));
            return true;
        }

        private static byte[] BuildDataPacketPrefix(int totalLength, uint sequence, ushort major, ushort minor, string password, ReadOnlySpan<byte> ipMarker, out int payloadOffset)
        {
            byte[] packet = new byte[totalLength];
            WriteHeader(packet, PacketTypeData, (uint)totalLength, sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16, 4), (uint)(totalLength - 24));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20, 2), major);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(22, 2), minor);
            WritePassword(packet.AsSpan(24, PasswordFieldSize), password);

            ReadOnlySpan<byte> marker = ipMarker.Length == 4 ? ipMarker : ZeroIpMarker;
            marker.CopyTo(packet.AsSpan(44, 4));

            payloadOffset = 48;
            return packet;
        }

        public static byte[] BuildPositionUpdate(uint sequence, string password, ReadOnlySpan<byte> ipMarker, string name, int x, int y, int z, int map)
        {
            // 48 (prefix incl. ip marker) + 92 (noip block: name64+reserved12+position16) = 140 = 0x8c
            byte[] packet = BuildDataPacketPrefix(140, sequence, 0x0000, 0x0001, password, ipMarker, out int payloadOffset);
            UOAMPlayerInfoCodec.Encode(packet.AsSpan(payloadOffset, UOAMPlayerInfoCodec.Size), name, x, y, z, map);
            return packet;
        }

        public static byte[] BuildQueryListRequest(uint sequence, string password, ReadOnlySpan<byte> ipMarker) =>
            BuildDataPacketPrefix(48, sequence, 0x0000, 0x0002, password, ipMarker, out _);

        public static byte[] BuildChatPollRequest(uint sequence, string password, ReadOnlySpan<byte> ipMarker, string playerName)
        {
            playerName ??= string.Empty;
            byte[] nameBytes = Encoding.ASCII.GetBytes(playerName);
            uint nameCount = (uint)nameBytes.Length + 1;

            // 48 (prefix incl. ip marker) + 4 (unknown constant, matches every real capture)
            // + 12 (NDR string header) + nameCount (chars + NUL)
            int totalLength = 48 + 4 + 12 + (int)nameCount;

            byte[] packet = BuildDataPacketPrefix(totalLength, sequence, 0x0001, 0x0001, password, ipMarker, out int payloadOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(payloadOffset, 4), 0x00020000); // unknown constant, observed fixed
            WriteNdrString(packet.AsSpan(payloadOffset + 4), playerName);

            return packet;
        }

        private static int RoundUpTo4(int n) => (n + 3) & ~3;

        // Chat text send, verified against a real client sending 4 different messages
        // (including one that happened to describe changing font/color, which is not
        // related to the actual font/color-announce packet type - that one carries the
        // same major/minor 1/0 but a subtype of 3 instead of 1, plus extra RGB/font-name
        // fields this client never sends). Structure after the 48-byte data-packet
        // prefix: 4 zero bytes, a uint32 subtype (1 = text, matching uoamhub.c's
        // `data[52]==0x01` convention once the richer prefix is accounted for), a fixed
        // 0x00020000 marker, the sender's name as a 4-byte-aligned NDR string, then the
        // message text as a second (unaligned, since it's the last field) NDR-ish string
        // guarded by a 0x00020004 marker instead of the usual zero "offset" field.
        public static byte[] BuildChatSendRequest(uint sequence, string password, ReadOnlySpan<byte> ipMarker, string playerName, string text)
        {
            playerName ??= string.Empty;
            text ??= string.Empty;

            byte[] nameBytes = Encoding.ASCII.GetBytes(playerName);
            byte[] textBytes = Encoding.ASCII.GetBytes(text);
            uint nameCount = (uint)nameBytes.Length + 1;
            uint textCount = (uint)textBytes.Length + 1;

            int nameBlockLen = RoundUpTo4(12 + (int)nameCount);
            int textBlockLen = 12 + (int)textCount; // last field: no trailing padding

            int totalLength = 48 + 4 + 4 + 4 + nameBlockLen + textBlockLen;

            byte[] packet = BuildDataPacketPrefix(totalLength, sequence, 0x0001, 0x0000, password, ipMarker, out int payloadOffset);

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(payloadOffset, 4), 0); // unknown, observed fixed
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(payloadOffset + 4, 4), 1); // subtype: text
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(payloadOffset + 8, 4), 0x00020000); // unknown constant

            int nameOffset = payloadOffset + 12;
            WriteNdrString(packet.AsSpan(nameOffset, 12 + nameBytes.Length + 1), playerName);

            int textOffset = nameOffset + nameBlockLen;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(textOffset, 4), textCount);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(textOffset + 4, 4), 0x00020004); // unknown constant, observed fixed
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(textOffset + 8, 4), textCount);
            textBytes.CopyTo(packet, textOffset + 12);
            packet[textOffset + 12 + textBytes.Length] = 0;

            return packet;
        }

        // Chat registration - a real client sends this exactly once per connection,
        // right after its first position update and before ever polling or sending
        // chat, using the same major/minor (1/0) as chat send/font-color but subtype 2
        // (not 1=text or 3=font/color). Without it, every subsequent chat poll/send on
        // that connection gets an RPC fault (packet type 0x03, magic byte 0x23 instead
        // of the usual 0x03) - verified live: replaying byte-identical real chat-send
        // bytes on a connection that skipped this step still faults, while the same
        // bytes succeeded in the connection that registered first. Structure: prefix(48)
        // + 4 zero bytes + subtype(4, =2) + 0x00020000 marker(4) + the player's own name
        // as an NDR string (unpadded) + a fixed 10-byte zero trailer. That trailer's
        // exact byte count is only confirmed for one captured name length (6 bytes
        // incl. NUL) - if this doesn't generalize to other name lengths, that's the
        // first thing to re-derive from a fresh capture.
        public static byte[] BuildChatRegister(uint sequence, string password, ReadOnlySpan<byte> ipMarker, string playerName)
        {
            playerName ??= string.Empty;
            byte[] nameBytes = Encoding.ASCII.GetBytes(playerName);
            uint nameCount = (uint)nameBytes.Length + 1;

            const int trailerSize = 10;
            int totalLength = 48 + 4 + 4 + 4 + 12 + (int)nameCount + trailerSize;

            byte[] packet = BuildDataPacketPrefix(totalLength, sequence, 0x0001, 0x0000, password, ipMarker, out int payloadOffset);

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(payloadOffset, 4), 0); // unknown, observed fixed
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(payloadOffset + 4, 4), 2); // subtype: chat register
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(payloadOffset + 8, 4), 0x00020000); // unknown constant

            int nameOffset = payloadOffset + 12;
            WriteNdrString(packet.AsSpan(nameOffset, 12 + nameBytes.Length + 1), playerName);
            // remaining trailerSize bytes already zero from `new byte[totalLength]`

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

            if (data[2] != 0x02 || data.Length < 24)
            {
                return UOAMResponseKind.Unknown;
            }

            // offset16-19 is *always* (length-24) for type-0x02 responses (the server's
            // respond() rewrites it unconditionally) so it carries no subtype information.
            // offset20-23 plus total length are what distinguish ack/no-chat/list/chat.
            // Verified sizes from real captures: a plain Ack is always exactly 28 bytes
            // regardless of marker20 (0 for position/list acks, 1 for chat-register/
            // chat-send acks - an ack is an ack either way), NoChat is always exactly 44
            // bytes (NOT 28 - uoamhub.c's 28-byte packet_no_chat constant does not match
            // this real server), PlayerList is never smaller than 40 (36-byte header +
            // 4-byte trailer, 0 players), and ChatData is never smaller than ~61
            // (24-byte header + subtype + marker + minimal name/text NDR strings) - so
            // length alone cleanly separates each pair once marker20 narrows it to
            // "position/list family" (0) vs "chat family" (1).
            uint marker20 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));

            if (data.Length == 28)
            {
                return UOAMResponseKind.Ack;
            }

            if (marker20 == 0)
            {
                return UOAMResponseKind.PlayerList;
            }

            if (marker20 == 1)
            {
                return data.Length == 44 ? UOAMResponseKind.NoChat : UOAMResponseKind.ChatData;
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

        // Chat poll response, verified against a real "Ahoj svet.." message: header(24)
        // + subtype(uint32, 1=text/3=font-color) + 0x00020000 marker(4) + sender-name as
        // a 4-byte-aligned NDR string + message text as a second (unaligned) NDR-ish
        // string guarded by a 0x00020004 marker, same shape as BuildChatSendRequest's
        // tail. Font/color payloads carry extra RGB/font-name fields after the message
        // field that this parser doesn't need and ignores.
        public static bool TryParseChatData(ReadOnlySpan<byte> data, out byte subtype, out string senderName, out string text)
        {
            subtype = 0;
            senderName = null;
            text = null;

            if (Classify(data) != UOAMResponseKind.ChatData || data.Length < 36)
            {
                return false;
            }

            subtype = (byte)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4));

            uint nameMaxCount = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32, 4));
            int nameBlockLen = RoundUpTo4(12 + (int)nameMaxCount);
            int nameCharsOffset = 32 + 12;

            if (nameCharsOffset + nameMaxCount > data.Length)
            {
                return false;
            }

            senderName = ReadAsciiNulTerminated(data.Slice(nameCharsOffset, (int)nameMaxCount));

            if (subtype != 0x01)
            {
                return true; // font/color or unknown sub-sub-type: no text to extract
            }

            int messageOffset = 32 + nameBlockLen;

            if (messageOffset + 12 > data.Length)
            {
                return false;
            }

            uint textMaxCount = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(messageOffset, 4));
            int textCharsOffset = messageOffset + 12;

            if (textCharsOffset + textMaxCount > data.Length)
            {
                return false;
            }

            text = ReadAsciiNulTerminated(data.Slice(textCharsOffset, (int)textMaxCount));

            return true;
        }

        private static string ReadAsciiNulTerminated(ReadOnlySpan<byte> data)
        {
            int nul = data.IndexOf((byte)0);
            return Encoding.ASCII.GetString(nul >= 0 ? data.Slice(0, nul) : data);
        }
    }
}
