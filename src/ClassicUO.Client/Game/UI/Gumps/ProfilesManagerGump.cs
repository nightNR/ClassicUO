// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Assets;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Named client profiles: pick one for the current character (applied live), create a copy of the active one,
    /// rename or delete unused ones. Characters keep using their assigned profile on the next login.
    /// </summary>
    internal sealed class ProfilesManagerGump : Gump
    {
        private const int WIDTH = 430;
        private const int HEIGHT = 380;
        private const int ROW_H = 22;
        private const ushort HUE_FONT = 0xFFFF;
        private const ushort HUE_ACTIVE = 0x0034;
        private const ushort HUE_DIM = 0x0386;

        private enum Btn { Switch = 1, SwitchSession, New, Rename, Delete }

        private ScrollArea _list;
        private Label _usedBy;
        private Label _status;
        private string _selected;

        internal ProfilesManagerGump(World world) : base(world, 0, 0)
        {
            X = 120;
            Y = 90;
            CanMove = true;
            AcceptMouseInput = true;

            Add(new AlphaBlendControl(0.95f) { X = 1, Y = 1, Width = WIDTH, Height = HEIGHT, Hue = 999, AcceptMouseInput = true, CanCloseWithRightClick = true, CanMove = true });
            Add(new Line(0, 0, WIDTH, 1, Color.Gray.PackedValue));
            Add(new Line(0, 0, 1, HEIGHT, Color.Gray.PackedValue));
            Add(new Line(0, HEIGHT, WIDTH, 1, Color.Gray.PackedValue));
            Add(new Line(WIDTH, 0, 1, HEIGHT, Color.Gray.PackedValue));
            Add(new Line(0, 28, WIDTH, 1, Color.Gray.PackedValue));

            Add(new Label("Profiles", true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = 10, Y = 6 });

            _selected = ProfileManager.ActiveProfileName;

            AddButton(8, 262, 130, "Switch (keep)", Btn.Switch, "Use the selected profile for this character from now on (also on next login)");
            AddButton(142, 262, 130, "Switch (session)", Btn.SwitchSession, "Use the selected profile until you log out; the character's assigned profile stays unchanged");
            AddButton(8, 290, 130, "New (copy active)", Btn.New, "Create a new profile as a copy of the active one");
            AddButton(142, 290, 130, "Rename", Btn.Rename, "Rename the selected profile (not the active one)");
            AddButton(276, 290, 130, "Delete", Btn.Delete, "Delete the selected profile (only if no character uses it)");

            Add(_usedBy = new Label("", true, HUE_DIM, WIDTH - 20, 255, FontStyle.BlackBorder | FontStyle.Cropped) { X = 10, Y = 322 });
            Add(_status = new Label("", true, 0x0035, WIDTH - 20, 255, FontStyle.BlackBorder) { X = 10, Y = 346 });

            Rebuild();
            SetInScreen();
        }

        private static string Root => ProfileManager.RootPath;

        private void AddButton(int x, int y, int w, string text, Btn id, string tooltip)
        {
            var b = new NiceButton(x, y, w, 24, ButtonAction.Activate, text) { ButtonParameter = (int)id, IsSelectable = false };
            b.SetTooltip(tooltip);
            Add(b);
        }

        private void Say(string text)
        {
            _status.Text = text;
        }

        private void Rebuild()
        {
            int scroll = _list?.ScrollValue ?? 0;

            if (_list != null)
            {
                Remove(_list);
                _list.Dispose();
            }

            List<string> names = ProfileCatalog.ListProfiles(Root).ToList();

            if (_selected == null || !names.Contains(_selected, StringComparer.OrdinalIgnoreCase))
            {
                _selected = ProfileManager.ActiveProfileName ?? names.FirstOrDefault();
            }

            _list = new ScrollArea(8, 34, WIDTH - 16, 220, true);

            int y = 0;

            foreach (string name in names)
            {
                _list.Add(new ProfileRow(this, name, y));
                y += ROW_H;
            }

            Add(_list);
            _list.SetScrollValueAfterLayout(scroll);

            List<string> users = _selected == null ? new List<string>() : ProfileCatalog.AssignedCharacters(Root, _selected);
            _usedBy.Text = users.Count == 0 ? "Used by: nobody" : "Used by: " + string.Join(", ", users.Select(k => k.Split('|').Last()));
        }

        private bool IsActive(string name) => string.Equals(name, ProfileManager.ActiveProfileName, StringComparison.OrdinalIgnoreCase);

        public override void OnButtonClick(int buttonID)
        {
            switch ((Btn)buttonID)
            {
                case Btn.Switch:
                case Btn.SwitchSession:
                    SwitchTo(buttonID == (int)Btn.Switch);

                    break;
                case Btn.New:
                    if (ProfileManager.ActiveProfileName == null) { Say("Profiles are unavailable (legacy profile in use)."); break; }

                    UIManager.Add(new EntryDialog(World, 250, 150, "New profile name (copy of active)", name =>
                    {
                        if (string.IsNullOrWhiteSpace(name)) { return; }

                        SaveActive();

                        if (ProfileCatalog.Duplicate(Root, ProfileManager.ActiveProfileName, name))
                        {
                            _selected = ProfileCatalog.SanitizeName(name);
                            Say($"Created '{_selected}'.");
                            PluginProfiles.RaiseListChanged();
                            Rebuild();
                        }
                        else
                        {
                            Say("A profile with that name already exists.");
                        }
                    }) { CanCloseWithRightClick = true });

                    break;
                case Btn.Rename:
                    if (_selected == null) { break; }
                    if (IsActive(_selected)) { Say("Switch to another profile first; the active one cannot be renamed."); break; }

                    string oldName = _selected;

                    UIManager.Add(new EntryDialog(World, 250, 150, "Rename profile", name =>
                    {
                        if (string.IsNullOrWhiteSpace(name)) { return; }

                        if (ProfileCatalog.Rename(Root, oldName, name))
                        {
                            _selected = ProfileCatalog.SanitizeName(name);
                            PluginProfiles.RaiseListChanged();
                            Rebuild();
                        }
                        else
                        {
                            Say("Cannot rename (name taken?).");
                        }
                    }, oldName) { CanCloseWithRightClick = true });

                    break;
                case Btn.Delete:
                    if (_selected == null) { break; }
                    if (IsActive(_selected)) { Say("The active profile cannot be deleted."); break; }

                    int used = ProfileCatalog.AssignedCount(Root, _selected);

                    if (used > 0) { Say($"Used by {used} character(s). Switch them to another profile first."); break; }

                    string toDelete = _selected;

                    UIManager.Add(new QuestionGump(World, $"Delete profile '{toDelete}'?", yes =>
                    {
                        if (yes && ProfileCatalog.Delete(Root, toDelete))
                        {
                            Say($"Deleted '{toDelete}'.");
                            PluginProfiles.RaiseListChanged();
                            Rebuild();
                        }
                    }));

                    break;
            }
        }

        private void SaveActive()
        {
            Profile current = ProfileManager.CurrentProfile;

            if (current == null)
            {
                return;
            }

            current.Save(World, ProfileManager.ProfilePath);
            World.Macros.Save();
            World.InfoBars.Save();
            World.StatusbarColorManager.Save();
        }

        private void SwitchTo(bool assign)
        {
            if (_selected == null) { return; }

            if (IsActive(_selected)) { Say("That profile is already active."); return; }

            if (Client.Game.Scene is not GameScene scene || !scene.SwitchProfile(_selected, assign))
            {
                Say("Switching failed (see log).");
            }
            // on success every gump (including this one) is rebuilt by the switch
        }

        private sealed class ProfileRow : Control
        {
            public ProfileRow(ProfilesManagerGump gump, string name, int y)
            {
                Y = y;
                Width = WIDTH - 40;
                Height = ROW_H;
                CanMove = true;

                bool selected = string.Equals(name, gump._selected, StringComparison.OrdinalIgnoreCase);
                bool active = gump.IsActive(name);
                int users = ProfileCatalog.AssignedCount(Root, name);

                var hit = new RowHit(0, 0, Width, ROW_H - 1, null, selected);
                hit.MouseUp += (s, e) =>
                {
                    if (e.Button == MouseButtonType.Left)
                    {
                        gump._selected = name;
                        gump.Rebuild();
                    }
                };

                Add(hit);
                Add(new Label((active ? "* " : "") + name, true, active ? HUE_ACTIVE : HUE_FONT, Width - 90, 255, FontStyle.BlackBorder | FontStyle.Cropped) { X = 6, Y = 2 });
                Add(new Label(users == 1 ? "1 character" : users + " characters", true, HUE_DIM, 80, 255, FontStyle.BlackBorder, TEXT_ALIGN_TYPE.TS_RIGHT) { X = Width - 90, Y = 2 });
            }
        }
    }
}
