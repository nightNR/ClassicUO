// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Resources;

namespace ClassicUO.Game.UI.Controls
{
    internal sealed class UOAMAliasRow : Control
    {
        private readonly StbTextBox _uoamNameBox;
        private readonly StbTextBox _charNameBox;

        public string UoamName => _uoamNameBox.Text;
        public string CharName => _charNameBox.Text;

        public UOAMAliasRow(string uoamName, string charName)
        {
            Add(new ResizePic(0x0BB8) { X = 0, Y = 0, Width = 150, Height = 20 });
            _uoamNameBox = new StbTextBox(0xFF, 30, 145) { X = 4, Y = 2, Width = 145, Height = 16 };
            _uoamNameBox.SetText(uoamName ?? string.Empty);
            Add(_uoamNameBox);

            Add(new Label("->", true, 0xFFFF) { X = 158, Y = 2 });

            Add(new ResizePic(0x0BB8) { X = 178, Y = 0, Width = 150, Height = 20 });
            _charNameBox = new StbTextBox(0xFF, 30, 145) { X = 182, Y = 2, Width = 145, Height = 16 };
            _charNameBox.SetText(charName ?? string.Empty);
            Add(_charNameBox);

            NiceButton removeButton = new NiceButton(336, 0, 60, 20, ButtonAction.Activate, ResGumps.Delete) { ButtonParameter = 999 };
            removeButton.MouseUp += (s, e) =>
            {
                Dispose();
                ((DataBox)Parent)?.ReArrangeChildren();
            };
            Add(removeButton);

            Width = 400;
            Height = 24;
        }
    }
}
