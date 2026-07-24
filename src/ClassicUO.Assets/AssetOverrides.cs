// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

            if (image.Data.Length < width * height * 4)
                return false;

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

        private readonly Dictionary<AssetOverrideKind, Dictionary<uint, string>> _images =
            new Dictionary<AssetOverrideKind, Dictionary<uint, string>>();
        private readonly Dictionary<(uint id, int group, int dir, int frame), string> _anim =
            new Dictionary<(uint, int, int, int), string>();
        private readonly Dictionary<uint, string> _sounds = new Dictionary<uint, string>();

        private static readonly (AssetOverrideKind kind, string folder)[] _imageFolders =
        {
            (AssetOverrideKind.Gump, "Gump"),
            (AssetOverrideKind.Art, "Art"),
            (AssetOverrideKind.Land, "Land"),
            (AssetOverrideKind.Texmap, "Texmap"),
            (AssetOverrideKind.Light, "Light"),
        };

        public void Build(string overridesRoot)
        {
            _images.Clear();
            _anim.Clear();
            _sounds.Clear();
            Enabled = false;

            foreach (var (kind, _) in _imageFolders)
                _images[kind] = new Dictionary<uint, string>();

            if (string.IsNullOrEmpty(overridesRoot) || !Directory.Exists(overridesRoot))
                return;

            Enabled = true;

            foreach (var (kind, folder) in _imageFolders)
            {
                string dir = Path.Combine(overridesRoot, folder);
                if (!Directory.Exists(dir))
                    continue;

                try
                {
                    foreach (string file in Directory.EnumerateFiles(dir, "*.png"))
                    {
                        string stem = Path.GetFileNameWithoutExtension(file);
                        if (TryParseId(stem, out uint id))
                            _images[kind][id] = file;
                        else
                            Log.Warn($"AssetOverrides: skipping unparseable file name '{file}'");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"AssetOverrides: failed to enumerate '{dir}' ({ex.Message}); skipping.");
                }
            }

            string animRoot = Path.Combine(overridesRoot, "Anim");
            if (Directory.Exists(animRoot))
            {
                try
                {
                    foreach (string idDir in Directory.EnumerateDirectories(animRoot))
                    {
                        if (!TryParseId(Path.GetFileName(idDir), out uint id))
                        {
                            Log.Warn($"AssetOverrides: skipping non-id anim folder '{idDir}'");
                            continue;
                        }

                        try
                        {
                            foreach (string file in Directory.EnumerateFiles(idDir, "*.png"))
                            {
                                string stem = Path.GetFileNameWithoutExtension(file);
                                string[] parts = stem.Split('_');
                                if (parts.Length == 3 &&
                                    int.TryParse(parts[0], out int group) &&
                                    int.TryParse(parts[1], out int dir) &&
                                    int.TryParse(parts[2], out int frame))
                                {
                                    _anim[(id, group, dir, frame)] = file;
                                }
                                else
                                {
                                    Log.Warn($"AssetOverrides: skipping unparseable anim frame '{file}'");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"AssetOverrides: failed to enumerate '{idDir}' ({ex.Message}); skipping.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"AssetOverrides: failed to enumerate '{animRoot}' ({ex.Message}); skipping.");
                }
            }

            string soundDir = Path.Combine(overridesRoot, "Sound");
            if (Directory.Exists(soundDir))
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(soundDir, "*.wav"))
                    {
                        string stem = Path.GetFileNameWithoutExtension(file);
                        if (TryParseId(stem, out uint id))
                            _sounds[id] = file;
                        else
                            Log.Warn($"AssetOverrides: skipping unparseable file name '{file}'");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"AssetOverrides: failed to enumerate '{soundDir}' ({ex.Message}); skipping.");
                }
            }
        }

        private static bool LoadImageFile(
            string path, string sidecar, out uint[] pixels, out int width, out int height,
            out bool hasCenter, out int centerX, out int centerY)
        {
            pixels = null; width = 0; height = 0;
            hasCenter = false; centerX = 0; centerY = 0;

            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) { Log.Warn($"AssetOverrides: cannot read '{path}' ({ex.Message})"); return false; }

            if (!TryDecodePng(bytes, out pixels, out width, out height))
                return false;

            if (File.Exists(sidecar))
            {
                try
                {
                    if (TryParseSidecarCenter(File.ReadAllText(sidecar), out centerX, out centerY))
                        hasCenter = true;
                    else
                        Log.Warn($"AssetOverrides: malformed sidecar '{sidecar}'");
                }
                catch (Exception ex) { Log.Warn($"AssetOverrides: cannot read sidecar '{sidecar}' ({ex.Message})"); }
            }

            return true;
        }

        public bool TryGetImage(
            AssetOverrideKind kind, uint id,
            out uint[] pixels, out int width, out int height,
            out bool hasCenter, out int centerX, out int centerY)
        {
            pixels = null; width = 0; height = 0;
            hasCenter = false; centerX = 0; centerY = 0;

            if (!Enabled || !_images.TryGetValue(kind, out var byId) || !byId.TryGetValue(id, out string path))
                return false;

            string sidecar = Path.Combine(
                Path.GetDirectoryName(path) ?? string.Empty,
                id.ToString(CultureInfo.InvariantCulture) + ".json");

            return LoadImageFile(path, sidecar, out pixels, out width, out height, out hasCenter, out centerX, out centerY);
        }

        public bool TryGetAnimationFrame(
            uint id, int group, int dir, int frame,
            out uint[] pixels, out int width, out int height,
            out bool hasCenter, out int centerX, out int centerY)
        {
            pixels = null; width = 0; height = 0;
            hasCenter = false; centerX = 0; centerY = 0;

            if (!Enabled || !_anim.TryGetValue((id, group, dir, frame), out string path))
                return false;

            string sidecar = Path.Combine(
                Path.GetDirectoryName(path) ?? string.Empty,
                $"{group}_{dir}_{frame}.json");

            return LoadImageFile(path, sidecar, out pixels, out width, out height, out hasCenter, out centerX, out centerY);
        }

        public bool TryGetSound(uint id, out byte[] pcm)
        {
            pcm = null;

            if (!Enabled || !_sounds.TryGetValue(id, out string path))
                return false;

            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) { Log.Warn($"AssetOverrides: cannot read '{path}' ({ex.Message})"); return false; }

            return TryExtractWavPcm(bytes, out pcm);
        }
    }
}
