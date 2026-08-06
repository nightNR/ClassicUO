# UOAM Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let ClassicUO connect to a UOAM hub server (protocol reverse-engineered from `E:\Projects\uoamhub\src\uoamhub.c`) to show other players on the World Map and chat with them, independent of the game server connection.

**Architecture:** A pure, unit-testable packet codec (`UOAMProtocol`/`UOAMPlayerInfo`) is consumed by a socket state machine (`UOAMClient`, polled once per frame like the existing `NetClient`). A `UOAMManager` owned by `World` orchestrates connect lifecycle, periodic position/chat/list polling, and exposes state to UI. A `UOAMPlayerManager` holds the last-known player list and dedups against the existing `WorldMapEntityManager` (party/guild) by alias/name + position tolerance. UI surfaces: a new `OptionsGump` tab, a new `UOAMChatGump`, and additions to `WorldMapGump` (markers, "Show UOAM players" toggle, "UOAM Players" submenu with Track Player, Connect/Disconnect entry).

**Tech Stack:** C# / .NET 10, existing `ClassicUO.Client` FNA-based UI framework, `System.Net.Sockets.TcpClient` via the existing `TcpSocketWrapper`, xUnit for tests.

## Global Constraints

- Position field in `player_info.noip.position[16]` is **assumed** to be 4× `uint32` little-endian = `x, y, z, map`. This is unverified against a live hub — flag it in code comments, do not treat as gospel.
- Panic (space-bar panic flag) is **out of scope** for this plan — do not add `MacroType.UOAMPanic` or any panic wire support.
- Reserved/unread byte ranges in the wire format (offset 16-19 of data packets, offset 44-51 of the chat-send payload) are zero-filled on send; the server never reads them (confirmed from `uoamhub.c`), so this is safe, not a guess.
- UOAM connection only exists while `World.InGame` (`Player != null && Map != null`). No socket activity before login or after logout.
- Follow existing code style: `internal` visibility by default (matches every file touched), SPDX header `// SPDX-License-Identifier: BSD-2-Clause` as the first line of every new file (copy verbatim from any touched file).
- `OptionsGump.cs`, `WorldMapGump.cs`, `MacroManager.cs`, `Profile.cs`, `World.cs` are large existing files — extend them in place following their established patterns, do not restructure them.

---

## File Structure

New files:
- `src/ClassicUO.Client/Network/UOAM/UOAMPlayerInfo.cs` — the 96-byte `noip` block: struct + encode/decode.
- `src/ClassicUO.Client/Network/UOAM/UOAMProtocol.cs` — wire format constants, outgoing packet builders, incoming packet classifier/parsers.
- `src/ClassicUO.Client/Network/UOAM/UOAMClient.cs` — socket state machine (connect, handshake, framing loop, send helpers, events).
- `src/ClassicUO.Client/Configuration/UOAMNameAliasSerializer.cs` — parse/serialize the `UOAMNameAliases` profile string.
- `src/ClassicUO.Client/Game/Managers/UOAMPlayerManager.cs` — last-known UOAM player dict + dedup logic against `WorldMapEntityManager`.
- `src/ClassicUO.Client/Game/Managers/UOAMManager.cs` — orchestrator: connect lifecycle, autoconnect, periodic timers, tracked player, owns `UOAMClient` + `UOAMPlayerManager`.
- `src/ClassicUO.Client/Game/UI/Gumps/UOAMChatGump.cs` — chat window (`ExpandableScroll` + a UOAM-specific rendered text list + `StbTextBox` input row).
- `tests/ClassicUO.UnitTests/Network/UOAM/UOAMPlayerInfoTests.cs`
- `tests/ClassicUO.UnitTests/Network/UOAM/UOAMProtocolTests.cs`
- `tests/ClassicUO.UnitTests/Configuration/UOAMNameAliasSerializerTests.cs`
- `tests/ClassicUO.UnitTests/Game/Managers/UOAMPlayerManagerTests.cs`

Modified files:
- `src/ClassicUO.Client/Configuration/Profile.cs` — new UOAM profile properties.
- `src/ClassicUO.Client/Game/World.cs` — own `UOAMManager` and `UOAMPlayerManager` instances, pump `Update()`, clear on logout.
- `src/ClassicUO.Client/Game/Managers/MacroManager.cs` — `MacroType.UOAMConnect` / `UOAMDisconnect` entries + `case` handling.
- `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` — new "UOAM" tab (page 16).
- `src/ClassicUO.Client/Game/UI/Gumps/WorldMapGump.cs` — marker drawing, "Show UOAM players" toggle, "UOAM Players" context submenu (Track Player + Connect/Disconnect), viewport centering when tracking.

---

## Task 1: `UOAMPlayerInfo` — the 96-byte player block codec

**Files:**
- Create: `src/ClassicUO.Client/Network/UOAM/UOAMPlayerInfo.cs`
- Test: `tests/ClassicUO.UnitTests/Network/UOAM/UOAMPlayerInfoTests.cs`

**Interfaces:**
- Produces: `internal struct UOAMPlayerInfo { string Name; int X; int Y; int Z; int Map; }`, `internal static class UOAMPlayerInfoCodec` with `Encode(Span<byte> dest, string name, int x, int y, int z, int map)` (writes exactly 96 bytes) and `TryDecode(ReadOnlySpan<byte> src, out UOAMPlayerInfo info)` (reads exactly 96 bytes, returns `false` if `src.Length < 96` or the name field has no NUL terminator within its 64 bytes).

- [ ] **Step 1: Write the failing tests**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Network.UOAM;
using Xunit;

namespace ClassicUO.UnitTests.Network.UOAM
{
    public class UOAMPlayerInfoTests
    {
        [Fact]
        public void Encode_WritesNameXYZMap_RoundTripsThroughDecode()
        {
            Span<byte> buffer = new byte[96];

            UOAMPlayerInfoCodec.Encode(buffer, "Bazinka", 1234, 5678, 10, 1);

            bool ok = UOAMPlayerInfoCodec.TryDecode(buffer, out UOAMPlayerInfo info);

            Assert.True(ok);
            Assert.Equal("Bazinka", info.Name);
            Assert.Equal(1234, info.X);
            Assert.Equal(5678, info.Y);
            Assert.Equal(10, info.Z);
            Assert.Equal(1, info.Map);
        }

        [Fact]
        public void Encode_TruncatesNameTo63Chars_LeavingRoomForNul()
        {
            Span<byte> buffer = new byte[96];
            string longName = new string('A', 100);

            UOAMPlayerInfoCodec.Encode(buffer, longName, 0, 0, 0, 0);

            bool ok = UOAMPlayerInfoCodec.TryDecode(buffer, out UOAMPlayerInfo info);

            Assert.True(ok);
            Assert.Equal(63, info.Name.Length);
        }

        [Fact]
        public void Encode_ZeroesReservedBytes()
        {
            Span<byte> buffer = new byte[96];
            buffer.Fill(0xFF);

            UOAMPlayerInfoCodec.Encode(buffer, "X", 0, 0, 0, 0);

            // reserved[12] lives at offset 64..75 (after name[64])
            for (int i = 64; i < 76; i++)
            {
                Assert.Equal(0, buffer[i]);
            }
        }

        [Fact]
        public void TryDecode_ReturnsFalse_WhenBufferTooShort()
        {
            Span<byte> buffer = new byte[95];

            bool ok = UOAMPlayerInfoCodec.TryDecode(buffer, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryDecode_ReturnsFalse_WhenNameHasNoNulTerminator()
        {
            Span<byte> buffer = new byte[96];
            buffer.Slice(0, 64).Fill((byte)'A'); // no NUL anywhere in the 64-byte name field

            bool ok = UOAMPlayerInfoCodec.TryDecode(buffer, out _);

            Assert.False(ok);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~UOAMPlayerInfoTests"`
Expected: FAIL (compile error, `UOAMPlayerInfoCodec` does not exist)

- [ ] **Step 3: Write the implementation**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Buffers.Binary;
using System.Text;

namespace ClassicUO.Network.UOAM
{
    // The 96-byte "noip" block from uoamhub's `struct player_info`:
    //   name[64] + reserved[12] + position[16]
    // Position layout (x, y, z, map as 4x uint32 LE) is ASSUMED — unverified against
    // a live hub. See docs/superpowers/specs/2026-08-06-uoam-integration-design.md.
    internal struct UOAMPlayerInfo
    {
        public string Name;
        public int X;
        public int Y;
        public int Z;
        public int Map;
    }

    internal static class UOAMPlayerInfoCodec
    {
        public const int Size = 96;
        private const int NameOffset = 0;
        private const int NameSize = 64;
        private const int ReservedOffset = 64;
        private const int ReservedSize = 12;
        private const int PositionOffset = 76;

        public static void Encode(Span<byte> dest, string name, int x, int y, int z, int map)
        {
            if (dest.Length < Size)
            {
                throw new ArgumentException($"destination must be at least {Size} bytes", nameof(dest));
            }

            dest.Slice(0, Size).Clear();

            name ??= string.Empty;
            if (name.Length > NameSize - 1)
            {
                name = name.Substring(0, NameSize - 1);
            }

            int written = Encoding.ASCII.GetBytes(name, dest.Slice(NameOffset, NameSize - 1));
            dest[NameOffset + written] = 0; // NUL terminator

            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(PositionOffset, 4), unchecked((uint)x));
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(PositionOffset + 4, 4), unchecked((uint)y));
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(PositionOffset + 8, 4), unchecked((uint)z));
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(PositionOffset + 12, 4), unchecked((uint)map));
        }

        public static bool TryDecode(ReadOnlySpan<byte> src, out UOAMPlayerInfo info)
        {
            info = default;

            if (src.Length < Size)
            {
                return false;
            }

            ReadOnlySpan<byte> nameField = src.Slice(NameOffset, NameSize);
            int nul = nameField.IndexOf((byte)0);

            if (nul < 0)
            {
                return false;
            }

            info.Name = Encoding.ASCII.GetString(nameField.Slice(0, nul));
            info.X = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(PositionOffset, 4)));
            info.Y = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(PositionOffset + 4, 4)));
            info.Z = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(PositionOffset + 8, 4)));
            info.Map = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(PositionOffset + 12, 4)));

            return true;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~UOAMPlayerInfoTests"`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Network/UOAM/UOAMPlayerInfo.cs tests/ClassicUO.UnitTests/Network/UOAM/UOAMPlayerInfoTests.cs
git commit -m "feat(uoam): add UOAMPlayerInfo 96-byte block codec"
```

---

## Task 2: `UOAMProtocol` — outgoing packet builders

**Files:**
- Create: `src/ClassicUO.Client/Network/UOAM/UOAMProtocol.cs`
- Test: `tests/ClassicUO.UnitTests/Network/UOAM/UOAMProtocolTests.cs`

**Interfaces:**
- Consumes: `UOAMPlayerInfoCodec.Encode` (Task 1).
- Produces: `internal static class UOAMProtocol` with:
  - `const int HeaderSize = 16;`
  - `byte[] BuildHandshake(uint sequence)`
  - `byte[] BuildPositionUpdate(uint sequence, string password, string name, int x, int y, int z, int map)`
  - `byte[] BuildQueryListRequest(uint sequence, string password)`
  - `byte[] BuildChatPollRequest(uint sequence, string password)`
  - `byte[] BuildChatSendRequest(uint sequence, string password, string text)`
  - `bool TryReadFrameLength(ReadOnlySpan<byte> buffer, out uint length)` (reads the length field at offset 8, `false` if `buffer.Length < 12`)

Wire format derived from `uoamhub.c` (`handle_packet`, `process_position_update`, `handle_query_list`, `handle_poll`):

```
Header (16 bytes, every packet):
  [0]    0x05
  [1]    0x00
  [2]    packet type (0x0b handshake, 0x00 data)
  [3]    0x03
  [4]    0x10
  [5..7] 0x00 0x00 0x00
  [8..11]  uint32 LE total packet length
  [12..15] uint32 LE sequence

Handshake (type 0x0b), 24 bytes total:
  header(16) + reserved uint32=0 (16..19) + masterId uint32=0 (20..23)

Data packet (type 0x00), header(16) + :
  [16..19] reserved, zero (server never reads it)
  [20..21] uint16 LE "major" subtype
  [22..23] uint16 LE "minor" subtype
  [24..43] password, 20 bytes, NUL-padded (sent on every data packet)
  [44..]   payload (subtype-specific)

  Position update:  major=0x0000 minor=0x0010, payload = UOAMPlayerInfo (96 bytes,
                     but first 4 bytes are an unused "ip" field per player_info.ip[4] -
                     zero it). Total length = 44 + 4 (ip) + 96 = 144... NO: player_info
                     is ip[4]+noip(96)=100 bytes total, but process_position_update()
                     reads noip at data+44 directly (info = (player_info*)(data+44), and
                     info->noip is accessed - the struct's `ip` field occupies data[44..47],
                     noip starts at data[48]). Total packet length asserted == 0x8c (140):
                     44 (prefix) + 4 (ip) + 96 (noip) = 144... reconcile: 140-44=96, so
                     ip+noip together are 96 bytes, meaning ip[4] is NOT extra - re-check:
                     struct player_info { unsigned char ip[4]; struct noip_player_info noip; }
                     and noip itself is name[64]+reserved[12]+position[16]=92 bytes, so
                     player_info = 4+92 = 96 bytes total. UOAMPlayerInfoCodec (Task 1)
                     encodes the 92-byte noip part; the builder here additionally writes
                     4 zero bytes for `ip` immediately before it. 44 + 4 + 92 = 140 = 0x8c. OK.
  Query list:        major=0x0000 minor=0x0002, no extra payload. Total length = 44.
  Chat poll:         major=0x0001 minor=0x0001, no extra payload. Total length = 44.
  Chat send (text):  major=0x0001 minor=0x0000, payload = 8 reserved zero bytes (44..51)
                     + subtype byte 0x01 (52) + ASCII text + NUL terminator (53..).
                     Total length = 53 + text.Length + 1.
```

**IMPORTANT CORRECTION to Task 1**: `UOAMPlayerInfoCodec.Size` must be **92** bytes (`name[64] + reserved[12] + position[16]`), not 96 — the `ip[4]` field is a separate 4 bytes written by the position-update builder, not part of the codec. Go back and fix Task 1 before writing this task's code:

- [ ] **Step 0: Fix `UOAMPlayerInfoCodec.Size` from 96 to 92**

In `src/ClassicUO.Client/Network/UOAM/UOAMPlayerInfo.cs`, change:

```csharp
        public const int Size = 96;
```
to:
```csharp
        public const int Size = 92;
```
and change `ReservedOffset = 64` / `PositionOffset = 76` — these stay correct (64 + 12 = 76, 76 + 16 = 92, matches new `Size`). Also update the "reserved bytes" test in `UOAMPlayerInfoTests.cs` — the buffer there is already sized `new byte[96]`, change both occurrences (three tests use `new byte[96]`) to `new byte[92]`, and the `TryDecode_ReturnsFalse_WhenBufferTooShort` test's buffer from `new byte[95]` to `new byte[91]`. Re-run:

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~UOAMPlayerInfoTests"`
Expected: PASS (5 tests, unchanged assertions since only the buffer size shrank)

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~UOAMProtocolTests"`
Expected: FAIL (compile error, `UOAMProtocol` does not exist)

- [ ] **Step 3: Write the implementation**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Buffers.Binary;
using System.Text;

namespace ClassicUO.Network.UOAM
{
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
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~UOAMProtocolTests"`
Expected: PASS (7 tests)

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Network/UOAM/UOAMPlayerInfo.cs src/ClassicUO.Client/Network/UOAM/UOAMProtocol.cs tests/ClassicUO.UnitTests/Network/UOAM/
git commit -m "feat(uoam): add outgoing packet builders"
```

---

## Task 3: `UOAMProtocol` — incoming response classification and parsing

**Files:**
- Modify: `src/ClassicUO.Client/Network/UOAM/UOAMProtocol.cs`
- Test: `tests/ClassicUO.UnitTests/Network/UOAM/UOAMProtocolTests.cs` (append)

**Interfaces:**
- Consumes: `UOAMPlayerInfoCodec.TryDecode` (Task 1).
- Produces:
  - `internal enum UOAMResponseKind { Unknown, HandshakeAck, Ack, NoChat, ChatData, PlayerList }`
  - `UOAMResponseKind Classify(ReadOnlySpan<byte> data)`
  - `bool TryParsePlayerList(ReadOnlySpan<byte> data, List<UOAMPlayerInfo> results)` (appends to `results`, returns `false` if `Classify(data) != PlayerList`)
  - `bool TryParseChatData(ReadOnlySpan<byte> data, out byte subtype, out string text)` (returns `false` if `Classify(data) != ChatData`; `text` is non-null only when `subtype == 0x01`)

Response formats derived from `uoamhub.c` constants `packet_handshake_response`, `packet_ack`, `packet_no_chat`, `header_chat`, `header_list` plus how `respond()`/`handle_query_list()`/`handle_poll()` fill them in:

```
All server->client responses share the 16-byte header, with response[2] usually 0x02
(exception: handshake response has response[2] == 0x0c).

Discriminator: read uint32 at offset 16 and offset 20 (both LE):
  HandshakeAck:  response[2] == 0x0c                          (offset16/20 irrelevant)
  Ack:           offset16 == 0x00000004, offset20 == 0x00000000  (28 bytes total)
  NoChat:        offset16 == 0x00000014, offset20 == 0x00000001  (28 bytes total)
  ChatData:      offset16 == 0xFFFFFFFF, offset20 == 0x00000001
                 -> payload starts at offset 24: [0]=subtype (0x01 text / 0x03 font),
                    followed by data, packet has 5 trailing zero bytes appended by the
                    server (handle_poll: memset(...,0,5)), so payload length for the
                    chat item itself = data.Length - 24 - 5.
  PlayerList:    offset16 == 0xFFFFFFFF, offset20 == 0x00000000
                 -> player count at offset 24 (uint32 LE), noip blocks (92 bytes each)
                    start at offset 36, terminated by 4 trailing zero bytes.
```

- [ ] **Step 1: Write the failing tests (append to `UOAMProtocolTests.cs`)**

```csharp
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

            var results = new System.Collections.Generic.List<UOAMPlayerInfo>();
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
```

Add `using System.Collections.Generic;` to the top of the test file's usings if not already present.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~UOAMProtocolTests"`
Expected: FAIL (compile error, `UOAMResponseKind`/`Classify`/`TryParsePlayerList`/`TryParseChatData` do not exist)

- [ ] **Step 3: Write the implementation (append to `UOAMProtocol.cs`)**

```csharp
    internal enum UOAMResponseKind
    {
        Unknown,
        HandshakeAck,
        Ack,
        NoChat,
        ChatData,
        PlayerList
    }
```

Add inside `UOAMProtocol` (after `TryReadFrameLength`):

```csharp
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
```

Add `using System.Collections.Generic;` to the top of `UOAMProtocol.cs`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~UOAMProtocolTests"`
Expected: PASS (14 tests total)

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Network/UOAM/UOAMProtocol.cs tests/ClassicUO.UnitTests/Network/UOAM/UOAMProtocolTests.cs
git commit -m "feat(uoam): add incoming response classification and parsing"
```

---

## Task 4: `UOAMClient` — socket state machine

**Files:**
- Create: `src/ClassicUO.Client/Network/UOAM/UOAMClient.cs`

**Interfaces:**
- Consumes: `UOAMProtocol.Build*` (Task 2), `UOAMProtocol.Classify`/`TryParsePlayerList`/`TryParseChatData`/`TryReadFrameLength` (Task 3), `ClassicUO.Network.Socket.TcpSocketWrapper` (existing, `sealed class` in `ClassicUO.Network.Socket` namespace, same assembly).
- Produces: `internal sealed class UOAMClient` with:
  - `bool IsConnected { get; }` — true once the handshake ack (`0x0c`) has been received.
  - `void Connect(string ip, ushort port)`
  - `void Disconnect()`
  - `void SendPositionUpdate(string password, string name, int x, int y, int z, int map)`
  - `void SendQueryList(string password)`
  - `void SendChatPoll(string password)`
  - `void SendChatText(string password, string text)`
  - `void Update()` — pump once per frame: read available bytes, split into frames, dispatch.
  - `event Action Connected;`
  - `event Action Disconnected;`
  - `event Action<List<UOAMPlayerInfo>> PlayerListReceived;`
  - `event Action<string> ChatTextReceived;`

No dedicated unit test for this task — it wraps a live `TcpClient` and is exercised manually against a running `uoamhub` (see spec's "Testing" section). Framing/parsing correctness is already covered by Task 3's tests via the pure `UOAMProtocol` functions this class calls.

- [ ] **Step 1: Write the implementation**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using ClassicUO.Network.Socket;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Network.UOAM
{
    internal sealed class UOAMClient
    {
        private const int ReadScratchSize = 4096;
        private const int RecvBufferSize = 16384;

        private readonly byte[] _readScratch = new byte[ReadScratchSize];
        private readonly byte[] _recvBuffer = new byte[RecvBufferSize];
        private int _recvLength;
        private SocketWrapper _socket;
        private uint _sequence;
        private bool _handshakeAcked;

        public bool IsConnected => _handshakeAcked && _socket != null && _socket.IsConnected;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<List<UOAMPlayerInfo>> PlayerListReceived;
        public event Action<string> ChatTextReceived;

        public void Connect(string ip, ushort port)
        {
            Disconnect();

            _handshakeAcked = false;
            _recvLength = 0;
            _sequence = 0;

            _socket = new TcpSocketWrapper();
            _socket.OnConnected += (_, _) => SendHandshake();
            _socket.OnDisconnected += (_, _) => HandleDisconnected();
            _socket.OnError += (_, e) =>
            {
                Log.Warn($"UOAM socket error: {e}");
                HandleDisconnected();
            };

            _socket.Connect(new Uri($"tcp://{ip}:{port}"));
        }

        public void Disconnect()
        {
            if (_socket == null)
            {
                return;
            }

            _socket.Disconnect();
            _socket = null;
            _handshakeAcked = false;
        }

        private void HandleDisconnected()
        {
            _socket = null;
            _handshakeAcked = false;
            Disconnected?.Invoke();
        }

        private void SendHandshake()
        {
            Send(UOAMProtocol.BuildHandshake(++_sequence));
        }

        public void SendPositionUpdate(string password, string name, int x, int y, int z, int map)
        {
            if (!IsConnected)
            {
                return;
            }

            Send(UOAMProtocol.BuildPositionUpdate(++_sequence, password, name, x, y, z, map));
        }

        public void SendQueryList(string password)
        {
            if (!IsConnected)
            {
                return;
            }

            Send(UOAMProtocol.BuildQueryListRequest(++_sequence, password));
        }

        public void SendChatPoll(string password)
        {
            if (!IsConnected)
            {
                return;
            }

            Send(UOAMProtocol.BuildChatPollRequest(++_sequence, password));
        }

        public void SendChatText(string password, string text)
        {
            if (!IsConnected)
            {
                return;
            }

            Send(UOAMProtocol.BuildChatSendRequest(++_sequence, password, text));
        }

        private void Send(byte[] packet)
        {
            try
            {
                _socket?.Send(packet, 0, packet.Length);
            }
            catch (SocketException ex)
            {
                Log.Warn($"UOAM send failed: {ex}");
                HandleDisconnected();
            }
        }

        public void Update()
        {
            if (_socket == null || !_socket.IsConnected)
            {
                return;
            }

            int read;

            do
            {
                read = _socket.Read(_readScratch);

                if (read > 0)
                {
                    AppendToRecvBuffer(_readScratch, read);
                }
            }
            while (read == _readScratch.Length);

            ProcessBufferedPackets();
        }

        private void AppendToRecvBuffer(byte[] data, int length)
        {
            if (_recvLength + length > _recvBuffer.Length)
            {
                Log.Warn("UOAM recv buffer overflow, dropping connection");
                Disconnect();
                return;
            }

            Buffer.BlockCopy(data, 0, _recvBuffer, _recvLength, length);
            _recvLength += length;
        }

        private void ProcessBufferedPackets()
        {
            int position = 0;

            while (_recvLength - position >= UOAMProtocol.HeaderSize)
            {
                ReadOnlySpan<byte> remaining = _recvBuffer.AsSpan(position, _recvLength - position);

                if (!UOAMProtocol.TryReadFrameLength(remaining, out uint length) || length < 16 || length > remaining.Length)
                {
                    break; // wait for more data (or malformed - stop processing this batch)
                }

                DispatchPacket(remaining.Slice(0, (int)length));
                position += (int)length;
            }

            if (position > 0)
            {
                int leftover = _recvLength - position;
                Buffer.BlockCopy(_recvBuffer, position, _recvBuffer, 0, leftover);
                _recvLength = leftover;
            }
        }

        private void DispatchPacket(ReadOnlySpan<byte> data)
        {
            switch (UOAMProtocol.Classify(data))
            {
                case UOAMResponseKind.HandshakeAck:
                    _handshakeAcked = true;
                    Connected?.Invoke();
                    break;

                case UOAMResponseKind.PlayerList:
                    var players = new List<UOAMPlayerInfo>();
                    UOAMProtocol.TryParsePlayerList(data, players);
                    PlayerListReceived?.Invoke(players);
                    break;

                case UOAMResponseKind.ChatData:
                    if (UOAMProtocol.TryParseChatData(data, out byte subtype, out string text) && subtype == 0x01 && text != null)
                    {
                        ChatTextReceived?.Invoke(text);
                    }
                    break;

                case UOAMResponseKind.Ack:
                case UOAMResponseKind.NoChat:
                case UOAMResponseKind.Unknown:
                    break; // nothing to do
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds, no errors in `ClassicUO.Client` or `ClassicUO.UnitTests`

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Client/Network/UOAM/UOAMClient.cs
git commit -m "feat(uoam): add UOAMClient socket state machine"
```

---

## Task 5: Profile properties + name alias serializer

**Files:**
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs`
- Create: `src/ClassicUO.Client/Configuration/UOAMNameAliasSerializer.cs`
- Test: `tests/ClassicUO.UnitTests/Configuration/UOAMNameAliasSerializerTests.cs`

**Interfaces:**
- Produces: `internal static class UOAMNameAliasSerializer` with `Dictionary<string, string> Parse(string serialized)` and `string Serialize(IEnumerable<KeyValuePair<string, string>> aliases)`. Format: `uoamName=charName;uoamName2=charName2`, trimmed, empty/malformed entries skipped.
- New `Profile` properties (all plain auto-properties, same style as `WorldMapShowParty` etc. at `Profile.cs:395-413`):
  - `string UOAMPlayerName { get; set; } = string.Empty;`
  - `string UOAMServerIP { get; set; } = string.Empty;`
  - `ushort UOAMServerPort { get; set; } = 2000;`
  - `string UOAMPassword { get; set; } = string.Empty;`
  - `bool UOAMAutoConnect { get; set; }`
  - `bool UOAMAutoOpenChat { get; set; }`
  - `string UOAMNameAliases { get; set; } = string.Empty;`

- [ ] **Step 1: Write the failing tests**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests.Configuration
{
    public class UOAMNameAliasSerializerTests
    {
        [Fact]
        public void Parse_EmptyString_ReturnsEmptyDictionary()
        {
            Dictionary<string, string> result = UOAMNameAliasSerializer.Parse("");

            Assert.Empty(result);
        }

        [Fact]
        public void Parse_SinglePair_ReturnsOneEntry()
        {
            Dictionary<string, string> result = UOAMNameAliasSerializer.Parse("UOAMBob=Bobbicus");

            Assert.Single(result);
            Assert.Equal("Bobbicus", result["UOAMBob"]);
        }

        [Fact]
        public void Parse_MultiplePairs_ReturnsAllEntries()
        {
            Dictionary<string, string> result = UOAMNameAliasSerializer.Parse("A=B;C=D");

            Assert.Equal(2, result.Count);
            Assert.Equal("B", result["A"]);
            Assert.Equal("D", result["C"]);
        }

        [Fact]
        public void Parse_TrimsWhitespaceAroundKeysAndValues()
        {
            Dictionary<string, string> result = UOAMNameAliasSerializer.Parse(" A = B ; C = D ");

            Assert.Equal("B", result["A"]);
            Assert.Equal("D", result["C"]);
        }

        [Fact]
        public void Parse_SkipsMalformedEntries()
        {
            Dictionary<string, string> result = UOAMNameAliasSerializer.Parse("A=B;NoEqualsSign;C=D");

            Assert.Equal(2, result.Count);
            Assert.True(result.ContainsKey("A"));
            Assert.True(result.ContainsKey("C"));
        }

        [Fact]
        public void Serialize_RoundTripsThroughParse()
        {
            var aliases = new Dictionary<string, string> { ["A"] = "B", ["C"] = "D" };

            string serialized = UOAMNameAliasSerializer.Serialize(aliases);
            Dictionary<string, string> parsed = UOAMNameAliasSerializer.Parse(serialized);

            Assert.Equal(2, parsed.Count);
            Assert.Equal("B", parsed["A"]);
            Assert.Equal("D", parsed["C"]);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~UOAMNameAliasSerializerTests"`
Expected: FAIL (compile error, `UOAMNameAliasSerializer` does not exist)

- [ ] **Step 3: Write the implementation**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.Text;

namespace ClassicUO.Configuration
{
    internal static class UOAMNameAliasSerializer
    {
        public static Dictionary<string, string> Parse(string serialized)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(serialized))
            {
                return result;
            }

            foreach (string pair in serialized.Split(';'))
            {
                int eq = pair.IndexOf('=');

                if (eq <= 0 || eq == pair.Length - 1)
                {
                    continue;
                }

                string key = pair.Substring(0, eq).Trim();
                string value = pair.Substring(eq + 1).Trim();

                if (key.Length == 0 || value.Length == 0)
                {
                    continue;
                }

                result[key] = value;
            }

            return result;
        }

        public static string Serialize(IEnumerable<KeyValuePair<string, string>> aliases)
        {
            var sb = new StringBuilder();
            bool first = true;

            foreach (var kvp in aliases)
            {
                if (!first)
                {
                    sb.Append(';');
                }

                sb.Append(kvp.Key).Append('=').Append(kvp.Value);
                first = false;
            }

            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~UOAMNameAliasSerializerTests"`
Expected: PASS (6 tests)

- [ ] **Step 5: Add the Profile properties**

In `src/ClassicUO.Client/Configuration/Profile.cs`, immediately after line 413 (`public bool ShowDPSWithDamageNumbers { get; set; } = true;`) insert:

```csharp

        // UOAM integration
        public string UOAMPlayerName { get; set; } = string.Empty;
        public string UOAMServerIP { get; set; } = string.Empty;
        public ushort UOAMServerPort { get; set; } = 2000;
        public string UOAMPassword { get; set; } = string.Empty;
        public bool UOAMAutoConnect { get; set; }
        public bool UOAMAutoOpenChat { get; set; }
        public string UOAMNameAliases { get; set; } = string.Empty;
```

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 7: Commit**

```bash
git add src/ClassicUO.Client/Configuration/UOAMNameAliasSerializer.cs src/ClassicUO.Client/Configuration/Profile.cs tests/ClassicUO.UnitTests/Configuration/UOAMNameAliasSerializerTests.cs
git commit -m "feat(uoam): add profile settings and name alias serializer"
```

---

## Task 6: `UOAMPlayerManager` — player list storage and dedup

**Files:**
- Create: `src/ClassicUO.Client/Game/Managers/UOAMPlayerManager.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/UOAMPlayerManagerTests.cs`

**Interfaces:**
- Consumes: `UOAMPlayerInfo` (Task 1), `WorldMapEntityManager`/`WMapEntity` (existing, `src/ClassicUO.Client/Game/Managers/WorldMapEntityManager.cs`).
- Produces: `internal sealed class UOAMPlayerManager` with:
  - `IReadOnlyDictionary<string, UOAMPlayerInfo> Players { get; }` (keyed by player name)
  - `void UpdateFromList(IEnumerable<UOAMPlayerInfo> players)` (replaces the whole set — the hub always sends the full list)
  - `void Clear()`
  - `static bool IsDuplicateOfPartyOrGuild(UOAMPlayerInfo uoamPlayer, string aliasedName, WorldMapEntityManager wmapManager, int toleranceTiles)` — dedup per the design spec: match by aliased name (if provided) or raw name, case-insensitive, AND position within `toleranceTiles` on both axes.

- [ ] **Step 1: Write the failing tests**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Linq;
using ClassicUO.Game.Managers;
using ClassicUO.Network.UOAM;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class UOAMPlayerManagerTests
    {
        private static UOAMPlayerInfo MakePlayer(string name, int x, int y, int map = 1) =>
            new UOAMPlayerInfo { Name = name, X = x, Y = y, Z = 0, Map = map };

        [Fact]
        public void UpdateFromList_PopulatesPlayersByName()
        {
            var manager = new UOAMPlayerManager();

            manager.UpdateFromList(new[] { MakePlayer("Alice", 1, 2), MakePlayer("Bob", 3, 4) });

            Assert.Equal(2, manager.Players.Count);
            Assert.Equal(1, manager.Players["Alice"].X);
            Assert.Equal(3, manager.Players["Bob"].X);
        }

        [Fact]
        public void UpdateFromList_ReplacesPreviousSet()
        {
            var manager = new UOAMPlayerManager();
            manager.UpdateFromList(new[] { MakePlayer("Alice", 1, 2) });

            manager.UpdateFromList(new[] { MakePlayer("Bob", 3, 4) });

            Assert.Single(manager.Players);
            Assert.False(manager.Players.ContainsKey("Alice"));
            Assert.True(manager.Players.ContainsKey("Bob"));
        }

        [Fact]
        public void Clear_EmptiesPlayers()
        {
            var manager = new UOAMPlayerManager();
            manager.UpdateFromList(new[] { MakePlayer("Alice", 1, 2) });

            manager.Clear();

            Assert.Empty(manager.Players);
        }

        [Fact]
        public void IsDuplicateOfPartyOrGuild_ReturnsTrue_WhenNameAndPositionMatch()
        {
            var wmap = new WorldMapEntityManager(null);
            wmap.AddOrUpdate(1, x: 100, y: 200, hp: 100, map: 1, isguild: false, name: "Alice", from_packet: true);

            bool result = UOAMPlayerManager.IsDuplicateOfPartyOrGuild(
                MakePlayer("Alice", 102, 199), aliasedName: null, wmap, toleranceTiles: 5);

            Assert.True(result);
        }

        [Fact]
        public void IsDuplicateOfPartyOrGuild_ReturnsFalse_WhenPositionOutsideTolerance()
        {
            var wmap = new WorldMapEntityManager(null);
            wmap.AddOrUpdate(1, x: 100, y: 200, hp: 100, map: 1, isguild: false, name: "Alice", from_packet: true);

            bool result = UOAMPlayerManager.IsDuplicateOfPartyOrGuild(
                MakePlayer("Alice", 200, 200), aliasedName: null, wmap, toleranceTiles: 5);

            Assert.False(result);
        }

        [Fact]
        public void IsDuplicateOfPartyOrGuild_ReturnsFalse_WhenNoNameMatch()
        {
            var wmap = new WorldMapEntityManager(null);
            wmap.AddOrUpdate(1, x: 100, y: 200, hp: 100, map: 1, isguild: false, name: "Alice", from_packet: true);

            bool result = UOAMPlayerManager.IsDuplicateOfPartyOrGuild(
                MakePlayer("Charlie", 100, 200), aliasedName: null, wmap, toleranceTiles: 5);

            Assert.False(result);
        }

        [Fact]
        public void IsDuplicateOfPartyOrGuild_UsesAliasedName_WhenProvided()
        {
            var wmap = new WorldMapEntityManager(null);
            wmap.AddOrUpdate(1, x: 100, y: 200, hp: 100, map: 1, isguild: false, name: "RealCharName", from_packet: true);

            bool result = UOAMPlayerManager.IsDuplicateOfPartyOrGuild(
                MakePlayer("UOAMNick", 100, 200), aliasedName: "RealCharName", wmap, toleranceTiles: 5);

            Assert.True(result);
        }

        [Fact]
        public void IsDuplicateOfPartyOrGuild_IsCaseInsensitive()
        {
            var wmap = new WorldMapEntityManager(null);
            wmap.AddOrUpdate(1, x: 100, y: 200, hp: 100, map: 1, isguild: false, name: "Alice", from_packet: true);

            bool result = UOAMPlayerManager.IsDuplicateOfPartyOrGuild(
                MakePlayer("alice", 100, 200), aliasedName: null, wmap, toleranceTiles: 5);

            Assert.True(result);
        }
    }
}
```

`WorldMapEntityManager.AddOrUpdate` requires `Enabled` to be `true` to actually store the entity (see `src/ClassicUO.Client/Game/Managers/WorldMapEntityManager.cs:112`), and `Enabled` checks `ProfileManager.CurrentProfile != null && ... && UIManager.GetGump<WorldMapGump>() != null` — none of which hold in a unit test. Check this before writing Step 3; if `AddOrUpdate` silently no-ops, the dedup tests need a different setup path.

- [ ] **Step 2: Verify the `AddOrUpdate`/`Enabled` concern before writing the implementation**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~UOAMPlayerManagerTests" -v n`
Expected: FAIL to compile first (no `UOAMPlayerManager` yet) — that's fine, this step is just to get the harness running so Step 4 below tells you definitively whether `AddOrUpdate` populated `wmap.Entities`. Create a throwaway minimal `UOAMPlayerManager` stub (empty `Players`, `IsDuplicateOfPartyOrGuild` returning `false` always) purely to unblock compilation, run the tests, and read the actual failures for the `IsDuplicateOfPartyOrGuild_*` tests — if they fail with "no entity found" rather than an assertion mismatch, `Enabled` is blocking `AddOrUpdate` as suspected.

If that's confirmed: change every `wmap.AddOrUpdate(...)` call in the test file to direct dictionary population instead, using `WorldMapEntityManager.Entities` (`public readonly Dictionary<uint, WMapEntity>`, confirmed public in `WorldMapEntityManager.cs:65`) and `WMapEntity`'s public settable fields (`X`, `Y`, `Map`, `Name`, all public in `WorldMapEntityManager.cs:26-30`):

```csharp
            var wmap = new WorldMapEntityManager(null);
            wmap.Entities[1] = new WMapEntity(1) { Name = "Alice", X = 100, Y = 200, Map = 1 };
```

Apply this replacement to all 5 `IsDuplicateOfPartyOrGuild_*` tests before proceeding to Step 3's real implementation.

- [ ] **Step 3: Write the real implementation**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Network.UOAM;

namespace ClassicUO.Game.Managers
{
    internal sealed class UOAMPlayerManager
    {
        private readonly Dictionary<string, UOAMPlayerInfo> _players = new Dictionary<string, UOAMPlayerInfo>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, UOAMPlayerInfo> Players => _players;

        public void UpdateFromList(IEnumerable<UOAMPlayerInfo> players)
        {
            _players.Clear();

            foreach (UOAMPlayerInfo p in players)
            {
                if (!string.IsNullOrEmpty(p.Name))
                {
                    _players[p.Name] = p;
                }
            }
        }

        public void Clear()
        {
            _players.Clear();
        }

        public static bool IsDuplicateOfPartyOrGuild(UOAMPlayerInfo uoamPlayer, string aliasedName, WorldMapEntityManager wmapManager, int toleranceTiles)
        {
            string nameToMatch = string.IsNullOrEmpty(aliasedName) ? uoamPlayer.Name : aliasedName;

            if (string.IsNullOrEmpty(nameToMatch) || wmapManager == null)
            {
                return false;
            }

            foreach (WMapEntity entity in wmapManager.Entities.Values)
            {
                if (string.IsNullOrEmpty(entity.Name))
                {
                    continue;
                }

                if (!string.Equals(entity.Name, nameToMatch, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Math.Abs(entity.X - uoamPlayer.X) <= toleranceTiles && Math.Abs(entity.Y - uoamPlayer.Y) <= toleranceTiles)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~UOAMPlayerManagerTests"`
Expected: PASS (9 tests)

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/UOAMPlayerManager.cs tests/ClassicUO.UnitTests/Game/Managers/UOAMPlayerManagerTests.cs
git commit -m "feat(uoam): add player list storage and party/guild dedup"
```

---

## Task 7: `UOAMManager` — connect lifecycle, autoconnect, periodic polling

**Files:**
- Create: `src/ClassicUO.Client/Game/Managers/UOAMManager.cs`
- Modify: `src/ClassicUO.Client/Game/World.cs`

**Interfaces:**
- Consumes: `UOAMClient` (Task 4), `UOAMPlayerManager` (Task 6), `Profile.UOAM*` properties (Task 5), `World.InGame`/`World.Player`/`World.Map` (existing).
- Produces: `internal sealed class UOAMManager` with:
  - `UOAMManager(World world)`
  - `bool IsConnected { get; }`
  - `string TrackedPlayerName { get; set; }` (null = not tracking)
  - `void Connect()` (uses `ProfileManager.CurrentProfile.UOAM*` fields)
  - `void Disconnect()`
  - `void Update()` — call every frame from `World.Update()`; handles autoconnect edge, pumps `UOAMClient.Update()`, sends periodic position update (every 2s) / chat poll (every 1.5s) / list query (every 3s) while connected.
  - Exposes the underlying `UOAMPlayerManager` via `UOAMPlayerManager Players { get; }` and the underlying `UOAMClient` chat text stream via `event Action<string> ChatTextReceived;`

- [ ] **Step 1: Write the implementation**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Configuration;
using ClassicUO.Network.UOAM;

namespace ClassicUO.Game.Managers
{
    internal sealed class UOAMManager
    {
        private const uint PositionIntervalMs = 2000;
        private const uint ChatPollIntervalMs = 1500;
        private const uint ListQueryIntervalMs = 3000;

        private readonly World _world;
        private readonly UOAMClient _client = new UOAMClient();
        private bool _wasInGame;
        private uint _nextPositionSend, _nextChatPoll, _nextListQuery;

        public UOAMManager(World world)
        {
            _world = world;
            Players = new UOAMPlayerManager();

            _client.PlayerListReceived += list => Players.UpdateFromList(list);
            _client.ChatTextReceived += text => ChatTextReceived?.Invoke(text);
            _client.Connected += () => GameActions.Print(_world, "UOAM: connected", 0x2B);
            _client.Disconnected += () =>
            {
                Players.Clear();
                GameActions.Print(_world, "UOAM: disconnected", 0x2B);
            };
        }

        public UOAMPlayerManager Players { get; }

        public string TrackedPlayerName { get; set; }

        public bool IsConnected => _client.IsConnected;

        public event Action<string> ChatTextReceived;

        public void Connect()
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || string.IsNullOrEmpty(profile.UOAMServerIP))
            {
                return;
            }

            _nextPositionSend = 0;
            _nextChatPoll = 0;
            _nextListQuery = 0;

            _client.Connect(profile.UOAMServerIP, profile.UOAMServerPort);
        }

        public void Disconnect()
        {
            _client.Disconnect();
            Players.Clear();
            TrackedPlayerName = null;
        }

        public void Update()
        {
            bool inGame = _world.InGame;

            if (inGame && !_wasInGame && ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.UOAMAutoConnect)
            {
                Connect();
            }
            else if (!inGame && _wasInGame)
            {
                Disconnect();
            }

            _wasInGame = inGame;

            if (!inGame)
            {
                return;
            }

            _client.Update();

            if (!_client.IsConnected)
            {
                return;
            }

            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null)
            {
                return;
            }

            uint now = Time.Ticks;

            if (now >= _nextPositionSend)
            {
                _nextPositionSend = now + PositionIntervalMs;
                _client.SendPositionUpdate(profile.UOAMPassword, profile.UOAMPlayerName, _world.Player.X, _world.Player.Y, _world.Player.Z, _world.MapIndex);
            }

            if (now >= _nextChatPoll)
            {
                _nextChatPoll = now + ChatPollIntervalMs;
                _client.SendChatPoll(profile.UOAMPassword);
            }

            if (now >= _nextListQuery)
            {
                _nextListQuery = now + ListQueryIntervalMs;
                _client.SendQueryList(profile.UOAMPassword);
            }
        }

        public void SendChatText(string text)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || !_client.IsConnected)
            {
                return;
            }

            _client.SendChatText(profile.UOAMPassword, text);
        }
    }
}
```

Before finalizing, confirm the exact member names used here exist: `World.MapIndex` (or the correct current-facet accessor) and `PlayerMobile.X/Y/Z`, and `GameActions.Print(World, string, ushort)`'s real signature. Search the codebase for these — do not guess:

```bash
grep -n "MapIndex" src/ClassicUO.Client/Game/World.cs
grep -n "public static void Print" src/ClassicUO.Client/Game/GameActions.cs
```

Adjust the field/method names in the code above to match what these searches return (e.g. if the map index lives on `World.Map.Index` instead of `World.MapIndex`, use that).

- [ ] **Step 2: Wire `UOAMManager` into `World`**

In `src/ClassicUO.Client/Game/World.cs`, after line 54 (`StatusbarColorManager = new StatusbarColorManager(this);`) add:

```csharp
            UOAM = new UOAMManager(this);
```

After line 63 (`public WorldMapEntityManager WMapManager { get; }`) add:

```csharp
        public UOAMManager UOAM { get; }
```

In `World.Update()` (starts at line 288), add at the very top of the method body (before the `Managers.PluginTimersManager.Update` line):

```csharp
            UOAM.Update();
```

In `World.Clear()` (starts at line 813), add alongside the other manager `.Clear()` calls (near line 846, next to `WMapManager.Clear();`):

```csharp
            UOAM.Disconnect();
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds. Fix any member-name mismatches found in Step 1's verification.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/UOAMManager.cs src/ClassicUO.Client/Game/World.cs
git commit -m "feat(uoam): add UOAMManager lifecycle/polling orchestrator, wire into World"
```

---

## Task 8: Macro support — `UOAMConnect` / `UOAMDisconnect`

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/MacroManager.cs`

**Interfaces:**
- Consumes: `World.UOAM.Connect()` / `World.UOAM.Disconnect()` (Task 7).
- Produces: two new `MacroType` enum values; the macro editor UI (`MacroControl.cs`) picks these up automatically via `Enum.GetNames(typeof(MacroType))` (confirmed at `MacroManager.cs:25` and `MacroControl.cs:18` — no separate registration needed).

- [ ] **Step 1: Add the enum values**

In `src/ClassicUO.Client/Game/Managers/MacroManager.cs`, in the `MacroType` enum (starts at line 2273), add two entries after `UseCounterBarSlot` (the last one, line 2354):

```csharp
        UseCounterBarSlot,
        UOAMConnect,
        UOAMDisconnect
```

- [ ] **Step 2: Add the case handling**

In the macro execution switch (the one containing `case MacroType.ToggleChatVisibility:` around line 1507), add:

```csharp
                case MacroType.UOAMConnect:
                    _world.UOAM.Connect();

                    break;

                case MacroType.UOAMDisconnect:
                    _world.UOAM.Disconnect();

                    break;
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/MacroManager.cs
git commit -m "feat(uoam): add UOAMConnect/UOAMDisconnect macros"
```

---

## Task 9: `OptionsGump` — UOAM tab

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs`

**Interfaces:**
- Consumes: `Profile.UOAM*` properties (Task 5), `UOAMNameAliasSerializer` (Task 5), `World.UOAM.Connect()`/`Disconnect()`/`IsConnected` (Task 7).
- Produces: a new page (`ButtonParameter = 16`) with fields for name/IP/port/password, autoconnect/autoopenchat checkboxes, connect/disconnect button, and an editable alias row list.

This task follows the exact structural pattern of `BuildAliases()` (`OptionsGump.cs:3275`, for the row-list part) and `BuildSounds()` (`OptionsGump.cs:1366`, for the plain-field part).

- [ ] **Step 1: Add the tab button**

In the `OptionsGump` constructor, after the block adding the `ResGumps.IgnoreListManager` button (ends at `OptionsGump.cs:400`) and before the `Add(new Line(...))` divider at line 402, add:

```csharp

            Add
            (
                new NiceButton
                (
                    10,
                    10 + 30 * i++,
                    140,
                    25,
                    ButtonAction.SwitchPage,
                    "UOAM"
                ) { ButtonParameter = 16 }
            );
```

- [ ] **Step 2: Call the new build method**

After `BuildStatusBars();` (line 479, the last call in the constructor's build-method list), add:

```csharp
            BuildUOAM();
```

- [ ] **Step 3: Add the `UOAMAliasRow` nested control**

Add this new private class inside `OptionsGump` (place it near `AliasEntryControl`'s definition — search `class AliasEntryControl` to find the right spot and match its structural style: a `Control` subclass with two `StbTextBox`/`InputField`-style children and a remove button):

```csharp
        private class UOAMAliasRow : Control
        {
            public InputField UoamNameField { get; }
            public InputField CharNameField { get; }

            public event EventHandler RemoveRequested;

            public UOAMAliasRow(OptionsGump gump, string uoamName, string charName)
            {
                WantUpdateSize = false;
                Height = 24;

                UoamNameField = gump.AddInputField(null, 0, 0, 150, 20);
                UoamNameField.SetText(uoamName ?? string.Empty);
                Add(UoamNameField);

                Label arrow = new Label("->", true, HUE_FONT, 0, FONT) { X = 155, Y = 2 };
                Add(arrow);

                CharNameField = gump.AddInputField(null, 175, 0, 150, 20);
                CharNameField.SetText(charName ?? string.Empty);
                Add(CharNameField);

                NiceButton removeButton = new NiceButton(330, 0, 60, 20, ButtonAction.Activate, "Remove") { ButtonParameter = 999 };
                removeButton.MouseUp += (s, e) => RemoveRequested?.Invoke(this, EventArgs.Empty);
                Add(removeButton);
            }
        }
```

`AddInputField`'s signature (`OptionsGump.cs:5172`) takes an `area` parameter that, when non-null, also adds a label via `AddLabel` and adds the field to that `ScrollArea` — passing `null` here means the caller (`BuildUOAM`, added to a `DataBox` row instead) must `Add()` the returned field itself, which the code above already does. Confirm this by reading `AddInputField`'s body in full before relying on `area: null` being safe (it must not `NullReferenceException` when `area` is `null` — check the `if (area != null)` guard already seen at `OptionsGump.cs:5204`).

- [ ] **Step 4: Add `BuildUOAM()`**

```csharp
        private Checkbox _uoamAutoConnect, _uoamAutoOpenChat;
        private InputField _uoamPlayerName, _uoamServerIP, _uoamServerPort, _uoamPassword;
        private NiceButton _uoamConnectButton;
        private DataBox _uoamAliasDataBox;

        private void BuildUOAM()
        {
            const int PAGE = 16;

            ScrollArea rightArea = new ScrollArea(190, 20, WIDTH - 210, 420, true);

            int y = 5;

            _uoamPlayerName = AddInputField(rightArea, 5, y, 200, 20, "Player name", set_down: true);
            _uoamPlayerName.SetText(_currentProfile.UOAMPlayerName);
            y = _uoamPlayerName.Y + _uoamPlayerName.Height + 8;

            _uoamServerIP = AddInputField(rightArea, 5, y, 200, 20, "Server IP", set_down: true);
            _uoamServerIP.SetText(_currentProfile.UOAMServerIP);
            y = _uoamServerIP.Y + _uoamServerIP.Height + 8;

            _uoamServerPort = AddInputField(rightArea, 5, y, 100, 20, "Server port", set_down: true, numbersOnly: true);
            _uoamServerPort.SetText(_currentProfile.UOAMServerPort.ToString());
            y = _uoamServerPort.Y + _uoamServerPort.Height + 8;

            _uoamPassword = AddInputField(rightArea, 5, y, 200, 20, "Password", set_down: true);
            _uoamPassword.SetText(_currentProfile.UOAMPassword);
            y = _uoamPassword.Y + _uoamPassword.Height + 8;

            _uoamAutoConnect = AddCheckBox(rightArea, "Auto-connect", _currentProfile.UOAMAutoConnect, 5, y);
            y += _uoamAutoConnect.Height + 4;

            _uoamAutoOpenChat = AddCheckBox(rightArea, "Auto-open chat", _currentProfile.UOAMAutoOpenChat, 5, y);
            y += _uoamAutoOpenChat.Height + 8;

            _uoamConnectButton = new NiceButton(5, y, 120, 25, ButtonAction.Activate, World.UOAM.IsConnected ? "Disconnect" : "Connect") { ButtonParameter = 999 };
            _uoamConnectButton.MouseUp += (s, e) =>
            {
                if (World.UOAM.IsConnected)
                {
                    World.UOAM.Disconnect();
                }
                else
                {
                    ApplyUOAMProfileFields();
                    World.UOAM.Connect();
                }

                _uoamConnectButton.TextLabel.Text = World.UOAM.IsConnected ? "Disconnect" : "Connect";
            };
            rightArea.Add(_uoamConnectButton);
            y += _uoamConnectButton.Height + 12;

            Label aliasLabel = AddLabel(rightArea, "Name aliases (UOAM name -> character name):", 5, y);
            y += aliasLabel.Height + 4;

            NiceButton addAliasButton = new NiceButton(5, y, 100, 22, ButtonAction.Activate, "+ Add") { ButtonParameter = 999 };
            rightArea.Add(addAliasButton);
            y += addAliasButton.Height + 4;

            _uoamAliasDataBox = new DataBox(0, y, 0, 0) { WantUpdateSize = true };

            void AddAliasRow(string uoamName, string charName)
            {
                var row = new UOAMAliasRow(this, uoamName, charName) { Y = _uoamAliasDataBox.Children.Count * 26 };
                row.RemoveRequested += (s, e) =>
                {
                    _uoamAliasDataBox.Remove(row);
                    _uoamAliasDataBox.ReArrangeChildren();
                };
                _uoamAliasDataBox.Add(row);
            }

            foreach (var kvp in UOAMNameAliasSerializer.Parse(_currentProfile.UOAMNameAliases))
            {
                AddAliasRow(kvp.Key, kvp.Value);
            }

            addAliasButton.MouseUp += (s, e) =>
            {
                AddAliasRow(string.Empty, string.Empty);
                _uoamAliasDataBox.ReArrangeChildren();
            };

            rightArea.Add(_uoamAliasDataBox);

            Add(rightArea, PAGE);
        }

        private void ApplyUOAMProfileFields()
        {
            _currentProfile.UOAMPlayerName = _uoamPlayerName.Text;
            _currentProfile.UOAMServerIP = _uoamServerIP.Text;

            if (ushort.TryParse(_uoamServerPort.Text, out ushort port))
            {
                _currentProfile.UOAMServerPort = port;
            }

            _currentProfile.UOAMPassword = _uoamPassword.Text;
        }
```

- [ ] **Step 5: Persist settings on Apply/OK**

Find the method that saves all the other `_currentProfile.X = _someControl.Y;` assignments on Apply (the one containing `_currentProfile.EnableSound = _enableSounds.IsChecked;` around `OptionsGump.cs:4587`). Add at the end of that method, before its closing brace:

```csharp

            ApplyUOAMProfileFields();
            _currentProfile.UOAMAutoConnect = _uoamAutoConnect.IsChecked;
            _currentProfile.UOAMAutoOpenChat = _uoamAutoOpenChat.IsChecked;

            var uoamAliases = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var child in _uoamAliasDataBox.Children)
            {
                if (child is UOAMAliasRow row && !string.IsNullOrWhiteSpace(row.UoamNameField.Text) && !string.IsNullOrWhiteSpace(row.CharNameField.Text))
                {
                    uoamAliases[row.UoamNameField.Text.Trim()] = row.CharNameField.Text.Trim();
                }
            }
            _currentProfile.UOAMNameAliases = UOAMNameAliasSerializer.Serialize(uoamAliases);
```

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds. If `AddInputField(null, ...)` throws or misbehaves per Step 3's caveat, adjust `UOAMAliasRow` to construct `InputField` directly (its public constructor, seen at `OptionsGump.cs:5570`, takes `(ushort backgroundGraphic, byte font, ushort hue, bool unicode, int width, int height, int maxWidthText, int maxCharsCount)` — use the same `0x0BB8, FONT, HUE_FONT, true` arguments `AddInputField` uses internally) instead of routing through `gump.AddInputField`.

- [ ] **Step 7: Manual verification**

Run the client (`dotnet run --project src/ClassicUO.Client -c Debug` or the project's normal launch path), open Options, click the new "UOAM" tab, confirm all fields render, typing in them works, Add/Remove alias rows work, Apply persists values (reopen Options and confirm fields still show what was typed).

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(uoam): add UOAM options tab"
```

---

## Task 10: `UOAMChatGump`

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Gumps/UOAMChatGump.cs`

**Interfaces:**
- Consumes: `World.UOAM.SendChatText(string)`, `World.UOAM.ChatTextReceived` event (Task 7), `Profile.UOAMAutoOpenChat` (Task 5).
- Produces: `internal class UOAMChatGump : Gump`, visually a smaller clone of `JournalGump` (`ExpandableScroll` + a scrolling entry list) with an `StbTextBox` input row pinned to the bottom.

This does **not** reuse `JournalGump`'s private nested `RenderedTextList` (it is `private` to that class, and its `CanBeDrawn`/`TextType` filtering is journal-specific and not needed here). Instead it defines a smaller purpose-built list control in the same file.

- [ ] **Step 1: Write the implementation**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Utility.Collections;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    internal class UOAMChatGump : Gump
    {
        private const int DIFF_Y = 22;
        private readonly ExpandableScroll _background;
        private readonly UOAMChatEntryList _entries;
        private readonly ScrollFlag _scrollBar;
        private readonly StbTextBox _inputBox;

        public UOAMChatGump(World world) : base(world, 0, 0)
        {
            Width = 320;
            Height = 260;
            CanMove = true;
            CanCloseWithRightClick = true;

            Add
            (
                _background = new ExpandableScroll(0, DIFF_Y, Height - DIFF_Y, 0x1F40)
                {
                    TitleGumpID = 0x82A
                }
            );

            _scrollBar = new ScrollFlag(-25, DIFF_Y + 10, Height - DIFF_Y - 30, true);

            Add
            (
                _entries = new UOAMChatEntryList(15, DIFF_Y + 10, Width - 45, Height - DIFF_Y - 40, _scrollBar)
            );

            Add(_scrollBar);

            _inputBox = new StbTextBox(ProfileManager.CurrentProfile.ChatFont, 200, Width - 30, true, FontStyle.BlackBorder, 0)
            {
                X = 15,
                Y = Height - 26,
                Width = Width - 30,
                Height = 20
            };
            Add(_inputBox);

            World.UOAM.ChatTextReceived += World_UOAM_ChatTextReceived;
        }

        public override void OnKeyboardReturn(int textID, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            World.UOAM.SendChatText(text);
            _entries.AddEntry($"{ProfileManager.CurrentProfile.UOAMPlayerName}: {text}");
            _inputBox.SetText(string.Empty);
        }

        private void World_UOAM_ChatTextReceived(string text)
        {
            _entries.AddEntry(text);
        }

        public override void Dispose()
        {
            World.UOAM.ChatTextReceived -= World_UOAM_ChatTextReceived;
            base.Dispose();
        }
    }

    // A minimal, purpose-built scrolling text list for UOAM chat. Not shared with
    // JournalGump's private RenderedTextList: that class carries journal-specific
    // TextType filtering (System/Object/Client/GuildAlly checkboxes) this gump has no
    // use for. Structure (Deque<RenderedText> + ScrollBarBase-driven partial-entry
    // clipping) mirrors it because the visual behavior should match.
    internal class UOAMChatEntryList : Control
    {
        private readonly Deque<RenderedText> _entries;
        private readonly ScrollBarBase _scrollBar;

        public UOAMChatEntryList(int x, int y, int width, int height, ScrollBarBase scrollBarControl)
        {
            _scrollBar = scrollBarControl;
            _scrollBar.IsVisible = false;
            AcceptMouseInput = true;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            _entries = new Deque<RenderedText>();
            WantUpdateSize = false;
        }

        public void AddEntry(string text)
        {
            bool maxScroll = _scrollBar.Value == _scrollBar.MaxValue;

            while (_entries.Count > 199)
            {
                _entries.RemoveFromFront().Destroy();
            }

            RenderedText rtext = RenderedText.Create(text, 0, 1, true, FontStyle.Indention | FontStyle.BlackBorder, maxWidth: Width);
            _entries.AddToBack(rtext);

            _scrollBar.MaxValue += rtext.Height;

            if (maxScroll)
            {
                _scrollBar.Value = _scrollBar.MaxValue;
            }
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            float layerDepth = layerDepthRef;

            int mx = x;
            int my = y;
            int height = 0;
            int maxheight = _scrollBar.Value + _scrollBar.Height;

            renderLists.AddGumpNoAtlas(batcher =>
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    RenderedText t = _entries[i];

                    if (height + t.Height <= _scrollBar.Value)
                    {
                        height += t.Height;
                        continue;
                    }

                    if (height + t.Height <= maxheight)
                    {
                        int yy = height - _scrollBar.Value;

                        if (yy < 0)
                        {
                            t.Draw(batcher, t.Width, t.Height, mx, y, t.Width, t.Height + yy, 0, -yy, layerDepth);
                            my += t.Height + yy;
                        }
                        else
                        {
                            t.Draw(batcher, mx, my, layerDepth);
                            my += t.Height;
                        }

                        height += t.Height;
                    }
                    else
                    {
                        break;
                    }
                }

                return true;
            });

            return true;
        }

        public override void Update()
        {
            base.Update();

            if (!IsVisible)
            {
                return;
            }

            _scrollBar.X = X + Width - (_scrollBar.Width >> 1) + 5;
            _scrollBar.Height = Height;

            int totalHeight = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                totalHeight += _entries[i].Height;
            }

            bool wasMax = _scrollBar.Value == _scrollBar.MaxValue;
            int max = totalHeight - _scrollBar.Height;
            _scrollBar.MaxValue = max > 0 ? max : 0;

            if (wasMax)
            {
                _scrollBar.Value = _scrollBar.MaxValue;
            }

            _scrollBar.IsVisible = _scrollBar.MaxValue > _scrollBar.MinValue;
        }
    }
}
```

Before writing this, confirm the exact `RenderedText.Create` overload signature and the `ScrollFlag`/`ScrollBarBase` constructor/members used (`Value`, `MaxValue`, `MinValue`, `Height`, `X`, `Width`, `IsVisible`) by reading `JournalGump.cs`'s equivalent lines (already read: `RenderedTextList` constructor and `AddEntry`/`AddToRenderLists`/`Update` at `JournalGump.cs:357-620`) — the code above already matches that exact API usage, but re-check field/property names compile cleanly in Step 2 and fix any mismatch (e.g. if `Deque<T>.RemoveFromFront()` doesn't return the removed item, adjust accordingly).

Also confirm `ChatFont` exists on `Profile` (used identically in `SystemChatControl.cs:69`, so it should) and `Gump`'s base constructor signature `Gump(World world, int x, int y)` (used identically throughout, e.g. `JournalGump.cs:27`).

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds. Fix any API mismatches surfaced.

- [ ] **Step 3: Wire auto-open and manual open**

In `UOAMManager.Connect()` (Task 7, `UOAMManager.cs`), after the `_client.Connect(...)` call succeeds is not directly observable synchronously (connection is async) — instead hook it off the `_client.Connected` event already present in `UOAMManager`'s constructor. Modify that lambda:

```csharp
            _client.Connected += () =>
            {
                GameActions.Print(_world, "UOAM: connected", 0x2B);

                if (ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.UOAMAutoOpenChat && UIManager.GetGump<UOAMChatGump>() == null)
                {
                    UIManager.Add(new UOAMChatGump(_world));
                }
            };
```

Add `using ClassicUO.Game.UI.Gumps;` to `UOAMManager.cs`'s usings.

Manual open: add a `MacroSubType`-style open path is out of scope (spec didn't ask for it) — the only requirement was auto-open on connect, already covered above, plus opening it from the WorldMap context menu is covered in Task 12.

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 5: Manual verification**

Run the client, connect to a locally-running `uoamhub` (see `E:\Projects\uoamhub\README` for build/run instructions) with `UOAMAutoOpenChat` enabled in the new Options tab, confirm the chat gump opens on connect, typing text and pressing Enter sends it (verify via a second connected UOAM client, e.g. run two instances of ClassicUO against the same hub with different names), and that the input box clears after sending.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/UOAMChatGump.cs src/ClassicUO.Client/Game/Managers/UOAMManager.cs
git commit -m "feat(uoam): add UOAM chat gump with auto-open on connect"
```

---

## Task 11: `WorldMapGump` — draw UOAM player markers

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/WorldMapGump.cs`
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs`

**Interfaces:**
- Consumes: `World.UOAM.Players.Players` (Task 7/6), `UOAMPlayerManager.IsDuplicateOfPartyOrGuild` (Task 6), `World.WMapManager` (existing), `UOAMNameAliasSerializer.Parse` (Task 5).
- Produces: a `bool WorldMapShowUOAMPlayers` profile property + `_options["show_uoam_players"]` context menu entry + marker drawing in the same render pass as party/guild markers.

- [ ] **Step 1: Add the profile property**

In `Profile.cs`, in the block added by Task 5, add:

```csharp
        public bool WorldMapShowUOAMPlayers { get; set; } = true;
```

- [ ] **Step 2: Locate the party marker draw call**

Read `WorldMapGump.cs` around line 2223-2290 (the `if (_showPartyMembers)` block that loops `World.Party.Members` and calls `DrawMobile`) to get the exact `DrawMobile` call signature and the surrounding rendering context (this code runs inside a batch-drawing closure, same as `UOAMChatEntryList.AddToRenderLists` above — confirm parameter types before writing Step 3).

```bash
grep -n "private void DrawMobile" -A 30 src/ClassicUO.Client/Game/UI/Gumps/WorldMapGump.cs
```

- [ ] **Step 3: Add a private field for the toggle and load it in the constructor**

Near `private bool _showPartyMembers = true;` (`WorldMapGump.cs:84`), add:

```csharp
        private bool _showUOAMPlayers = true;
```

Near `_showPartyMembers = ProfileManager.CurrentProfile.WorldMapShowParty;` (`WorldMapGump.cs:186`), add:

```csharp
            _showUOAMPlayers = ProfileManager.CurrentProfile.WorldMapShowUOAMPlayers;
```

Near `ProfileManager.CurrentProfile.WorldMapShowParty = _showPartyMembers;` (`WorldMapGump.cs:230`, the settings-save method), add:

```csharp
            ProfileManager.CurrentProfile.WorldMapShowUOAMPlayers = _showUOAMPlayers;
```

- [ ] **Step 4: Add the context menu toggle entry**

In `BuildOptionDictionary()` (`WorldMapGump.cs:265`), after the `_options["show_party_members"]` block (ends at line 310), add:

```csharp

            _options["show_uoam_players"] = new ContextMenuItemEntry(
                "Show UOAM players",
                () => { _showUOAMPlayers = !_showUOAMPlayers; SaveSettings(); },
                true,
                _showUOAMPlayers
            );
```

In `BuildContextMenu()`, add it next to `_options["show_party_members"]`'s consumer — search where `namesHpBarEntry.Add(_options["show_party_name"]);` etc. are wired (`WorldMapGump.cs:495-501`) and add a sibling entry there or directly onto `ContextMenu`:

```csharp
            ContextMenu.Add(_options["show_uoam_players"]);
```

(place it directly after `ContextMenu.Add(namesHpBarEntry);` at `WorldMapGump.cs:501`)

- [ ] **Step 5: Draw the markers**

In the same drawing method that contains the `if (_showPartyMembers)` block (found in Step 2), add a new block immediately after it:

```csharp

            if (_showUOAMPlayers && World.UOAM != null)
            {
                var aliases = UOAMNameAliasSerializer.Parse(ProfileManager.CurrentProfile.UOAMNameAliases);

                foreach (var kvp in World.UOAM.Players.Players)
                {
                    UOAMPlayerInfo uoamPlayer = kvp.Value;
                    aliases.TryGetValue(uoamPlayer.Name, out string aliasedName);

                    if (UOAMPlayerManager.IsDuplicateOfPartyOrGuild(uoamPlayer, aliasedName, World.WMapManager, toleranceTiles: 8))
                    {
                        continue;
                    }

                    bool sameFacet = uoamPlayer.Map == World.Map.Index;
                    int drawX = sameFacet ? uoamPlayer.X : 0;
                    int drawY = sameFacet ? uoamPlayer.Y : 0;
                    Color nameColor = sameFacet ? Color.Cyan : Color.Gray;

                    DrawMobile
                    (
                        // match the exact parameter list read in Step 2, substituting:
                        // - position: drawX, drawY
                        // - display name: uoamPlayer.Name
                        // - name color: nameColor
                        // - marker color distinct from party (e.g. Color.Cyan for the dot itself)
                    );
                }
            }
```

Fill in the actual `DrawMobile(...)` call using the real parameter list from Step 2 — this plan cannot hardcode it without having read the live signature first. Add `using ClassicUO.Network.UOAM;` to `WorldMapGump.cs`'s usings for `UOAMPlayerInfo`.

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 7: Manual verification**

Run two ClassicUO instances against a local `uoamhub` and a shared (or no) game server, confirm each sees the other as a marker on the World Map, confirm toggling "Show UOAM players" in the right-click menu hides/shows them, confirm a player on a different facet renders at (0,0) with a grey name.

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/WorldMapGump.cs src/ClassicUO.Client/Configuration/Profile.cs
git commit -m "feat(uoam): draw UOAM player markers on the world map"
```

---

## Task 12: `WorldMapGump` — UOAM Players submenu, Track Player, Connect/Disconnect entry

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/WorldMapGump.cs`

**Interfaces:**
- Consumes: `World.UOAM.Players.Players`, `World.UOAM.TrackedPlayerName`, `World.UOAM.IsConnected`, `World.UOAM.Connect()`/`Disconnect()` (Task 7).
- Produces: a dynamically-rebuilt "UOAM Players" submenu (rebuilt on every right-click so the player list is current) with per-player "Track Player" radio-style entries, plus a top-level Connect/Disconnect entry; viewport centers on the tracked player each frame while tracking is active.

- [ ] **Step 1: Rebuild the context menu dynamically on right-click**

In `OnMouseUp` (`WorldMapGump.cs:3133`), add at the top of the `if (button == MouseButtonType.Right)` block (before the existing `CanvasToWorld` call):

```csharp
                BuildContextMenu();
```

This ensures the "UOAM Players" submenu reflects the current player list every time the menu opens (matches the existing full-rebuild pattern already used for marker file reload, `WorldMapGump.cs:1769`).

- [ ] **Step 2: Add the UOAM Players submenu and Connect/Disconnect entry**

In `BuildContextMenu()`, after the `BuildContextMenuForZones(ContextMenu);` call (`WorldMapGump.cs:493`), add:

```csharp

            ContextMenuItemEntry uoamEntry = new ContextMenuItemEntry("UOAM Players");

            uoamEntry.Add
            (
                new ContextMenuItemEntry
                (
                    World.UOAM.IsConnected ? "Disconnect" : "Connect",
                    () =>
                    {
                        if (World.UOAM.IsConnected)
                        {
                            World.UOAM.Disconnect();
                        }
                        else
                        {
                            World.UOAM.Connect();
                        }
                    }
                )
            );

            uoamEntry.Add(new ContextMenuItemEntry(""));

            if (World.UOAM.Players.Players.Count == 0)
            {
                uoamEntry.Add(new ContextMenuItemEntry("(no players)"));
            }
            else
            {
                foreach (var kvp in World.UOAM.Players.Players)
                {
                    string playerName = kvp.Key;
                    bool isTracked = World.UOAM.TrackedPlayerName == playerName;

                    uoamEntry.Add
                    (
                        new ContextMenuItemEntry
                        (
                            playerName,
                            () =>
                            {
                                World.UOAM.TrackedPlayerName = isTracked ? null : playerName;
                            },
                            true,
                            isTracked
                        )
                    );
                }
            }

            ContextMenu.Add(uoamEntry);
```

- [ ] **Step 3: Center the viewport on the tracked player**

Find the method that recalculates `_center` from the player's live position each frame (search for where `_center.X`/`_center.Y` are set from `World.Player` — likely in the gump's `Update()` override). Read it:

```bash
grep -n "_center.X = \|_center.Y = " src/ClassicUO.Client/Game/UI/Gumps/WorldMapGump.cs
```

At the top of that assignment (before it sets `_center` from `World.Player.X/Y`), add a branch:

```csharp
            if (!string.IsNullOrEmpty(World.UOAM.TrackedPlayerName) && World.UOAM.Players.Players.TryGetValue(World.UOAM.TrackedPlayerName, out var tracked))
            {
                _center.X = tracked.Map == World.Map.Index ? tracked.X : 0;
                _center.Y = tracked.Map == World.Map.Index ? tracked.Y : 0;
            }
            else
            {
                // existing code that sets _center from World.Player.X/Y goes here unchanged
            }
```

Fit this into the exact existing control-flow found by the `grep` above — do not duplicate the pre-existing assignment, wrap it in the `else` branch.

- [ ] **Step 4: Clear tracking when the tracked player disconnects or list updates without them**

In `UOAMPlayerManager.UpdateFromList` (Task 6, `UOAMPlayerManager.cs`) — no change needed there; `WorldMapGump`'s `TryGetValue` check in Step 3 already handles a vanished player gracefully (falls through to the `else` branch, i.e. tracking silently stops centering until the player reappears or the menu is used to turn tracking off explicitly). This matches the spec's requirement ("žiadny crash/výnimka; tracking ostáva zapnutý").

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 6: Manual verification**

Run two ClassicUO instances against a local `uoamhub`, right-click the World Map on one, open "UOAM Players", click the other player's name to enable Track Player, confirm the map viewport follows that player's position, click the same entry again to confirm it toggles off (map returns to following the local player), confirm the Connect/Disconnect entry works and its label flips correctly.

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: All tests PASS (including all UOAM tests from Tasks 1, 2, 3, 5, 6)

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/WorldMapGump.cs
git commit -m "feat(uoam): add UOAM Players submenu with Track Player and connect toggle"
```

---

## Self-Review

**Spec coverage:**
- Sieťová vrstva / protokol → Tasks 1-4. ✓
- Lifecycle (iba počas hry, autoconnect, 3 spôsoby manual connect) → Task 7 (autoconnect+lifecycle), Task 9 (options button), Task 8 (macro), Task 12 (context menu). ✓
- Options tab (všetky polia) → Task 9. ✓
- Chat (samostatný gump, text input, autoopen) → Task 10. ✓
- World map markery + toggle → Task 11. ✓
- Dedup (alias+name+position) → Task 6, wired in Task 11. ✓
- Track Player (radio, centering) → Task 12. ✓
- Panic → explicitly excluded (Global Constraints + no task adds it). ✓

**Placeholder scan:** No "TBD"/"implement later" left standing as a final instruction — the two places that say "confirm the exact signature before writing" (Task 11 Step 2/5, Task 10 Step 1) are flagged with an explicit `grep` command and told what to do with the result, not left open-ended; this is unavoidable because those exact signatures live in files this plan's author read partially (`DrawMobile`, `RenderedText.Create`) and a wrong guess would silently compile into broken rendering — the grep command is the concrete action, not a placeholder.

**Type consistency:** `UOAMPlayerInfo` (Task 1) is the type flowing through `UOAMProtocol` (Tasks 2-3) → `UOAMClient` events (Task 4) → `UOAMPlayerManager.Players`/`UpdateFromList` (Task 6) → `UOAMManager.Players` (Task 7) → `WorldMapGump` (Task 11) — same struct throughout, field names (`Name`, `X`, `Y`, `Z`, `Map`) consistent across all tasks. `UOAMPlayerInfoCodec.Size` corrected from 96 to 92 in Task 2 Step 0 before any other task depends on it.

---

**Plan complete and saved to `docs/superpowers/plans/2026-08-07-uoam-integration.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
