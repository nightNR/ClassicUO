# Asset Overrides Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users replace built-in UO assets (gumps, art, land, texmaps, lights, animation frames, sounds) by dropping image/sound files into an `Overrides/` folder next to the client.

**Architecture:** One new GPU-free, unit-testable class `AssetOverrides` (in `ClassicUO.Assets`) owns a startup-built index (asset kind + id → file path), PNG decoding (StbImageSharp → packed `uint[]`), sidecar-center parsing, and WAV→PCM extraction. It is owned by `UOFileManager`. Each existing renderer wrapper (`Gump`, `Art`, `Texmap`, `Light`, `Animation`, `Sound`) gains one branch: before calling its built-in loader, ask `AssetOverrides` for a replacement and, on a hit, feed those pixels/PCM into the existing atlas-upload + cache path. Discovery is a single startup scan; decode is lazy on first request; there is no file watching.

**Tech Stack:** C# `net10.0`, xUnit, StbImageSharp 2.30.15 (already resolved transitively; added as a direct ref to `ClassicUO.Assets`), FNA (renderer only).

## Global Constraints

- Target framework `net10.0`, `LangVersion=Preview`, `AllowUnsafeBlocks=true` (from `src/Directory.Build.props`).
- Every new source file carries the BSD-2 license header: first line `// SPDX-License-Identifier: BSD-2-Clause` (matches existing files, e.g. `UOFileManager.cs:1`, `AtlasFontFileTests.cs:1`).
- No copyrighted assets committed. Test fixtures are self-generated PNG/WAV byte arrays embedded as base64 constants.
- Pixel packing MUST be R in the low byte: `uint px = (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24)`. This matches `SurfaceFormat.Color` / the `SDL_PIXELFORMAT_ABGR8888` layout the client already uses for UO pixels (`Art.cs:186`).
- `0` = fully transparent (alpha 0, rgb 0). The `PixelPicker` hit-test treats `0` as "no pixel".
- Performance path: overrides are looked up only on the first `GetX(id)` for an id (cache miss); the built-in path is byte-for-byte unchanged when there is no override and when `Overrides/` is absent.
- Test projects use xUnit (`[Fact]`, `[Theory]`), namespace `ClassicUO.UnitTests`.

---

## File Structure

- **Create** `src/ClassicUO.Assets/AssetOverrides.cs` — the whole override subsystem (enum, index, id parse, PNG decode, sidecar parse, WAV parse, public query API).
- **Create** `tests/ClassicUO.UnitTests/Assets/AssetOverridesTests.cs` — unit tests for id parse, index build, PNG decode, sidecar, WAV.
- **Modify** `src/ClassicUO.Assets/ClassicUO.Assets.csproj` — add StbImageSharp package ref.
- **Modify** `src/ClassicUO.Assets/UOFileManager.cs` — own an `AssetOverrides` instance + `LoadOverrides(root, enabled)`.
- **Modify** `src/ClassicUO.Client/Configuration/Settings.cs` — add `enable_asset_overrides` flag.
- **Modify** `src/ClassicUO.Client/Client.cs` — build overrides after `FileManager.Load`, pass instance into the renderer wrappers.
- **Modify** `src/ClassicUO.Renderer/Gumps/Gump.cs`, `Arts/Art.cs`, `Texmaps/Texmap.cs`, `Lights/Light.cs`, `Animations/Animation.cs`, `Sounds/Sound.cs` — ctor takes `AssetOverrides`, `GetX` gains the override branch.
- **Modify** `README` / add a docs note — folder layout + examples.

---

### Task 1: `AssetOverrides` skeleton + id parsing

**Files:**
- Modify: `src/ClassicUO.Assets/ClassicUO.Assets.csproj`
- Create: `src/ClassicUO.Assets/AssetOverrides.cs`
- Create: `tests/ClassicUO.UnitTests/Assets/AssetOverridesTests.cs`

**Interfaces:**
- Produces: `enum AssetOverrideKind { Gump, Art, Land, Texmap, Light }`; `public static bool AssetOverrides.TryParseId(string stem, out uint id)` — decimal or `0x`-prefixed hex (case-insensitive), returns false for anything else.

- [ ] **Step 1: Add the package reference**

In `src/ClassicUO.Assets/ClassicUO.Assets.csproj`, inside the existing `<ItemGroup>` that holds the `ProjectReference` lines (currently lines 8-9), add:

```xml
    <PackageReference Include="StbImageSharp" Version="2.30.15" />
```

- [ ] **Step 2: Write the failing test**

Create `tests/ClassicUO.UnitTests/Assets/AssetOverridesTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class AssetOverridesTests
    {
        [Theory]
        [InlineData("1234", true, 1234u)]
        [InlineData("0", true, 0u)]
        [InlineData("0x04D2", true, 1234u)]
        [InlineData("0X04d2", true, 1234u)]
        [InlineData("", false, 0u)]
        [InlineData("abc", false, 0u)]
        [InlineData("12.3", false, 0u)]
        public void TryParseId_ParsesDecimalAndHex(string stem, bool ok, uint expected)
        {
            bool result = AssetOverrides.TryParseId(stem, out uint id);
            Assert.Equal(ok, result);
            if (ok)
                Assert.Equal(expected, id);
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AssetOverridesTests.TryParseId"`
Expected: FAIL — compile error, `AssetOverrides` / `TryParseId` not defined.

- [ ] **Step 4: Write the minimal implementation**

Create `src/ClassicUO.Assets/AssetOverrides.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Globalization;

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
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AssetOverridesTests.TryParseId"`
Expected: PASS (all 7 cases).

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Assets/ClassicUO.Assets.csproj src/ClassicUO.Assets/AssetOverrides.cs tests/ClassicUO.UnitTests/Assets/AssetOverridesTests.cs
git commit -m "feat(overrides): AssetOverrides skeleton + id parsing"
```

---

### Task 2: PNG decode to packed `uint[]`

**Files:**
- Modify: `src/ClassicUO.Assets/AssetOverrides.cs`
- Modify: `tests/ClassicUO.UnitTests/Assets/AssetOverridesTests.cs`

**Interfaces:**
- Produces: `public static bool AssetOverrides.TryDecodePng(byte[] fileBytes, out uint[] pixels, out int width, out int height)` — decodes PNG bytes to a packed RGBA `uint[]` (R low byte, row-major, length `width*height`); returns false on null/empty/undecodable input.

- [ ] **Step 1: Write the failing test**

Add to `AssetOverridesTests.cs` (inside the class). The base64 is a real 2×2 PNG: (0,0) red opaque, (1,0) green opaque, (0,1) blue opaque, (1,1) fully transparent.

```csharp
        // 2x2 RGBA PNG: red, green / blue, transparent
        private const string Png2x2 =
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAAXNSR0IArs4c6QAAAARnQU1B" +
            "AACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAUSURBVBhXY/jPwPAfDBkY/oMIBgA/" +
            "0gX7juj8nQAAAABJRU5ErkJggg==";

        [Fact]
        public void TryDecodePng_ReturnsPackedRgbaWithRLowByte()
        {
            byte[] bytes = System.Convert.FromBase64String(Png2x2);

            bool ok = AssetOverrides.TryDecodePng(bytes, out uint[] px, out int w, out int h);

            Assert.True(ok);
            Assert.Equal(2, w);
            Assert.Equal(2, h);
            Assert.Equal(4, px.Length);
            Assert.Equal(0xFF_00_00_FFu, px[0]); // red opaque  (a=FF,b=00,g=00,r=FF)
            Assert.Equal(0xFF_00_FF_00u, px[1]); // green opaque (a=FF,b=00,g=FF,r=00)
            Assert.Equal(0xFF_FF_00_00u, px[2]); // blue opaque  (a=FF,b=FF,g=00,r=00)
            Assert.Equal(0x00_00_00_00u, px[3]); // transparent
        }

        [Fact]
        public void TryDecodePng_RejectsGarbage()
        {
            Assert.False(AssetOverrides.TryDecodePng(new byte[] { 1, 2, 3 }, out _, out _, out _));
            Assert.False(AssetOverrides.TryDecodePng(null, out _, out _, out _));
        }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AssetOverridesTests.TryDecodePng"`
Expected: FAIL — `TryDecodePng` not defined.

- [ ] **Step 3: Write the minimal implementation**

Add to `AssetOverrides.cs`. Add `using StbImageSharp;` and `using ClassicUO.Utility.Logging;` at the top, then inside the class:

```csharp
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
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AssetOverridesTests.TryDecodePng"`
Expected: PASS (both facts).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Assets/AssetOverrides.cs tests/ClassicUO.UnitTests/Assets/AssetOverridesTests.cs
git commit -m "feat(overrides): PNG decode to packed uint[]"
```

---

### Task 3: Sidecar center + WAV PCM parsing

**Files:**
- Modify: `src/ClassicUO.Assets/AssetOverrides.cs`
- Modify: `tests/ClassicUO.UnitTests/Assets/AssetOverridesTests.cs`

**Interfaces:**
- Produces: `public static bool TryParseSidecarCenter(string json, out int centerX, out int centerY)` — parses `{ "centerX": int, "centerY": int }`, false if missing/malformed.
- Produces: `public static bool TryExtractWavPcm(byte[] fileBytes, out byte[] pcm)` — validates a PCM WAV that is 22050 Hz / 16-bit / mono and returns the raw `data` chunk bytes; false (with a warning) on format mismatch or parse failure.

- [ ] **Step 1: Write the failing test**

Add to `AssetOverridesTests.cs`. The WAV base64 is a real 22050 Hz / 16-bit / mono PCM file with two samples (`0x1111`, `0x2222`), so its `data` chunk is the 4 bytes `11 11 22 22`.

```csharp
        // 22050Hz 16-bit mono PCM WAV, data = 0x11,0x11,0x22,0x22
        private const string Wav22050Mono16 =
            "UklGRigAAABXQVZFZm10IBAAAAABAAEAIlYAAESsAAACABAAZGF0YQQAAAARESIi";

        [Fact]
        public void TryParseSidecarCenter_ReadsCenter()
        {
            Assert.True(AssetOverrides.TryParseSidecarCenter(
                "{ \"centerX\": 12, \"centerY\": 40 }", out int cx, out int cy));
            Assert.Equal(12, cx);
            Assert.Equal(40, cy);
        }

        [Fact]
        public void TryParseSidecarCenter_RejectsMalformed()
        {
            Assert.False(AssetOverrides.TryParseSidecarCenter("not json", out _, out _));
            Assert.False(AssetOverrides.TryParseSidecarCenter("{}", out _, out _));
        }

        [Fact]
        public void TryExtractWavPcm_ReturnsDataChunk()
        {
            byte[] bytes = System.Convert.FromBase64String(Wav22050Mono16);
            Assert.True(AssetOverrides.TryExtractWavPcm(bytes, out byte[] pcm));
            Assert.Equal(new byte[] { 0x11, 0x11, 0x22, 0x22 }, pcm);
        }

        [Fact]
        public void TryExtractWavPcm_RejectsGarbage()
        {
            Assert.False(AssetOverrides.TryExtractWavPcm(new byte[] { 1, 2, 3, 4 }, out _));
        }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AssetOverridesTests.TryParseSidecarCenter|FullyQualifiedName~AssetOverridesTests.TryExtractWavPcm"`
Expected: FAIL — methods not defined.

- [ ] **Step 3: Write the minimal implementation**

Add `using System.Text.Json;` at the top of `AssetOverrides.cs`, then add:

```csharp
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
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AssetOverridesTests.TryParseSidecarCenter|FullyQualifiedName~AssetOverridesTests.TryExtractWavPcm"`
Expected: PASS (all four facts).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Assets/AssetOverrides.cs tests/ClassicUO.UnitTests/Assets/AssetOverridesTests.cs
git commit -m "feat(overrides): sidecar center + WAV PCM parsing"
```

---

### Task 4: Index build + public query API

**Files:**
- Modify: `src/ClassicUO.Assets/AssetOverrides.cs`
- Modify: `tests/ClassicUO.UnitTests/Assets/AssetOverridesTests.cs`

**Interfaces:**
- Consumes: `TryParseId`, `TryDecodePng`, `TryParseSidecarCenter`, `TryExtractWavPcm` from Tasks 1-3.
- Produces:
  - `public void Build(string overridesRoot)` — sets `Enabled = Directory.Exists(overridesRoot)`; when enabled, recursively indexes the folders below.
  - `public bool TryGetImage(AssetOverrideKind kind, uint id, out uint[] pixels, out int width, out int height, out bool hasCenter, out int centerX, out int centerY)`
  - `public bool TryGetAnimationFrame(uint id, int group, int dir, int frame, out uint[] pixels, out int width, out int height, out bool hasCenter, out int centerX, out int centerY)`
  - `public bool TryGetSound(uint id, out byte[] pcm)`
- Folder→kind map: `Gump/`, `Art/`, `Land/`, `Texmap/`, `Light/` (single-image, `<id>.png`); `Anim/<id>/<group>_<dir>_<frame>.png`; `Sound/<id>.wav`. Sidecar for a single image is `<id>.json` beside it; for an anim frame it is `<group>_<dir>_<frame>.json`.

- [ ] **Step 1: Write the failing test**

Add to `AssetOverridesTests.cs`. This builds a real temp directory tree, then queries it. Uses the `Png2x2` and `Wav22050Mono16` constants from Tasks 2-3.

```csharp
        private static string WriteTempTree()
        {
            byte[] png = System.Convert.FromBase64String(Png2x2);
            byte[] wav = System.Convert.FromBase64String(Wav22050Mono16);

            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cuo_ovr_" + System.Guid.NewGuid().ToString("N"));

            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Gump"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Art"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Anim", "400"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Sound"));

            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Gump", "1234.png"), png);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Gump", "bogus.png"), png); // skipped
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Art", "0x10.png"), png);    // id 16
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Art", "16.json"),
                System.Text.Encoding.UTF8.GetBytes("{ \"centerX\": 3, \"centerY\": 7 }"));
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Anim", "400", "1_2_3.png"), png);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Sound", "55.wav"), wav);

            return root;
        }

        [Fact]
        public void Build_IndexesAndServesOverrides()
        {
            string root = WriteTempTree();
            try
            {
                var ovr = new AssetOverrides();
                ovr.Build(root);
                Assert.True(ovr.Enabled);

                // Gump: decimal id, no sidecar
                Assert.True(ovr.TryGetImage(AssetOverrideKind.Gump, 1234,
                    out uint[] gpx, out int gw, out int gh, out bool gHasC, out _, out _));
                Assert.Equal(4, gpx.Length);
                Assert.Equal(2, gw);
                Assert.Equal(2, gh);
                Assert.False(gHasC);

                // bogus filename was skipped, missing id returns false
                Assert.False(ovr.TryGetImage(AssetOverrideKind.Gump, 9999,
                    out _, out _, out _, out _, out _, out _));

                // Art: hex id + sidecar center
                Assert.True(ovr.TryGetImage(AssetOverrideKind.Art, 16,
                    out _, out _, out _, out bool aHasC, out int acx, out int acy));
                Assert.True(aHasC);
                Assert.Equal(3, acx);
                Assert.Equal(7, acy);

                // Animation frame
                Assert.True(ovr.TryGetAnimationFrame(400, 1, 2, 3,
                    out uint[] fpx, out _, out _, out _, out _, out _));
                Assert.Equal(4, fpx.Length);
                Assert.False(ovr.TryGetAnimationFrame(400, 9, 9, 9,
                    out _, out _, out _, out _, out _, out _));

                // Sound
                Assert.True(ovr.TryGetSound(55, out byte[] pcm));
                Assert.Equal(new byte[] { 0x11, 0x11, 0x22, 0x22 }, pcm);
            }
            finally
            {
                System.IO.Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Build_MissingRoot_Disabled()
        {
            var ovr = new AssetOverrides();
            ovr.Build(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cuo_ovr_missing_" + System.Guid.NewGuid().ToString("N")));
            Assert.False(ovr.Enabled);
            Assert.False(ovr.TryGetImage(AssetOverrideKind.Gump, 1, out _, out _, out _, out _, out _, out _));
        }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AssetOverridesTests.Build"`
Expected: FAIL — `Build` / `TryGetImage` / `TryGetAnimationFrame` / `TryGetSound` not defined.

- [ ] **Step 3: Write the minimal implementation**

Add `using System.Collections.Generic;` and `using System.IO;` at the top of `AssetOverrides.cs`, then add the index fields and methods to the class:

```csharp
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

                foreach (string file in Directory.EnumerateFiles(dir, "*.png"))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (TryParseId(stem, out uint id))
                        _images[kind][id] = file;
                    else
                        Log.Warn($"AssetOverrides: skipping unparseable file name '{file}'");
                }
            }

            string animRoot = Path.Combine(overridesRoot, "Anim");
            if (Directory.Exists(animRoot))
            {
                foreach (string idDir in Directory.EnumerateDirectories(animRoot))
                {
                    if (!TryParseId(Path.GetFileName(idDir), out uint id))
                    {
                        Log.Warn($"AssetOverrides: skipping non-id anim folder '{idDir}'");
                        continue;
                    }

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
            }

            string soundDir = Path.Combine(overridesRoot, "Sound");
            if (Directory.Exists(soundDir))
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
        }

        private static bool LoadImageFile(
            string path, out uint[] pixels, out int width, out int height,
            out bool hasCenter, out int centerX, out int centerY)
        {
            pixels = null; width = 0; height = 0;
            hasCenter = false; centerX = 0; centerY = 0;

            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) { Log.Warn($"AssetOverrides: cannot read '{path}' ({ex.Message})"); return false; }

            if (!TryDecodePng(bytes, out pixels, out width, out height))
                return false;

            string sidecar = Path.ChangeExtension(path, ".json");
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

            return LoadImageFile(path, out pixels, out width, out height, out hasCenter, out centerX, out centerY);
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

            return LoadImageFile(path, out pixels, out width, out height, out hasCenter, out centerX, out centerY);
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
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AssetOverridesTests.Build"`
Expected: PASS (both facts).

- [ ] **Step 5: Run the whole AssetOverrides suite**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AssetOverridesTests"`
Expected: PASS (all tests from Tasks 1-4).

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Assets/AssetOverrides.cs tests/ClassicUO.UnitTests/Assets/AssetOverridesTests.cs
git commit -m "feat(overrides): index build + query API"
```

---

### Task 5: Own `AssetOverrides` in `UOFileManager`; wire client + settings

**Files:**
- Modify: `src/ClassicUO.Assets/UOFileManager.cs:31-76` (construct + expose)
- Modify: `src/ClassicUO.Client/Configuration/Settings.cs`
- Modify: `src/ClassicUO.Client/Client.cs:191-192`

**Interfaces:**
- Consumes: `AssetOverrides.Build(string)` from Task 4.
- Produces: `UOFileManager.Overrides` (type `AssetOverrides`, non-null); `Settings.GlobalSettings.EnableAssetOverrides` (bool, default true).

- [ ] **Step 1: Add the property + construction to `UOFileManager`**

In `src/ClassicUO.Assets/UOFileManager.cs`, in the ctor body (after line 50, `StringDictionary = ...`), add:

```csharp
            Overrides = new AssetOverrides();
```

And in the properties block (after line 76, `public StringDictionaryLoader StringDictionary { get; }`), add:

```csharp
        public AssetOverrides Overrides { get; }
```

- [ ] **Step 2: Add the settings flag**

In `src/ClassicUO.Client/Configuration/Settings.cs`, alongside the other `[JsonPropertyName(...)]` properties (the block around lines 43-53), add:

```csharp
        [JsonPropertyName("enable_asset_overrides")] public bool EnableAssetOverrides { get; set; } = true;
```

- [ ] **Step 3: Build overrides after asset load**

In `src/ClassicUO.Client/Client.cs`, immediately after line 192 (`FileManager.Load(...)`), add:

```csharp
            if (Settings.GlobalSettings.EnableAssetOverrides)
            {
                FileManager.Overrides.Build(
                    System.IO.Path.Combine(CUOEnviroment.ExecutablePath, "Overrides"));
            }
```

- [ ] **Step 4: Build the solution**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds (no behavior change yet — nothing consumes `Overrides` on the render path).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Assets/UOFileManager.cs src/ClassicUO.Client/Utility/Settings.cs src/ClassicUO.Client/Client.cs
git commit -m "feat(overrides): own AssetOverrides in UOFileManager + settings flag"
```

---

### Task 6: Gump override hook

**Files:**
- Modify: `src/ClassicUO.Renderer/Gumps/Gump.cs`
- Modify: `src/ClassicUO.Client/Client.cs:86`

**Interfaces:**
- Consumes: `AssetOverrides.TryGetImage(AssetOverrideKind, uint, ...)` from Task 4; `UOFileManager.Overrides` from Task 5.
- Produces: `Gump` ctor signature `Gump(GumpsLoader gumpsLoader, AssetOverrides overrides, GraphicsDevice device)`.

- [ ] **Step 1: Add the override field + ctor parameter**

In `src/ClassicUO.Renderer/Gumps/Gump.cs`, add the field after line 11 (`private readonly GumpsLoader _gumpsLoader;`):

```csharp
        private readonly AssetOverrides _overrides;
```

Change the ctor (line 13) to accept and store it:

```csharp
        public Gump(GumpsLoader gumpsLoader, AssetOverrides overrides, GraphicsDevice device)
        {
            _gumpsLoader = gumpsLoader;
            _overrides = overrides;
            _atlas = new TextureAtlas(device, 4096, 4096, SurfaceFormat.Color);
            _spriteInfos = new SpriteInfo[gumpsLoader.File.Entries.Length];
        }
```

- [ ] **Step 2: Add the override branch in `GetGump`**

In `GetGump` (line 20), replace the body of the `if (spriteInfo.Texture == null)` block (lines 27-41) with:

```csharp
            if (spriteInfo.Texture == null)
            {
                if (_overrides != null && _overrides.TryGetImage(
                        AssetOverrideKind.Gump, idx,
                        out uint[] opx, out int ow, out int oh, out _, out _, out _))
                {
                    spriteInfo.Texture = _atlas.AddSprite(opx, ow, oh, out spriteInfo.UV);
                    _picker.Set(idx, ow, oh, opx);
                    return ref spriteInfo;
                }

                var gumpInfo = _gumpsLoader.GetGump(idx);
                if (!gumpInfo.Pixels.IsEmpty)
                {
                    spriteInfo.Texture = _atlas.AddSprite(
                        gumpInfo.Pixels,
                        gumpInfo.Width,
                        gumpInfo.Height,
                        out spriteInfo.UV
                    );

                    _picker.Set(idx, gumpInfo.Width, gumpInfo.Height, gumpInfo.Pixels);
                }
            }
```

- [ ] **Step 3: Update the construction site**

In `src/ClassicUO.Client/Client.cs`, change line 86 to pass the overrides instance:

```csharp
            Gumps = new Renderer.Gumps.Gump(FileManager.Gumps, FileManager.Overrides, game.GraphicsDevice);
```

- [ ] **Step 4: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds.

- [ ] **Step 5: Manual verification**

Create `Overrides/Gump/` next to the built client and drop a `<id>.png` for a gump you can see on screen (e.g. a paperdoll/backpack gump id, or `0x0BB8`-range). Launch the client. Confirm the PNG renders in place of the built-in gump. Remove the file → built-in returns. Note in the commit message which id you verified.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Renderer/Gumps/Gump.cs src/ClassicUO.Client/Client.cs
git commit -m "feat(overrides): gump override hook"
```

---

### Task 7: Art + Land override hook

**Files:**
- Modify: `src/ClassicUO.Renderer/Arts/Art.cs`
- Modify: `src/ClassicUO.Client/Client.cs:85`

**Interfaces:**
- Consumes: `AssetOverrides.TryGetImage` (kinds `Art`, `Land`); `UOFileManager.Overrides`.
- Produces: `Art` ctor signature `Art(ArtLoader artLoader, HuesLoader huesLoader, AssetOverrides overrides, GraphicsDevice device)`.

Note on keying: `GetLand(idx)` calls `Get(idx & ~0x4000)` and `GetArt(idx)` calls `Get(idx + 0x4000)`. So inside `Get`, `idx >= 0x4000` is static art (natural id = `idx - 0x4000`, kind `Art`) and `idx < 0x4000` is land (natural id = `idx`, kind `Land`). The override lookup uses the natural id so folder names map 1:1.

- [ ] **Step 1: Add the override field + ctor parameter**

In `src/ClassicUO.Renderer/Arts/Art.cs`, add after line 19 (`private readonly HuesLoader _huesLoader;`):

```csharp
        private readonly AssetOverrides _overrides;
```

Change the ctor (line 21) to:

```csharp
        public Art(ArtLoader artLoader, HuesLoader huesLoader, AssetOverrides overrides, GraphicsDevice device)
        {
            _artLoader = artLoader;
            _huesLoader = huesLoader;
            _overrides = overrides;
            _atlas = new TextureAtlas(device, 4096, 4096, SurfaceFormat.Color);
            _spriteInfos = new SpriteInfo[_artLoader.File.Entries.Length];
            _realArtBounds = new Rectangle[_spriteInfos.Length];
        }
```

- [ ] **Step 2: Add the override branch at the top of `Get`**

In `Get` (line 36), insert the override branch as the first thing inside `if (spriteInfo.Texture == null)` (before `var artInfo = _artLoader.GetArt(idx);` at line 45):

```csharp
            if (spriteInfo.Texture == null)
            {
                bool isStatic = idx >= 0x4000;
                uint naturalId = isStatic ? idx - 0x4000 : idx;

                if (_overrides != null && _overrides.TryGetImage(
                        isStatic ? AssetOverrideKind.Art : AssetOverrideKind.Land, naturalId,
                        out uint[] opx, out int ow, out int oh, out bool oHasC, out int ocx, out int ocy))
                {
                    spriteInfo.Texture = _atlas.AddSprite(opx, ow, oh, out spriteInfo.UV);
                    if (oHasC)
                        spriteInfo.Center = new Point(ocx, ocy);

                    if (isStatic)
                    {
                        _picker.Set(naturalId, ow, oh, opx);

                        int pos1 = 0, minX = ow, minY = oh, maxX = 0, maxY = 0;
                        for (int y = 0; y < oh; ++y)
                        {
                            for (int x = 0; x < ow; ++x)
                            {
                                if (opx[pos1++] != 0)
                                {
                                    minX = Math.Min(minX, x);
                                    maxX = Math.Max(maxX, x);
                                    minY = Math.Min(minY, y);
                                    maxY = Math.Max(maxY, y);
                                }
                            }
                        }
                        _realArtBounds[naturalId] = new Rectangle(minX, minY, maxX - minX, maxY - minY);
                    }

                    return ref spriteInfo;
                }

                var artInfo = _artLoader.GetArt(idx);
```

(The remainder of the method — the `artInfo.Pixels.IsEmpty` fallback, `AddSprite`, and the built-in real-art-bounds loop — stays exactly as it is.)

- [ ] **Step 3: Update the construction site**

In `src/ClassicUO.Client/Client.cs`, change line 85 to:

```csharp
            Arts = new Renderer.Arts.Art(FileManager.Arts, FileManager.Hues, FileManager.Overrides, game.GraphicsDevice);
```

- [ ] **Step 4: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds.

- [ ] **Step 5: Manual verification**

Drop `Overrides/Art/<staticId>.png` for a visible ground item (e.g. a gold pile or a sign) and, separately, `Overrides/Land/<landId>.png` for a floor tile. Launch and confirm both replace in-world. Verify hit-testing still selects the static item (mouse-over highlight). Remove → built-in returns.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Renderer/Arts/Art.cs src/ClassicUO.Client/Client.cs
git commit -m "feat(overrides): art + land override hook"
```

---

### Task 8: Texmap + Light override hooks

**Files:**
- Modify: `src/ClassicUO.Renderer/Texmaps/Texmap.cs`
- Modify: `src/ClassicUO.Renderer/Lights/Light.cs`
- Modify: `src/ClassicUO.Client/Client.cs:87-88`

**Interfaces:**
- Consumes: `AssetOverrides.TryGetImage` (kinds `Texmap`, `Light`).
- Produces: `Texmap(TexmapsLoader, AssetOverrides, GraphicsDevice)`; `Light(LightsLoader, AssetOverrides, GraphicsDevice)`.

- [ ] **Step 1: Texmap — field, ctor, branch**

In `src/ClassicUO.Renderer/Texmaps/Texmap.cs`, add after line 11 (`private readonly TexmapsLoader _texmapsLoader;`):

```csharp
        private readonly AssetOverrides _overrides;
```

Change the ctor (line 13) to store `overrides`:

```csharp
        public Texmap(TexmapsLoader texmapsLoader, AssetOverrides overrides, GraphicsDevice device)
        {
            _texmapsLoader = texmapsLoader;
            _overrides = overrides;
            _atlas = new TextureAtlas(device, 2048, 2048, SurfaceFormat.Color);
            _spriteInfos = new SpriteInfo[texmapsLoader.File.Entries.Length];
        }
```

Replace the `if (spriteInfo.Texture == null)` body (lines 27-41) with:

```csharp
            if (spriteInfo.Texture == null)
            {
                if (_overrides != null && _overrides.TryGetImage(
                        AssetOverrideKind.Texmap, idx,
                        out uint[] opx, out int ow, out int oh, out _, out _, out _))
                {
                    spriteInfo.Texture = _atlas.AddSprite(opx, ow, oh, out spriteInfo.UV);
                    _picker.Set(idx, ow, oh, opx);
                    return ref spriteInfo;
                }

                var texmapInfo = _texmapsLoader.GetTexmap(idx);
                if (!texmapInfo.Pixels.IsEmpty)
                {
                    spriteInfo.Texture = _atlas.AddSprite(
                        texmapInfo.Pixels,
                        texmapInfo.Width,
                        texmapInfo.Height,
                        out spriteInfo.UV
                    );

                    _picker.Set(idx, texmapInfo.Width, texmapInfo.Height, texmapInfo.Pixels);
                }
            }
```

- [ ] **Step 2: Light — field, ctor, branch**

In `src/ClassicUO.Renderer/Lights/Light.cs`, add after line 11 (`private readonly LightsLoader _lightsLoader;`):

```csharp
        private readonly AssetOverrides _overrides;
```

Change the ctor (line 13) to store `overrides`:

```csharp
        public Light(LightsLoader lightsLoader, AssetOverrides overrides, GraphicsDevice device)
        {
            _lightsLoader = lightsLoader;
            _overrides = overrides;
            _atlas = new TextureAtlas(device, 2048, 2048, SurfaceFormat.Color);
            _spriteInfos = new SpriteInfo[lightsLoader.File.Entries.Length];
        }
```

Replace the `if (spriteInfo.Texture == null)` body (lines 27-39) with:

```csharp
            if (spriteInfo.Texture == null)
            {
                if (_overrides != null && _overrides.TryGetImage(
                        AssetOverrideKind.Light, idx,
                        out uint[] opx, out int ow, out int oh, out _, out _, out _))
                {
                    spriteInfo.Texture = _atlas.AddSprite(opx, ow, oh, out spriteInfo.UV);
                    return ref spriteInfo;
                }

                var lightInfo = _lightsLoader.GetLight(idx);
                if (!lightInfo.Pixels.IsEmpty)
                {
                    spriteInfo.Texture = _atlas.AddSprite(
                        lightInfo.Pixels,
                        lightInfo.Width,
                        lightInfo.Height,
                        out spriteInfo.UV
                    );
                }
            }
```

- [ ] **Step 3: Add `using ClassicUO.Assets;` if needed**

`Texmap.cs` and `Light.cs` already have `using ClassicUO.Assets;` (line 1 of each). No change needed — `AssetOverrides` and `AssetOverrideKind` resolve from there.

- [ ] **Step 4: Update the construction sites**

In `src/ClassicUO.Client/Client.cs`, change lines 87-88 to:

```csharp
            Texmaps = new Renderer.Texmaps.Texmap(FileManager.Texmaps, FileManager.Overrides, game.GraphicsDevice);
            Lights = new Renderer.Lights.Light(FileManager.Lights, FileManager.Overrides, game.GraphicsDevice);
```

- [ ] **Step 5: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Renderer/Texmaps/Texmap.cs src/ClassicUO.Renderer/Lights/Light.cs src/ClassicUO.Client/Client.cs
git commit -m "feat(overrides): texmap + light override hooks"
```

---

### Task 9: Animation frame override hook

**Files:**
- Modify: `src/ClassicUO.Renderer/Animations/Animation.cs` (ctor + the frame loop near line 354-381)
- Modify: `src/ClassicUO.Client/Client.cs:84`

**Interfaces:**
- Consumes: `AssetOverrides.TryGetAnimationFrame(uint id, int group, int dir, int frame, ...)`.
- Produces: `Animations(AnimationsLoader, AssetOverrides, GraphicsDevice)` ctor (verify exact current ctor name/params in Step 1).

- [ ] **Step 1: Inspect and update the ctor**

Open `src/ClassicUO.Renderer/Animations/Animation.cs`. Find the class constructor (it currently takes `(AnimationsLoader ..., GraphicsDevice ...)`). Add a private field:

```csharp
        private readonly AssetOverrides _overrides;
```

Add an `AssetOverrides overrides` parameter to the ctor (place it before the `GraphicsDevice` parameter, matching the other wrappers) and store it: `_overrides = overrides;`. Ensure `using ClassicUO.Assets;` is present (the file already references `AnimationsLoader` from that namespace).

- [ ] **Step 2: Add the override branch inside the frame loop**

In the `for (int i = 0; i < frames.Length; i++)` loop (lines 354-381), the loop currently reads `ref var frame = ref frames[i];` then skips empty frames and uploads `frame.Pixels`. Insert an override check right after `ref var frame = ref frames[i];` (line 356) and before the `if (frame.Width <= 0 ...)` check. `action` and `dir` are already in scope (method parameters used at line 367); `frame.Num` is the frame index:

```csharp
                    ref var frame = ref frames[i];

                    if (_overrides != null && _overrides.TryGetAnimationFrame(
                            id, action, dir, frame.Num,
                            out uint[] ofpx, out int ofw, out int ofh,
                            out bool ofHasC, out int ofcx, out int ofcy))
                    {
                        ref var oSprite = ref animDir.SpriteInfos[frame.Num];
                        uint oKeyUpper = (uint)((action | (dir << 8) | ((useUOP ? 1 : 0) << 16)));
                        uint oKeyLower = (uint)((id | (frame.Num << 16)));
                        ulong oKey = (oKeyLower | ((ulong)oKeyUpper << 32));

                        _picker.Set(oKey, ofw, ofh, ofpx);
                        oSprite.Center.X = ofHasC ? ofcx : frame.CenterX;
                        oSprite.Center.Y = ofHasC ? ofcy : frame.CenterY;
                        oSprite.Texture = _atlas.AddSprite(ofpx.AsSpan(), ofw, ofh, out oSprite.UV);
                        continue;
                    }
```

The existing built-in per-frame code (the `if (frame.Width <= 0 ...)` skip and the `AddSprite` block at lines 359-380) stays unchanged and runs for every frame with no override.

- [ ] **Step 3: Update the construction site**

In `src/ClassicUO.Client/Client.cs`, change line 84 to:

```csharp
            Animations = new Renderer.Animations.Animations(FileManager.Animations, FileManager.Overrides, game.GraphicsDevice);
```

- [ ] **Step 4: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds.

- [ ] **Step 5: Manual verification**

Pick a body id you can spawn/see (e.g. a common creature). Create `Overrides/Anim/<id>/<group>_<dir>_<frame>.png` for one frame (start with group `0`, dir `0`, frame `0`). Launch, view that creature idling/facing that direction, and confirm the overridden frame appears while other frames stay built-in. Remove → built-in returns.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Renderer/Animations/Animation.cs src/ClassicUO.Client/Client.cs
git commit -m "feat(overrides): animation frame override hook"
```

---

### Task 10: Sound override hook

**Files:**
- Modify: `src/ClassicUO.Renderer/Sounds/Sound.cs` (ctor + `GetSound` near line 29-36)
- Modify: `src/ClassicUO.Client/Client.cs:90`

**Interfaces:**
- Consumes: `AssetOverrides.TryGetSound(uint id, out byte[] pcm)`.
- Produces: `Sound(SoundsLoader, AssetOverrides)` ctor.

- [ ] **Step 1: Inspect the current `Sound` ctor and `GetSound`**

Open `src/ClassicUO.Renderer/Sounds/Sound.cs`. Confirm the ctor is `Sound(SoundsLoader soundsLoader)` and `GetSound(int index)` builds `new UOSound(name, index, data)` at line 35 after `_soundsLoader.TryGetSound(index, out byte[] data, out string name)` (line 33). Ensure `using ClassicUO.Assets;` is present.

- [ ] **Step 2: Add the override field + ctor parameter**

Add a private field:

```csharp
        private readonly AssetOverrides _overrides;
```

Change the ctor to accept and store it (keep the existing loader assignment):

```csharp
        public Sound(SoundsLoader soundsLoader, AssetOverrides overrides)
        {
            _soundsLoader = soundsLoader;
            _overrides = overrides;
        }
```

(If the ctor has additional existing statements, keep them; only add the parameter and the `_overrides = overrides;` line.)

- [ ] **Step 3: Add the override branch in `GetSound`**

Replace the block at lines 33-36:

```csharp
                if (sound == null && _soundsLoader.TryGetSound(index, out byte[] data, out string name))
                {
                    sound = new UOSound(name, index, data);
                }
```

with:

```csharp
                if (sound == null)
                {
                    if (_overrides != null && _overrides.TryGetSound((uint)index, out byte[] opcm))
                    {
                        sound = new UOSound($"override_{index}", index, opcm);
                    }
                    else if (_soundsLoader.TryGetSound(index, out byte[] data, out string name))
                    {
                        sound = new UOSound(name, index, data);
                    }
                }
```

- [ ] **Step 4: Update the construction site**

In `src/ClassicUO.Client/Client.cs`, change line 90 to:

```csharp
            Sounds = new Renderer.Sounds.Sound(FileManager.Sounds, FileManager.Overrides);
```

- [ ] **Step 5: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds.

- [ ] **Step 6: Manual verification**

Drop `Overrides/Sound/<id>.wav` (must be **PCM 22050 Hz, 16-bit, mono**) for a sound you can trigger (e.g. a spell or UI click sound id). Launch, trigger it, confirm your WAV plays. Try a 44100 Hz stereo WAV → confirm it is ignored (built-in plays) and a warning is logged. Remove → built-in returns.

- [ ] **Step 7: Commit**

```bash
git add src/ClassicUO.Renderer/Sounds/Sound.cs src/ClassicUO.Client/Client.cs
git commit -m "feat(overrides): sound override hook"
```

---

### Task 11: Full test pass + documentation

**Files:**
- Modify: `README.md` (or add `docs/asset-overrides.md` and link it from the README)

**Interfaces:** none.

- [ ] **Step 1: Run the full unit-test suite**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS, including all `AssetOverridesTests`.

- [ ] **Step 2: Write the documentation**

Create `docs/asset-overrides.md`:

```markdown
# Asset Overrides

Drop replacement images and sounds into an `Overrides/` folder next to the
client to override built-in UO assets. No repacking, no tools.

Enabled automatically when `Overrides/` exists. Set
`"enable_asset_overrides": false` in `settings.json` to force it off.

## Folder layout

```
Overrides/
  Gump/     <id>.png            # UI graphics
  Art/      <id>.png            # static item art (natural static id)
  Land/     <id>.png            # land tiles
  Texmap/   <id>.png            # land texture maps
  Light/    <id>.png            # light sprites
  Anim/     <id>/<group>_<dir>_<frame>.png
  Sound/    <id>.wav            # PCM 22050 Hz, 16-bit, mono
```

- IDs are decimal (`1234.png`) or hex (`0x04D2.png`).
- Images are PNG (RGBA; `0` alpha = transparent).
- Sounds are WAV, PCM 22050 Hz / 16-bit / mono. Other formats are ignored
  (a warning is logged and the built-in sound plays).
- Overrides are scanned once at startup and decoded on first use. Restart the
  client to pick up added/changed files.

## Draw offset (optional)

Static art and animation frames draw relative to a center point. If your
replacement is a different size and needs precise placement, add a sidecar
JSON next to the image:

- single image: `<id>.json`
- animation frame: `<group>_<dir>_<frame>.json`

```json
{ "centerX": 12, "centerY": 40 }
```

Without a sidecar, gumps/texmaps/lights use top-left origin, and
art/animation frames keep the original asset's center.
```

If a `README.md` "Features" or "Configuration" section exists, add a one-line link: `- [Asset overrides](docs/asset-overrides.md)`.

- [ ] **Step 3: Commit**

```bash
git add docs/asset-overrides.md README.md
git commit -m "docs(overrides): folder layout + usage"
```

---

## Self-Review Notes

- **Spec coverage:** discovery/enable (Task 5), folder layout + id parse (Tasks 1, 4), all seven families (Tasks 6-10), center derivation + sidecar (Tasks 3, 4, 7, 9), animation partial fallback (Task 9), WAV format validation (Tasks 3, 10), error handling = warn-and-fallback (throughout), tests (Tasks 1-4, 11), docs (Task 11). Covered.
- **Center behavior:** Gump/Texmap/Light ignore center (SpriteInfo.Center stays 0,0 as today). Art sets Center only when a sidecar is present. Animation keeps the built-in frame center unless a sidecar overrides. Consistent across tasks.
- **Type consistency:** `TryGetImage`/`TryGetAnimationFrame` signatures (8 out params incl. `hasCenter`, `centerX`, `centerY`) are identical everywhere they are consumed. `AssetOverrideKind` values match folder names. Ctor signatures updated at every construction site in `Client.cs`.
- **Deferred-to-runtime detail (flagged, not a placeholder):** Task 9 Step 1 and Task 10 Step 1 direct the implementer to confirm the exact current `Animations` / `Sound` ctor bodies before editing, because those two wrappers may carry extra ctor statements not shown here. The edits themselves are fully specified.
```
