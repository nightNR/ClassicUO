// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using System;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Login;
using ClassicUO.Renderer;
using ClassicUO.Renderer.LoginFonts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDL3;

namespace ClassicUO.Game.UI.Controls
{
    /// <summary>
    /// A login-scene text input that reuses <see cref="StbTextBox"/> purely for its editing/caret
    /// state machine (typing, backspace, arrow-key navigation, selection, undo/redo) while replacing
    /// all rendering with the TTF font backend (<see cref="ILoginFont"/>) and a PNG background that
    /// swaps between neutral/focus art from <see cref="LoginAssets"/>.
    ///
    /// Integration choice: SUBCLASS StbTextBox rather than compose. StbTextBox.AddToRenderLists is
    /// `public virtual` (already re-overridden once by LoginGump's private PasswordStbTextBox to do
    /// exactly this kind of "keep base editing, replace drawing" swap), so overriding it here is the
    /// smallest-glue integration: no duplicate TextEdit/Stb instance, no manual event forwarding for
    /// keyboard/mouse/focus, and CaretIndex/Text/SetText/SelectionStart/SelectionEnd all come free.
    /// </summary>
    internal class TtfTextBox : StbTextBox
    {
        // Backing UO bitmap font used only to drive StbTextBox's internal glyph-width probing
        // (OnTextInput rejects characters with zero width in this font) and its private
        // RenderedText instances, which are created but never drawn. Font 5 + isunicode:false
        // matches the values LoginGump already uses for its account/password fields.
        private const byte BACKING_FONT = 5;

        private static readonly Color TEXT_COLOR = new Color(0xD3, 0xC2, 0xA1);
        private const float TEXT_OPACITY = 0.9f;
        private static readonly Color PLACEHOLDER_COLOR = new Color(0x9A, 0x8C, 0x72);
        private const float PLACEHOLDER_OPACITY = 0.6f;

        /// <summary>Dim hint text shown inside the field while it is empty.</summary>
        public string Placeholder { get; set; }

        private readonly ILoginFont _font;
        private readonly float _fontSize;
        private readonly bool _isPassword;

        /// <summary>
        /// Raised when Enter/Return is pressed while this control has keyboard focus. Passes the
        /// current (unmasked) <see cref="Text"/>. This is in addition to the inherited
        /// StbTextBox behavior of calling `Parent?.OnKeyboardReturn(0, Text)` (see
        /// StbTextBox.OnKeyDown), so a hosting gump may use either: override OnKeyboardReturn like
        /// LoginGump does, or subscribe to this event directly regardless of parent hierarchy.
        /// </summary>
        public event Action<string> EnterPressed;

        public bool IsPassword => _isPassword;

        public TtfTextBox
        (
            ILoginFont font,
            float fontSize,
            int x,
            int y,
            int w,
            int h,
            bool isPassword,
            int maxChars = 32
        ) : base(BACKING_FONT, maxChars, 0, false, hue: 0)
        {
            _font = font ?? throw new ArgumentNullException(nameof(font));
            _fontSize = fontSize;
            _isPassword = isPassword;

            X = x;
            Y = y;
            Width = w;
            Height = h;
        }

        public new void SetText(string s)
        {
            base.SetText(s ?? string.Empty);
        }

        protected override void OnKeyDown(SDL.SDL_Keycode key, SDL.SDL_Keymod mod)
        {
            base.OnKeyDown(key, mod);

            if (key == SDL.SDL_Keycode.SDLK_RETURN || key == SDL.SDL_Keycode.SDLK_KP_ENTER)
            {
                EnterPressed?.Invoke(Text);
            }
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float depth = layerDepthRef;
            Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);
            Texture2D bg = HasKeyboardFocus ? LoginAssets.InputFocus : LoginAssets.InputNeutral;
            var dest = new Rectangle(x, y, Width, Height);
            // Crop the ornate transparent padding: draw only the visible input bar
            // so the control W×H maps to the bar itself (correct aspect, tight hitbox).
            var bgSrc = new Rectangle(
                (int)(bg.Width * 0.04f),
                (int)(bg.Height * 0.35f),
                (int)(bg.Width * 0.92f),
                (int)(bg.Height * 0.315f));

            string real = Text ?? string.Empty;
            string display = _isPassword ? new string('*', real.Length) : real;

            // Caret metrics: measure the display string up to the caret index and place a thin
            // '|' glyph there. This is an approximation (glyph-index based, not true stb caret
            // pixel offsets from StbTextBox's UO-font renderer) — acceptable first pass per the
            // task brief; refine with real per-glyph caret metrics from ILoginFont in Task 9.
            int caretIndex = Math.Clamp(CaretIndex, 0, real.Length);
            string caretPrefix = _isPassword ? new string('*', caretIndex) : real.Substring(0, caretIndex);

            ILoginFont font = _font;
            float fontSize = _fontSize;
            bool focused = HasKeyboardFocus;
            string placeholder = Placeholder;
            int width = Width;

            renderLists.AddGumpNoAtlas(batcher =>
            {
                batcher.SetSampler(SamplerState.AnisotropicClamp);
                // Soft drop shadow: low-opacity black taps at increasing offset.
                batcher.DrawTinted(bg, new Rectangle(dest.X + 2, dest.Y + 3, dest.Width, dest.Height), bgSrc, Vector3.Zero, 0.09f, depth);
                batcher.DrawTinted(bg, new Rectangle(dest.X + 4, dest.Y + 5, dest.Width, dest.Height), bgSrc, Vector3.Zero, 0.08f, depth);
                batcher.DrawTinted(bg, new Rectangle(dest.X + 6, dest.Y + 8, dest.Width, dest.Height), bgSrc, Vector3.Zero, 0.06f, depth);
                batcher.Draw(bg, dest, bgSrc, hue, depth);

                // Left margin clears the bar's ornate end cap (~10% of width), then
                // ~10px of clear space; text vertically centered to the field height.
                float tx = x + width * 0.10f + 10;
                float lineH = font.Measure("Ag", fontSize).Y;
                float ty = y + (Height - lineH) / 2f;

                if (display.Length > 0)
                {
                    font.Draw(batcher, display, tx, ty, fontSize, TEXT_COLOR, TEXT_OPACITY);
                }
                else if (!string.IsNullOrEmpty(placeholder))
                {
                    font.Draw(batcher, placeholder, tx, ty, fontSize, PLACEHOLDER_COLOR, PLACEHOLDER_OPACITY);
                }

                if (focused)
                {
                    float caretX = tx;

                    if (caretPrefix.Length > 0)
                    {
                        caretX += font.Measure(caretPrefix, fontSize).X;
                    }

                    font.Draw(batcher, "|", caretX, ty, fontSize, TEXT_COLOR, TEXT_OPACITY);
                }

                batcher.SetSampler(SamplerState.PointClamp);
                return true;
            });

            return true;
        }
    }
}
#endif
