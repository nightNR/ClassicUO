# TTF in-game fonts + rune-font removal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hide the rune unicode fonts (slots 7–12) from the Options font picker, and add the four bundled TTF families as anti-aliased, selectable in-game fonts via build-time pre-generated 8bpp coverage atlases loaded into new unicode font slots.

**Architecture:** The UO unicode text pipeline (`FontsLoader` → `RenderedText`/`TextRenderer`) is extended so a glyph may optionally carry an 8bpp coverage buffer. The rasterizer branches on that per glyph: original 1bpp fonts keep their exact hard-edged path; new TTF slots alpha-blend coverage. TTF glyphs are baked offline by a build-time tool into a compact atlas file, embedded via FileEmbed, and loaded into slots 20–31. Layout/measurement/hue are untouched.

**Tech Stack:** C# `net10.0`, xUnit, FNA. `FontStashSharp 1.3.10` (+ StbTrueTypeSharp) — build-time baker only. FileEmbed source generator for shipping atlas bytes.

## Global Constraints

- License header on every new source file, first line: `// SPDX-License-Identifier: BSD-2-Clause`.
- Never mention Claude/AI in commit messages.
- **Backward compatibility is mandatory:** original unicode fonts (slots 0–19) must render byte-for-byte as before. The 8bpp path is gated per glyph by `IsAntiAliased` (true only when `Coverage != null`), which original glyphs never set.
- Rune range to hide from the picker: unicode slots **7–12 inclusive**. Filter is picker-only; the render path stays able to draw them.
- TTF families: **Cinzel, Cormorant Garamond, Inter, Source Sans 3**. Sizes: **14, 18, 22 px**. 4 × 3 = **12 atlas slots**, occupying unicode font indices **20–31**.
- Char coverage baked: **U+0020 – U+017F** (must include all Slovak/Czech diacritics — á ä č ď ě é í ĺ ľ ň ó ô ŕ ř š ť ú ů ý ž and uppercase).
- Font-slot count constant: `MAX_UNICODE_FONTS = 32`. Every `20` / `>= 20` / `< 20` font-index literal in `FontsLoader.cs` must route through it.
- The NativeAOT client must not gain a FontStashSharp/Stb runtime dependency; the baker is a separate build-time tool.
- Atlas glyph struct: `FontCharacterDataUnicode` at `src/ClassicUO.Assets/FontsLoader.cs:4091` has `sbyte OffsetX, OffsetY, Width, Height; byte[] Data`.

## File structure

- `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` — `FontSelector` (line 5428): rune filter + widen to `MAX_UNICODE_FONTS` + TTF labels.
- `src/ClassicUO.Assets/FontsLoader.cs` — glyph struct + coverage; rasterizer branches; `MAX_UNICODE_FONTS`; atlas registration.
- `src/ClassicUO.Assets/AtlasFontFile.cs` (new) — atlas binary format reader (pure, testable).
- `tools/FontAtlasBaker/` (new console project) — TTF → atlas baker (build-time only).
- `src/ClassicUO.Client/Resources/Loader.cs` — FileEmbed the 12 baked atlases (co-located with existing TTF embeds at line 68).
- Font-init call site (where `FontsLoader.Load()` runs) — call `RegisterAtlasFont` for each atlas.
- `tests/ClassicUO.UnitTests/` — new test files per task.
- Baked artifacts committed under `Assets/fonts/atlas/<family>-<size>.uofont`.

---

## Phase 0 — Hide rune fonts (independent, mergeable alone)

### Task 0: Rune-font filter in the picker

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs:5428-5466` (`FontSelector` ctor)
- Test: `tests/ClassicUO.UnitTests/FontSelectorFilterTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static bool OptionsGump.FontSelector.IsSelectableUnicodeSlot(int slot)` — false for 7–12, true otherwise.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/FontSelectorFilterTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class FontSelectorFilterTests
    {
        [Theory]
        [InlineData(0, true)]
        [InlineData(6, true)]
        [InlineData(7, false)]   // rune range start
        [InlineData(10, false)]
        [InlineData(12, false)]  // rune range end
        [InlineData(13, true)]
        [InlineData(20, true)]   // future TTF slot
        public void IsSelectableUnicodeSlot_ExcludesRunes(int slot, bool expected)
        {
            Assert.Equal(expected, OptionsGump.FontSelector.IsSelectableUnicodeSlot(slot));
        }
    }
}
```

Note: `FontSelector` is a `private` nested class today. To make it test-visible without exposing it broadly, change `private class FontSelector` (line 5428) to `internal class FontSelector` and make the predicate `internal static`. `ClassicUO.Client` already exposes internals to `ClassicUO.UnitTests` (see CLAUDE.md), so no new attribute is needed.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~FontSelectorFilterTests"`
Expected: FAIL — `FontSelector` inaccessible / `IsSelectableUnicodeSlot` undefined.

- [ ] **Step 3: Implement**

In `OptionsGump.cs`, change line 5428 `private class FontSelector : Control` → `internal class FontSelector : Control`. Add the constant + predicate at the top of the class:

```csharp
            // Unicode font slots 7–12 render decorative rune glyphs, not useful
            // as a readable UI font. Hidden from the picker (render path unaffected).
            private const int RUNE_FONT_FIRST = 7;
            private const int RUNE_FONT_LAST = 12;

            internal static bool IsSelectableUnicodeSlot(int slot)
            {
                return slot < RUNE_FONT_FIRST || slot > RUNE_FONT_LAST;
            }
```

Then in the ctor loop (line 5441-5443), skip runes:

```csharp
                for (byte i = 0; i < max_font; i++)
                {
                    if (IsSelectableUnicodeSlot(i) && Client.Game.UO.FileManager.Fonts.UnicodeFontExists(i))
                    {
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~FontSelectorFilterTests"`
Expected: PASS (7 cases).

- [ ] **Step 5: Build + full suite**

Run: `dotnet build ClassicUO.sln -c Debug` → 0 errors.
Run: `dotnet test tests/ClassicUO.UnitTests` → no regressions.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs tests/ClassicUO.UnitTests/FontSelectorFilterTests.cs
git commit -m "feat(fonts): hide rune unicode fonts (7-12) from the picker"
```

---

## Phase 1 — 8bpp coverage in the glyph struct + rasterizer

### Task 1: Coverage field + pure blend/threshold helpers

**Files:**
- Modify: `src/ClassicUO.Assets/FontsLoader.cs:4091-4105` (`FontCharacterDataUnicode` struct)
- Modify: `src/ClassicUO.Assets/FontsLoader.cs` (add pure static helpers near the other `internal`/`private static` helpers, e.g. after `GetASCIIIndex`)
- Test: `tests/ClassicUO.UnitTests/CoverageBlendTests.cs` (create)

**Interfaces:**
- Produces:
  - `FontCharacterDataUnicode.Coverage` (`byte[]`, 8bpp, row-major `Width*Height`, null for bitmap glyphs) and `readonly bool IsAntiAliased => Coverage != null;`
  - `internal static uint FontsLoader.BlendCoverage(uint dst, uint src, byte coverage)` — alpha-composite `src` over `dst` weighted by `coverage/255`. Both args are `0xAARRGGBB`-packed as used by `pData` (see note below).
  - `internal static bool FontsLoader.CoverageIsSet(byte coverage)` — `coverage >= COVERAGE_EDGE_THRESHOLD` (128), for border/solid passes.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/CoverageBlendTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class CoverageBlendTests
    {
        // pData packs 0xAABBGGRR (little-endian RGBA); blend is per-channel so
        // the exact channel order does not matter to these endpoint assertions.
        [Fact]
        public void BlendCoverage_ZeroCoverage_KeepsDst()
        {
            Assert.Equal(0xFF102030u, FontsLoader.BlendCoverage(0xFF102030u, 0xFFAABBCCu, 0));
        }

        [Fact]
        public void BlendCoverage_FullCoverage_TakesSrc()
        {
            Assert.Equal(0xFFAABBCCu, FontsLoader.BlendCoverage(0xFF102030u, 0xFFAABBCCu, 255));
        }

        [Fact]
        public void BlendCoverage_HalfCoverage_IsMidpoint()
        {
            // each channel ~ (dst+src)/2, within rounding
            uint r = FontsLoader.BlendCoverage(0xFF000000u, 0xFF0000FFu, 128);
            Assert.InRange(r & 0xFF, 0x7Eu, 0x81u);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(127, false)]
        [InlineData(128, true)]
        [InlineData(255, true)]
        public void CoverageIsSet_Thresholds(byte c, bool expected)
        {
            Assert.Equal(expected, FontsLoader.CoverageIsSet(c));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~CoverageBlendTests"`
Expected: FAIL — members undefined.

- [ ] **Step 3: Implement**

Extend the struct (`FontsLoader.cs:4091`):

```csharp
    public struct FontCharacterDataUnicode
    {
        public FontCharacterDataUnicode(sbyte w, sbyte h, sbyte offX, sbyte offY, byte[] data)
        {
            OffsetX = offX;
            OffsetY = offY;
            Width = w;
            Height = h;
            Data = data;
            Coverage = null;
        }

        public sbyte OffsetX, OffsetY;
        public sbyte Width, Height;
        public byte[] Data;            // 1bpp bitmask (original UO fonts)
        public byte[] Coverage;        // 8bpp coverage (TTF atlas fonts), else null

        public readonly bool IsAntiAliased => Coverage != null;
        // "Glyph has drawable pixels" — the whole unicode pipeline currently
        // uses `Data != null` for this; atlas glyphs carry Coverage instead.
        public readonly bool HasPixels => Data != null || Coverage != null;
    }
```

Add the helpers in the `FontsLoader` class body:

```csharp
        internal const byte COVERAGE_EDGE_THRESHOLD = 128;

        // Alpha-composite src over dst weighted by coverage/255, per 8-bit channel.
        // dst/src are packed the same way as the pData pixel buffer.
        internal static uint BlendCoverage(uint dst, uint src, byte coverage)
        {
            if (coverage == 0) return dst;
            if (coverage == 255) return src;

            uint a = coverage;
            uint ia = 255u - a;

            uint BlendChannel(int shift)
            {
                uint d = (dst >> shift) & 0xFF;
                uint s = (src >> shift) & 0xFF;
                return (((s * a) + (d * ia) + 127u) / 255u) << shift;
            }

            return BlendChannel(0) | BlendChannel(8) | BlendChannel(16) | BlendChannel(24);
        }

        internal static bool CoverageIsSet(byte coverage) => coverage >= COVERAGE_EDGE_THRESHOLD;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~CoverageBlendTests"`
Expected: PASS.

- [ ] **Step 5: Build + full suite, then commit**

Run: `dotnet build ClassicUO.sln -c Debug` → 0 errors. `dotnet test tests/ClassicUO.UnitTests` → green.

```bash
git add src/ClassicUO.Assets/FontsLoader.cs tests/ClassicUO.UnitTests/CoverageBlendTests.cs
git commit -m "feat(fonts): 8bpp coverage field + blend/threshold helpers"
```

### Task 1b: Migrate glyph-presence guards to `HasPixels`

**Files:**
- Modify: `src/ClassicUO.Assets/FontsLoader.cs` — the unicode-glyph presence checks at lines **225, 229** (`GetCharUni`), **1217, 1230, 1288, 1315, 1431, 1876, 1894, 2372, 3635, 3873** (measurement / wrap / render / caret).
- Test: extend `tests/ClassicUO.UnitTests/CoverageBlendTests.cs` (or a new `CoveragePresenceTests.cs`).

**Interfaces:** Consumes `FontCharacterDataUnicode.HasPixels` (Task 1). Produces no new API; makes the whole unicode path treat a Coverage-only glyph as a real glyph.

**Why:** `GetWidthUnicode` (line 1288) advances only when `@char.Data != null`; `GetCharUni` (225-229) lazily calls `LoadChar` and returns `_nullChar` when `Data == null`. Atlas glyphs have `Data == null, Coverage != null`, so without this migration all TTF text measures 0 wide and renders as `_nullChar` (nothing).

**Critical detail — `GetCharUni` (lines 214-235):** it currently does `if (cc.Data == null) { LoadChar(...); if (cc.Data == null) return ref _nullChar; }`. Atlas glyphs are pre-populated (Task 8) with `Coverage != null` but `Data == null`, and `LoadChar` early-returns for atlas slots (no `UOFile`). Change both checks to `!cc.HasPixels` so a Coverage-only glyph is returned as-is and never routed to `LoadChar`.

- [ ] **Step 1: Write the failing test**

Add to the test file a case that constructs a Coverage-only glyph and asserts presence-based width. Because `GetWidthUnicode` needs a populated `_fontDataUNICODE`, drive it through the Task 8 seam if available; if Task 1b runs first, unit-test the pure decision by asserting `HasPixels` on a Coverage-only struct AND add the real width assertion in Task 8's test. Minimum here:

```csharp
        [Fact]
        public void HasPixels_TrueForCoverageOnlyGlyph()
        {
            var g = new ClassicUO.Assets.FontCharacterDataUnicode
            {
                Width = 3, Height = 4, Coverage = new byte[12]
            };
            Assert.True(g.HasPixels);
            Assert.Null(g.Data);
        }
```

(The end-to-end measurement proof lives in Task 8's `GetWidthUnicode(20,"A")` assertion, which fails until this migration lands.)

- [ ] **Step 2: Run to verify it fails** (before adding `HasPixels` usage — this specific test passes once Task 1 struct exists, so its real value is guarding the struct; the migration itself is proven by Task 8). Run the focused test; expect PASS for the struct assertion.

- [ ] **Step 3: Implement the migration**

Replace, at each listed line, the presence test:
- `@char.Data != null` → `@char.HasPixels`
- `@char.Data == null` → `!@char.HasPixels`
- In `GetCharUni` (225, 229): `cc.Data == null` → `!cc.HasPixels`.

Do **not** touch the raw pixel-access lines 1992/3915 (`@char.Data[...]`) — those stay inside the 1bpp branch added in Tasks 2/3. Do **not** touch `LoadChar`'s own `cc.Data = new byte[...]` (266) or line 714/1841/3803 which access `.Data` for actual bytes on the bitmap path — read each in context and only change presence *predicates*, not byte reads. (Line 3803 `fcd.Data == null` guards a bitmap-only helper; if that helper is not on the atlas render path leave it, but verify.)

- [ ] **Step 4: Build + full suite (proves bitmap path intact)**

Run: `dotnet build ClassicUO.sln -c Debug` → 0 errors. `dotnet test tests/ClassicUO.UnitTests` → green.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Assets/FontsLoader.cs tests/ClassicUO.UnitTests/CoverageBlendTests.cs
git commit -m "feat(fonts): treat coverage-only glyphs as present across unicode path"
```

### Task 2: Branch the main GenerateUnicode rasterizer on coverage

**Files:**
- Modify: `src/ClassicUO.Assets/FontsLoader.cs:1949-2001` (the `for (int y...)` glyph loop inside `GenerateUnicode`)

**Interfaces:**
- Consumes: `@char.IsAntiAliased`, `@char.Coverage`, `BlendCoverage` (Task 1).
- Produces: nothing new.

**Testing note:** GPU/atlas text render — not unit-testable (no test builds a rendered string here). Gate: builds + existing suite green (proves the 1bpp path is untouched); anti-aliased output verified in Phase 5.

- [ ] **Step 1: Implement the branch**

The current inner body (lines 1972-2000) tests one bit and writes `pData[block] = charcolor`. Wrap it so coverage glyphs blend instead. Replace the `for (int c...)` scanline loop body so that, when `@char.IsAntiAliased`, the pixel comes from `Coverage` indexed row-major:

```csharp
                                if (@char.IsAntiAliased)
                                {
                                    // 8bpp coverage path (TTF atlas glyphs)
                                    for (int x = 0; x < dw; x++)
                                    {
                                        int nowX = testX + x;
                                        if (nowX >= width) break;

                                        byte cov = @char.Coverage[y * dw + x];
                                        if (cov == 0) continue;

                                        int block = testY * width + nowX;
                                        pData[block] = BlendCoverage(pData[block], charcolor, cov);
                                    }
                                }
                                else
                                {
                                    // existing 1bpp path — unchanged
                                    for (int c = 0; c < scanlineCount; c++)
                                    {
                                        int coff = c << 3;
                                        for (int j = 0; j < 8; j++)
                                        {
                                            int x = coff + j;
                                            if (x >= dw) break;
                                            int nowX = testX + x;
                                            if (nowX >= width) break;
                                            byte cl = (byte)(@char.Data[scanLineOff + c] & (1 << (7 - j)));
                                            int block = testY * width + nowX;
                                            if (cl != 0) pData[block] = charcolor;
                                        }
                                    }
                                }
```

Keep the surrounding `for (int y...)` loop, `testY`/`italicOffset`/`testX` setup, and `scanLineOff += scanlineCount` exactly as they are (the coverage path ignores `scanLineOff` but the increment is harmless). Preserve `dw`/`dh` as the glyph's `Width`/`Height`.

- [ ] **Step 2: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: 0 errors.

- [ ] **Step 3: Full suite (proves 1bpp path intact)**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: green — no regression to bitmap-font rendering.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Assets/FontsLoader.cs
git commit -m "feat(fonts): coverage-aware branch in GenerateUnicode rasterizer"
```

### Task 3: Branch the second draw path on coverage

**Files:**
- Modify: `src/ClassicUO.Assets/FontsLoader.cs:3899-3920` (the second glyph expansion loop with `ch.Data[scanLineOff + sc] & (1 << (7 - j))`)

**Interfaces:** Consumes the same Task 1 members. Produces nothing.

**Testing note:** As Task 2 — build + suite green; visual in Phase 5.

- [ ] **Step 1: Implement**

Mirror Task 2's branch at this site: when `ch.IsAntiAliased`, iterate `ch.Coverage[y*dw + x]` and `BlendCoverage` into the destination buffer this method writes to; else run the existing bit-test loop unchanged. Match this method's destination variable and pixel packing (read the method around 3855-3925 first to bind the exact destination buffer name and color variable).

- [ ] **Step 2: Build + suite + commit**

Run: `dotnet build ClassicUO.sln -c Debug` → 0 errors. `dotnet test tests/ClassicUO.UnitTests` → green.

```bash
git add src/ClassicUO.Assets/FontsLoader.cs
git commit -m "feat(fonts): coverage-aware branch in secondary glyph draw path"
```

### Task 4: Make black-border / solid passes coverage-aware

**Files:**
- Modify: `src/ClassicUO.Assets/FontsLoader.cs:1786-2116` (the `isSolid`/`isBlackBorder` post-passes over the rasterized buffer)

**Interfaces:** Consumes `CoverageIsSet` (Task 1). Produces nothing.

**Testing note:** Build + suite green; the visual correctness of AA outlines is a Phase 5 check.

- [ ] **Step 1: Implement**

The solid/border passes (around 2003-2116) detect "is this a glyph pixel" to grow a 1px outline / solid fill. They currently rely on the pixel being exactly `charcolor` (binary). For coverage glyphs the interior is anti-aliased, so use a coverage-derived mask instead: where the code decides a source pixel is "set", route through `CoverageIsSet(cov)` for AA glyphs (threshold at 128) and keep the exact equality test for bitmap glyphs. Read 2003-2116 to bind the exact predicate; gate the new threshold path behind the same `isAntiAliased` boolean captured for this glyph. Original fonts must hit the unchanged branch.

- [ ] **Step 2: Build + suite + commit**

Run: `dotnet build ClassicUO.sln -c Debug` → 0 errors. `dotnet test tests/ClassicUO.UnitTests` → green.

```bash
git add src/ClassicUO.Assets/FontsLoader.cs
git commit -m "feat(fonts): coverage-aware black-border/solid passes"
```

---

## Phase 2 — Atlas format + build-time baker

### Task 5: Atlas binary format reader (pure, testable)

**Files:**
- Create: `src/ClassicUO.Assets/AtlasFontFile.cs`
- Test: `tests/ClassicUO.UnitTests/AtlasFontFileTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class AtlasFontFile` with `int PixelSize, Ascent, Descent, LineHeight; char FirstChar, LastChar;` and `bool TryGetGlyph(char c, out AtlasGlyph g)`.
  - `public readonly struct AtlasGlyph { sbyte OffsetX, OffsetY; byte Width, Height; short Advance; byte[] Coverage; }`
  - `public static AtlasFontFile Read(ReadOnlySpan<byte> bytes)`
  - `public static byte[] Write(AtlasFontHeader header, IReadOnlyList<AtlasGlyph> glyphs)` — shared with the baker so the round-trip is one code path.

**Format (little-endian):** magic `"UOAF"` (4 bytes), `version` (u16 = 1), `pixelSize` (u16), `ascent` (i16), `descent` (i16), `lineHeight` (u16), `firstChar` (u16), `lastChar` (u16). Then, for each code point `firstChar..lastChar` inclusive: `offsetX` (i8), `offsetY` (i8), `width` (u8), `height` (u8), `advance` (i16), then `width*height` coverage bytes (absent when width*height == 0).

- [ ] **Step 1: Write the failing test**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Assets;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class AtlasFontFileTests
    {
        [Fact]
        public void WriteThenRead_RoundTripsHeaderAndGlyph()
        {
            var header = new AtlasFontHeader
            {
                PixelSize = 18, Ascent = 15, Descent = -3, LineHeight = 20,
                FirstChar = 'A', LastChar = 'B'
            };
            var glyphs = new List<AtlasGlyph>
            {
                new AtlasGlyph { OffsetX = 1, OffsetY = -2, Width = 2, Height = 2, Advance = 10,
                                 Coverage = new byte[] { 0, 255, 128, 64 } },
                new AtlasGlyph { OffsetX = 0, OffsetY = 0, Width = 0, Height = 0, Advance = 6,
                                 Coverage = new byte[0] },
            };

            byte[] bytes = AtlasFontFile.Write(header, glyphs);
            AtlasFontFile f = AtlasFontFile.Read(bytes);

            Assert.Equal(18, f.PixelSize);
            Assert.Equal(20, f.LineHeight);
            Assert.True(f.TryGetGlyph('A', out AtlasGlyph a));
            Assert.Equal(2, a.Width);
            Assert.Equal((short)10, a.Advance);
            Assert.Equal(new byte[] { 0, 255, 128, 64 }, a.Coverage);
            Assert.True(f.TryGetGlyph('B', out AtlasGlyph b));
            Assert.Equal(0, b.Width);          // blank glyph
            Assert.False(f.TryGetGlyph('Z', out _)); // out of range
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AtlasFontFileTests"`
Expected: FAIL — types undefined.

- [ ] **Step 3: Implement `AtlasFontFile.cs`**

Write the struct/class/`Read`/`Write` exactly matching the format above. `Read` parses the header, then reads glyphs into an array indexed by `c - FirstChar`. `TryGetGlyph` bounds-checks `FirstChar..LastChar`. `Write` emits header then each glyph. Use `BinaryPrimitives` for endianness. (Full implementation is mechanical from the format spec; keep it allocation-light but clarity over micro-opt.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AtlasFontFileTests"`
Expected: PASS.

- [ ] **Step 5: Build + suite + commit**

```bash
git add src/ClassicUO.Assets/AtlasFontFile.cs tests/ClassicUO.UnitTests/AtlasFontFileTests.cs
git commit -m "feat(fonts): atlas font binary format reader/writer"
```

### Task 6: FontAtlasBaker build-time tool

**Files:**
- Create: `tools/FontAtlasBaker/FontAtlasBaker.csproj` (console, `net10.0`, references `ClassicUO.Assets` for `AtlasFontFile.Write` + `FontStashSharp` 1.3.10)
- Create: `tools/FontAtlasBaker/Program.cs`
- Create/commit outputs: `Assets/fonts/atlas/<family>-<size>.uofont` (12 files)

**Interfaces:**
- Consumes: `AtlasFontFile.Write`, `AtlasFontHeader`, `AtlasGlyph` (Task 5); FontStashSharp `FontSystem`/`DynamicSpriteFont` to rasterize glyph coverage.
- Produces: the 12 committed atlas files. Not shipped as a project dependency of the client.

**Testing note:** A build tool; verified by running it and by Task 5's round-trip test + Phase 5. No unit test of its own beyond what Task 5 covers.

- [ ] **Step 1: Implement the baker**

`Program.cs` takes (or hardcodes) the family→TTF-path map and `int[] sizes = {14,18,22}`, `char first=' '`, `char last='ſ'`. For each family/size:
  - Load the TTF via FontStashSharp `FontSystem.AddFont(ttfBytes)`; `GetFont(size)`.
  - For each code point, rasterize to an 8bpp coverage bitmap. FontStashSharp exposes glyph rendering via its atlas/`ITexture2DManager`; the simplest deterministic path is to render each glyph to a small in-memory RGBA buffer through a headless `IFontStashRenderer`/`ITexture2DManager` that captures `SetTextureData`, then take the alpha channel as coverage. Alternatively use the bundled StbTrueTypeSharp directly (`StbTrueType.stbtt_*`) to get single-channel coverage + metrics (offset, advance) without a GraphicsDevice — this is cleaner for a headless tool. Prefer StbTrueTypeSharp: `stbtt_InitFont`, `stbtt_ScaleForPixelHeight(size)`, `stbtt_GetCodepointBitmap` (returns 8bpp coverage + w/h/xoff/yoff), `stbtt_GetCodepointHMetrics` (advance). Capture ascent/descent/lineGap from `stbtt_GetFontVMetrics` scaled.
  - Warn to stderr for any code point in range the font lacks (`stbtt_FindGlyphIndex == 0`), emitting a blank glyph (w=h=0).
  - `AtlasFontFile.Write(header, glyphs)` → write to `Assets/fonts/atlas/<family>-<size>.uofont`.

- [ ] **Step 2: Wire build regeneration (optional target)**

Add an MSBuild target (in the baker csproj or a `Directory.Build` hook under `tools/`) that runs the baker when a source TTF or the baker changes. Because the outputs are committed, normal client builds do not run the baker. Document the manual command in the tool's README: `dotnet run --project tools/FontAtlasBaker`.

- [ ] **Step 3: Run the baker, verify outputs**

Run: `dotnet run --project tools/FontAtlasBaker -c Release`
Expected: 12 files under `Assets/fonts/atlas/`; stderr shows no missing SK/CZ code points for any family (if it does, note which family/char).

- [ ] **Step 4: Commit tool + atlases**

```bash
git add tools/FontAtlasBaker Assets/fonts/atlas
git commit -m "feat(fonts): build-time TTF atlas baker + baked atlases"
```

---

## Phase 3 — Load atlases into synthetic slots

### Task 7: `MAX_UNICODE_FONTS` constant + guard migration

**Files:**
- Modify: `src/ClassicUO.Assets/FontsLoader.cs` — lines 75, 119, 299, 1180, 1266, 1276, 1308, 1337, 1386, 1676, 3521, 3580, 3855.

**Interfaces:** Produces `internal const int FontsLoader.MAX_UNICODE_FONTS = 32;`. No behavior change for slots 0–19.

- [ ] **Step 1: Implement**

Add near the top of the class: `internal const int MAX_UNICODE_FONTS = 32;`
- Line 75: `private readonly UOFile[] _unicodeFontAddress = new UOFile[MAX_UNICODE_FONTS];`
- Line 119: `for (int i = 0; i < MAX_UNICODE_FONTS; i++)` — **but** keep the `unifontN.mul` read only for `i < 20` (there is no `unifont20.mul`). Guard the file read: `if (i < 20) { ... existing unifont load ... }`. Slots 20+ are filled by `RegisterAtlasFont` (Task 8), not files.
- Every `font >= 20` (lines 299 uses `< 20`; 1180/1266/1276/1308/1337/1386/1676/3521/3580/3855 use `>= 20`): replace the literal `20` with `MAX_UNICODE_FONTS`.

`_fontDataUNICODE` (line 209) already sizes from `_unicodeFontAddress.Length`, so it grows automatically.

- [ ] **Step 2: Build + suite**

Run: `dotnet build ClassicUO.sln -c Debug` → 0 errors.
Run: `dotnet test tests/ClassicUO.UnitTests` → green (slots 0–19 unchanged).

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Assets/FontsLoader.cs
git commit -m "refactor(fonts): MAX_UNICODE_FONTS constant, widen slots to 32"
```

### Task 8: `RegisterAtlasFont` populates synthetic slots

**Files:**
- Modify: `src/ClassicUO.Assets/FontsLoader.cs` — add `RegisterAtlasFont` + a slot→name registry; ensure `UnicodeFontExists` and the metric paths accept atlas slots.

**Interfaces:**
- Consumes: `AtlasFontFile` (Task 5), `FontCharacterDataUnicode.Coverage` (Task 1), `MAX_UNICODE_FONTS` (Task 7).
- Produces:
  - `public void RegisterAtlasFont(int slot, string displayName, ReadOnlySpan<byte> atlasBytes)`
  - `public bool IsAtlasFont(int slot)` and `public string GetFontDisplayName(int slot)` (for the picker).

**Testing note:** Needs no GraphicsDevice, but populates internal arrays; a light unit test is feasible — see Step 1.

- [ ] **Step 1: Write the failing test**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Assets;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class RegisterAtlasFontTests
    {
        [Fact]
        public void RegisterAtlasFont_MakesSlotExistWithMetrics()
        {
            var header = new AtlasFontHeader { PixelSize = 18, Ascent = 15, Descent = -3, LineHeight = 20, FirstChar = 'A', LastChar = 'A' };
            var glyphs = new List<AtlasGlyph> { new AtlasGlyph { OffsetX = 0, OffsetY = 0, Width = 2, Height = 2, Advance = 9, Coverage = new byte[] { 10, 20, 30, 40 } } };
            byte[] bytes = AtlasFontFile.Write(header, glyphs);

            var loader = new FontsLoader(); // ctor accessible per existing pattern
            loader.RegisterAtlasFont(20, "Test 18", bytes);

            Assert.True(loader.UnicodeFontExists(20));
            Assert.True(loader.IsAtlasFont(20));
            Assert.Equal("Test 18", loader.GetFontDisplayName(20));
            // Advance model: OffsetX(0) + Width(2) + 1 = 3
            Assert.Equal(3, loader.GetWidthUnicode(20, "A"));
        }
    }
}
```

Note: confirm `FontsLoader` is constructible in tests (check its ctor/`Load` coupling). If `GetWidthUnicode` requires `_fontDataUNICODE` allocated, call the minimal init the ctor/`Load` provides, or allocate `_fontDataUNICODE` inside `RegisterAtlasFont` if null so the method is self-sufficient for atlas slots. Adjust the test to the real constructibility; the essential assertions are `UnicodeFontExists`/`IsAtlasFont`/`GetFontDisplayName`/metric.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~RegisterAtlasFontTests"`
Expected: FAIL — members undefined.

- [ ] **Step 3: Implement**

- Add `private readonly string[] _atlasFontNames = new string[MAX_UNICODE_FONTS];` and a sentinel (non-null `_unicodeFontAddress[slot]` is the "exists" signal today; atlas slots have no `UOFile`). Add `private readonly bool[] _isAtlasFont = new bool[MAX_UNICODE_FONTS];`.
- `UnicodeFontExists(font)`: `return font < MAX_UNICODE_FONTS && (_unicodeFontAddress[font] != null || _isAtlasFont[font]);`. Audit the many `_unicodeFontAddress[font] == null` guards (1180 etc.): they must also accept atlas slots — change them to a helper `bool SlotUsable(byte font) => font < MAX_UNICODE_FONTS && (_unicodeFontAddress[font] != null || _isAtlasFont[font]);` and replace the `font >= MAX_UNICODE_FONTS || _unicodeFontAddress[font] == null` clauses with `!SlotUsable(font)`.
- `RegisterAtlasFont(slot, name, bytes)`: parse via `AtlasFontFile.Read`; ensure `_fontDataUNICODE` allocated; for each code point, set `_fontDataUNICODE[slot, c] = new FontCharacterDataUnicode { OffsetX=g.OffsetX, OffsetY=g.OffsetY, Width=g.Width, Height=g.Height, Data=null, Coverage=g.Coverage }`.
- **Advance model (decided):** the unicode pipeline advances the pen by `OffsetX + Width + 1` (see `GetWidthUnicode` line 1290) — there is no separate advance metric. Atlas glyphs therefore reuse this model: the **baker** (Task 6) sets `OffsetX = left side bearing` and `Width = glyph bbox width` (the coverage bitmap's width), so `OffsetX + Width + 1` approximates the TTF advance. The `Advance` field in the atlas format is stored for future use but is **not** consumed by measurement in this plan. Spacing may read slightly tight (right side bearing dropped); Phase 5 checks it, and if needed the baker can widen `Width` to `advance - OffsetX - 1` while keeping the coverage left-aligned within it (a baker-only change, no pipeline impact).
- `GetCharUni`/`LoadChar`: already handled by Task 1b (the `!cc.HasPixels` migration) — atlas glyphs are pre-populated with `Coverage != null`, so `GetCharUni` returns them directly and never calls `LoadChar`. No further change here.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~RegisterAtlasFontTests"`
Expected: PASS.

- [ ] **Step 5: Build + suite + commit**

```bash
git add src/ClassicUO.Assets/FontsLoader.cs tests/ClassicUO.UnitTests/RegisterAtlasFontTests.cs
git commit -m "feat(fonts): RegisterAtlasFont populates synthetic unicode slots"
```

### Task 9: Embed + register the 12 atlases

**Files:**
- Modify: `src/ClassicUO.Client/Resources/Loader.cs` (FileEmbed the 12 `.uofont` files, next to the TTF embeds at line 68)
- Modify: the font-init call site where `FontsLoader.Load()` runs (find via `Fonts.Load()` / `FileManager.Fonts`) — call `RegisterAtlasFont(20+i, name, bytes)` for each atlas after `Load()`.

**Interfaces:** Consumes `RegisterAtlasFont` (Task 8) + FileEmbed getters. Produces the runtime-populated slots 20–31.

**Testing note:** Wiring; verified in Phase 5. Gate: build + suite green.

- [ ] **Step 1: FileEmbed the atlases**

In `Loader.cs`, add for each family/size (12 entries), e.g.:

```csharp
        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/Cinzel-14.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasCinzel14();
        // ... 11 more
```

- [ ] **Step 2: Register at font init**

At the site that owns `FontsLoader` init, after `Load()`, define the fixed slot order (20=Cinzel14, 21=Cinzel18, 22=Cinzel22, 23=Cormorant14, ... 31=SourceSans22) and:

```csharp
            fonts.RegisterAtlasFont(20, "Cinzel 14", Loader.GetAtlasCinzel14());
            fonts.RegisterAtlasFont(21, "Cinzel 18", Loader.GetAtlasCinzel18());
            // ... through slot 31
```

Keep the slot↔name mapping in one place (a static table) so Task 10's picker and this registration cannot drift.

- [ ] **Step 3: Build + suite + commit**

Run: `dotnet build ClassicUO.sln -c Debug` → 0 errors. `dotnet test tests/ClassicUO.UnitTests` → green.

```bash
git add src/ClassicUO.Client/Resources/Loader.cs <font-init-file>
git commit -m "feat(fonts): embed + register 12 TTF atlas slots"
```

---

## Phase 4 — Picker exposure + persistence

### Task 10: Show TTF fonts in the picker with labels

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` — `BuildFonts` (line 2409, the `new FontSelector(20, ...)`) and `FontSelector` ctor (5432).

**Interfaces:** Consumes `IsAtlasFont`/`GetFontDisplayName` (Task 8), `IsSelectableUnicodeSlot` (Task 0), `MAX_UNICODE_FONTS`.

**Testing note:** UI wiring; Phase 5 verifies. Gate: build + suite green.

- [ ] **Step 1: Implement**

- Change the two `new FontSelector(20, ...)` / `new FontSelector(7, ...)` call sites (chat line 2409; tooltip line 2340) to pass `FontsLoader.MAX_UNICODE_FONTS` as `max_font` (tooltip may keep its own cap if desired — confirm the tooltip should also offer TTF; if yes, widen both).
- In the `FontSelector` ctor loop, for atlas slots use the display name as the radio label instead of the shared markup: `string label = fonts.IsAtlasFont(i) ? fonts.GetFontDisplayName(i) : markup;`. Keep the rune filter from Task 0. The loop already skips slots where `UnicodeFontExists(i)` is false, so unbaked slots never show.

- [ ] **Step 2: Build + suite + commit**

Run: `dotnet build ClassicUO.sln -c Debug` → 0 errors. `dotnet test tests/ClassicUO.UnitTests` → green.

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(fonts): expose TTF atlas fonts in the Options picker"
```

---

## Phase 5 — Verify in the running client

### Task 11: Manual client verification

**Files:** none.

- [ ] **Step 1: Launch (verify/run skill) and check:**
- [ ] Rune fonts (7–12) absent from the Options → Fonts picker.
- [ ] All 12 TTF entries (Cinzel/Cormorant/Inter/Source Sans 3 × 14/18/22) appear and are selectable.
- [ ] Selecting a TTF font renders chat/journal/tooltip text **anti-aliased**; original UO fonts still render unchanged when reselected.
- [ ] Hue/color still applies to TTF text; black-border/solid text is legible.
- [ ] `OverrideAllFonts` with a TTF slot applies everywhere.
- [ ] **Slovak+Czech pangram** (á ä č ď ě é í ĺ ľ ň ó ô ŕ ř š ť ú ů ý ž + uppercase) renders fully in each TTF family — no blank glyphs.
- [ ] Restart persists the selected TTF font (`ChatFont`/`TooltipFont` byte round-trips).

- [ ] **Step 2:** If a check fails, loop back to the owning task; otherwise the branch is ready for finishing.
