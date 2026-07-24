// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Globalization;
using System.Text.Json;
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

        public static bool TryParseSidecarCenter(string json, out int centerX, out int centerY)
        {
            centerX = 0;
            centerY = 0;

            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                    return false;

                if (!root.TryGetProperty("centerX", out JsonElement xEl) ||
                    !root.TryGetProperty("centerY", out JsonElement yEl) ||
                    xEl.ValueKind != JsonValueKind.Number ||
                    yEl.ValueKind != JsonValueKind.Number)
                {
                    return false;
                }

                centerX = xEl.GetInt32();
                centerY = yEl.GetInt32();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryExtractWavPcm(byte[] fileBytes, out byte[] pcm)
        {
            pcm = null;

            if (fileBytes == null || fileBytes.Length < 44)
                return false;

            try
            {
                ReadOnlySpan<byte> b = fileBytes;

                if (b[0] != (byte)'R' || b[1] != (byte)'I' || b[2] != (byte)'F' || b[3] != (byte)'F' ||
                    b[8] != (byte)'W' || b[9] != (byte)'A' || b[10] != (byte)'V' || b[11] != (byte)'E')
                {
                    return false;
                }

                int pos = 12;
                short audioFormat = 0, channels = 0, bitsPerSample = 0;
                int sampleRate = 0;
                bool haveFmt = false;

                while (pos + 8 <= b.Length)
                {
                    uint chunkId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(b.Slice(pos, 4));
                    int chunkSize = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(b.Slice(pos + 4, 4));
                    int body = pos + 8;

                    if (chunkSize < 0 || body + chunkSize > b.Length)
                        return false;

                    if (chunkId == 0x666D7420u) // "fmt "
                    {
                        audioFormat = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(b.Slice(body, 2));
                        channels = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(b.Slice(body + 2, 2));
                        sampleRate = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(b.Slice(body + 4, 4));
                        bitsPerSample = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(b.Slice(body + 14, 2));
                        haveFmt = true;
                    }
                    else if (chunkId == 0x64617461u) // "data"
                    {
                        if (!haveFmt)
                            return false;

                        if (audioFormat != 1 || channels != 1 || bitsPerSample != 16 || sampleRate != 22050)
                        {
                            Log.Warn(
                                $"AssetOverrides: sound override must be PCM 22050Hz 16-bit mono " +
                                $"(got fmt={audioFormat} ch={channels} rate={sampleRate} bits={bitsPerSample}); ignoring."
                            );
                            return false;
                        }

                        pcm = b.Slice(body, chunkSize).ToArray();
                        return true;
                    }

                    pos = body + chunkSize + (chunkSize & 1); // chunks are word-aligned
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
