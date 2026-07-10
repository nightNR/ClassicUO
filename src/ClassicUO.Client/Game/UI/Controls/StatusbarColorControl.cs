// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Resources;

namespace ClassicUO.Game.UI.Controls
{
    internal sealed class StatusbarColorControl : Control
    {
        private readonly StbTextBox _graphicBox;
        private readonly StbTextBox _huesBox;
        private readonly ClickableColorBox _colorBox;
        private readonly StatusbarColorRule _rule;
        private readonly Gump _gump;
        private ushort _lastColor;

        public StatusbarColorControl(Gump gump, StatusbarColorRule rule)
        {
            _gump = gump;
            _rule = rule;
            _lastColor = rule.Color;

            _graphicBox = new StbTextBox(0xFF, 10, 90) { X = 5, Y = 0, Width = 90, Height = 26 };
            _graphicBox.SetText("0x" + rule.Graphic.ToString("X"));
            _graphicBox.TextChanged += (s, e) => Commit();

            _huesBox = new StbTextBox(0xFF, 40, 150) { X = 100, Y = 0, Width = 150, Height = 26 };
            _huesBox.SetText(StatusbarColorManager.FormatHues(rule.Hues));
            _huesBox.TextChanged += (s, e) => Commit();

            _colorBox = new ClickableColorBox(_gump.World, 260, 0, 13, 14, rule.Color);

            NiceButton deleteButton = new NiceButton(300, 0, 60, 25, ButtonAction.Activate, ResGumps.Delete) { ButtonParameter = 999 };
            deleteButton.MouseUp += (sender, e) =>
            {
                _gump.World.StatusbarColorManager.Remove(_rule);
                _gump.World.StatusbarColorManager.Save();
                Dispose();
                ((DataBox)Parent)?.ReArrangeChildren();
            };

            Add(new ResizePic(0x0BB8) { X = 0, Y = 0, Width = 95, Height = 26 });
            Add(new ResizePic(0x0BB8) { X = 100, Y = 0, Width = 155, Height = 26 });
            Add(_graphicBox);
            Add(_huesBox);
            Add(_colorBox);
            Add(deleteButton);

            Width = 365;
            Height = 26;
        }

        public override void Update()
        {
            base.Update();

            if (_colorBox.Hue != _lastColor)
            {
                _lastColor = _colorBox.Hue;
                _rule.Color = _colorBox.Hue;
                _gump.World.StatusbarColorManager.Save();
            }
        }

        private void Commit()
        {
            if (StatusbarColorManager.TryParseUShort(_graphicBox.Text, out ushort g))
                _rule.Graphic = g;

            _rule.Hues = StatusbarColorManager.ParseHues(_huesBox.Text);
            _rule.Color = _colorBox.Hue;
            _lastColor = _colorBox.Hue;

            _gump.World.StatusbarColorManager.Save();
        }
    }
}
