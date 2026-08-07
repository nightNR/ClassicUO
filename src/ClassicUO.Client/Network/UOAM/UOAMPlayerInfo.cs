// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Buffers.Binary;
using System.Text;

namespace ClassicUO.Network.UOAM
{
    // The 92-byte "noip" block, name[64] + reserved[12] + position[16].
    // Position layout verified against a real hub session capture
    // (map.knuckleheads.dk:2000, 2026-08-07): the 16-byte position field is
    // [3 zero/reserved bytes][1 signed byte Z][uint32 LE X][uint32 LE Y][uint32 LE Map] -
    // NOT four same-sized X/Y/Z/Map fields as originally assumed from uoamhub.c alone.
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
        private const int ZOffset = PositionOffset + 3;
        private const int XOffset = PositionOffset + 4;
        private const int YOffset = PositionOffset + 8;
        private const int MapOffset = PositionOffset + 12;

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

            dest[ZOffset] = unchecked((byte)(sbyte)z);
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(XOffset, 4), unchecked((uint)x));
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(YOffset, 4), unchecked((uint)y));
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(MapOffset, 4), unchecked((uint)map));
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
            info.Z = unchecked((sbyte)src[ZOffset]);
            info.X = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(XOffset, 4)));
            info.Y = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(YOffset, 4)));
            info.Map = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(MapOffset, 4)));

            return true;
        }
    }
}
