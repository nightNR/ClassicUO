// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Resources;

namespace ClassicUO.Game.UI.Controls
{
    internal sealed class AliasEntryControl : Control
    {
        private readonly StbTextBox _aliasBox;
        private readonly Checkbox _globalBox;
        private readonly uint _serial;
        private readonly Gump _gump;

        internal uint Serial => _serial;

        public AliasEntryControl(Gump gump, AliasEntry entry)
        {
            _gump = gump;
            _serial = entry.Serial;

            _globalBox = new Checkbox(0x00D2, 0x00D3) { X = 5, Y = 5, IsChecked = entry.Global };
            _globalBox.ValueChanged += (s, e) =>
                _gump.World.AliasManager.Set(_serial, _aliasBox.Text, _globalBox.IsChecked);

            _aliasBox = new StbTextBox(0xFF, 30, 130) { X = 30, Y = 0, Width = 130, Height = 26 };
            _aliasBox.SetText(entry.Alias);
            _aliasBox.TextChanged += (s, e) =>
                _gump.World.AliasManager.Set(_serial, _aliasBox.Text, _globalBox.IsChecked);

            Label nameLabel = new Label($"0x{_serial:X8}", true, 0x0386, 200, 1) { X = 175, Y = 5 };

            NiceButton deleteButton = new NiceButton(390, 0, 60, 25, ButtonAction.Activate, ResGumps.Delete) { ButtonParameter = 999 };
            deleteButton.MouseUp += (sender, e) =>
            {
                _gump.World.AliasManager.Remove(_serial);
                Dispose();
                ((DataBox)Parent)?.ReArrangeChildren();
            };

            Add(new ResizePic(0x0BB8) { X = 25, Y = 0, Width = 140, Height = 26 });
            Add(_globalBox);
            Add(_aliasBox);
            Add(nameLabel);
            Add(deleteButton);

            Width = 450;
            Height = 26;
        }
    }
}
