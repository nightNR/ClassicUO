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
    // A realm/server row rendered from the input-field art: neutral = default,
    // focus = hovered OR selected, disabled = pressed. Shows the realm name on the
    // left and (once pinged) the latency right-aligned. Periodically re-pings.
    internal class RealmButton : Control
    {
        private static readonly Color TEXT = new Color(0xD3, 0xC2, 0xA1);
        private const float OPACITY = 0.9f;

        private readonly ServerListEntry _entry;
        private readonly ILoginFont _font;
        private readonly float _fontSize;
        private bool _pressed;
        private long _nextPing;

        public bool Selected { get; set; }
        public ServerListEntry Entry => _entry;
        public Action<RealmButton> Clicked;
        public Action<RealmButton> Activated;   // double-click → proceed

        public RealmButton(ServerListEntry entry, ILoginFont font, float fontSize, int x, int y, int w, int h)
        {
            _entry = entry;
            _font = font;
            _fontSize = fontSize;
            X = x;
            Y = y;
            Width = w;
            Height = h;
            AcceptMouseInput = true;
            CanMove = false;
        }

        public override void Update()
        {
            base.Update();

            if (Time.Ticks > _nextPing)
            {
                _nextPing = (long)Time.Ticks + 2000;
                _entry.DoPing();
            }
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
                    Clicked?.Invoke(this);
                }
            }
        }

        protected override void OnMouseExit(int x, int y)
        {
            _pressed = false;
        }

        protected override bool OnMouseDoubleClick(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left)
            {
                Selected = true;
                Activated?.Invoke(this);
                return true;
            }

            return false;
        }

        private Texture2D CurrentBg()
        {
            if (_pressed) return LoginAssets.InputDisabled;
            if (Selected || MouseIsOver) return LoginAssets.InputFocus;
            return LoginAssets.InputNeutral;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float depth = layerDepthRef;
            Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);
            Texture2D bg = CurrentBg();
            var dest = new Rectangle(x, y, Width, Height);
            var bgSrc = new Rectangle(
                (int)(bg.Width * 0.04f),
                (int)(bg.Height * 0.35f),
                (int)(bg.Width * 0.92f),
                (int)(bg.Height * 0.315f));

            string name = _entry.Name ?? string.Empty;
            int ping = _entry.Ping;
            string latency = ping > 0 ? $"{ping}ms" : null;

            ILoginFont font = _font;
            float fs = _fontSize;
            int w = Width;

            renderLists.AddGumpNoAtlas(batcher =>
            {
                batcher.SetSampler(SamplerState.AnisotropicClamp);
                batcher.DrawTinted(bg, new Rectangle(dest.X + 2, dest.Y + 3, dest.Width, dest.Height), bgSrc, Vector3.Zero, 0.07f, depth);
                batcher.Draw(bg, dest, bgSrc, hue, depth);

                float lineH = font.Measure("Ag", fs).Y;
                float ty = y + (Height - lineH) / 2f;
                float pad = w * 0.10f + 10;

                font.Draw(batcher, name, x + pad, ty, fs, TEXT, OPACITY);

                if (latency != null)
                {
                    float lw = font.Measure(latency, fs).X;
                    font.Draw(batcher, latency, x + Width - pad - lw, ty, fs, TEXT, OPACITY);
                }

                batcher.SetSampler(SamplerState.PointClamp);
                return true;
            });

            return true;
        }
    }
}
#endif
