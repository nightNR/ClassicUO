// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace ClassicUO.Game
{
    internal static class RegionMapBuilder
    {
        private static readonly int[] OffX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] OffY = { -1, -1, 0, 1, 1, 1, 0, -1 };

        /// <summary>Label 8-connected components of the passability grid. Result
        /// is length width*height indexed x*height+y; 0 = impassable, ids from 1.</summary>
        public static int[] Build(int width, int height, Func<int, int, bool> passable)
        {
            int[] ids = new int[width * height];
            var stack = new Stack<int>(); // packed x*height+y
            int nextId = 0;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int start = x * height + y;

                    if (ids[start] != 0 || !passable(x, y))
                    {
                        continue;
                    }

                    nextId++;
                    ids[start] = nextId;
                    stack.Push(start);

                    while (stack.Count > 0)
                    {
                        int cur = stack.Pop();
                        int cx = cur / height;
                        int cy = cur % height;

                        for (int d = 0; d < 8; d++)
                        {
                            int nx = cx + OffX[d];
                            int ny = cy + OffY[d];

                            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            {
                                continue;
                            }

                            int nidx = nx * height + ny;

                            if (ids[nidx] != 0 || !passable(nx, ny))
                            {
                                continue;
                            }

                            ids[nidx] = nextId;
                            stack.Push(nidx);
                        }
                    }
                }
            }

            return ids;
        }
    }

    internal sealed class RegionMap
    {
        public RegionMap(int facet, int width, int height, int[] ids)
        {
            Facet = facet;
            Width = width;
            Height = height;
            Ids = ids;
        }

        public int Facet { get; }
        public int Width { get; }
        public int Height { get; }
        public int[] Ids { get; }

        public int RegionOf(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return 0;
            }

            return Ids[x * Height + y];
        }

        public bool SameRegion(int x1, int y1, int x2, int y2)
        {
            int a = RegionOf(x1, y1);

            return a != 0 && a == RegionOf(x2, y2);
        }
    }

    internal static class RegionGate
    {
        /// <summary>True only when the region map proves the goal cannot be
        /// reached from the start on foot. Optimistic: a null map, or an
        /// endpoint the static map marks impassable/unknown (region 0), never
        /// rejects — the live A* is left to decide.</summary>
        public static bool IsUnreachable(RegionMap map, int startX, int startY, int goalX, int goalY)
        {
            if (map == null)
            {
                return false;
            }

            int a = map.RegionOf(startX, startY);
            int b = map.RegionOf(goalX, goalY);

            if (a == 0 || b == 0)
            {
                return false;
            }

            return a != b;
        }
    }
}
