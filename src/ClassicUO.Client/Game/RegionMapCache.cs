// src/ClassicUO.Client/Game/RegionMapCache.cs
// SPDX-License-Identifier: BSD-2-Clause

using System.IO;

namespace ClassicUO.Game
{
    internal static class RegionMapCache
    {
        public const uint Magic = 0x4E475243; // 'C','R','G','N'
        public const int Version = 1;

        public static void Write(Stream stream, RegionMap map)
        {
            using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            w.Write(Magic);
            w.Write(Version);
            w.Write(map.Facet);
            w.Write(map.Width);
            w.Write(map.Height);

            int[] ids = map.Ids;
            int i = 0;

            while (i < ids.Length)
            {
                int value = ids[i];
                int run = 1;

                while (i + run < ids.Length && ids[i + run] == value)
                {
                    run++;
                }

                w.Write(value);
                w.Write(run);
                i += run;
            }
        }

        public static bool TryRead(Stream stream, int expectedFacet, out RegionMap map)
        {
            map = null;

            try
            {
                using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

                if (r.ReadUInt32() != Magic || r.ReadInt32() != Version)
                {
                    return false;
                }

                int facet = r.ReadInt32();
                int width = r.ReadInt32();
                int height = r.ReadInt32();

                if (facet != expectedFacet || width <= 0 || height <= 0)
                {
                    return false;
                }

                long total = (long)width * height;
                int[] ids = new int[total];
                int i = 0;

                while (i < total)
                {
                    int value = r.ReadInt32();
                    int run = r.ReadInt32();

                    if (run <= 0 || i + run > total)
                    {
                        return false;
                    }

                    for (int j = 0; j < run; j++)
                    {
                        ids[i + j] = value;
                    }

                    i += run;
                }

                map = new RegionMap(facet, width, height, ids);
                return true;
            }
            catch (EndOfStreamException)
            {
                map = null;
                return false;
            }
        }
    }
}
