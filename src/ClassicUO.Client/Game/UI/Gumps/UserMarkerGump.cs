using System;
using System.Linq;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using static ClassicUO.Game.UI.Gumps.WorldMapGump;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>Add (x, y given) or edit (existing marker) a user marker: name, note, category, color, icon.</summary>
    internal sealed class UserMarkersGump : Gump
    {
        private const ushort HUE_FONT = 0xFFFF;
        private const ushort LABEL_OFFSET = 60;
        private const ushort Y_OFFSET = 30;
        private const int WIDTH = 340;
        private const int HEIGHT = 330;
        private const int MAX_CORD_LEN = 10;
        private const int MAX_NAME_LEN = 25;
        private const int MAX_NOTE_LEN = 80;
        private const int MAP_MIN_CORD = 0;

        private readonly StbTextBox _textBoxX;
        private readonly StbTextBox _textBoxY;
        private readonly StbTextBox _markerName;
        private readonly StbTextBox _markerNote;
        private readonly Combobox _categoryCombo;
        private readonly Combobox _colorsCombo;
        private readonly NiceButton _iconButton;
        private readonly Label _warning;

        private readonly UserMarkerStore _store = UserMarkers;
        private readonly WMapMarker _editing;
        private readonly int _mapId;
        private readonly int _mapMaxX;
        private readonly int _mapMaxY;

        private string _iconName = "";
        private bool _dupWarned;

        private enum ButtonsOption
        {
            ADD_BTN,
            EDIT_BTN,
            CANCEL_BTN,
            ICON_BTN,
        }

        /// <summary>Add a new marker at the given coordinates on the current map.</summary>
        internal UserMarkersGump(World world, int x, int y, string categoryId = null) : this(world, null, x, y, categoryId)
        {
        }

        /// <summary>Edit an existing user marker.</summary>
        internal UserMarkersGump(World world, WMapMarker marker) : this(world, marker, marker.X, marker.Y, null)
        {
        }

        private UserMarkersGump(World world, WMapMarker editing, int x, int y, string categoryId) : base(world, 0, 0)
        {
            CanMove = true;
            X = Client.Game.Scene.Camera.Bounds.Width / 2 - WIDTH / 2;
            Y = 150;

            _editing = editing;
            _mapId = editing?.MapId ?? world.MapIndex;
            _mapMaxX = Client.Game.UO.FileManager.Maps.MapsDefaultSize[_mapId, 0];
            _mapMaxY = Client.Game.UO.FileManager.Maps.MapsDefaultSize[_mapId, 1];

            bool isEdit = editing != null;
            MarkerCategory category = isEdit ? _store.FindCategory(editing) : _store.GetCategory(categoryId) ?? _store.Categories[0];
            string[] categoryNames = _store.Categories.Select(c => c.Name).ToArray();
            int selectedCategory = Math.Max(0, _store.Categories.IndexOf(category));

            string color = isEdit ? editing.ColorName : "none";
            int selectedColor = Math.Max(0, Array.IndexOf(MarkerColors.Names, color));

            if (isEdit)
            {
                _iconName = editing.MarkerIconName ?? "";
            }

            Add(new AlphaBlendControl(0.95f) { Width = WIDTH, Height = HEIGHT, Hue = 999, CanMove = true, CanCloseWithRightClick = true, AcceptMouseInput = true });
            Add(new Label(isEdit ? ResGumps.EditMarker : ResGumps.AddMarker, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = 120, Y = 5 });

            int fx = 8;
            int fy = 30;

            _textBoxX = AddTextField(ResGumps.MarkerX, fx, fy, 90, MAX_CORD_LEN, x.ToString());
            Add(new Label(ResGumps.MarkerY, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = fx + LABEL_OFFSET + 100, Y = fy });
            _textBoxY = AddTextField(null, fx + LABEL_OFFSET + 130, fy, 90, MAX_CORD_LEN, y.ToString());

            fy += Y_OFFSET;
            _markerName = AddTextField(ResGumps.MarkerName, fx, fy, 250, MAX_NAME_LEN, isEdit ? editing.Name : ResGumps.MarkerDefName);

            fy += Y_OFFSET;
            _markerNote = AddTextField("Note", fx, fy, 250, MAX_NOTE_LEN, isEdit ? editing.Note ?? "" : "");

            fy += Y_OFFSET;
            Add(new Label("Category", true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = fx, Y = fy });
            _categoryCombo = new Combobox(fx + LABEL_OFFSET, fy, 250, categoryNames, selectedCategory);

            fy += Y_OFFSET;
            Add(new Label(ResGumps.MarkerColor, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = fx, Y = fy });
            _colorsCombo = new Combobox(fx + LABEL_OFFSET, fy, 250, MarkerColors.Names, selectedColor);

            fy += Y_OFFSET;
            Add(new Label(ResGumps.MarkerIcon, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = fx, Y = fy });
            _iconButton = new NiceButton(fx + LABEL_OFFSET, fy, 250, 25, ButtonAction.Activate, IconLabel()) { ButtonParameter = (int)ButtonsOption.ICON_BTN, IsSelectable = false };
            Add(_iconButton);

            fy += Y_OFFSET;
            _warning = new Label("", true, 0x0021, WIDTH - 20, 255, FontStyle.BlackBorder) { X = fx, Y = fy };
            Add(_warning);

            Add(new NiceButton(13, HEIGHT - 30, 80, 25, ButtonAction.Activate, isEdit ? ResGumps.Edit : ResGumps.CreateMarker)
            {
                ButtonParameter = (int)(isEdit ? ButtonsOption.EDIT_BTN : ButtonsOption.ADD_BTN),
                IsSelectable = false
            });

            Add(new NiceButton(100, HEIGHT - 30, 80, 25, ButtonAction.Activate, ResGumps.Cancel) { ButtonParameter = (int)ButtonsOption.CANCEL_BTN, IsSelectable = false });

            // comboboxes last so their drop-downs draw above the fields below them
            Add(_colorsCombo);
            Add(_categoryCombo);

            SetInScreen();
        }

        private StbTextBox AddTextField(string label, int x, int y, int width, int maxLen, string text)
        {
            if (label != null)
            {
                Add(new Label(label, true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = x, Y = y });
            }

            int fx = label != null ? x + LABEL_OFFSET : x;

            Add(new ResizePic(0x0BB8) { X = fx, Y = y, Width = width, Height = 25 });

            var box = new StbTextBox(0xFF, maxLen, width, true, FontStyle.BlackBorder | FontStyle.Fixed)
            {
                X = fx,
                Y = y,
                Width = width,
                Height = 25,
                Text = text
            };

            Add(box);

            return box;
        }

        private string IconLabel()
        {
            return string.IsNullOrEmpty(_iconName) ? "(category default / none)" : _iconName;
        }

        private bool TryRead(out int x, out int y, out string name)
        {
            name = null;

            if (!int.TryParse(_textBoxX.Text, out x) || !int.TryParse(_textBoxY.Text, out y))
            {
                y = 0;
                _warning.Text = "Invalid coordinates";

                return false;
            }

            if (x > _mapMaxX || x < MAP_MIN_CORD || y > _mapMaxY || y < MAP_MIN_CORD)
            {
                _warning.Text = $"Coordinates out of range (0..{_mapMaxX}, 0..{_mapMaxY})";

                return false;
            }

            name = (_markerName.Text ?? "").Replace(",", "").Trim();

            if (name.Length == 0)
            {
                _warning.Text = "Name required";

                return false;
            }

            return true;
        }

        private void Save()
        {
            if (!TryRead(out int x, out int y, out string name))
            {
                return;
            }

            MarkerCategory target = _store.Categories[Math.Max(0, _categoryCombo.SelectedIndex)];
            string color = MarkerColors.Names[_colorsCombo.SelectedIndex];
            string note = (_markerNote.Text ?? "").Trim();

            if (_editing == null)
            {
                WMapMarker dup = _store.FindDuplicate(x, y, _mapId);

                if (dup != null && !_dupWarned)
                {
                    _dupWarned = true;
                    _warning.Text = $"'{dup.Name}' already exists here. Press Create again to add anyway.";

                    return;
                }

                _store.AddMarker(target.Id, new WMapMarker
                {
                    Name = name,
                    X = x,
                    Y = y,
                    MapId = _mapId,
                    ColorName = color,
                    MarkerIconName = _iconName,
                    Note = string.IsNullOrEmpty(note) ? null : note,
                    ZoomIndex = 3,
                });
            }
            else
            {
                _editing.Name = name;
                _editing.X = x;
                _editing.Y = y;
                _editing.ColorName = color;
                _editing.MarkerIconName = _iconName;
                _editing.Note = string.IsNullOrEmpty(note) ? null : note;

                if (_store.FindCategory(_editing) != target)
                {
                    _store.MoveMarkers(new[] { _editing }, target.Id);
                }

                _store.NotifyMarkerEdited(_editing);
            }

            Dispose();
        }

        public override void OnButtonClick(int buttonId)
        {
            switch (buttonId)
            {
                case (int)ButtonsOption.ADD_BTN:
                case (int)ButtonsOption.EDIT_BTN:
                    Save();

                    break;
                case (int)ButtonsOption.ICON_BTN:
                    UIManager.Add(new MarkerIconPickerGump(World, _iconName, picked =>
                    {
                        _iconName = picked;
                        _iconButton.TextLabel.Text = IconLabel();
                    }));

                    break;
                case (int)ButtonsOption.CANCEL_BTN:
                    Dispose();

                    break;
            }
        }
    }
}
