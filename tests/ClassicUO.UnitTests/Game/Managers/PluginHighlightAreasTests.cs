// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class PluginHighlightAreasTests
    {
        public PluginHighlightAreasTests()
        {
            PluginHighlightAreas.Reset();
        }

        [Fact]
        public void GetTimer_ReturnsZero_WhenIdDoesNotExist()
        {
            Assert.Equal(0, PluginHighlightAreas.GetTimer("missing", now: 0));
        }

        [Fact]
        public void GetTimer_ReturnsMaxValue_WhenAreaNeverExpires()
        {
            // Add(world, id, durationMs, snap, anchorSerial, hue, rangeX, rangeY, objectTypes, x, y, now)
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x21, 3, 3, HighlightObjectTypes.All, 100, 100, 0);

            Assert.Equal(int.MaxValue, PluginHighlightAreas.GetTimer("a", 999999));
        }

        [Fact]
        public void GetTimer_ReturnsRemainingMs_ForTimedArea()
        {
            PluginHighlightAreas.Add(null, "a", 5000, HighlightSnap.Position, 0, (ushort)0x21, 3, 3, HighlightObjectTypes.All, 100, 100, 1000);

            Assert.Equal(3000, PluginHighlightAreas.GetTimer("a", 3000));
        }

        [Fact]
        public void Update_RemovesExpiredArea()
        {
            PluginHighlightAreas.Add(null, "a", 1000, HighlightSnap.Position, 0, (ushort)0x21, 3, 3, HighlightObjectTypes.All, 100, 100, 0);

            PluginHighlightAreas.Update(null, 1500);

            Assert.Equal(0, PluginHighlightAreas.GetTimer("a", 1500));
        }

        [Fact]
        public void TryResolve_MatchesPositionSnap_WithinRange()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0099, 3, 3, HighlightObjectTypes.Land, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(102, 101, 0, HighlightObjectTypes.Land, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0099, hue);
        }

        [Fact]
        public void TryResolve_ReturnsFalse_OutsideRange()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0099, 3, 3, HighlightObjectTypes.Land, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(200, 200, 0, HighlightObjectTypes.Land, out _);

            Assert.False(found);
        }

        [Fact]
        public void TryResolve_ReturnsFalse_WhenObjectTypeNotFlagged()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0099, 3, 3, HighlightObjectTypes.Mobile, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.Land, out _);

            Assert.False(found);
        }

        [Fact]
        public void TryResolve_LastAddedWins_OnOverlap()
        {
            PluginHighlightAreas.Add(null, "first", -1, HighlightSnap.Position, 0, (ushort)0x0011, 5, 5, HighlightObjectTypes.Land, 100, 100, 0);
            PluginHighlightAreas.Add(null, "second", -1, HighlightSnap.Position, 0, (ushort)0x0022, 5, 5, HighlightObjectTypes.Land, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.Land, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0022, hue);
        }

        [Fact]
        public void Add_SerialSnap_ResolvesCenterFromSerialResolver()
        {
            PluginHighlightAreas.SerialResolver = serial => serial == 0xAAAA ? (true, 50, 60, (sbyte)0) : (false, 0, 0, (sbyte)0);

            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Serial, 0xAAAA, (ushort)0x0033, 2, 2, HighlightObjectTypes.Mobile, 0, 0, 0);

            bool found = PluginHighlightAreas.TryResolve(51, 60, 0, HighlightObjectTypes.Mobile, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0033, hue);
        }

        [Fact]
        public void Update_SerialSnap_AutoRemovesWhenAnchorLost()
        {
            PluginHighlightAreas.SerialResolver = _ => (true, 50, 60, (sbyte)0);
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Serial, 0xAAAA, (ushort)0x0033, 2, 2, HighlightObjectTypes.Mobile, 0, 0, 0);

            PluginHighlightAreas.SerialResolver = _ => (false, 0, 0, (sbyte)0);
            PluginHighlightAreas.Update(null, 1);

            Assert.Equal(0, PluginHighlightAreas.GetTimer("a", 1));
        }

        [Fact]
        public void Add_MouseSnap_ResolvesCenterFromMouseWorldResolver()
        {
            PluginHighlightAreas.MouseWorldResolver = () => (77, 88, (sbyte)0);

            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Mouse, 0, (ushort)0x0044, 1, 1, HighlightObjectTypes.Static, 0, 0, 0);

            bool found = PluginHighlightAreas.TryResolve(77, 88, 0, HighlightObjectTypes.Static, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0044, hue);
        }

        [Fact]
        public void Remove_DropsArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0011, 5, 5, HighlightObjectTypes.All, 100, 100, 0);

            PluginHighlightAreas.Remove("a");

            Assert.False(PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.All, out _));
        }

        [Fact]
        public void ClearAll_DropsEveryArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0011, 5, 5, HighlightObjectTypes.All, 100, 100, 0);
            PluginHighlightAreas.Add(null, "b", -1, HighlightSnap.Position, 0, (ushort)0x0022, 5, 5, HighlightObjectTypes.All, 200, 200, 0);

            PluginHighlightAreas.ClearAll();

            Assert.False(PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.All, out _));
            Assert.False(PluginHighlightAreas.TryResolve(200, 200, 0, HighlightObjectTypes.All, out _));
        }

        // --- Spatial index (chunk-bucketed lookup) ---
        // These prove TryResolve stays correct once matching is bucketed by chunk
        // instead of a full linear scan — the fix for thousands of concurrently
        // registered areas (e.g. a mass point-of-interest file) freezing the client.

        [Fact]
        public void TryResolve_FindsMatch_AcrossFarApartAreas_WithoutCrossContamination()
        {
            PluginHighlightAreas.Add(null, "near-origin", -1, HighlightSnap.Position, 0, (ushort)0x0011, 3, 3, HighlightObjectTypes.Land, 100, 100, 0);
            PluginHighlightAreas.Add(null, "far-away", -1, HighlightSnap.Position, 0, (ushort)0x0022, 3, 3, HighlightObjectTypes.Land, 5000, 5000, 0);

            bool foundNear = PluginHighlightAreas.TryResolve(101, 99, 0, HighlightObjectTypes.Land, out ushort hueNear);
            bool foundFar = PluginHighlightAreas.TryResolve(5001, 5002, 0, HighlightObjectTypes.Land, out ushort hueFar);
            bool foundBetween = PluginHighlightAreas.TryResolve(2500, 2500, 0, HighlightObjectTypes.Land, out _);

            Assert.True(foundNear);
            Assert.Equal((ushort)0x0011, hueNear);
            Assert.True(foundFar);
            Assert.Equal((ushort)0x0022, hueFar);
            Assert.False(foundBetween);
        }

        [Fact]
        public void TryResolve_MatchesAcrossChunkBoundary_WhenAreaBboxStraddlesIt()
        {
            // Chunk size is 8 tiles. Centering exactly on a chunk boundary (x=104
            // is the start of a new chunk) with range 3 makes the bbox (101..107)
            // straddle two chunks — both must be indexed, or the tile just past
            // the boundary would silently miss the match.
            PluginHighlightAreas.Add(null, "boundary", -1, HighlightSnap.Position, 0, (ushort)0x0055, 3, 3, HighlightObjectTypes.Land, 104, 104, 0);

            bool foundJustBefore = PluginHighlightAreas.TryResolve(102, 104, 0, HighlightObjectTypes.Land, out ushort hueBefore);
            bool foundJustAfter = PluginHighlightAreas.TryResolve(106, 104, 0, HighlightObjectTypes.Land, out ushort hueAfter);

            Assert.True(foundJustBefore);
            Assert.Equal((ushort)0x0055, hueBefore);
            Assert.True(foundJustAfter);
            Assert.Equal((ushort)0x0055, hueAfter);
        }

        [Fact]
        public void Update_SerialSnap_ReindexesWhenAnchorCrossesChunkBoundary()
        {
            PluginHighlightAreas.SerialResolver = _ => (true, 100, 100, (sbyte)0);
            PluginHighlightAreas.Add(null, "follow", -1, HighlightSnap.Serial, 0xAAAA, (ushort)0x0066, 2, 2, HighlightObjectTypes.Mobile, 0, 0, 0);

            Assert.True(PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.Mobile, out _));

            // Move the anchor far enough to land in a completely different chunk.
            PluginHighlightAreas.SerialResolver = _ => (true, 900, 900, (sbyte)0);
            PluginHighlightAreas.Update(null, 1);

            bool stillAtOldSpot = PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.Mobile, out _);
            bool foundAtNewSpot = PluginHighlightAreas.TryResolve(900, 900, 0, HighlightObjectTypes.Mobile, out ushort hue);

            Assert.False(stillAtOldSpot);
            Assert.True(foundAtNewSpot);
            Assert.Equal((ushort)0x0066, hue);
        }

        [Fact]
        public void TryResolve_StaysCorrect_WithManyScatteredAreas()
        {
            for (int i = 0; i < 500; i++)
            {
                int x = i * 20;
                int y = i * 20;
                PluginHighlightAreas.Add(null, $"area-{i}", -1, HighlightSnap.Position, 0, (ushort)(i + 1), 3, 3, HighlightObjectTypes.Land, x, y, 0);
            }

            bool found250 = PluginHighlightAreas.TryResolve(250 * 20, 250 * 20, 0, HighlightObjectTypes.Land, out ushort hue250);
            bool foundGap = PluginHighlightAreas.TryResolve(250 * 20 + 10, 250 * 20, 0, HighlightObjectTypes.Land, out _);

            Assert.True(found250);
            Assert.Equal((ushort)251, hue250);
            Assert.False(foundGap);
        }
    }
}
