// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static ClassicUO.Game.UI.Gumps.WorldMapGump;

namespace ClassicUO.Game.Managers
{
    internal static class MarkerColors
    {
        public static readonly string[] Names = { "none", "red", "green", "blue", "purple", "black", "yellow", "white" };

        private static readonly Dictionary<string, Color> _map = new Dictionary<string, Color>
        {
            { "red", Color.Red },
            { "green", Color.Green },
            { "blue", Color.Blue },
            { "purple", Color.Purple },
            { "black", Color.Black },
            { "yellow", Color.Yellow },
            { "white", Color.White },
            { "none", Color.Transparent },
        };

        public static Color Get(string name)
        {
            return name != null && _map.TryGetValue(name, out Color color) ? color : Color.White;
        }
    }

    internal sealed class MarkerCategory
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; }
        public string IconName { get; set; } = "";
        public string ColorName { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public List<WMapMarker> Markers { get; set; } = new List<WMapMarker>();
    }

    // ---- persistence DTOs (no textures / colors) ----

    internal sealed class MarkerDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int MapId { get; set; }
        public string Icon { get; set; }
        public string Color { get; set; }
        public int Zoom { get; set; } = 3;
        public bool Enabled { get; set; } = true;
        public string Note { get; set; }
    }

    internal sealed class MarkerCategoryDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Color { get; set; }
        public bool Enabled { get; set; } = true;
        public List<MarkerDto> Markers { get; set; } = new List<MarkerDto>();
    }

    internal sealed class UserMarkerFileDto
    {
        public int Version { get; set; } = 1;
        public List<MarkerCategoryDto> Categories { get; set; } = new List<MarkerCategoryDto>();
    }

    [JsonSourceGenerationOptions(WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(UserMarkerFileDto), GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(MarkerCategoryDto), GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(MarkerDto), GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(List<MarkerDto>), GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(List<MarkerCategoryDto>), GenerationMode = JsonSourceGenerationMode.Metadata)]
    internal sealed partial class UserMarkerJsonContext : JsonSerializerContext { }

    /// <summary>
    /// User marker categories + markers, persisted as JSON. Always holds at least one category.
    /// Every mutating method saves and raises <see cref="Changed"/> once.
    /// </summary>
    internal sealed class UserMarkerStore
    {
        public const string DEFAULT_CATEGORY_NAME = "Default";
        public const int MAX_NAME_LEN = 50;

        private readonly string _path;

        public List<MarkerCategory> Categories { get; } = new List<MarkerCategory>();

        /// <summary>Resolves an icon name to a texture; may return null.</summary>
        public Func<string, Texture2D> IconResolver { get; set; }

        public bool AutoSave { get; set; } = true;

        public event Action Changed;

        public UserMarkerStore(string path)
        {
            _path = path;
        }

        // ---------------------------------------------------------------- load / save

        /// <summary>
        /// Loads the store from <paramref name="path"/>. If missing and <paramref name="legacyUsrPath"/> exists,
        /// migrates it into the "Default" category and renames the legacy file to <c>.bak</c>.
        /// </summary>
        public static UserMarkerStore Load(string path, string legacyUsrPath = null, Func<string, Texture2D> iconResolver = null)
        {
            var store = new UserMarkerStore(path) { IconResolver = iconResolver };
            bool needSave = false;

            if (File.Exists(path))
            {
                try
                {
                    UserMarkerFileDto dto = JsonSerializer.Deserialize(File.ReadAllText(path), UserMarkerJsonContext.Default.UserMarkerFileDto);

                    foreach (MarkerCategoryDto c in dto?.Categories ?? new List<MarkerCategoryDto>())
                    {
                        store.Categories.Add(FromDto(c));
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Corrupt marker store '{path}', keeping a copy: {e.Message}");

                    try { File.Move(path, path + ".corrupt", true); } catch { /* best effort */ }

                    store.Categories.Clear();
                }
            }
            else if (!string.IsNullOrEmpty(legacyUsrPath) && File.Exists(legacyUsrPath))
            {
                MarkerCategory def = store.NewCategoryObject(DEFAULT_CATEGORY_NAME);

                foreach (string line in File.ReadAllLines(legacyUsrPath))
                {
                    if (TryParseCsvLine(line, out WMapMarker m))
                    {
                        def.Markers.Add(m);
                    }
                }

                store.Categories.Add(def);
                needSave = true;

                // save first; only retire the legacy file once the JSON is safely on disk
                if (store.SaveInternal())
                {
                    try { File.Move(legacyUsrPath, legacyUsrPath + ".bak", true); } catch (Exception e) { Log.Error($"Could not rename legacy markers: {e.Message}"); }
                }

                needSave = false;
            }

            if (store.Categories.Count == 0)
            {
                store.Categories.Add(store.NewCategoryObject(DEFAULT_CATEGORY_NAME));
                needSave = true;
            }

            store.RehydrateAll();

            if (needSave)
            {
                store.SaveInternal();
            }

            return store;
        }

        public bool Save()
        {
            return SaveInternal();
        }

        private bool SaveInternal()
        {
            try
            {
                string dir = Path.GetDirectoryName(_path);

                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var dto = new UserMarkerFileDto { Categories = Categories.Select(ToDto).ToList() };
                string tmp = _path + ".tmp";

                File.WriteAllText(tmp, JsonSerializer.Serialize(dto, UserMarkerJsonContext.Default.UserMarkerFileDto));
                File.Move(tmp, _path, true);

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Could not save markers '{_path}': {e.Message}");

                return false;
            }
        }

        private void Commit()
        {
            if (AutoSave)
            {
                SaveInternal();
            }

            Changed?.Invoke();
        }

        /// <summary>Call after a marker was edited in place.</summary>
        public void NotifyMarkerEdited(WMapMarker marker)
        {
            Hydrate(marker, FindCategory(marker));
            Commit();
        }

        // ---------------------------------------------------------------- hydration

        public void RehydrateAll()
        {
            foreach (MarkerCategory c in Categories)
            {
                foreach (WMapMarker m in c.Markers)
                {
                    Hydrate(m, c);
                }
            }
        }

        public void Hydrate(WMapMarker marker, MarkerCategory category)
        {
            string colorName = !string.IsNullOrEmpty(marker.ColorName) ? marker.ColorName : category?.ColorName;
            marker.Color = MarkerColors.Get(colorName);

            string iconName = !string.IsNullOrEmpty(marker.MarkerIconName) ? marker.MarkerIconName : category?.IconName;
            marker.MarkerIcon = string.IsNullOrEmpty(iconName) ? null : IconResolver?.Invoke(iconName);
        }

        // ---------------------------------------------------------------- queries

        public MarkerCategory GetCategory(string id)
        {
            return Categories.FirstOrDefault(c => c.Id == id);
        }

        public MarkerCategory FindCategory(WMapMarker marker)
        {
            return Categories.FirstOrDefault(c => c.Markers.Contains(marker));
        }

        public WMapMarker FindDuplicate(int x, int y, int mapId)
        {
            return Categories.SelectMany(c => c.Markers).FirstOrDefault(m => m.X == x && m.Y == y && m.MapId == mapId);
        }

        // ---------------------------------------------------------------- categories

        public MarkerCategory AddCategory(string name, string iconName = "", string colorName = "")
        {
            MarkerCategory c = NewCategoryObject(name);
            c.IconName = iconName ?? "";
            c.ColorName = colorName ?? "";
            Categories.Add(c);
            Commit();

            return c;
        }

        public bool RenameCategory(string id, string name)
        {
            MarkerCategory c = GetCategory(id);

            if (c == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            c.Name = UniqueName(name, c);
            Commit();

            return true;
        }

        public bool SetCategoryStyle(string id, string iconName, string colorName)
        {
            MarkerCategory c = GetCategory(id);

            if (c == null)
            {
                return false;
            }

            c.IconName = iconName ?? "";
            c.ColorName = colorName ?? "";
            RehydrateAll();
            Commit();

            return true;
        }

        /// <summary>Deletes a category. Markers go to <paramref name="moveMarkersToId"/>, or are deleted when null. The last category cannot be deleted.</summary>
        public bool DeleteCategory(string id, string moveMarkersToId = null)
        {
            MarkerCategory c = GetCategory(id);

            if (c == null || Categories.Count <= 1)
            {
                return false;
            }

            MarkerCategory target = moveMarkersToId == null ? null : GetCategory(moveMarkersToId);

            if (moveMarkersToId != null && (target == null || target == c))
            {
                return false;
            }

            if (target != null)
            {
                target.Markers.AddRange(c.Markers);
            }

            Categories.Remove(c);
            RehydrateAll();
            Commit();

            return true;
        }

        public bool MoveCategory(string id, int delta)
        {
            int idx = Categories.FindIndex(c => c.Id == id);
            int to = idx + delta;

            if (idx < 0 || to < 0 || to >= Categories.Count || delta == 0)
            {
                return false;
            }

            MarkerCategory c = Categories[idx];
            Categories.RemoveAt(idx);
            Categories.Insert(to, c);
            Commit();

            return true;
        }

        public void SetCategoryEnabled(string id, bool enabled)
        {
            MarkerCategory c = GetCategory(id);

            if (c != null)
            {
                c.Enabled = enabled;
                Commit();
            }
        }

        public void SetAllCategoriesEnabled(bool enabled)
        {
            foreach (MarkerCategory c in Categories)
            {
                c.Enabled = enabled;
            }

            Commit();
        }

        // ---------------------------------------------------------------- markers

        public bool AddMarker(string categoryId, WMapMarker marker)
        {
            MarkerCategory c = GetCategory(categoryId);

            if (c == null || marker == null)
            {
                return false;
            }

            c.Markers.Add(marker);
            Hydrate(marker, c);
            Commit();

            return true;
        }

        public bool RemoveMarkers(IEnumerable<WMapMarker> markers)
        {
            HashSet<WMapMarker> set = markers.ToHashSet();
            bool any = false;

            foreach (MarkerCategory c in Categories)
            {
                any |= c.Markers.RemoveAll(set.Contains) > 0;
            }

            if (any)
            {
                Commit();
            }

            return any;
        }

        /// <summary>Moves markers (from any user category) to <paramref name="targetCategoryId"/>, keeping their relative order.</summary>
        public int MoveMarkers(IEnumerable<WMapMarker> markers, string targetCategoryId)
        {
            MarkerCategory target = GetCategory(targetCategoryId);

            if (target == null)
            {
                return 0;
            }

            HashSet<WMapMarker> set = markers.ToHashSet();
            var moved = new List<WMapMarker>();

            foreach (MarkerCategory c in Categories)
            {
                if (c == target)
                {
                    continue;
                }

                moved.AddRange(c.Markers.Where(set.Contains));
                c.Markers.RemoveAll(set.Contains);
            }

            target.Markers.AddRange(moved);
            RehydrateAll();

            if (moved.Count > 0)
            {
                Commit();
            }

            return moved.Count;
        }

        /// <summary>Adds copies (new ids) of any markers, e.g. from imported read-only files, to a category.</summary>
        public int CopyMarkers(IEnumerable<WMapMarker> markers, string targetCategoryId)
        {
            MarkerCategory target = GetCategory(targetCategoryId);

            if (target == null)
            {
                return 0;
            }

            int count = 0;

            foreach (WMapMarker m in markers)
            {
                var copy = new WMapMarker
                {
                    Name = m.Name,
                    X = m.X,
                    Y = m.Y,
                    MapId = m.MapId,
                    MarkerIconName = m.MarkerIconName,
                    ColorName = string.IsNullOrEmpty(m.ColorName) ? ColorToName(m.Color) : m.ColorName,
                    ZoomIndex = m.ZoomIndex,
                    Note = m.Note,
                    Enabled = true,
                };

                target.Markers.Add(copy);
                Hydrate(copy, target);
                count++;
            }

            if (count > 0)
            {
                Commit();
            }

            return count;
        }

        public void SetMarkersEnabled(IEnumerable<WMapMarker> markers, bool enabled)
        {
            foreach (WMapMarker m in markers)
            {
                m.Enabled = enabled;
            }

            Commit();
        }

        public void SetMarkersStyle(IEnumerable<WMapMarker> markers, string iconName, string colorName)
        {
            foreach (WMapMarker m in markers)
            {
                if (iconName != null)
                {
                    m.MarkerIconName = iconName;
                }

                if (colorName != null)
                {
                    m.ColorName = colorName;
                }
            }

            RehydrateAll();
            Commit();
        }

        // ---------------------------------------------------------------- import / export

        public bool ExportCategoryJson(string id, string path)
        {
            MarkerCategory c = GetCategory(id);

            if (c == null)
            {
                return false;
            }

            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(ToDto(c), UserMarkerJsonContext.Default.MarkerCategoryDto));

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Export failed: {e.Message}");

                return false;
            }
        }

        /// <summary>Imports a JSON category (fresh ids, unique name). Returns null on failure.</summary>
        public MarkerCategory ImportCategoryJson(string path)
        {
            try
            {
                MarkerCategoryDto dto = JsonSerializer.Deserialize(File.ReadAllText(path), UserMarkerJsonContext.Default.MarkerCategoryDto);

                if (dto == null)
                {
                    return null;
                }

                MarkerCategory c = FromDto(dto);
                c.Id = Guid.NewGuid().ToString("N");
                c.Name = UniqueName(string.IsNullOrWhiteSpace(c.Name) ? "Imported" : c.Name, null);

                foreach (WMapMarker m in c.Markers)
                {
                    m.Id = Guid.NewGuid().ToString("N");
                }

                Categories.Add(c);
                RehydrateAll();
                Commit();

                return c;
            }
            catch (Exception e)
            {
                Log.Error($"Import failed: {e.Message}");

                return null;
            }
        }

        /// <summary>Exports a category as the legacy CSV (x,y,map,name,icon,color,zoom).</summary>
        public bool ExportCategoryCsv(string id, string path)
        {
            MarkerCategory c = GetCategory(id);

            if (c == null)
            {
                return false;
            }

            try
            {
                File.WriteAllLines(path, c.Markers.Select(m =>
                    string.Join(",", m.X, m.Y, m.MapId, m.Name?.Replace(",", ""), m.MarkerIconName, m.ColorName, m.ZoomIndex)));

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Export failed: {e.Message}");

                return false;
            }
        }

        /// <summary>Appends markers from a CSV file (x,y,map,name,icon,color[,zoom]) to a category. Returns the count added.</summary>
        public int ImportCsv(string categoryId, string path)
        {
            MarkerCategory c = GetCategory(categoryId);

            if (c == null || !File.Exists(path))
            {
                return 0;
            }

            int count = 0;

            foreach (string line in File.ReadAllLines(path))
            {
                if (TryParseCsvLine(line, out WMapMarker m))
                {
                    c.Markers.Add(m);
                    Hydrate(m, c);
                    count++;
                }
            }

            if (count > 0)
            {
                Commit();
            }

            return count;
        }

        // ---------------------------------------------------------------- helpers

        private MarkerCategory NewCategoryObject(string name)
        {
            return new MarkerCategory { Name = UniqueName(string.IsNullOrWhiteSpace(name) ? "Category" : name.Trim(), null) };
        }

        private string UniqueName(string name, MarkerCategory ignore)
        {
            name = name.Trim();

            if (name.Length > MAX_NAME_LEN)
            {
                name = name.Substring(0, MAX_NAME_LEN);
            }

            string candidate = name;
            int n = 2;

            while (Categories.Any(c => c != ignore && string.Equals(c.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{name} ({n++})";
            }

            return candidate;
        }

        private static string ColorToName(Color c)
        {
            foreach (string n in MarkerColors.Names)
            {
                if (MarkerColors.Get(n) == c)
                {
                    return n;
                }
            }

            return "white";
        }

        internal static bool TryParseCsvLine(string line, out WMapMarker marker)
        {
            marker = null;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] s = line.Split(',');

            if (s.Length < 6 ||
                !int.TryParse(s[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(s[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
                !int.TryParse(s[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int map))
            {
                return false;
            }

            int zoom = 3;

            if (s.Length >= 7)
            {
                int.TryParse(s[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out zoom);
            }

            marker = new WMapMarker
            {
                X = x,
                Y = y,
                MapId = map,
                Name = s[3].Trim(),
                MarkerIconName = s[4].Trim().ToLowerInvariant(),
                ColorName = s[5].Trim(),
                ZoomIndex = zoom,
            };

            return true;
        }

        private static MarkerCategoryDto ToDto(MarkerCategory c)
        {
            return new MarkerCategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                Icon = c.IconName,
                Color = c.ColorName,
                Enabled = c.Enabled,
                Markers = c.Markers.Select(m => new MarkerDto
                {
                    Id = m.Id,
                    Name = m.Name,
                    X = m.X,
                    Y = m.Y,
                    MapId = m.MapId,
                    Icon = m.MarkerIconName,
                    Color = m.ColorName,
                    Zoom = m.ZoomIndex,
                    Enabled = m.Enabled,
                    Note = m.Note,
                }).ToList()
            };
        }

        private static MarkerCategory FromDto(MarkerCategoryDto d)
        {
            return new MarkerCategory
            {
                Id = string.IsNullOrEmpty(d.Id) ? Guid.NewGuid().ToString("N") : d.Id,
                Name = string.IsNullOrWhiteSpace(d.Name) ? "Category" : d.Name,
                IconName = d.Icon ?? "",
                ColorName = d.Color ?? "",
                Enabled = d.Enabled,
                Markers = (d.Markers ?? new List<MarkerDto>()).Select(m => new WMapMarker
                {
                    Id = string.IsNullOrEmpty(m.Id) ? Guid.NewGuid().ToString("N") : m.Id,
                    Name = m.Name,
                    X = m.X,
                    Y = m.Y,
                    MapId = m.MapId,
                    MarkerIconName = m.Icon ?? "",
                    ColorName = m.Color ?? "",
                    ZoomIndex = m.Zoom,
                    Enabled = m.Enabled,
                    Note = m.Note,
                }).ToList()
            };
        }
    }
}
