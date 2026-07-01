// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Game;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    /// <summary>
    /// Asset-backed. Resolves a real UO data dir from CUO_UO_TEST_DIR (default
    /// E:/Games/Ultima Online Classic) and returns early when absent so CI
    /// without assets stays green.
    /// </summary>
    public class StaticMapPassabilityIntegrationTests
    {
        private static string ResolveDir()
        {
            string dir = Environment.GetEnvironmentVariable("CUO_UO_TEST_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                dir = @"E:/Games/Ultima Online Classic";
            }
            return Directory.Exists(dir) ? dir : null;
        }

        private static UOFileManager LoadFileManager(string dir)
        {
            if (!ClientVersionHelper.TryParseFromFile(Path.Combine(dir, "client.exe"), out string vtext)
                || !ClientVersionHelper.IsClientVersionValid(vtext, out ClientVersion version))
            {
                version = ClientVersion.CV_70796;
            }

            var fm = new UOFileManager(version, dir);
            fm.Load(false, "enu");
            return fm;
        }

        [Fact]
        public void Felucca_region_map_separates_water_split_landmasses()
        {
            string dir = ResolveDir();
            if (dir == null)
            {
                return; // no data dir: skip
            }

            UOFileManager fm = LoadFileManager(dir);
            fm.Maps.LoadMap(0);

            int w = StaticMapPassability.Width(fm, 0);
            int h = StaticMapPassability.Height(fm, 0);
            Assert.Equal(7168, w);
            Assert.Equal(4096, h);

            bool[] passable = StaticMapPassability.Build(fm, 0);
            int[] ids = RegionMapBuilder.Build(w, h, (x, y) => passable[x * h + y]);
            var map = new RegionMap(0, w, h, ids);

            // Sanity: both passable and impassable tiles exist.
            bool anyPassable = false, anyBlocked = false;
            for (int i = 0; i < ids.Length && !(anyPassable && anyBlocked); i += 997)
            {
                if (ids[i] != 0) anyPassable = true; else anyBlocked = true;
            }
            Assert.True(anyPassable && anyBlocked);

            // Britain mainland vs. Moonglow (Verity Isle) — ocean-separated in
            // Felucca; scan a small window around each anchor for a passable tile.
            Assert.True(TryFindPassableNear(map, 1416, 1690, out int bx, out int by), "no passable tile near Britain");
            Assert.True(TryFindPassableNear(map, 4467, 1283, out int mx, out int my), "no passable tile near Moonglow");
            Assert.False(map.SameRegion(bx, by, mx, my), "Britain and Moonglow must be different regions");
        }

        private static bool TryFindPassableNear(RegionMap map, int cx, int cy, out int fx, out int fy)
        {
            for (int r = 0; r <= 40; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (map.RegionOf(x, y) != 0)
                        {
                            fx = x; fy = y; return true;
                        }
                    }
                }
            }
            fx = fy = 0; return false;
        }
    }
}
