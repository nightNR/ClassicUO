// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using ClassicUO.Game.Scenes;
using ClassicUO.Renderer;
using ClassicUO.Renderer.LoginFonts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls
{
    internal class TtfLabel : Control
    {
        private readonly ILoginFont _font;
        private readonly float _size;
        private readonly Color _color;
        private readonly float _opacity;
        private readonly float _letterSpacing;

        public string Text { get; set; }

        public TtfLabel(string text, ILoginFont font, float size, Color color, float opacity,
            int x, int y, float letterSpacing = 0f)
        {
            Text = text ?? string.Empty;
            _font = font;
            _size = size;
            _color = color;
            _opacity = opacity;
            _letterSpacing = letterSpacing;
            X = x;
            Y = y;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            if (string.IsNullOrEmpty(Text)) return true;

            string text = Text;
            float px = x, py = y, size = _size, sp = _letterSpacing, op = _opacity;
            Color col = _color;

            renderLists.AddGumpNoAtlas(batcher =>
            {
                batcher.SetSampler(SamplerState.AnisotropicClamp);
                _font.Draw(batcher, text, px, py, size, col, op, sp);
                batcher.SetSampler(SamplerState.PointClamp);
                return true;
            });

            return true;
        }
    }
}
#endif
