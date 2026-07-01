// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ClassicUO.Assets;
using Microsoft.Win32.SafeHandles;

namespace ClassicUO.Game
{
    internal static class StaticMapPassability
    {
        /// <summary>True when a static tile forms a walkable floor. Mirrors the
        /// live walker (Pathfinder.CreateItemList), which treats both SURFACE and
        /// BRIDGE statics as standable — bridges/stairs are frequently flagged
        /// BRIDGE only, so omitting them walls genuinely-connected areas apart.</summary>
        public static bool IsPassableStatic(in StaticTiles sd)
        {
            return (sd.IsSurface || sd.IsBridge) && !sd.IsImpassable;
        }

        public static int Width(UOFileManager fm, int facet)
        {
            return fm.Maps.MapBlocksSize[facet, 0] << 3;
        }

        public static int Height(UOFileManager fm, int facet)
        {
            return fm.Maps.MapBlocksSize[facet, 1] << 3;
        }

        public static bool[] Build(UOFileManager fm, int facet)
        {
            // The caller is responsible for having already loaded this facet on the
            // game thread (World's MapIndex setter calls Maps.LoadMap(value, ...) with
            // the account's correct useXFiles flag before EnsureRegionMap runs). We must
            // NOT call LoadMap ourselves here: LoadMap defaults useXFiles to false, and
            // calling it off-thread with the wrong flag causes MapLoader to detect a file
            // mismatch and reallocate BlockData[facet], racing the game thread. Reading
            // BlockData/MapBlocksSize off-thread is safe once loaded: those fields are
            // immutable after load, and we never touch the shared FileReader position
            // (we open our own handles).
            int blockW = fm.Maps.MapBlocksSize[facet, 0];
            int blockH = fm.Maps.MapBlocksSize[facet, 1];
            int height = blockH << 3;

            if (fm.Maps.BlockData[facet] == null)
            {
                // Facet not loaded: never build against a null/absent block table.
                // Return all-impassable (optimism-safe: caller treats absence of a
                // passable static map as "no info", not "no dangerous restriction").
                return blockW <= 0 || blockH <= 0 ? new bool[0] : new bool[(blockW << 3) * height];
            }

            var passable = new bool[(blockW << 3) * height];

            int landCount = fm.TileData.LandData.Length;
            int staticCount = fm.TileData.StaticData.Length;

            int mapBlockSize = Marshal.SizeOf<MapBlock>();
            int staBlockSize = Marshal.SizeOf<StaticsBlock>();
            Span<byte> mapBuf = stackalloc byte[mapBlockSize];

            // Resolve handles PER BLOCK from the block's own reader path: a block that
            // MapLoader.ApplyPatches repointed to a .dif file has idx.MapFile/StaticFile
            // pointing at that dif file, with idx.MapAddress/StaticAddress as offsets INTO
            // it. Reading those offsets from a single per-facet main-file handle would
            // decode garbage for patched blocks. Cache one handle per distinct file path.
            var handles = new Dictionary<string, SafeFileHandle>();

            SafeFileHandle HandleFor(string path)
            {
                if (path == null)
                {
                    return null;
                }
                if (handles.TryGetValue(path, out SafeFileHandle h))
                {
                    return h;
                }
                h = OpenShared(path);
                handles[path] = h; // cache null too, so we don't retry a failed open
                return h;
            }

            try
            {
                for (int bx = 0; bx < blockW; bx++)
                {
                    for (int by = 0; by < blockH; by++)
                    {
                        int block = bx * blockH + by;
                        ref var idx = ref fm.Maps.BlockData[facet][block];
                        if (!idx.IsValid())
                        {
                            continue;
                        }

                        SafeFileHandle mapH = HandleFor(idx.MapFile?.FilePath);
                        if (mapH == null)
                        {
                            continue; // no/unopenable map file for this block: leave impassable (optimism-safe)
                        }

                        if (!TryReadExact(mapH, mapBuf, (long)idx.MapAddress))
                        {
                            continue; // short/failed read: leave block impassable (optimism-safe)
                        }

                        MapBlock mb = MemoryMarshal.Read<MapBlock>(mapBuf);

                        for (int mx = 0; mx < 8; mx++)
                        {
                            for (int my = 0; my < 8; my++)
                            {
                                ushort g = mb.Cells[(my << 3) + mx].TileID;
                                bool landOk = g == 0x0002 || g == 0x01DB ||
                                    (g < landCount && !fm.TileData.LandData[g].IsImpassable);
                                if (landOk)
                                {
                                    int x = (bx << 3) + mx;
                                    int y = (by << 3) + my;
                                    passable[x * height + y] = true;
                                }
                            }
                        }

                        SafeFileHandle staH = HandleFor(idx.StaticFile?.FilePath);
                        if (staH != null && idx.StaticFile != null && idx.StaticCount > 0)
                        {
                            int n = (int)idx.StaticCount;
                            int bytes = n * staBlockSize;
                            byte[] rent = ArrayPool<byte>.Shared.Rent(bytes);
                            try
                            {
                                var span = rent.AsSpan(0, bytes);
                                if (TryReadExact(staH, span, (long)idx.StaticAddress))
                                {
                                    for (int s = 0; s < n; s++)
                                    {
                                        StaticsBlock sb = MemoryMarshal.Read<StaticsBlock>(span.Slice(s * staBlockSize, staBlockSize));
                                        ushort g = sb.Color; // graphic id
                                        if (g >= staticCount)
                                        {
                                            continue;
                                        }
                                        ref readonly var sd = ref fm.TileData.StaticData[g];
                                        if (IsPassableStatic(sd))
                                        {
                                            int x = (bx << 3) + sb.X;
                                            int y = (by << 3) + sb.Y;
                                            passable[x * height + y] = true;
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(rent);
                            }
                        }
                    }
                }
            }
            finally
            {
                foreach (SafeFileHandle h in handles.Values)
                {
                    h?.Dispose();
                }
            }

            return passable;
        }

        private static bool TryReadExact(SafeFileHandle handle, Span<byte> buffer, long offset)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = RandomAccess.Read(handle, buffer.Slice(total), offset + total);
                if (read <= 0)
                {
                    return false; // short/failed read: caller treats as impassable, never throws
                }
                total += read;
            }
            return true;
        }

        private static SafeFileHandle OpenShared(string path)
        {
            try
            {
                return File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch
            {
                return null; // can't open our own handle: caller returns all-impassable (optimism-safe)
            }
        }
    }
}
