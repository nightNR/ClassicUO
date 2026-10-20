// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Free RGB picker (three sliders + live swatch + hex readout). UO hue pickers can only
    /// produce palette colors, which is not enough for the modern status bar colors.
    /// </summary>
    internal class RgbColorPickerGump : Gump
    {
        private const int OK = 1;
        private const int CANCEL = 2;

        private readonly HSliderBar _r, _g, _b;
        private readonly AlphaBlendControl _swatch;
        private readonly Label _hex;
        private readonly Action<uint> _onChanged;
        private readonly Action<uint> _onAccepted;
        private readonly uint _original;

        public RgbColorPickerGump(World world, string title, uint rgb, int x, int y, Action<uint> onChanged, Action<uint> onAccepted)
            : base(world, 0, 0)
        {
            CanMove = true;
            CanCloseWithRightClick = true;
            AcceptMouseInput = true;
            X = x;
            Y = y;
            _original = rgb;
            _onChanged = onChanged;
            _onAccepted = onAccepted;

            Add(new AlphaBlendControl(0.92f) { Width = 260, Height = 168 });
            Add(new Label(title, true, 0xFFFF, 240, 1, FontStyle.BlackBorder) { X = 10, Y = 8 });

            Add(_swatch = new AlphaBlendControl(1f) { X = 10, Y = 34, Width = 56, Height = 56 });
            Add(_hex = new Label(string.Empty, true, 0xFFFF, 0, 1, FontStyle.BlackBorder) { X = 10, Y = 96 });

            _r = AddChannel(ResGumps.PickerRed, 0, (int)((rgb >> 16) & 0xFF));
            _g = AddChannel(ResGumps.PickerGreen, 1, (int)((rgb >> 8) & 0xFF));
            _b = AddChannel(ResGumps.PickerBlue, 2, (int)(rgb & 0xFF));

            Add(new NiceButton(10, 130, 110, 24, ButtonAction.Activate, ResGumps.PickerOk) { ButtonParameter = OK });
            Add(new NiceButton(130, 130, 110, 24, ButtonAction.Activate, ResGumps.PickerCancel) { ButtonParameter = CANCEL });

            Refresh(false);
        }

        public uint Rgb => ((uint)_r.Value << 16) | ((uint)_g.Value << 8) | (uint)_b.Value;

        private HSliderBar AddChannel(string label, int index, int value)
        {
            int y = 32 + index * 30;

            Add(new Label(label, true, 0xFFFF, 0, 1, FontStyle.BlackBorder) { X = 78, Y = y });

            var slider = new HSliderBar(78, y + 14, 170, 0, 255, value, HSliderBarStyle.MetalWidgetRecessedBar, true, 1, 0xFFFF);
            slider.ValueChanged += (s, e) => Refresh(true);
            Add(slider);

            return slider;
        }

        private void Refresh(bool notify)
        {
            if (_b == null)
            {
                return;
            }

            uint rgb = Rgb;
            _swatch.BaseColor = new Color((int)((rgb >> 16) & 0xFF), (int)((rgb >> 8) & 0xFF), (int)(rgb & 0xFF));
            _hex.Text = "#" + rgb.ToString("X6");

            if (notify)
            {
                _onChanged?.Invoke(rgb);
            }
        }

        public override void OnButtonClick(int buttonID)
        {
            if (buttonID == OK)
            {
                _onAccepted?.Invoke(Rgb);
            }
            else
            {
                _onChanged?.Invoke(_original);
            }

            Dispose();
        }
    }

    /// <summary>Swatch showing an RGB color; clicking it opens <see cref="RgbColorPickerGump"/>.</summary>
    internal class ClickableRgbBox : Control
    {
        private readonly World _world;
        private readonly string _title;
        private readonly AlphaBlendControl _fill;

        public ClickableRgbBox(World world, string title, uint rgb)
        {
            _world = world;
            _title = title;
            CanMove = false;
            AcceptMouseInput = true;
            WantUpdateSize = false;
            Width = 44;
            Height = 22;

            Add(new AlphaBlendControl(1f) { Width = Width, Height = Height, BaseColor = Color.White });
            Add(_fill = new AlphaBlendControl(1f) { X = 2, Y = 2, Width = Width - 4, Height = Height - 4 });
            Rgb = rgb;
        }

        public uint Rgb
        {
            get => _rgb;
            set
            {
                _rgb = value & 0xFFFFFF;
                _fill.BaseColor = new Color((int)((_rgb >> 16) & 0xFF), (int)((_rgb >> 8) & 0xFF), (int)(_rgb & 0xFF));
            }
        }

        private uint _rgb;

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button != MouseButtonType.Left)
            {
                return;
            }

            UIManager.GetGump<RgbColorPickerGump>()?.Dispose();

            UIManager.Add(new RgbColorPickerGump(_world, _title, _rgb, ScreenCoordinateX + Width + 10, ScreenCoordinateY - 40, v => Rgb = v, v => Rgb = v));
        }
    }
}
