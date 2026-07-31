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
        internal const int Oversample = 3;

        private readonly byte[] _ttf;           // retained: stbtt reads from it
        private readonly stbtt_fontinfo _info;
        private readonly float _scale;
        private readonly int _ascentRaw;

        public int PixelSize { get; }

        private TtfGlyphSource(byte[] ttf, stbtt_fontinfo info, float scale, int ascentRaw, int pixelSize)
        {
            _ttf = ttf;
            _info = info;
            _scale = scale;
            _ascentRaw = ascentRaw;
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

            return new TtfGlyphSource(ttfBytes, info, scale, ascentRaw, pixelSize);
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

            // Rasterize at Oversample x the target size for full-coverage
            // interiors + smooth AA, then box-downsample to target.
            float scaleN = _scale * Oversample;

            int w, h, xoff, yoff;
            byte* bitmap = stbtt_GetCodepointBitmap(_info, scaleN, scaleN, codepoint, &w, &h, &xoff, &yoff);

            if (bitmap == null || w <= 0 || h <= 0)
            {
                if (bitmap != null)
                {
                    stbtt_FreeBitmap(bitmap, null);
                }
                return false;
            }

            byte[] over = new byte[w * h];
            fixed (byte* pOver = over)
            {
                Buffer.MemoryCopy(bitmap, pOver, over.Length, over.Length);
            }
            stbtt_FreeBitmap(bitmap, null);

            byte[] cov = Downsample(over, w, h, Oversample, out int dstW, out int dstH);

            int ascentPxN = (int)MathF.Round(_ascentRaw * scaleN);

            offsetX = ClampToSByte((int)MathF.Round(xoff / (float)Oversample));
            offsetY = ClampToSByte((int)MathF.Round((ascentPxN + yoff) / (float)Oversample));
            width = ClampToSByte(dstW);
            height = ClampToSByte(dstH);
            coverage = cov;
            return true;
        }

        private static sbyte ClampToSByte(int value)
        {
            if (value < sbyte.MinValue) return sbyte.MinValue;
            if (value > sbyte.MaxValue) return sbyte.MaxValue;
            return (sbyte)value;
        }

        // Box-downsample an 8-bit coverage bitmap by an integer factor. Output
        // dimensions are ceil(src/factor); edge blocks average over only the
        // source pixels that exist. Rounded average per block.
        internal static byte[] Downsample(byte[] src, int srcW, int srcH, int factor, out int dstW, out int dstH)
        {
            dstW = (srcW + factor - 1) / factor;
            dstH = (srcH + factor - 1) / factor;

            byte[] dst = new byte[dstW * dstH];

            for (int dy = 0; dy < dstH; dy++)
            {
                int sy0 = dy * factor;
                int sy1 = System.Math.Min(sy0 + factor, srcH);

                for (int dx = 0; dx < dstW; dx++)
                {
                    int sx0 = dx * factor;
                    int sx1 = System.Math.Min(sx0 + factor, srcW);

                    int sum = 0;
                    int count = 0;

                    for (int sy = sy0; sy < sy1; sy++)
                    {
                        int row = sy * srcW;
                        for (int sx = sx0; sx < sx1; sx++)
                        {
                            sum += src[row + sx];
                            count++;
                        }
                    }

                    dst[dy * dstW + dx] = count == 0 ? (byte)0 : (byte)((sum + count / 2) / count);
                }
            }

            return dst;
        }
    }
}
