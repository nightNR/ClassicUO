// SPDX-License-Identifier: BSD-2-Clause
using ClassicUO.Configuration;

namespace ClassicUO.Game.UI.Controls
{
    internal sealed class FontLocationRow : Control
    {
        private readonly Combobox _face;
        private readonly Combobox _size;
        private readonly string[] _faceIds;   // index 0 = "" (Classic)

        public FontLocationRow(int x, int y, FontChoice current)
        {
            X = x;
            Y = y;
            AcceptMouseInput = true;

            _faceIds = FontLocationRowLogic.BuildFaceIds();
            string[] faceLabels = FontLocationRowLogic.BuildFaceLabels();

            int faceIndex = FontLocationRowLogic.ResolveFaceIndex(_faceIds, current.Face);

            _face = new Combobox(0, 0, 140, faceLabels, faceIndex);
            Add(_face);

            var sizes = new string[FontLocationRowLogic.SIZE_MAX - FontLocationRowLogic.SIZE_MIN + 1];
            for (int s = 0; s < sizes.Length; s++)
            {
                sizes[s] = (FontLocationRowLogic.SIZE_MIN + s).ToString();
            }

            int sizeIndex = FontLocationRowLogic.ResolveSizeIndex(current.Size);
            _size = new Combobox(150, 0, 60, sizes, sizeIndex) { IsVisible = faceIndex != 0 };
            Add(_size);

            _face.OnOptionSelected += (_, i) => _size.IsVisible = i != 0;

            Width = 210;
            Height = 25;
        }

        public FontChoice GetChoice()
        {
            return FontLocationRowLogic.BuildChoice(_face.SelectedIndex, _size.SelectedIndex, _faceIds);
        }

        // Resets the row to the "Classic" default (used by the Options "restore
        // defaults" action, which historically pointed both Chat/Tooltip font
        // pickers back at the location's classic fallback slot).
        public void SelectClassic()
        {
            _face.SelectedIndex = 0;
        }
    }
}
