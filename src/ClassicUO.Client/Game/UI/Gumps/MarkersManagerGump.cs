// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Assets;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static ClassicUO.Game.UI.Gumps.WorldMapGump;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Map marker manager: category list on the left (enable/disable, add/rename/delete/reorder, import/export),
    /// filtered + paged marker list on the right (per-marker enable, edit, goto, multi-select bulk actions).
    /// </summary>
    internal sealed class MarkersManagerGump : Gump
    {
        private const int WIDTH = 800;
        private const int HEIGHT = 550;
        private const int LEFT_W = 222;
        private const int RIGHT_X = 244;
        private const int RIGHT_W = 548;
        private const int ROW_H = 22;
        private const int PAGE_SIZE = 100;
        private const ushort HUE_FONT = 0xFFFF;
        private const ushort HUE_SELECTED = 0x0034;
        private const ushort HUE_DISABLED = 0x0386;

        private static readonly string[] SortModes = { "Name", "Distance", "Map" };

        private enum Btn
        {
            CatAdd = 1, CatRename, CatDelete, CatUp, CatDown, CatIcon, CatAllOn, CatAllOff, CatExport, CatImport,
            Search, ClearSearch, PagePrev, PageNext,
            SelAll, SelNone, Enable, Disable, Move, Copy, SetIcon, Delete, AddMarker,
        }

        private readonly UserMarkerStore _store = UserMarkers;

        private ScrollArea _catArea;
        private ScrollArea _markerArea;
        private Combobox _targetCombo;
        private Label _pageLabel;
        private Label _selectedLabel;
        private Label _status;
        private StbTextBox _searchBox;

        private string _selectedKey; // category id (user) or file name (imported)
        private string _search = "";
        private bool _currentMapOnly;
        private bool _allCategories;
        private int _sortMode;
        private int _page;
        private bool _suppressRebuild;

        private readonly HashSet<WMapMarker> _selection = new HashSet<WMapMarker>();

        internal MarkersManagerGump(World world) : base(world, 0, 0)
        {
            X = 50;
            Y = 50;
            CanMove = true;
            AcceptMouseInput = true;

            Add(new AlphaBlendControl(0.95f) { X = 1, Y = 1, Width = WIDTH, Height = HEIGHT, Hue = 999, AcceptMouseInput = true, CanCloseWithRightClick = true, CanMove = true });

            AddBorder(0, 0, WIDTH, 1);
            AddBorder(0, 0, 1, HEIGHT);
            AddBorder(0, HEIGHT, WIDTH, 1);
            AddBorder(WIDTH, 0, 1, HEIGHT);
            AddBorder(RIGHT_X - 8, 28, 1, HEIGHT - 28);
            AddBorder(0, 28, WIDTH, 1);

            Add(new Label(ResGumps.MarkersManager, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = 10, Y = 6 });

            _selectedKey = _store.Categories[0].Id;

            BuildStatic();
            RebuildAll();

            _store.Changed += OnStoreChanged;

            SetInScreen();
        }

        public override void Dispose()
        {
            _store.Changed -= OnStoreChanged;
            base.Dispose();
        }

        // ------------------------------------------------------------------ static chrome

        private void AddBorder(int x, int y, int w, int h)
        {
            Add(new Line(x, y, w, h, Color.Gray.PackedValue));
        }

        private NiceButton AddButton(int x, int y, int w, string text, Btn id, string tooltip = null)
        {
            var b = new NiceButton(x, y, w, 24, ButtonAction.Activate, text) { ButtonParameter = (int)id, IsSelectable = false };

            if (tooltip != null)
            {
                b.SetTooltip(tooltip);
            }

            Add(b);

            return b;
        }

        private void BuildStatic()
        {
            // --- left: category actions
            int by = 376;
            AddButton(8, by, 70, "Add", Btn.CatAdd, "New category");
            AddButton(80, by, 70, "Rename", Btn.CatRename);
            AddButton(152, by, 70, "Delete", Btn.CatDelete);
            by += 26;
            AddButton(8, by, 70, "Up", Btn.CatUp, "Move category up");
            AddButton(80, by, 70, "Down", Btn.CatDown, "Move category down");
            AddButton(152, by, 70, "Icon", Btn.CatIcon, "Default icon for markers without own icon");
            by += 26;
            AddButton(8, by, 105, "All on", Btn.CatAllOn, "Enable all categories");
            AddButton(117, by, 105, "All off", Btn.CatAllOff, "Disable all categories");
            by += 26;
            AddButton(8, by, 105, "Export", Btn.CatExport, "Export selected category to Data/Client/MarkerExports (.json + .csv)");
            AddButton(117, by, 105, "Import", Btn.CatImport, "Import *.json categories (and *.csv into the selected category) from Data/Client/MarkerImports");

            // --- right: search + filters
            Add(new Label(ResGumps.MarkerSearch, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = RIGHT_X, Y = 36 });
            Add(new ResizePic(0x0BB8) { X = RIGHT_X + 55, Y = 33, Width = 220, Height = 25 });
            Add(_searchBox = new StbTextBox(0xFF, 30, 220, true, FontStyle.BlackBorder | FontStyle.Fixed) { X = RIGHT_X + 58, Y = 36, Width = 220, Height = 25 });
            Add(new Button((int)Btn.Search, 0xFB7, 0xFB9) { X = RIGHT_X + 280, Y = 34, ButtonAction = ButtonAction.Activate });
            Add(new Button((int)Btn.ClearSearch, 0xFB1, 0xFB2) { X = RIGHT_X + 312, Y = 34, ButtonAction = ButtonAction.Activate });

            var mapOnly = new Checkbox(0x00D2, 0x00D3, "Current map only", 0, HUE_FONT, true, 0, FontStyle.BlackBorder) { X = RIGHT_X, Y = 66 };
            mapOnly.ValueChanged += (s, e) => { _currentMapOnly = mapOnly.IsChecked; _page = 0; RebuildMarkers(); };
            Add(mapOnly);

            var all = new Checkbox(0x00D2, 0x00D3, "All categories", 0, HUE_FONT, true, 0, FontStyle.BlackBorder) { X = RIGHT_X + 160, Y = 66 };
            all.ValueChanged += (s, e) => { _allCategories = all.IsChecked; _page = 0; RebuildMarkers(); };
            Add(all);

            Add(new Label("Sort", true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = RIGHT_X + 330, Y = 68 });

            // header
            int hy = 94;
            Add(new Label(ResGumps.MarkerIcon, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = RIGHT_X + 22, Y = hy });
            Add(new Label(ResGumps.MarkerName, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = RIGHT_X + 66, Y = hy });
            Add(new Label(ResGumps.MarkerX, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = RIGHT_X + 262, Y = hy });
            Add(new Label(ResGumps.MarkerY, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = RIGHT_X + 312, Y = hy });
            Add(new Label("Map", true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = RIGHT_X + 358, Y = hy });
            Add(new Label(ResGumps.Edit, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = RIGHT_X + 400, Y = hy });
            Add(new Label(ResGumps.Remove, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = RIGHT_X + 436, Y = hy });
            Add(new Label(ResGumps.MarkerGoTo, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = RIGHT_X + 478, Y = hy });
            AddBorder(RIGHT_X - 8, hy + 18, RIGHT_X + RIGHT_W, 1);

            // paging
            AddButton(RIGHT_X, 416, 60, "Prev", Btn.PagePrev);
            AddButton(RIGHT_X + 64, 416, 60, "Next", Btn.PageNext);
            Add(_pageLabel = new Label("", true, HUE_FONT, 300, 255, FontStyle.BlackBorder) { X = RIGHT_X + 132, Y = 419 });

            // bulk bar
            AddBorder(RIGHT_X - 8, 444, RIGHT_W + 8, 1);
            Add(_selectedLabel = new Label("", true, HUE_FONT, 120, 255, FontStyle.BlackBorder) { X = RIGHT_X, Y = 452 });
            AddButton(RIGHT_X + 110, 448, 70, "Sel all", Btn.SelAll, "Select all markers of the current filter (current page)");
            AddButton(RIGHT_X + 184, 448, 70, "Sel none", Btn.SelNone);
            AddButton(RIGHT_X + 258, 448, 70, "Enable", Btn.Enable, "Show selected markers on the map");
            AddButton(RIGHT_X + 332, 448, 70, "Disable", Btn.Disable, "Hide selected markers on the map");
            AddButton(RIGHT_X + 406, 448, 70, "Icon...", Btn.SetIcon, "Set icon of selected markers");
            AddButton(RIGHT_X + 480, 448, 66, "Delete", Btn.Delete, "Delete selected markers");

            Add(new Label("Target", true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = RIGHT_X, Y = 484 });
            AddButton(RIGHT_X + 260, 480, 70, "Move", Btn.Move, "Move selected markers to the target category");
            AddButton(RIGHT_X + 334, 480, 70, "Copy", Btn.Copy, "Copy selected markers (also from imported files) to the target category");
            AddButton(RIGHT_X + 480, 480, 66, "+ Marker", Btn.AddMarker, "Add marker at player position");

            Add(_status = new Label("", true, 0x0035, RIGHT_W, 255, FontStyle.BlackBorder) { X = RIGHT_X, Y = 518 });
        }

        // ------------------------------------------------------------------ dynamic parts

        private void OnStoreChanged()
        {
            if (_suppressRebuild)
            {
                return;
            }

            SyncUserCategoryFiles();
            RebuildAll();
        }

        private void RebuildAll()
        {
            if (!_store.Categories.Any(c => c.Id == _selectedKey) && !_markerFiles.Any(f => f.Category == null && f.Name == _selectedKey))
            {
                _selectedKey = _store.Categories[0].Id;
            }

            RebuildCategories();
            RebuildTargetCombo();
            RebuildMarkers();
        }

        private WMapMarkerFile SelectedFile()
        {
            return _markerFiles.FirstOrDefault(f => f.Category != null ? f.Category.Id == _selectedKey : f.Name == _selectedKey);
        }

        private static string KeyOf(WMapMarkerFile f)
        {
            return f.Category != null ? f.Category.Id : f.Name;
        }

        private void RebuildCategories()
        {
            int scroll = _catArea?.ScrollValue ?? 0;

            if (_catArea != null)
            {
                Remove(_catArea);
                _catArea.Dispose();
            }

            _catArea = new ScrollArea(8, 34, LEFT_W, 336, true);

            int y = 0;
            bool importedHeader = false;

            foreach (WMapMarkerFile f in _markerFiles)
            {
                if (f.Category == null && !importedHeader)
                {
                    importedHeader = true;
                    _catArea.Add(new Label("Imported files (read-only)", true, HUE_DISABLED, 0, 255, FontStyle.BlackBorder) { X = 2, Y = y + 4 });
                    y += ROW_H + 2;
                }

                _catArea.Add(new CategoryRow(this, f, y));
                y += ROW_H;
            }

            Add(_catArea);
            _catArea.SetScrollValueAfterLayout(scroll);
        }

        private void RebuildTargetCombo()
        {
            if (_targetCombo != null)
            {
                Remove(_targetCombo);
                _targetCombo.Dispose();
            }

            int selected = Math.Max(0, _store.Categories.FindIndex(c => c.Id == _selectedKey));
            _targetCombo = new Combobox(RIGHT_X + 50, 480, 200, _store.Categories.Select(c => c.Name).ToArray(), selected);
            Add(_targetCombo);
        }

        private struct Row
        {
            public WMapMarkerFile File;
            public WMapMarker Marker;
        }

        private List<Row> CollectRows()
        {
            IEnumerable<WMapMarkerFile> files = _allCategories ? _markerFiles : new[] { SelectedFile() }.Where(f => f != null);
            IEnumerable<Row> rows = files.SelectMany(f => f.Markers.Select(m => new Row { File = f, Marker = m }));

            if (_currentMapOnly)
            {
                rows = rows.Where(r => r.Marker.MapId == World.MapIndex);
            }

            if (!string.IsNullOrEmpty(_search))
            {
                rows = rows.Where(r => (r.Marker.Name ?? "").Contains(_search, StringComparison.OrdinalIgnoreCase)
                                       || (r.Marker.Note ?? "").Contains(_search, StringComparison.OrdinalIgnoreCase));
            }

            switch (_sortMode)
            {
                case 1:
                    int px = World.Player?.X ?? 0, py = World.Player?.Y ?? 0, pm = World.MapIndex;

                    rows = rows.OrderBy(r => r.Marker.MapId == pm ? 0 : 1)
                               .ThenBy(r => (long)(r.Marker.X - px) * (r.Marker.X - px) + (long)(r.Marker.Y - py) * (r.Marker.Y - py));

                    break;
                case 2:
                    rows = rows.OrderBy(r => r.Marker.MapId).ThenBy(r => r.Marker.Name, StringComparer.OrdinalIgnoreCase);

                    break;
                default:
                    rows = rows.OrderBy(r => r.Marker.Name, StringComparer.OrdinalIgnoreCase);

                    break;
            }

            return rows.ToList();
        }

        private List<Row> _filtered = new List<Row>();

        private void RebuildMarkers()
        {
            if (_markerArea != null)
            {
                Remove(_markerArea);
                _markerArea.Dispose();
            }

            _filtered = CollectRows();

            int pages = Math.Max(1, (_filtered.Count + PAGE_SIZE - 1) / PAGE_SIZE);
            _page = Math.Clamp(_page, 0, pages - 1);

            _markerArea = new ScrollArea(RIGHT_X - 6, 116, RIGHT_W, 296, true);

            int y = 0;

            foreach (Row r in _filtered.Skip(_page * PAGE_SIZE).Take(PAGE_SIZE))
            {
                _markerArea.Add(new MarkerRow(this, r.File, r.Marker, y));
                y += ROW_H;
            }

            Add(_markerArea);

            _pageLabel.Text = $"Page {_page + 1}/{pages}   ({_filtered.Count} markers)";
            UpdateSelectedLabel();

            // keep the sort combobox above the rows
            AddSortCombo();
        }

        private Combobox _sortCombo;

        private void AddSortCombo()
        {
            if (_sortCombo != null)
            {
                Remove(_sortCombo);
                _sortCombo.Dispose();
            }

            _sortCombo = new Combobox(RIGHT_X + 362, 64, 120, SortModes, _sortMode);
            _sortCombo.OnOptionSelected += (s, i) => { _sortMode = i; _page = 0; RebuildMarkers(); };
            Add(_sortCombo);
        }

        private void UpdateSelectedLabel()
        {
            _selectedLabel.Text = $"Selected: {_selection.Count}";
        }

        private void Say(string text)
        {
            _status.Text = text;
        }

        // ------------------------------------------------------------------ actions

        private List<WMapMarker> EditableSelection()
        {
            return _selection.Where(m => _store.FindCategory(m) != null).ToList();
        }

        private MarkerCategory Target()
        {
            return _store.Categories[Math.Clamp(_targetCombo.SelectedIndex, 0, _store.Categories.Count - 1)];
        }

        private MarkerCategory SelectedUserCategory()
        {
            return _store.GetCategory(_selectedKey);
        }

        public override void OnButtonClick(int buttonID)
        {
            switch ((Btn)buttonID)
            {
                case Btn.CatAdd:
                    UIManager.Add(new EntryDialog(World, 250, 150, "New category name", name =>
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            _selectedKey = _store.AddCategory(name).Id;
                        }
                    }) { CanCloseWithRightClick = true });

                    break;
                case Btn.CatRename:
                    MarkerCategory rc = SelectedUserCategory();

                    if (rc == null) { Say("Imported files cannot be renamed."); break; }

                    UIManager.Add(new EntryDialog(World, 250, 150, "Rename category", name => _store.RenameCategory(rc.Id, name), rc.Name) { CanCloseWithRightClick = true });

                    break;
                case Btn.CatDelete:
                    MarkerCategory dc = SelectedUserCategory();

                    if (dc == null) { Say("Imported files cannot be deleted here."); break; }
                    if (_store.Categories.Count <= 1) { Say("The last category cannot be deleted."); break; }

                    string other = _store.Categories.First(c => c != dc).Id;

                    UIManager.Add(new QuestionGump(World, $"Delete '{dc.Name}'? Its {dc.Markers.Count} markers will be moved to '{_store.GetCategory(other).Name}'.", yes =>
                    {
                        if (yes)
                        {
                            _selectedKey = other;
                            _store.DeleteCategory(dc.Id, other);
                        }
                    }));

                    break;
                case Btn.CatUp:
                case Btn.CatDown:
                    MarkerCategory mc = SelectedUserCategory();

                    if (mc != null)
                    {
                        _store.MoveCategory(mc.Id, buttonID == (int)Btn.CatUp ? -1 : 1);
                    }

                    break;
                case Btn.CatIcon:
                    MarkerCategory ic = SelectedUserCategory();

                    if (ic == null) { Say("Select a user category."); break; }

                    UIManager.Add(new MarkerIconPickerGump(World, ic.IconName, icon => _store.SetCategoryStyle(ic.Id, icon, ic.ColorName)));

                    break;
                case Btn.CatAllOn:
                case Btn.CatAllOff:
                    bool on = buttonID == (int)Btn.CatAllOn;

                    _suppressRebuild = true;

                    try
                    {
                        foreach (WMapMarkerFile f in _markerFiles.Where(f => f.Category == null).ToList())
                        {
                            SetMarkerFileHidden(f, !on);
                        }

                        _store.SetAllCategoriesEnabled(on);
                    }
                    finally
                    {
                        _suppressRebuild = false;
                    }

                    SyncUserCategoryFiles();
                    RebuildAll();

                    break;
                case Btn.CatExport:
                    ExportSelected();

                    break;
                case Btn.CatImport:
                    ImportAll();

                    break;
                case Btn.Search:
                    ApplySearch();

                    break;
                case Btn.ClearSearch:
                    _searchBox.SetText("");
                    ApplySearch();

                    break;
                case Btn.PagePrev:
                    _page--;
                    RebuildMarkers();

                    break;
                case Btn.PageNext:
                    _page++;
                    RebuildMarkers();

                    break;
                case Btn.SelAll:
                    foreach (Row r in _filtered.Skip(_page * PAGE_SIZE).Take(PAGE_SIZE))
                    {
                        _selection.Add(r.Marker);
                    }

                    RebuildMarkers();

                    break;
                case Btn.SelNone:
                    _selection.Clear();
                    RebuildMarkers();

                    break;
                case Btn.Enable:
                case Btn.Disable:
                    List<WMapMarker> es = EditableSelection();

                    if (es.Count == 0) { Say("Select markers of your own categories first."); break; }

                    _store.SetMarkersEnabled(es, buttonID == (int)Btn.Enable);

                    break;
                case Btn.SetIcon:
                    List<WMapMarker> ims = EditableSelection();

                    if (ims.Count == 0) { Say("Select markers of your own categories first."); break; }

                    UIManager.Add(new MarkerIconPickerGump(World, "", icon => _store.SetMarkersStyle(ims, icon, null)));

                    break;
                case Btn.Delete:
                    List<WMapMarker> ds = EditableSelection();

                    if (ds.Count == 0) { Say("Select markers of your own categories first."); break; }

                    UIManager.Add(new QuestionGump(World, $"Delete {ds.Count} selected markers?", yes =>
                    {
                        if (yes)
                        {
                            _selection.ExceptWith(ds);
                            _store.RemoveMarkers(ds);
                        }
                    }));

                    break;
                case Btn.Move:
                    List<WMapMarker> ms = EditableSelection();

                    if (ms.Count == 0) { Say("Select markers of your own categories first (imported markers can only be copied)."); break; }

                    MarkerCategory mt = Target();
                    int moved = _store.MoveMarkers(ms, mt.Id);
                    Say($"Moved {moved} markers to '{mt.Name}'.");

                    break;
                case Btn.Copy:
                    if (_selection.Count == 0) { Say("Select markers first."); break; }

                    MarkerCategory ct = Target();
                    int copied = _store.CopyMarkers(_selection.ToList(), ct.Id);
                    Say($"Copied {copied} markers to '{ct.Name}'.");

                    break;
                case Btn.AddMarker:
                    if (World.InGame)
                    {
                        UIManager.GetGump<UserMarkersGump>()?.Dispose();
                        UIManager.Add(new UserMarkersGump(World, World.Player.X, World.Player.Y, SelectedUserCategory()?.Id ?? Target().Id));
                    }

                    break;
            }
        }

        public override void OnKeyboardReturn(int textID, string text)
        {
            ApplySearch();
        }

        private void ApplySearch()
        {
            string text = _searchBox.Text ?? "";

            if (text == _search)
            {
                return;
            }

            _search = text;
            _page = 0;
            RebuildMarkers();
        }

        private static string ExportDir => Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Client", "MarkerExports");
        private static string ImportDir => Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Client", "MarkerImports");

        private static string SafeFileName(string name)
        {
            return string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        }

        private void ExportSelected()
        {
            MarkerCategory c = SelectedUserCategory();

            if (c == null)
            {
                Say("Select a user category to export.");

                return;
            }

            Directory.CreateDirectory(ExportDir);
            string baseName = Path.Combine(ExportDir, SafeFileName(c.Name));

            bool ok = _store.ExportCategoryJson(c.Id, baseName + ".json") & _store.ExportCategoryCsv(c.Id, baseName + ".csv");
            Say(ok ? $"Exported to {ExportDir}" : "Export failed (see log).");
        }

        private void ImportAll()
        {
            Directory.CreateDirectory(ImportDir);

            int cats = 0, csvMarkers = 0;

            _suppressRebuild = true;

            try
            {
                foreach (string json in Directory.GetFiles(ImportDir, "*.json"))
                {
                    if (_store.ImportCategoryJson(json) != null)
                    {
                        cats++;
                    }
                }

                MarkerCategory target = SelectedUserCategory() ?? _store.Categories[0];

                foreach (string csv in Directory.GetFiles(ImportDir, "*.csv"))
                {
                    csvMarkers += _store.ImportCsv(target.Id, csv);
                }
            }
            finally
            {
                _suppressRebuild = false;
            }

            SyncUserCategoryFiles();
            RebuildAll();
            Say(cats + csvMarkers == 0 ? $"Nothing to import. Put *.json / *.csv into {ImportDir}" : $"Imported {cats} categories, {csvMarkers} CSV markers.");
        }

        // ------------------------------------------------------------------ rows

        private sealed class CategoryRow : Control
        {
            public CategoryRow(MarkersManagerGump gump, WMapMarkerFile file, int y)
            {
                Y = y;
                Width = LEFT_W - 16;
                Height = ROW_H;
                CanMove = true;

                bool selected = KeyOf(file) == gump._selectedKey;
                bool enabled = file.Category != null ? file.Category.Enabled : !file.Hidden;

                var cb = new Checkbox(0x00D2, 0x00D3, "", 0, HUE_FONT) { X = 0, Y = 1, IsChecked = enabled };

                cb.ValueChanged += (s, e) =>
                {
                    gump._suppressRebuild = true;

                    try { SetMarkerFileHidden(file, !cb.IsChecked); }
                    finally { gump._suppressRebuild = false; }

                    SyncUserCategoryFiles();
                };

                Add(cb);

                var hit = new HitBox(24, 0, LEFT_W - 40, ROW_H - 1, file.Category == null ? file.FullPath : null, selected ? 0.35f : 0f);
                hit.MouseUp += (s, e) =>
                {
                    if (e.Button == MouseButtonType.Left)
                    {
                        gump._selectedKey = KeyOf(file);
                        gump._page = 0;
                        gump.RebuildCategories();
                        gump.RebuildTargetCombo();
                        gump.RebuildMarkers();
                    }
                };

                Add(hit);

                string icon = file.Category?.IconName;
                int tx = 26;

                if (!string.IsNullOrEmpty(icon) && _markerIcons.TryGetValue(icon, out Texture2D tex) && tex != null && !tex.IsDisposed)
                {
                    Add(new DrawTexture(tex) { X = tx, Y = -6 });
                    tx += 20;
                }

                Add(new Label(file.Name, true, enabled ? (selected ? HUE_SELECTED : HUE_FONT) : HUE_DISABLED, LEFT_W - 90 - (tx - 26), 255, FontStyle.BlackBorder | FontStyle.Cropped) { X = tx, Y = 2 });
                Add(new Label(file.Markers.Count.ToString(), true, HUE_DISABLED, 40, 255, FontStyle.BlackBorder, TEXT_ALIGN_TYPE.TS_RIGHT) { X = LEFT_W - 70, Y = 2 });
            }
        }

        private sealed class MarkerRow : Control
        {
            private enum RowBtn { Edit, Remove, GoTo }

            private readonly MarkersManagerGump _gump;
            private readonly WMapMarkerFile _file;
            private readonly WMapMarker _marker;
            private readonly HitBox _hit;

            public MarkerRow(MarkersManagerGump gump, WMapMarkerFile file, WMapMarker marker, int y)
            {
                _gump = gump;
                _file = file;
                _marker = marker;

                Y = y;
                Width = RIGHT_W - 20;
                Height = ROW_H;
                CanMove = true;

                bool editable = file.Category != null;
                bool selected = gump._selection.Contains(marker);
                ushort hue = !marker.Enabled ? HUE_DISABLED : selected ? HUE_SELECTED : HUE_FONT;

                if (editable)
                {
                    var cb = new Checkbox(0x00D2, 0x00D3, "", 0, HUE_FONT) { X = 0, Y = 1, IsChecked = marker.Enabled };

                    cb.ValueChanged += (s, e) =>
                    {
                        gump._suppressRebuild = true;

                        try { gump._store.SetMarkersEnabled(new[] { marker }, cb.IsChecked); }
                        finally { gump._suppressRebuild = false; }

                        gump.RebuildMarkers();
                    };

                    Add(cb);
                }

                // selection / hover area over the name
                _hit = new HitBox(24, 0, 258, ROW_H - 1, BuildTooltip(), selected ? 0.3f : 0f);
                _hit.MouseUp += (s, e) =>
                {
                    if (e.Button != MouseButtonType.Left)
                    {
                        return;
                    }

                    if (!gump._selection.Remove(marker))
                    {
                        gump._selection.Add(marker);
                    }

                    _hit.Alpha = gump._selection.Contains(marker) ? 0.3f : 0f;
                    gump.UpdateSelectedLabel();
                };

                Add(_hit);

                if (marker.MarkerIcon != null && !marker.MarkerIcon.IsDisposed)
                {
                    Add(new DrawTexture(marker.MarkerIcon) { X = 26, Y = -6 });
                }

                Add(new Label(marker.Name ?? "", true, hue, 200, 255, FontStyle.BlackBorder | FontStyle.Cropped) { X = 46, Y = 2 });
                Add(new Label(marker.X.ToString(), true, hue, 45, 255, FontStyle.BlackBorder) { X = 258, Y = 2 });
                Add(new Label(marker.Y.ToString(), true, hue, 45, 255, FontStyle.BlackBorder) { X = 308, Y = 2 });
                Add(new Label(marker.MapId.ToString(), true, hue, 30, 255, FontStyle.BlackBorder) { X = 354, Y = 2 });

                if (editable)
                {
                    Add(new Button((int)RowBtn.Edit, 0xFAB, 0xFAC) { X = 392, Y = 0, ButtonAction = ButtonAction.Activate });
                    Add(new Button((int)RowBtn.Remove, 0xFB1, 0xFB2) { X = 428, Y = 0, ButtonAction = ButtonAction.Activate });
                }

                Add(new Button((int)RowBtn.GoTo, 0xFA5, 0xFA7) { X = 470, Y = 0, ButtonAction = ButtonAction.Activate });
            }

            private string BuildTooltip()
            {
                string note = string.IsNullOrWhiteSpace(_marker.Note) ? null : _marker.Note;

                return _file.Category != null ? note : (note ?? _file.Name);
            }

            public override void OnButtonClick(int buttonId)
            {
                switch ((RowBtn)buttonId)
                {
                    case RowBtn.Edit:
                        UIManager.GetGump<UserMarkersGump>()?.Dispose();
                        UIManager.Add(new UserMarkersGump(_gump.World, _marker));

                        break;
                    case RowBtn.Remove:
                        _gump._selection.Remove(_marker);
                        _gump._store.RemoveMarkers(new[] { _marker });

                        break;
                    case RowBtn.GoTo:
                        UIManager.GetGump<WorldMapGump>()?.GoToMarker(_marker.X, _marker.Y, false);

                        break;
                }
            }
        }

        internal class DrawTexture : Control
        {
            public Texture2D Texture;

            public DrawTexture(Texture2D texture)
            {
                Texture = texture;
                Width = Height = 15;
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float layerDepth = layerDepthRef;
                Vector3 hueVector = ShaderHueTranslator.GetHueVector(0);

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    batcher.Draw(Texture, new Rectangle(x, y + 7, Width, Height), hueVector, layerDepth);

                    return true;
                });

                return true;
            }
        }
    }
}
