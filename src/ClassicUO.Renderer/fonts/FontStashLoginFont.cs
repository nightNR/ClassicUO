// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Drawing;
using FontStashSharp;
using FontStashSharp.Interfaces;
using Microsoft.Xna.Framework.Graphics;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using NumVector2 = System.Numerics.Vector2;

namespace ClassicUO.Renderer.LoginFonts
{
    // Backs ILoginFont with FontStashSharp (the platform-agnostic "FontStashSharp"
    // package, not "FontStashSharp.FNA" which does not exist on NuGet - see
    // ClassicUO.Renderer.csproj comment). FontSystem/DynamicSpriteFont in this
    // package use System.Numerics.Vector2, FontStashSharp.FSColor and
    // System.Drawing.Rectangle/Point rather than the FNA/XNA equivalents, so this
    // class is also the conversion boundary between the two type systems.
    public sealed class FontStashLoginFont : ILoginFont, IDisposable
    {
        private readonly FontSystem _fontSystem;
        private readonly BatcherFontRenderer _renderer;

        public FontStashLoginFont(GraphicsDevice device, byte[] ttfBytes)
        {
            _fontSystem = new FontSystem();
            _fontSystem.AddFont(ttfBytes);
            _renderer = new BatcherFontRenderer(device);
        }

        public XnaVector2 Measure(ReadOnlySpan<char> text, float size, float letterSpacing = 0f)
        {
            DynamicSpriteFont font = _fontSystem.GetFont(size);
            NumVector2 measured = font.MeasureString(text.ToString(), characterSpacing: letterSpacing);
            return new XnaVector2(measured.X, measured.Y);
        }

        public void Draw(
            UltimaBatcher2D batcher,
            ReadOnlySpan<char> text,
            float x,
            float y,
            float size,
            XnaColor color,
            float opacity = 1f,
            float letterSpacing = 0f)
        {
            DynamicSpriteFont font = _fontSystem.GetFont(size);
            _renderer.Begin(batcher);

            FSColor fsColor = new FSColor(color.R, color.G, color.B, (byte)(color.A * opacity));

            font.DrawText(
                _renderer,
                text.ToString(),
                new NumVector2(x, y),
                fsColor,
                characterSpacing: letterSpacing);
        }

        public void Dispose() => _fontSystem.Dispose();

        // Bridges FontStashSharp glyph quads to UltimaBatcher2D + FNA textures.
        private sealed class BatcherFontRenderer : IFontStashRenderer, ITexture2DManager
        {
            private readonly GraphicsDevice _device;
            private UltimaBatcher2D _batcher;

            public BatcherFontRenderer(GraphicsDevice device) => _device = device;

            public ITexture2DManager TextureManager => this;

            public void Begin(UltimaBatcher2D batcher) => _batcher = batcher;

            // ITexture2DManager
            public object CreateTexture(int width, int height)
                => new Texture2D(_device, width, height);

            public Point GetTextureSize(object texture)
            {
                Texture2D t = (Texture2D)texture;
                return new Point(t.Width, t.Height);
            }

            public void SetTextureData(object texture, Rectangle bounds, byte[] data)
            {
                Texture2D t = (Texture2D)texture;
                t.SetData(0, new XnaRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    data, 0, data.Length);
            }

            // IFontStashRenderer
            public void Draw(object texture, NumVector2 pos, Rectangle? src,
                FSColor color, float rotation, NumVector2 scale, float depth)
            {
                Texture2D t = (Texture2D)texture;
                XnaRectangle source = src.HasValue
                    ? new XnaRectangle(src.Value.X, src.Value.Y, src.Value.Width, src.Value.Height)
                    : new XnaRectangle(0, 0, t.Width, t.Height);

                Microsoft.Xna.Framework.Vector3 tintRgb = new Microsoft.Xna.Framework.Vector3(
                    color.R / 255f, color.G / 255f, color.B / 255f);
                // Per-glyph draw; rotation/scale are unused because DrawText is
                // invoked without rotation or an explicit scale (baked into
                // FontStashSharp's source size selection via GetFont(size) instead).
                _batcher.DrawTextGlyph(t, new XnaVector2(pos.X, pos.Y), source, tintRgb, color.A / 255f, depth);
            }
        }
    }
}
