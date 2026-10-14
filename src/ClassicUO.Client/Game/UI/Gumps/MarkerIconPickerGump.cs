// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Linq;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static ClassicUO.Game.UI.Gumps.WorldMapGump;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>Grid of the loaded map-marker icons. Calls back with the picked icon name ("" = none).</summary>
    internal sealed class MarkerIconPickerGump : Gump
    {
        private const int CELL = 34;
        private const int COLUMNS = 10;
        private const int WIDTH = COLUMNS * CELL + 34;
        private const int HEIGHT = 300;
        private const ushort HUE_FONT = 0xFFFF;

        private readonly Action<string> _onPicked;

        internal MarkerIconPickerGump(World world, string current, Action<string> onPicked) : base(world, 0, 0)
        {
            _onPicked = onPicked;
            CanMove = true;
            CanCloseWithRightClick = true;
            AcceptMouseInput = true;
            X = 120;
            Y = 120;

            Add(new AlphaBlendControl(0.95f) { Width = WIDTH, Height = HEIGHT, Hue = 999, AcceptMouseInput = true, CanMove = true, CanCloseWithRightClick = true });
            Add(new Label("Pick icon", true, HUE_FONT, 0, 255, FontStyle.BlackBorder) { X = 8, Y = 5 });

            var area = new ScrollArea(6, 28, WIDTH - 12, HEIGHT - 34, true);
            int i = 0;

            // first cell = "no icon"
            AddCell(area, i++, "", null, string.IsNullOrEmpty(current));

            foreach (var kv in _markerIcons.Where(k => k.Value != null && !k.Value.IsDisposed).OrderBy(k => k.Key))
            {
                AddCell(area, i++, kv.Key, kv.Value, string.Equals(kv.Key, current, StringComparison.OrdinalIgnoreCase));
            }

            Add(area);
            SetInScreen();
        }

        private void AddCell(ScrollArea area, int index, string name, Texture2D texture, bool selected)
        {
            var cell = new IconCell(name, texture, selected)
            {
                X = (index % COLUMNS) * CELL,
                Y = (index / COLUMNS) * CELL,
            };

            cell.Picked += n =>
            {
                _onPicked?.Invoke(n);
                Dispose();
            };

            area.Add(cell);
        }

        private sealed class IconCell : Control
        {
            private readonly Texture2D _texture;
            private readonly bool _selected;

            public event Action<string> Picked;

            public string IconName { get; }

            public IconCell(string name, Texture2D texture, bool selected)
            {
                IconName = name;
                _texture = texture;
                _selected = selected;
                Width = Height = CELL - 2;
                AcceptMouseInput = true;
                CanMove = false;
                SetTooltip(string.IsNullOrEmpty(name) ? "(none)" : name);
            }

            protected override void OnMouseUp(int x, int y, MouseButtonType button)
            {
                if (button == MouseButtonType.Left)
                {
                    Picked?.Invoke(IconName);
                }

                base.OnMouseUp(x, y, button);
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float layerDepth = layerDepthRef;
                int w = Width, h = Height;
                bool over = MouseIsOver;
                Vector3 hue = ShaderHueTranslator.GetHueVector(0);

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    Color bg = _selected ? new Color(70, 110, 160, 200) : over ? new Color(90, 90, 90, 160) : new Color(30, 30, 30, 160);
                    batcher.Draw(SolidColorTextureCache.GetTexture(bg), new Rectangle(x, y, w, h), hue, layerDepth);

                    if (_texture != null)
                    {
                        // keep icons readable: scale down only when larger than the cell
                        int tw = Math.Min(_texture.Width, w - 4);
                        int th = Math.Min(_texture.Height, h - 4);
                        batcher.Draw(_texture, new Rectangle(x + ((w - tw) >> 1), y + ((h - th) >> 1), tw, th), hue, layerDepth);
                    }

                    return true;
                });

                return base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            }
        }
    }
}
