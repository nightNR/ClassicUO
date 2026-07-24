// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Globalization;
using ClassicUO.Utility.Logging;
using StbImageSharp;

namespace ClassicUO.Assets
{
    public enum AssetOverrideKind
    {
        Gump,
        Art,
        Land,
        Texmap,
        Light,
    }

    public sealed partial class AssetOverrides
    {
        public bool Enabled { get; private set; }

        public static bool TryParseId(string stem, out uint id)
        {
            id = 0;

            if (string.IsNullOrEmpty(stem))
                return false;

            if (stem.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return uint.TryParse(
                    stem.AsSpan(2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out id
                );
            }

            return uint.TryParse(
                stem,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out id
            );
        }

        public static bool TryDecodePng(byte[] fileBytes, out uint[] pixels, out int width, out int height)
        {
            pixels = null;
            width = 0;
            height = 0;

            if (fileBytes == null || fileBytes.Length == 0)
                return false;

            ImageResult image;
            try
            {
                image = ImageResult.FromMemory(fileBytes, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex)
            {
                Log.Warn($"AssetOverrides: failed to decode image ({ex.Message})");
                return false;
            }

            if (image == null || image.Width <= 0 || image.Height <= 0 || image.Data == null)
                return false;

            width = image.Width;
            height = image.Height;
            pixels = new uint[width * height];

            byte[] d = image.Data; // RGBA, 4 bytes per pixel
            for (int i = 0; i < pixels.Length; i++)
            {
                int o = i * 4;
                uint r = d[o];
                uint g = d[o + 1];
                uint b = d[o + 2];
                uint a = d[o + 3];
                pixels[i] = r | (g << 8) | (b << 16) | (a << 24);
            }

            return true;
        }
    }
}
