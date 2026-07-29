# TTF In-Game Fonts via Live StbTrueType Rasterization — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the offline-baked TTF atlas fonts (slots 20–31) with live StbTrueType glyph rasterization, reusing the entire existing measure/layout/composite/hit-test pipeline unchanged; then delete the baker tool, baked atlases, and `AtlasFontFile`.

**Architecture:** A new `TtfGlyphSource` opens a TTF with StbTrueTypeSharp and rasterizes one glyph on demand, reproducing the offline baker's exact metric mapping. `FontsLoader.RegisterAtlasFont` gains a TTF overload that stores a per-slot `TtfGlyphSource`; `FontsLoader.LoadChar` (already the lazy-fill hook behind `GetCharUni`) populates `_fontDataUNICODE[slot, c]` from that source on first access. Everything downstream (coverage/border/solid/hue/premultiplied composite, `FontGlyphAtlas`, `RenderedText`, measure APIs, hit-test) is untouched.

**Tech Stack:** C# net10.0, xUnit, StbTrueTypeSharp 1.26.12 (already used by the baker; pure-managed, NativeAOT-safe).

## Global Constraints

- Target framework `net10.0`; new source files start with `// SPDX-License-Identifier: BSD-2-Clause`.
- StbTrueTypeSharp package version exactly **1.26.12**.
- Trigger is font slot in range **20–31** (`_isAtlasFont[slot] == true`); classic fonts (unicode 0–19, ASCII) MUST remain byte-for-byte on the legacy path.
- Live rasterization MUST reproduce the offline baker's mapping exactly: `OffsetX = ClampToSByte(xoff)`, `OffsetY = ClampToSByte(ascentPx + yoff)`, `Width = (sbyte)w`, `Height = (sbyte)h`, `Coverage = w*h` bytes (row-major), where `scale = stbtt_ScaleForPixelHeight(info, pixelSize)`, `ascentPx = round(ascentRaw * scale)`, and `(w,h,xoff,yoff)` come from `stbtt_GetCodepointBitmap(info, scale, scale, codepoint, ...)`. Missing glyph (`stbtt_FindGlyphIndex == 0`) or empty bitmap → no pixels.
- `FontCharacterDataUnicode.Coverage` is `null` for empty glyphs (preserves `HasPixels==false` / space semantics); non-empty coverage otherwise.
- The interior-solidify fix in `StrengthenCoverage` stays as-is (do not touch it).
- xUnit; `ClassicUO.Assets` internals are visible to `ClassicUO.UnitTests`.

## File Structure

- **New** `src/ClassicUO.Assets/TtfGlyphSource.cs` — opens one TTF at one pixel size, rasterizes a single glyph → (offsets, size, coverage). Owns the `stbtt_fontinfo` + retained TTF byte buffer + scale + ascentPx. One responsibility: TTF → glyph coverage/metrics.
- **Modify** `src/ClassicUO.Assets/FontsLoader.cs` — TTF overload of `RegisterAtlasFont`, per-slot `TtfGlyphSource` + known-missing sets, `LoadChar` atlas branch.
- **Modify** `src/ClassicUO.Assets/ClassicUO.Assets.csproj` — StbTrueTypeSharp reference.
- **Modify** `src/ClassicUO.Client/Resources/Loader.cs` + `src/ClassicUO.Client/ClassicUO.Client.csproj` — embed 4 TTFs, drop 12 baked atlases; `Entries` carry TTF bytes + pixel size; `RegisterAll` passes them.
- **Remove** `src/ClassicUO.Assets/AtlasFontFile.cs`, `tests/ClassicUO.UnitTests/AtlasFontFileTests.cs`, `tools/FontAtlasBaker/`.
- **New tests** `tests/ClassicUO.UnitTests/Assets/TtfGlyphSourceTests.cs`, additions to a FontsLoader atlas test file.

---

### Task 1: `TtfGlyphSource` — live glyph rasterization

**Files:**
- Modify: `src/ClassicUO.Assets/ClassicUO.Assets.csproj`
- Create: `src/ClassicUO.Assets/TtfGlyphSource.cs`
- Create: `tests/ClassicUO.UnitTests/Assets/TtfGlyphSourceTests.cs`

**Interfaces:**
- Produces: `internal sealed class TtfGlyphSource` with
  `public static TtfGlyphSource Create(byte[] ttfBytes, int pixelSize)` (returns `null` on failure),
  `public int PixelSize { get; }`,
  `public bool TryRasterize(char c, out sbyte offsetX, out sbyte offsetY, out sbyte width, out sbyte height, out byte[] coverage)` (returns `false` for missing/empty glyph, with all outs zero/`null`).

- [ ] **Step 1: Add the package reference**

In `src/ClassicUO.Assets/ClassicUO.Assets.csproj`, inside the existing `<ItemGroup>` with the `ProjectReference` lines, add:

```xml
    <PackageReference Include="StbTrueTypeSharp" Version="1.26.12" />
```

- [ ] **Step 2: Write the failing test**

Create `tests/ClassicUO.UnitTests/Assets/TtfGlyphSourceTests.cs`. The test reads a real in-repo TTF the baker also used (`Assets/fonts/Inter/static/Inter_18pt-Regular.ttf`). Resolve the repo root from the test assembly location by walking up to `ClassicUO.sln`.

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Assets;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class TtfGlyphSourceTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ClassicUO.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("ClassicUO.sln not found");
        }

        private static byte[] InterTtf() =>
            File.ReadAllBytes(Path.Combine(
                RepoRoot(),
                "Assets", "fonts", "Inter", "static", "Inter_18pt-Regular.ttf"));

        [Fact]
        public void Create_ValidTtf_Succeeds()
        {
            TtfGlyphSource src = TtfGlyphSource.Create(InterTtf(), 22);
            Assert.NotNull(src);
            Assert.Equal(22, src.PixelSize);
        }

        [Fact]
        public void Create_GarbageBytes_ReturnsNull()
        {
            Assert.Null(TtfGlyphSource.Create(new byte[] { 1, 2, 3, 4 }, 22));
        }

        [Fact]
        public void TryRasterize_SolidGlyph_HasOpaqueInteriorAndPositiveMetrics()
        {
            TtfGlyphSource src = TtfGlyphSource.Create(InterTtf(), 22);

            bool ok = src.TryRasterize('M', out sbyte ox, out sbyte oy,
                out sbyte w, out sbyte h, out byte[] coverage);

            Assert.True(ok);
            Assert.True(w > 0);
            Assert.True(h > 0);
            Assert.Equal(w * h, coverage.Length);
            // A solid stroke glyph has at least one fully-covered interior pixel.
            byte max = 0;
            foreach (byte b in coverage) if (b > max) max = b;
            Assert.Equal(255, max);
        }

        [Fact]
        public void TryRasterize_Space_ReturnsFalse()
        {
            TtfGlyphSource src = TtfGlyphSource.Create(InterTtf(), 22);
            Assert.False(src.TryRasterize(' ', out _, out _, out _, out _, out _));
        }

        [Fact]
        public void TryRasterize_SlovakLetters_Present()
        {
            TtfGlyphSource src = TtfGlyphSource.Create(InterTtf(), 22);
            foreach (char c in new[] { 'č', 'š', 'ž', 'Č', 'Š', 'Ž' })
            {
                Assert.True(src.TryRasterize(c, out _, out _, out sbyte w, out sbyte h, out _),
                    $"missing {c}");
                Assert.True(w > 0 && h > 0);
            }
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TtfGlyphSourceTests"`
Expected: FAIL — `TtfGlyphSource` not defined.

- [ ] **Step 4: Write the implementation**

Create `src/ClassicUO.Assets/TtfGlyphSource.cs`. This mirrors `tools/FontAtlasBaker/Program.cs` `BakeFont` per-glyph logic exactly.

```csharp
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
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TtfGlyphSourceTests"`
Expected: PASS (all 5 facts).

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Assets/ClassicUO.Assets.csproj src/ClassicUO.Assets/TtfGlyphSource.cs tests/ClassicUO.UnitTests/Assets/TtfGlyphSourceTests.cs
git commit -m "feat(fonts): TtfGlyphSource live StbTrueType glyph rasterization"
```

---

### Task 2: Wire TTF source into FontsLoader (register + lazy fill)

**Files:**
- Modify: `src/ClassicUO.Assets/FontsLoader.cs` (add TTF `RegisterAtlasFont` overload near line 319; add fields near 76-79; extend `LoadChar` at 242-256)
- Create: `tests/ClassicUO.UnitTests/Assets/AtlasFontRegistrationTests.cs`

**Interfaces:**
- Consumes: `TtfGlyphSource.Create` / `TryRasterize` (Task 1).
- Produces: `public void RegisterAtlasFont(int slot, string displayName, ReadOnlySpan<byte> ttfBytes, int pixelSize)` (new 4-arg overload, coexisting with the existing 3-arg baked overload). Existing `IsAtlasFont(int)`, `SlotUsable(byte)`, `GetWidthUnicode(byte,string)`, `RenderSingleGlyphUnicode(...)` behave for TTF slots after registration.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Assets/AtlasFontRegistrationTests.cs`. It needs a `FontsLoader` instance. `FontsLoader`'s constructor takes a `UOFileManager`; construct one the way other tests do — if no test helper exists, this test constructs `FontsLoader` via its public constructor with a `UOFileManager` built from a throwaway path (the atlas path never touches UO files). Register the Inter TTF into slot 20 and exercise the public unicode APIs.

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class AtlasFontRegistrationTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ClassicUO.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("ClassicUO.sln not found");
        }

        private static byte[] InterTtf() =>
            File.ReadAllBytes(Path.Combine(
                RepoRoot(), "Assets", "fonts", "Inter", "static", "Inter_18pt-Regular.ttf"));

        private static FontsLoader NewFonts()
        {
            // The atlas/TTF path never reads UO .mul files, and _unicodeFontAddress
            // is initialized at field declaration (array of nulls), so an unloaded
            // FontsLoader is fine here — no FileManager.Load() / UO data needed.
            var mgr = new UOFileManager(ClientVersion.CV_300, Path.GetTempPath());
            return mgr.Fonts;
        }

        [Fact]
        public void RegisterAtlasFont_Ttf_MarksSlotUsable()
        {
            FontsLoader fonts = NewFonts();
            fonts.RegisterAtlasFont(20, "Inter 22", InterTtf(), 22);

            Assert.True(fonts.IsAtlasFont(20));
            Assert.Equal("Inter 22", fonts.GetFontDisplayName(20));
        }

        [Fact]
        public void RegisterAtlasFont_Ttf_MeasuresAndRastersLazily()
        {
            FontsLoader fonts = NewFonts();
            fonts.RegisterAtlasFont(20, "Inter 22", InterTtf(), 22);

            // Measure triggers GetCharUni -> LoadChar -> lazy fill.
            int width = fonts.GetWidthUnicode(20, "M");
            Assert.True(width > 0);

            // Render produces a glyph with an opaque interior pixel.
            FontsLoader.SingleGlyphInfo g =
                fonts.RenderSingleGlyphUnicode(20, 'M', hasBorder: false, isSolid: false);
            Assert.NotNull(g.Data);
            Assert.True(g.Width > 0 && g.Height > 0);
            bool anyOpaque = false;
            foreach (uint px in g.Data) if ((px >> 24) == 0xFF) { anyOpaque = true; break; }
            Assert.True(anyOpaque);
        }

        [Fact]
        public void RegisterAtlasFont_Ttf_MissingCodepoint_NoPixelsNoThrow()
        {
            FontsLoader fonts = NewFonts();
            fonts.RegisterAtlasFont(20, "Inter 22", InterTtf(), 22);

            // A codepoint Inter does not contain (CJK) must measure to no width
            // and must not throw on repeated access (known-missing guard).
            int w1 = fonts.GetWidthUnicode(20, "中");
            int w2 = fonts.GetWidthUnicode(20, "中");
            Assert.Equal(0, w1);
            Assert.Equal(0, w2);
        }

        [Fact]
        public void RegisterAtlasFont_CorruptTtf_SlotNotUsable()
        {
            FontsLoader fonts = NewFonts();
            fonts.RegisterAtlasFont(21, "bad", new byte[] { 1, 2, 3, 4 }, 22);
            Assert.False(fonts.IsAtlasFont(21));
        }
    }
}
```

(If `RenderSingleGlyphUnicode`'s return type is exposed as `FontsLoader.SingleGlyphInfo` under a different nesting, adjust the type reference to match the actual declaration — verify by opening `FontsLoader.cs` around line 4040; the fields `Data`/`Width`/`Height` are as used here.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AtlasFontRegistrationTests"`
Expected: FAIL — no 4-arg `RegisterAtlasFont` overload.

- [ ] **Step 3: Add per-slot source + known-missing state**

In `src/ClassicUO.Assets/FontsLoader.cs`, next to the existing atlas fields (`_isAtlasFont` at line 78, `_atlasFontNames` at line 79), add:

```csharp
        private readonly TtfGlyphSource[] _atlasGlyphSources = new TtfGlyphSource[MAX_UNICODE_FONTS];
        private readonly System.Collections.Generic.HashSet<int>[] _atlasMissing =
            new System.Collections.Generic.HashSet<int>[MAX_UNICODE_FONTS];
```

- [ ] **Step 4: Add the TTF `RegisterAtlasFont` overload**

In `src/ClassicUO.Assets/FontsLoader.cs`, immediately after the existing `RegisterAtlasFont(int, string, ReadOnlySpan<byte>)` method (ends at line 354), add the new overload:

```csharp
        /// <summary>
        /// Registers a TTF face into a synthetic unicode slot (20-31). Glyphs are
        /// rasterized on demand by <see cref="LoadChar"/> via <see cref="TtfGlyphSource"/>;
        /// nothing is baked up front. A corrupt/unloadable TTF leaves the slot unusable.
        /// </summary>
        public void RegisterAtlasFont(int slot, string displayName, ReadOnlySpan<byte> ttfBytes, int pixelSize)
        {
            TtfGlyphSource source = TtfGlyphSource.Create(ttfBytes.ToArray(), pixelSize);

            if (source == null)
            {
                Utility.Logging.Log.Warn($"RegisterAtlasFont: could not load TTF for slot {slot} ('{displayName}'); slot disabled.");
                return;
            }

            _fontDataUNICODE ??= new FontCharacterDataUnicode[_unicodeFontAddress.Length, 0x10000];

            _atlasGlyphSources[slot] = source;
            _atlasMissing[slot] = new System.Collections.Generic.HashSet<int>();
            _isAtlasFont[slot] = true;
            _atlasFontNames[slot] = displayName;
        }
```

- [ ] **Step 5: Extend `LoadChar` for atlas slots**

In `src/ClassicUO.Assets/FontsLoader.cs`, `LoadChar` (line 242). Insert an atlas-slot branch at the very top of the method body, before the existing `var file = _unicodeFontAddress[font];` line:

```csharp
        private void LoadChar(byte font, int index)
        {
            if (font < MAX_UNICODE_FONTS && _isAtlasFont[font])
            {
                LoadAtlasChar(font, index);
                return;
            }

            var file = _unicodeFontAddress[font];
            // ... existing body unchanged ...
```

Then add the new helper next to `LoadChar`:

```csharp
        private void LoadAtlasChar(byte font, int index)
        {
            TtfGlyphSource source = _atlasGlyphSources[font];
            if (source == null)
            {
                return;
            }

            // Skip glyphs already known to be absent from this face, so a missing
            // codepoint isn't re-rasterized on every access.
            System.Collections.Generic.HashSet<int> missing = _atlasMissing[font];
            if (missing != null && missing.Contains(index))
            {
                return;
            }

            if (source.TryRasterize((char)index, out sbyte offX, out sbyte offY,
                    out sbyte w, out sbyte h, out byte[] coverage))
            {
                _fontDataUNICODE[font, index] = new FontCharacterDataUnicode
                {
                    OffsetX = offX,
                    OffsetY = offY,
                    Width = w,
                    Height = h,
                    Data = null,
                    Coverage = (w <= 0 || h <= 0 || coverage == null || coverage.Length == 0) ? null : coverage
                };

                // A rasterized-but-empty glyph (shouldn't happen: TryRasterize
                // returns false for those) is treated as missing to avoid retries.
                if (_fontDataUNICODE[font, index].Coverage == null)
                {
                    missing?.Add(index);
                }
            }
            else
            {
                missing?.Add(index);
            }
        }
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AtlasFontRegistrationTests"`
Expected: PASS (all 4 facts).

- [ ] **Step 7: Build the solution**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: succeeds (both `RegisterAtlasFont` overloads present; the old baked overload still compiles and is still the one `Loader.cs` calls).

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Assets/FontsLoader.cs tests/ClassicUO.UnitTests/Assets/AtlasFontRegistrationTests.cs
git commit -m "feat(fonts): register TTF slots + lazy StbTrueType fill in FontsLoader"
```

---

### Task 3: Switch the registry to TTF; embed TTFs; drop baked atlases + baker + AtlasFontFile

**Files:**
- Modify: `src/ClassicUO.Client/Resources/Loader.cs` (`Entries`, `GetAtlasBytes`→`GetTtf*`, `RegisterAll` at 154-186; partials at 79)
- Modify: `src/ClassicUO.Client/ClassicUO.Client.csproj` (embedded-resource list)
- Modify: `src/ClassicUO.Assets/FontsLoader.cs` (delete the old 3-arg `RegisterAtlasFont` baked overload, 319-354)
- Remove: `src/ClassicUO.Assets/AtlasFontFile.cs`, `tests/ClassicUO.UnitTests/AtlasFontFileTests.cs`, `tools/FontAtlasBaker/`

**Interfaces:**
- Consumes: the 4-arg `RegisterAtlasFont(int, string, ReadOnlySpan<byte>, int)` (Task 2).
- Produces: `AtlasFontRegistry.RegisterAll(FontsLoader)` now feeds TTF bytes + pixel size per slot.

- [ ] **Step 1: Embed the four TTFs, drop the twelve baked atlases**

In `src/ClassicUO.Client/ClassicUO.Client.csproj`, find the embedded-resource entries for the 12 baked atlas files (the `.uofont`/atlas `.bin` resources backing `GetAtlasCinzel14()` etc.) and remove them. Add four TTF embeds (the same faces the baker read):

```xml
  <ItemGroup>
    <EmbeddedResource Include="..\..\Assets\fonts\Cinzel\static\Cinzel-SemiBold.ttf" LogicalName="Cinzel.ttf" />
    <EmbeddedResource Include="..\..\Assets\fonts\Cormorant_Garamond\static\CormorantGaramond-SemiBold.ttf" LogicalName="Cormorant.ttf" />
    <EmbeddedResource Include="..\..\Assets\fonts\Inter\static\Inter_18pt-Regular.ttf" LogicalName="Inter.ttf" />
    <EmbeddedResource Include="..\..\Assets\fonts\Source_Sans_3\static\SourceSans3-Regular.ttf" LogicalName="SourceSans.ttf" />
  </ItemGroup>
```

(Match the existing csproj's relative-path style. If the baked atlases are pulled in by a source generator rather than `<EmbeddedResource>`, remove that generator input instead; the goal is: 4 TTFs embedded, 12 atlases gone.)

- [ ] **Step 2: Rework `AtlasFontRegistry` to supply TTF bytes + size**

In `src/ClassicUO.Client/Resources/Loader.cs`, replace the baked-atlas plumbing. Remove the `GetAtlasCinzel14()`-style partials (line 79 area) and `GetAtlasBytes(slot)` (154-173). Add one TTF loader per face (reading the embedded resources) and make each `Entry` carry the face + pixel size. Rewrite `RegisterAll`:

```csharp
        private static byte[] LoadTtf(string logicalName)
        {
            using System.IO.Stream s = typeof(AtlasFontRegistry).Assembly
                .GetManifestResourceStream(logicalName)
                ?? throw new System.InvalidOperationException($"Embedded TTF '{logicalName}' not found.");
            using var ms = new System.IO.MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        public static void RegisterAll(ClassicUO.Assets.FontsLoader fonts)
        {
            foreach (Entry entry in Entries)
            {
                byte[] ttf = LoadTtf(entry.TtfResource);
                fonts.RegisterAtlasFont(entry.Slot, entry.DisplayName, ttf, entry.PixelSize);
            }
        }
```

Update the `Entry` type and `Entries` table so each of slots 20–31 maps to `(Slot, DisplayName, TtfResource, PixelSize)` — the same 12 rows as today (Cinzel/Cormorant/Inter/SourceSans × 14/18/22), where `TtfResource` is the matching logical name (`"Cinzel.ttf"` etc.) and `PixelSize` is 14/18/22. Keep the display names identical to today's (`"Cinzel 14"`, …) so the font picker is unchanged. Cache each face's bytes (load once, reuse across its three sizes) to avoid reading the same resource three times.

- [ ] **Step 3: Delete the old baked `RegisterAtlasFont` overload**

In `src/ClassicUO.Assets/FontsLoader.cs`, delete the 3-arg `RegisterAtlasFont(int slot, string displayName, ReadOnlySpan<byte> atlasBytes)` method (lines 319-354) — its only caller (`RegisterAll`) now uses the 4-arg overload.

- [ ] **Step 4: Remove `AtlasFontFile`, its tests, and the baker**

```bash
git rm src/ClassicUO.Assets/AtlasFontFile.cs tests/ClassicUO.UnitTests/AtlasFontFileTests.cs
git rm -r tools/FontAtlasBaker
```

If `tools/FontAtlasBaker` is listed in `ClassicUO.sln`, remove its project entry from the solution file as well.

- [ ] **Step 5: Build the solution**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: succeeds, 0 errors. Any compile error naming `AtlasFontFile`/`AtlasGlyph`/`GetAtlasCinzel14` means a residual reference — remove it (the spec verified `AtlasFontFile.Read` had no consumer beyond the deleted overload).

- [ ] **Step 6: Run the full unit suite**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS. `TtfGlyphSourceTests` + `AtlasFontRegistrationTests` pass; `AtlasFontFileTests` is gone; nothing else regressed.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(fonts): live TTF slots via embedded faces; remove baker + baked atlases + AtlasFontFile"
```

---

### Task 4: Manual in-client verification (user, Vulkan/phoenix)

**Files:** none.

**Interfaces:** none.

- [ ] **Step 1: Build + run the client** used in the phoenix bundle from the current branch.

- [ ] **Step 2: Verify TTF text** with a TTF slot (20–31) selected for overhead/chat and tooltip:
  - Overhead world text (character speech, `[xx%]`/status, names) renders with **solid, non-see-through infill** and smooth anti-aliased edges.
  - Gump/tooltip/journal text in a TTF font looks correct: border, solid, hue color all correct.
  - Slovak/Czech text (č, š, ž, ď, ľ, ...) renders (no missing boxes).

- [ ] **Step 3: Verify classic fonts unchanged** — switch a context back to a classic unicode/ASCII font; it renders exactly as before.

- [ ] **Step 4: Report** any discrepancy (screenshot). If glyph spacing differs from the old baked look, the metric mapping in `TtfGlyphSource.TryRasterize` is the place to check against the baker mapping in Global Constraints.

---

## Self-Review Notes

- **Spec coverage:** live rasterization (Task 1), register + lazy fill via `LoadChar`/`GetCharUni` reusing `_fontDataUNICODE` (Task 2), registry TTF + embed 4 / drop 12 (Task 3), remove baker + `AtlasFontFile` + tests (Task 3), StbTrueTypeSharp 1.26.12 ref (Task 1), interior-solidify `StrengthenCoverage` untouched (not modified anywhere), error handling — corrupt TTF disables slot (Task 2 Step 4 + test), missing codepoint no-retry (Task 2 Step 5 + test), manual verify incl. SK/CZ (Task 4). Covered.
- **Metric parity:** Task 1's `TryRasterize` copies the baker's exact formulas (Global Constraints), so `_fontDataUNICODE` entries match the previously-baked values → measure/layout identical.
- **Build-green ordering:** Task 2 adds the 4-arg overload alongside the 3-arg one (both compile; `Loader.cs` still calls the old one). Task 3 flips `Loader.cs` to the new overload and only then deletes the old overload + `AtlasFontFile`. No task leaves the tree non-building.
- **Type consistency:** `RegisterAtlasFont(int,string,ReadOnlySpan<byte>,int)`, `TtfGlyphSource.Create(byte[],int)`, `TryRasterize(char, out sbyte,out sbyte,out sbyte,out sbyte,out byte[])`, `FontCharacterDataUnicode { OffsetX, OffsetY, Width, Height, Data, Coverage }` used identically across tasks.
- **Flagged for implementer:** Task 2 Step 1 notes to verify the `RenderSingleGlyphUnicode` return type name and the `FontsLoader` construction helper against the live file; Task 3 Step 1 notes the atlas resources may be wired via a source generator rather than `<EmbeddedResource>`. Both are "confirm exact wiring", not undefined behavior.
