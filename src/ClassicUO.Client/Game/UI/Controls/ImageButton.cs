// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using System;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Login;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Renderer.LoginFonts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls
{
    internal enum ButtonStyle { Dark, Priority }

    internal class ImageButton : Control
    {
        private readonly ButtonStyle _style;
        private readonly string _label;
        private readonly ILoginFont _font;
        private readonly float _fontSize;
        private bool _pressed;

        public Action Clicked;

        public ImageButton(ButtonStyle style, string label, ILoginFont font, float fontSize,
            int x, int y, int w, int h)
        {
            _style = style;
            _label = (label ?? string.Empty).ToUpperInvariant();
            _font = font;
            _fontSize = fontSize;
            X = x; Y = y; Width = w; Height = h;
            AcceptMouseInput = true;
            CanMove = false;
            CanCloseWithEsc = false;
        }

        private Texture2D CurrentTexture()
        {
            if (_style == ButtonStyle.Priority)
            {
                return _pressed ? LoginAssets.ButtonPrioDown
                     : MouseIsOver ? LoginAssets.ButtonPrioHover : LoginAssets.ButtonPrioNeutral;
            }

            return _pressed ? LoginAssets.ButtonDown
                 : MouseIsOver ? LoginAssets.ButtonHover : LoginAssets.ButtonNeutral;
        }

        protected override void OnMouseDown(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left)
            {
                _pressed = true;
            }
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left && _pressed)
            {
                _pressed = false;

                if (MouseIsOver)
                {
                    Clicked?.Invoke();
                }
            }
        }

        protected override void OnMouseExit(int x, int y)
        {
            _pressed = false;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float depth = layerDepthRef;
            Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);
            Texture2D tex = CurrentTexture();
            var dest = new Rectangle(x, y, Width, Height);
            // Crop the ornate transparent padding: draw only the visible button band
            // so the control W×H maps to the button itself (correct aspect, no oversized hitbox).
            var src = new Rectangle(
                (int)(tex.Width * 0.025f),
                (int)(tex.Height * 0.27f),
                (int)(tex.Width * 0.95f),
                (int)(tex.Height * 0.46f));

            // Letter-spacing per Global Constraints (buttons: 2-4px). Use 3.
            const float SPACING = 3f;
            Vector2 size = _font.Measure(_label, _fontSize, SPACING);
            float tx = x + (Width - size.X) / 2f;
            float ty = y + (Height - size.Y) / 2f;
            var labelColor = new Color(0xF0, 0xE6, 0xC8); // warm parchment; refine in Task 9

            renderLists.AddGumpNoAtlas(batcher =>
            {
                batcher.SetSampler(SamplerState.AnisotropicClamp);
                // Soft drop shadow: a few low-opacity black taps at increasing offset
                // (black silhouette via the TEXT_RGB mode; the spread fakes a blur).
                batcher.DrawTinted(tex, new Rectangle(dest.X + 2, dest.Y + 3, dest.Width, dest.Height), src, Vector3.Zero, 0.09f, depth);
                batcher.DrawTinted(tex, new Rectangle(dest.X + 4, dest.Y + 5, dest.Width, dest.Height), src, Vector3.Zero, 0.08f, depth);
                batcher.DrawTinted(tex, new Rectangle(dest.X + 6, dest.Y + 8, dest.Width, dest.Height), src, Vector3.Zero, 0.06f, depth);
                batcher.Draw(tex, dest, src, hue, depth);
                _font.Draw(batcher, _label, tx, ty, _fontSize, labelColor, 1f, SPACING);
                batcher.SetSampler(SamplerState.PointClamp);
                return true;
            });

            return true;
        }
    }
}
#endif
