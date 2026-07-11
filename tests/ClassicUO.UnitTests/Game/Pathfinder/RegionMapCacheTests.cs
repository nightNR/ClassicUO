// tests/ClassicUO.UnitTests/Game/Pathfinder/RegionMapCacheTests.cs
// SPDX-License-Identifier: BSD-2-Clause

using System.IO;
using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class RegionMapCacheTests
    {
        [Fact]
        public void Roundtrip_preserves_ids_and_dims()
        {
            var original = new RegionMap(2, 5, 5, RegionMapBuilder.Build(5, 5, (x, y) => x != 2));

            using var ms = new MemoryStream();
            RegionMapCache.Write(ms, original);
            ms.Position = 0;

            Assert.True(RegionMapCache.TryRead(ms, 2, out RegionMap read));
            Assert.Equal(original.Width, read.Width);
            Assert.Equal(original.Height, read.Height);
            Assert.Equal(original.Facet, read.Facet);
            Assert.Equal(original.Ids, read.Ids);
        }

        [Fact]
        public void Wrong_facet_is_rejected()
        {
            var original = new RegionMap(0, 3, 3, RegionMapBuilder.Build(3, 3, (x, y) => true));
            using var ms = new MemoryStream();
            RegionMapCache.Write(ms, original);
            ms.Position = 0;

            Assert.False(RegionMapCache.TryRead(ms, 1, out RegionMap read));
            Assert.Null(read);
        }

        [Fact]
        public void Garbage_stream_is_rejected()
        {
            using var ms = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            Assert.False(RegionMapCache.TryRead(ms, 0, out RegionMap read));
            Assert.Null(read);
        }

        [Fact]
        public void Older_version_is_rejected()
        {
            // A cache written by a previous build (older Version constant) must be
            // rejected so a stale map isn't reused after a builder change.
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(RegionMapCache.Magic);
                w.Write(RegionMapCache.Version - 1);
                w.Write(0);   // facet
                w.Write(3);   // width
                w.Write(3);   // height
            }
            ms.Position = 0;

            Assert.False(RegionMapCache.TryRead(ms, 0, out RegionMap read));
            Assert.Null(read);
        }
    }
}
