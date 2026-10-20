// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls
{
    /// <summary>
    /// Pure coverage math for the procedural status-bar shapes. Everything returns
    /// premultiplied white RGBA so the batcher's free-RGB tint (DrawTinted) can recolor it.
    /// </summary>
    internal static class RoundedRectMath
    {
        /// <summary>Texture edge length for a 9-sliced rounded rect of the given corner radius.</summary>
        public static int TextureSize(int radius) => radius * 2 + 2;

        /// <summary>
        /// Anti-aliased rounded rect (thickness 0) or rounded ring (thickness &gt; 0), premultiplied.
        /// </summary>
        public static byte[] Build(int radius, int thickness)
        {
            int size = TextureSize(radius);
            byte[] data = new byte[size * size * 4];
            float half = size / 2f;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float dx = Math.Abs(px + 0.5f - half) - (half - radius);
                    float dy = Math.Abs(py + 0.5f - half) - (half - radius);
                    float outside = MathF.Sqrt(Math.Max(dx, 0f) * Math.Max(dx, 0f) + Math.Max(dy, 0f) * Math.Max(dy, 0f));
                    float distance = outside + Math.Min(Math.Max(dx, dy), 0f) - radius;

                    float coverage = Math.Clamp(0.5f - distance, 0f, 1f);

                    if (thickness > 0)
                    {
                        coverage -= Math.Clamp(0.5f - distance - thickness, 0f, 1f);
                    }

                    byte a = (byte)Math.Round(Math.Clamp(coverage, 0f, 1f) * 255f);
                    int i = (py * size + px) * 4;
                    data[i] = data[i + 1] = data[i + 2] = data[i + 3] = a;
                }
            }

            return data;
        }

        /// <summary>Supersampled (4x4) coverage of a unit-square glyph, premultiplied white.</summary>
        public static byte[] BuildGlyph(StatGlyph glyph, int size)
        {
            const int SS = 4;
            byte[] data = new byte[size * size * 4];

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    int hit = 0;

                    for (int sy = 0; sy < SS; sy++)
                    {
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float u = (px + (sx + 0.5f) / SS) / size;
                            float v = (py + (sy + 0.5f) / SS) / size;

                            if (Inside(glyph, u, v))
                            {
                                hit++;
                            }
                        }
                    }

                    byte a = (byte)(hit * 255 / (SS * SS));
                    int i = (py * size + px) * 4;
                    data[i] = data[i + 1] = data[i + 2] = data[i + 3] = a;
                }
            }

            return data;
        }

        private static readonly Vector2[] BoltPolygon =
        {
            new Vector2(0.62f, 0.00f), new Vector2(0.22f, 0.56f), new Vector2(0.46f, 0.56f),
            new Vector2(0.36f, 1.00f), new Vector2(0.80f, 0.40f), new Vector2(0.54f, 0.40f),
            new Vector2(0.72f, 0.00f),
        };

        private static readonly Vector2[] ArrowRightPolygon =
        {
            new Vector2(0.15f, 0.10f), new Vector2(0.90f, 0.50f), new Vector2(0.15f, 0.90f),
        };

        private static bool Inside(StatGlyph glyph, float u, float v)
        {
            switch (glyph)
            {
                case StatGlyph.Heart:
                {
                    float x = (u - 0.5f) * 2.7f;
                    float y = (0.5f - v) * 2.7f + 0.1f;
                    float a = x * x + y * y - 1f;

                    return a * a * a - x * x * y * y * y <= 0f;
                }
                case StatGlyph.Drop:
                {
                    float cx = u - 0.5f, cy = v - 0.66f;

                    if (cx * cx + cy * cy <= 0.30f * 0.30f)
                    {
                        return true;
                    }

                    // triangle apex (0.5, 0.05) widening to the circle's tangent height
                    if (v < 0.05f || v > 0.62f)
                    {
                        return false;
                    }

                    float halfWidth = (v - 0.05f) / (0.62f - 0.05f) * 0.29f;

                    return Math.Abs(cx) <= halfWidth;
                }
                case StatGlyph.ArrowRight:
                    return PointInPolygon(ArrowRightPolygon, u, v);
                case StatGlyph.ArrowLeft:
                    return PointInPolygon(ArrowRightPolygon, 1f - u, v);
                default:
                    return PointInPolygon(BoltPolygon, u, v);
            }
        }

        private static bool PointInPolygon(Vector2[] poly, float x, float y)
        {
            bool inside = false;

            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].Y > y) != (poly[j].Y > y)
                    && x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }

    internal enum StatGlyph
    {
        Heart,
        Drop,
        Bolt,
        ArrowLeft,
        ArrowRight,
    }

    /// <summary>
    /// 9-sliced rounded rect / ring / glyph drawing. One white premultiplied texture per
    /// (radius, thickness) is generated lazily and tinted to any RGB at draw time, so size and
    /// colour are free parameters.
    /// </summary>
    internal static class RoundedRect
    {
        private static readonly Dictionary<(int radius, int thickness), Texture2D> Shapes = new Dictionary<(int, int), Texture2D>();
        private static readonly Dictionary<(StatGlyph glyph, int size), Texture2D> Glyphs = new Dictionary<(StatGlyph, int), Texture2D>();

        private static Texture2D Shape(int radius, int thickness)
        {
            if (!Shapes.TryGetValue((radius, thickness), out Texture2D texture))
            {
                texture = Upload(RoundedRectMath.Build(radius, thickness), RoundedRectMath.TextureSize(radius));
                Shapes[(radius, thickness)] = texture;
            }

            return texture;
        }

        private static Texture2D Upload(byte[] premultiplied, int size)
        {
            var texture = new Texture2D(Client.Game.GraphicsDevice, size, size, false, SurfaceFormat.Color);
            texture.SetData(premultiplied);

            return texture;
        }

        /// <summary>Filled rounded rect. The radius is clamped to what the rect can hold.</summary>
        public static void Fill(UltimaBatcher2D batcher, Rectangle rect, int radius, Vector3 rgb, float alpha, float depth)
        {
            if (rect.Width <= 0 || rect.Height <= 0 || alpha <= 0f)
            {
                return;
            }

            radius = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            Slice(batcher, Shape(radius, 0), radius, rect, rgb, alpha, depth, includeCenter: true);
        }

        /// <summary>Rounded outline of the given thickness (clamped to the radius).</summary>
        public static void Ring(UltimaBatcher2D batcher, Rectangle rect, int radius, int thickness, Vector3 rgb, float alpha, float depth)
        {
            if (rect.Width <= 0 || rect.Height <= 0 || alpha <= 0f)
            {
                return;
            }

            radius = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            thickness = Math.Max(1, Math.Min(thickness, radius));
            Slice(batcher, Shape(radius, thickness), radius, rect, rgb, alpha, depth, includeCenter: false);
        }

        private static Texture2D GlyphTexture(StatGlyph glyph, int size)
        {
            if (!Glyphs.TryGetValue((glyph, size), out Texture2D texture))
            {
                texture = Upload(RoundedRectMath.BuildGlyph(glyph, size), size);
                Glyphs[(glyph, size)] = texture;
            }

            return texture;
        }

        public static void Glyph(UltimaBatcher2D batcher, StatGlyph glyph, Rectangle rect, Vector3 rgb, float alpha, float depth)
        {
            int size = Math.Max(4, Math.Min(rect.Width, rect.Height));

            batcher.DrawTinted(GlyphTexture(glyph, size), new Rectangle(rect.X, rect.Y, size, size), new Rectangle(0, 0, size, size), rgb, alpha, depth);
        }

        /// <summary>
        /// Glyph with a dark outline and a left-to-right color gradient (drawn as vertical strips).
        /// </summary>
        public static void GlyphOutlinedGradient(
            UltimaBatcher2D batcher,
            StatGlyph glyph,
            Rectangle rect,
            Vector3 leftRgb,
            Vector3 rightRgb,
            Vector3 outlineRgb,
            int outline,
            float depth
        )
        {
            int size = Math.Max(4, Math.Min(rect.Width, rect.Height));
            Texture2D texture = GlyphTexture(glyph, size);

            for (int dy = -outline; dy <= outline; dy++)
            {
                for (int dx = -outline; dx <= outline; dx++)
                {
                    if (dx != 0 || dy != 0)
                    {
                        batcher.DrawTinted(texture, new Rectangle(rect.X + dx, rect.Y + dy, size, size), new Rectangle(0, 0, size, size), outlineRgb, 1f, depth);
                    }
                }
            }

            int strips = Math.Min(size, 12);

            for (int i = 0; i < strips; i++)
            {
                int x0 = i * size / strips;
                int x1 = (i + 1) * size / strips;

                if (x1 <= x0)
                {
                    continue;
                }

                float t = strips == 1 ? 0f : i / (float)(strips - 1);

                batcher.DrawTinted(
                    texture,
                    new Rectangle(rect.X + x0, rect.Y, x1 - x0, size),
                    new Rectangle(x0, 0, x1 - x0, size),
                    Vector3.Lerp(leftRgb, rightRgb, t),
                    1f,
                    depth
                );
            }
        }

        private static void Slice(UltimaBatcher2D batcher, Texture2D texture, int r, Rectangle rect, Vector3 rgb, float alpha, float depth, bool includeCenter)
        {
            int size = texture.Width;
            int midSrc = size - 2 * r; // 2px stretchable middle band
            int midW = rect.Width - 2 * r;
            int midH = rect.Height - 2 * r;

            int[] srcX = { 0, r, r + midSrc };
            int[] srcW = { r, midSrc, r };
            int[] dstX = { rect.X, rect.X + r, rect.X + r + midW };
            int[] dstW = { r, midW, r };
            int[] srcY = srcX;
            int[] srcH = srcW;
            int[] dstY = { rect.Y, rect.Y + r, rect.Y + r + midH };
            int[] dstH = { r, midH, r };

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    if (!includeCenter && row == 1 && col == 1)
                    {
                        continue;
                    }

                    if (dstW[col] <= 0 || dstH[row] <= 0)
                    {
                        continue;
                    }

                    batcher.DrawTinted(
                        texture,
                        new Rectangle(dstX[col], dstY[row], dstW[col], dstH[row]),
                        new Rectangle(srcX[col], srcY[row], srcW[col], srcH[row]),
                        rgb,
                        alpha,
                        depth
                    );
                }
            }
        }
    }
}
