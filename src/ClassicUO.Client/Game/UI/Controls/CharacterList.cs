// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using System;
using System.Collections.Generic;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Login;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Renderer.LoginFonts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls
{
    // A scrollable vertical list of character rows, each drawn from the input-field
    // art (neutral / focus = hovered-or-selected / disabled = pressed), same look as
    // the realm rows. Mouse-wheel scrolls; rows are clipped to the viewport.
    internal class CharacterList : Control
    {
        private static readonly Color TEXT = new Color(0xD3, 0xC2, 0xA1);

        public readonly struct Item
        {
            public readonly string Name;
            public readonly int Slot;
            public readonly bool IsNew;   // gets extra separation above it
            public Item(string name, int slot, bool isNew = false) { Name = name; Slot = slot; IsNew = isNew; }
        }

        private const int ExtraGapBeforeNew = 30;

        private readonly List<Item> _items;
        private readonly ILoginFont _font;
        private readonly float _fontSize;
        private readonly int _rowH, _rowGap;
        private readonly int[] _offsets;   // content-relative top of each row
        private readonly int _contentH;
        private int _scroll;
        private bool _pressed;
        private int _pressedRow = -1;

        public int SelectedRow { get; set; }
        public Action<int> Selected;    // single-click: passes the character slot index
        public Action<int> Activated;   // double-click: proceed with the slot index

        public CharacterList(List<Item> items, ILoginFont font, float fontSize,
            int x, int y, int w, int h, int rowH, int rowGap)
        {
            _items = items;
            _font = font;
            _fontSize = fontSize;
            X = x; Y = y; Width = w; Height = h;
            _rowH = rowH;
            _rowGap = rowGap;
            AcceptMouseInput = true;
            SelectedRow = items.Count > 0 ? 0 : -1;

            // Precompute each row's content-relative top, with extra separation
            // above any "New" row.
            _offsets = new int[items.Count];
            int yoff = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) yoff += _rowGap;
                if (items[i].IsNew) yoff += ExtraGapBeforeNew;
                _offsets[i] = yoff;
                yoff += _rowH;
            }
            _contentH = items.Count > 0 ? yoff : 0;
        }

        public int SelectedSlot => (SelectedRow >= 0 && SelectedRow < _items.Count) ? _items[SelectedRow].Slot : -1;

        private int MaxScroll => Math.Max(0, _contentH - Height);

        protected override void OnMouseWheel(MouseEventType delta)
        {
            int step = _rowH + _rowGap;
            if (delta == MouseEventType.WheelScrollUp) _scroll -= step;
            else if (delta == MouseEventType.WheelScrollDown) _scroll += step;
            else return;

            _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        }

        private int RowAtLocal(int localY)
        {
            int t = localY + _scroll;
            for (int i = 0; i < _items.Count; i++)
            {
                if (t >= _offsets[i] && t < _offsets[i] + _rowH) return i;
            }
            return -1;
        }

        protected override void OnMouseDown(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left)
            {
                _pressed = true;
                _pressedRow = RowAtLocal(y);
            }
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left && _pressed)
            {
                _pressed = false;
                int r = RowAtLocal(y);
                if (r >= 0 && r == _pressedRow)
                {
                    SelectedRow = r;
                    Selected?.Invoke(_items[r].Slot);
                }
            }
        }

        protected override void OnMouseExit(int x, int y) => _pressed = false;

        protected override bool OnMouseDoubleClick(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left)
            {
                int r = RowAtLocal(y);
                if (r >= 0)
                {
                    SelectedRow = r;
                    Activated?.Invoke(_items[r].Slot);
                    return true;
                }
            }

            return false;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float depth = layerDepthRef;
            Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);
            Texture2D neu = LoginAssets.InputNeutral, foc = LoginAssets.InputFocus, dis = LoginAssets.InputDisabled;
            var src = new Rectangle((int)(neu.Width * 0.04f), (int)(neu.Height * 0.35f), (int)(neu.Width * 0.92f), (int)(neu.Height * 0.315f));

            int mlx = Mouse.Position.X - x, mly = Mouse.Position.Y - y;
            bool mouseIn = mlx >= 0 && mlx < Width && mly >= 0 && mly < Height;
            int hoverRow = mouseIn ? RowAtLocal(mly) : -1;

            int scroll = _scroll, rowH = _rowH, w = Width, h = Height, sel = SelectedRow;
            bool pressed = _pressed;
            int pressedRow = _pressedRow;
            var items = _items;
            var offsets = _offsets;
            ILoginFont font = _font;
            float fs = _fontSize;

            renderLists.AddGumpNoAtlas(batcher =>
            {
                if (!batcher.ClipBegin(x, y, w, h)) return true;
                batcher.SetSampler(SamplerState.AnisotropicClamp);

                for (int i = 0; i < items.Count; i++)
                {
                    int ry = y - scroll + offsets[i];
                    if (ry + rowH < y || ry > y + h) continue;

                    Texture2D bg = (pressed && i == pressedRow) ? dis
                                 : (i == sel || i == hoverRow) ? foc
                                 : neu;
                    var dest = new Rectangle(x, ry, w, rowH);

                    batcher.DrawTinted(bg, new Rectangle(dest.X + 2, dest.Y + 3, dest.Width, dest.Height), src, Vector3.Zero, 0.07f, depth);
                    batcher.Draw(bg, dest, src, hue, depth);

                    float lineH = font.Measure("Ag", fs).Y;
                    float ty = ry + (rowH - lineH) / 2f;
                    font.Draw(batcher, items[i].Name, x + w * 0.10f + 10, ty, fs, TEXT, 0.9f);
                }

                batcher.SetSampler(SamplerState.PointClamp);
                batcher.ClipEnd();
                return true;
            });

            return true;
        }
    }
}
#endif
