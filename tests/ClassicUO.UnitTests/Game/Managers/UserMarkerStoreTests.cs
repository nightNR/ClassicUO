// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using System.Linq;
using ClassicUO.Game.Managers;
using Xunit;
using static ClassicUO.Game.UI.Gumps.WorldMapGump;

namespace ClassicUO.UnitTests.Game.Managers
{
    public sealed class UserMarkerStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "cuo_markers_" + Guid.NewGuid().ToString("N"));
        private string Json => Path.Combine(_dir, "userMarkers.json");
        private string Usr => Path.Combine(_dir, "userMarkers.usr");

        public UserMarkerStoreTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static WMapMarker M(string name, int x = 1, int y = 2, int map = 0)
        {
            return new WMapMarker { Name = name, X = x, Y = y, MapId = map, ColorName = "red" };
        }

        [Fact]
        public void Load_NoFiles_CreatesDefaultCategory()
        {
            var s = UserMarkerStore.Load(Json);

            Assert.Single(s.Categories);
            Assert.Equal(UserMarkerStore.DEFAULT_CATEGORY_NAME, s.Categories[0].Name);
            Assert.True(File.Exists(Json));
        }

        [Fact]
        public void Load_LegacyUsr_MigratesAndRetiresLegacyFile()
        {
            File.WriteAllLines(Usr, new[]
            {
                "100,200,0,Bank,exit,blue,4",
                "garbage line",
                "300,400,1,Inn,,red,4",
            });

            var s = UserMarkerStore.Load(Json, Usr);

            Assert.Single(s.Categories);
            Assert.Equal(2, s.Categories[0].Markers.Count);
            Assert.Equal("Bank", s.Categories[0].Markers[0].Name);
            Assert.Equal(1, s.Categories[0].Markers[1].MapId);
            Assert.False(File.Exists(Usr));
            Assert.True(File.Exists(Usr + ".bak"));

            // second load reads the JSON, not the .bak
            var again = UserMarkerStore.Load(Json, Usr);
            Assert.Equal(2, again.Categories[0].Markers.Count);
        }

        [Fact]
        public void RoundTrip_PersistsCategoriesAndMarkerState()
        {
            var s = UserMarkerStore.Load(Json);
            var cat = s.AddCategory("Dungeons", "skull", "red");
            var m = M("Despise");
            m.Note = "north entrance";
            m.Enabled = false;
            s.AddMarker(cat.Id, m);

            var r = UserMarkerStore.Load(Json);

            Assert.Equal(2, r.Categories.Count);
            MarkerCategory rc = r.Categories[1];
            Assert.Equal("Dungeons", rc.Name);
            Assert.Equal("skull", rc.IconName);
            WMapMarker rm = Assert.Single(rc.Markers);
            Assert.Equal("north entrance", rm.Note);
            Assert.False(rm.Enabled);
            Assert.Equal(m.Id, rm.Id);
        }

        [Fact]
        public void CorruptJson_IsKeptAsCopy_AndStoreStartsFresh()
        {
            File.WriteAllText(Json, "{ not json");

            var s = UserMarkerStore.Load(Json);

            Assert.Single(s.Categories);
            Assert.True(File.Exists(Json + ".corrupt"));
        }

        [Fact]
        public void AddCategory_MakesNamesUnique()
        {
            var s = UserMarkerStore.Load(Json);

            var a = s.AddCategory("Towns");
            var b = s.AddCategory("towns");

            Assert.Equal("Towns", a.Name);
            Assert.Equal("towns (2)", b.Name);
        }

        [Fact]
        public void MoveMarkers_BetweenCategories()
        {
            var s = UserMarkerStore.Load(Json);
            var other = s.AddCategory("Other");
            var a = M("a");
            var b = M("b", 5);
            s.AddMarker(s.Categories[0].Id, a);
            s.AddMarker(s.Categories[0].Id, b);

            int moved = s.MoveMarkers(new[] { a }, other.Id);

            Assert.Equal(1, moved);
            Assert.Equal(new[] { b }, s.Categories[0].Markers);
            Assert.Equal(new[] { a }, other.Markers);
            Assert.Same(other, s.FindCategory(a));
        }

        [Fact]
        public void DeleteCategory_MovesOrDropsMarkers_AndKeepsLastCategory()
        {
            var s = UserMarkerStore.Load(Json);
            var def = s.Categories[0];
            var extra = s.AddCategory("Extra");
            s.AddMarker(extra.Id, M("x"));

            Assert.False(s.DeleteCategory(extra.Id, extra.Id));
            Assert.True(s.DeleteCategory(extra.Id, def.Id));
            Assert.Single(s.Categories);
            Assert.Single(def.Markers);
            Assert.False(s.DeleteCategory(def.Id)); // last one stays
        }

        [Fact]
        public void MoveCategory_ReordersWithinBounds()
        {
            var s = UserMarkerStore.Load(Json);
            var b = s.AddCategory("B");
            var c = s.AddCategory("C");

            Assert.True(s.MoveCategory(c.Id, -1));
            Assert.Equal(new[] { s.Categories[0].Name, "C", "B" }, s.Categories.Select(x => x.Name));
            Assert.False(s.MoveCategory(s.Categories[0].Id, -1));
            Assert.False(s.MoveCategory(b.Id, 1));
        }

        [Fact]
        public void EnableDisable_Category_Bulk_AndMarkers()
        {
            var s = UserMarkerStore.Load(Json);
            var cat = s.AddCategory("Cat");
            var m = M("m");
            s.AddMarker(cat.Id, m);

            s.SetAllCategoriesEnabled(false);
            Assert.All(s.Categories, c => Assert.False(c.Enabled));

            s.SetCategoryEnabled(cat.Id, true);
            Assert.True(cat.Enabled);

            s.SetMarkersEnabled(new[] { m }, false);
            Assert.False(m.Enabled);

            var r = UserMarkerStore.Load(Json);
            Assert.False(r.Categories[0].Enabled);
            Assert.True(r.Categories[1].Enabled);
            Assert.False(r.Categories[1].Markers[0].Enabled);
        }

        [Fact]
        public void CopyMarkers_CreatesIndependentCopies()
        {
            var s = UserMarkerStore.Load(Json);
            var imported = M("Imported");

            int n = s.CopyMarkers(new[] { imported }, s.Categories[0].Id);

            Assert.Equal(1, n);
            WMapMarker copy = Assert.Single(s.Categories[0].Markers);
            Assert.NotSame(imported, copy);
            Assert.NotEqual(imported.Id, copy.Id);
            Assert.Equal("Imported", copy.Name);
        }

        [Fact]
        public void FindDuplicate_MatchesCoordsAndMap()
        {
            var s = UserMarkerStore.Load(Json);
            s.AddMarker(s.Categories[0].Id, M("a", 10, 20, 1));

            Assert.NotNull(s.FindDuplicate(10, 20, 1));
            Assert.Null(s.FindDuplicate(10, 20, 0));
        }

        [Fact]
        public void ExportImport_Json_AssignsFreshIdsAndUniqueName()
        {
            var s = UserMarkerStore.Load(Json);
            var cat = s.AddCategory("Shared");
            var m = M("pin");
            s.AddMarker(cat.Id, m);

            string file = Path.Combine(_dir, "shared.json");
            Assert.True(s.ExportCategoryJson(cat.Id, file));

            var imported = s.ImportCategoryJson(file);

            Assert.NotNull(imported);
            Assert.Equal("Shared (2)", imported.Name);
            Assert.NotEqual(cat.Id, imported.Id);
            Assert.NotEqual(m.Id, imported.Markers[0].Id);
        }

        [Fact]
        public void ExportImport_Csv_RoundTrips()
        {
            var s = UserMarkerStore.Load(Json);
            s.AddMarker(s.Categories[0].Id, M("a,b", 7, 8, 2)); // comma in name must not break the line

            string file = Path.Combine(_dir, "out.csv");
            Assert.True(s.ExportCategoryCsv(s.Categories[0].Id, file));

            var other = s.AddCategory("Target");
            Assert.Equal(1, s.ImportCsv(other.Id, file));
            Assert.Equal("ab", other.Markers[0].Name);
            Assert.Equal(7, other.Markers[0].X);
            Assert.Equal(2, other.Markers[0].MapId);
        }

        [Fact]
        public void Changed_FiresOncePerMutation()
        {
            var s = UserMarkerStore.Load(Json);
            int fired = 0;
            s.Changed += () => fired++;

            var a = M("a");
            var b = M("b", 3);
            s.AddMarker(s.Categories[0].Id, a);
            s.AddMarker(s.Categories[0].Id, b);
            s.SetMarkersEnabled(new[] { a, b }, false);
            s.RemoveMarkers(new[] { a, b });

            Assert.Equal(4, fired);
        }

        [Fact]
        public void CategoryIcon_IsFallbackForMarkersWithoutIcon()
        {
            var s = UserMarkerStore.Load(Json);
            var seen = new System.Collections.Generic.List<string>();
            s.IconResolver = name => { seen.Add(name); return null; };

            var cat = s.AddCategory("C", "castle");
            var plain = M("plain");
            var own = M("own");
            own.MarkerIconName = "tower";
            s.AddMarker(cat.Id, plain);
            s.AddMarker(cat.Id, own);

            Assert.Contains("castle", seen);
            Assert.Contains("tower", seen);
        }
    }
}
