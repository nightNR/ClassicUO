// SPDX-License-Identifier: BSD-2-Clause

using System;
using StbTrueTypeSharp;
using static StbTrueTypeSharp.StbTrueType;

namespace ClassicUO.Assets
{
    /// <summary>
    /// Rasterizes single glyphs from one TTF face at one fixed pixel size via
    /// StbTrueType. Reproduces the metric mapping the offline atlas baker used,
    /// so live glyphs match the previously-baked ones byte-for-byte.
    /// </summary>
    internal sealed class TtfGlyphSource
    {
        private readonly byte[] _ttf;           // retained: stbtt reads from it
        private readonly stbtt_fontinfo _info;
        private readonly float _scale;
        private readonly int _ascentPx;

        public int PixelSize { get; }

        private TtfGlyphSource(byte[] ttf, stbtt_fontinfo info, float scale, int ascentPx, int pixelSize)
        {
            _ttf = ttf;
            _info = info;
            _scale = scale;
            _ascentPx = ascentPx;
            PixelSize = pixelSize;
        }

        public static TtfGlyphSource Create(byte[] ttfBytes, int pixelSize)
        {
            if (ttfBytes == null || ttfBytes.Length == 0 || pixelSize <= 0)
            {
                return null;
            }

            // Validate the sfnt header in managed code before handing the buffer to
            // native stbtt_InitFont. Malformed input can make the native parser read
            // out of bounds and raise an AccessViolationException, which is a
            // corrupted-state exception that a normal try/catch cannot intercept and
            // would otherwise abort the process.
            if (ttfBytes.Length < 12)
            {
                return null;
            }

            uint sfntVersion = (uint)((ttfBytes[0] << 24) | (ttfBytes[1] << 16) | (ttfBytes[2] << 8) | ttfBytes[3]);

            if (sfntVersion != 0x00010000 // TrueType outlines
                && sfntVersion != 0x4F54544F // 'OTTO' - CFF/OpenType
                && sfntVersion != 0x74727565 // 'true'
                && sfntVersion != 0x74746366) // 'ttcf' - TrueType Collection
            {
                return null;
            }

            stbtt_fontinfo info;
            try
            {
                info = CreateFont(ttfBytes, 0);
            }
            catch (Exception)
            {
                return null;
            }

            if (info == null)
            {
                return null;
            }

            float scale = stbtt_ScaleForPixelHeight(info, pixelSize);

            int ascentRaw, descentRaw, lineGapRaw;
            unsafe
            {
                stbtt_GetFontVMetrics(info, &ascentRaw, &descentRaw, &lineGapRaw);
            }

            int ascentPx = (int)MathF.Round(ascentRaw * scale);

            return new TtfGlyphSource(ttfBytes, info, scale, ascentPx, pixelSize);
        }

        public unsafe bool TryRasterize(
            char c, out sbyte offsetX, out sbyte offsetY,
            out sbyte width, out sbyte height, out byte[] coverage)
        {
            offsetX = 0; offsetY = 0; width = 0; height = 0; coverage = null;

            int codepoint = c;

            if (stbtt_FindGlyphIndex(_info, codepoint) == 0)
            {
                return false; // glyph not present in this face
            }

            int w, h, xoff, yoff;
            byte* bitmap = stbtt_GetCodepointBitmap(_info, _scale, _scale, codepoint, &w, &h, &xoff, &yoff);

            if (bitmap == null || w <= 0 || h <= 0)
            {
                if (bitmap != null)
                {
                    stbtt_FreeBitmap(bitmap, null);
                }
                return false;
            }

            byte[] cov = new byte[w * h];
            fixed (byte* pCov = cov)
            {
                Buffer.MemoryCopy(bitmap, pCov, cov.Length, cov.Length);
            }
            stbtt_FreeBitmap(bitmap, null);

            offsetX = ClampToSByte(xoff);
            offsetY = ClampToSByte(_ascentPx + yoff);
            width = ClampToSByte(w);
            height = ClampToSByte(h);
            coverage = cov;
            return true;
        }

        private static sbyte ClampToSByte(int value)
        {
            if (value < sbyte.MinValue) return sbyte.MinValue;
            if (value > sbyte.MaxValue) return sbyte.MaxValue;
            return (sbyte)value;
        }
    }
}
