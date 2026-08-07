// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Scenes;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using SDL3;

namespace ClassicUO.Game.UI.Controls
{
    // Renders '*' in place of typed characters while keeping the real text available
    // via RealText, for password-style single-line inputs.
    internal class PasswordStbTextBox : StbTextBox
    {
        private new Point _caretScreenPosition;
        private new readonly RenderedText _rendererCaret;

        private new readonly RenderedText _rendererText;

        public PasswordStbTextBox
        (
            byte font,
            int max_char_count = -1,
            int maxWidth = 0,
            bool isunicode = true,
            FontStyle style = FontStyle.None,
            ushort hue = 0,
            TEXT_ALIGN_TYPE align = TEXT_ALIGN_TYPE.TS_LEFT
        ) : base
        (
            font,
            max_char_count,
            maxWidth,
            isunicode,
            style,
            hue,
            align
        )
        {
            _rendererText = RenderedText.Create
            (
                string.Empty,
                hue,
                font,
                isunicode,
                style,
                align,
                maxWidth
            );

            _rendererCaret = RenderedText.Create
            (
                "_",
                hue,
                font,
                isunicode,
                (style & FontStyle.BlackBorder) != 0 ? FontStyle.BlackBorder : FontStyle.None,
                align
            );

            NoSelection = true;
        }

        internal string RealText
        {
            get => Text;
            set => SetText(value);
        }

        public new ushort Hue
        {
            get => _rendererText.Hue;
            set
            {
                if (_rendererText.Hue != value)
                {
                    _rendererText.Hue = value;
                    _rendererCaret.Hue = value;

                    _rendererText.CreateTexture();
                    _rendererCaret.CreateTexture();
                }
            }
        }

        protected override void DrawCaret(UltimaBatcher2D batcher, int x, int y, float layerDepth)
        {
            if (HasKeyboardFocus)
            {
                _rendererCaret.Draw(batcher, x + _caretScreenPosition.X, y + _caretScreenPosition.Y, layerDepth);
            }
        }

        protected override void OnMouseDown(int x, int y, MouseButtonType button)
        {
            base.OnMouseDown(x, y, button);

            if (button == MouseButtonType.Left)
            {
                UpdateCaretScreenPosition();
            }
        }

        protected override void OnKeyDown(SDL.SDL_Keycode key, SDL.SDL_Keymod mod)
        {
            base.OnKeyDown(key, mod);
            UpdateCaretScreenPosition();
        }

        public override void Dispose()
        {
            _rendererText?.Destroy();
            _rendererCaret?.Destroy();

            base.Dispose();
        }

        protected override void OnTextChanged(string previousText)
        {
            if (Text.Length > 0)
            {
                _rendererText.Text = new string('*', Text.Length);
            }
            else
            {
                _rendererText.Text = string.Empty;
            }

            base.OnTextChanged(previousText);
            UpdateCaretScreenPosition();
        }

        internal override void OnFocusEnter()
        {
            base.OnFocusEnter();
            CaretIndex = Text?.Length ?? 0;
            UpdateCaretScreenPosition();
        }

        private new void UpdateCaretScreenPosition()
        {
            _caretScreenPosition = _rendererText.GetCaretPosition(Stb.CursorIndex);
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float layerDepth = layerDepthRef;
            renderLists.AddGumpNoAtlas(
                batcher =>
                {
                    if (batcher.ClipBegin(x, y, Width, Height))
                    {
                        DrawSelection(batcher, x, y, layerDepth);

                        _rendererText.Draw(batcher, x, y, layerDepth);

                        DrawCaret(batcher, x, y, layerDepth);
                        batcher.ClipEnd();
                    }
                    return true;
                }
            );

            return true;
        }
    }
}
