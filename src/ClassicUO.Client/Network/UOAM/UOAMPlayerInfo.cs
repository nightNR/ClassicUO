// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Buffers.Binary;
using System.Text;

namespace ClassicUO.Network.UOAM
{
    // The 92-byte "noip" block from uoamhub's `struct noip_player_info`:
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
        public const int Size = 92;
        private const int NameOffset = 0;
        private const int NameSize = 64;
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
