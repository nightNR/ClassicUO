# TTF In-Game Fonts via Live StbTrueType Rasterization — Design

**Date:** 2026-07-29
**Status:** Approved (design), pending spec review
**Approach:** A — FSS/StbTrueType supplies glyph pixels + metrics; the legacy
measure/layout/composite pipeline is reused unchanged.

## Goal

Replace the offline-baked TTF atlas fonts (slots 20–31) with **live glyph
rasterization** via StbTrueTypeSharp (the rasterizer FontStashSharp already
bundles and that the current offline baker already uses). The existing
measure/layout, glyph GPU cache, HTML, border/solid/italic, hue coloring,
premultiplied composite, and hit-testing all stay exactly as they are — only
the *source* of a glyph's coverage bytes + metrics changes, from a pre-baked
`AtlasFontFile` to on-demand StbTrueType rasterization.

This removes the offline baker tool and the 12 embedded baked-atlas resources,
supports arbitrary Unicode codepoints (incl. Slavic) without re-baking, and
collapses the bespoke bake step to a single live path.

## Trigger

When a piece of text's effective font id is in the TTF slot range **20–31**
(`_isAtlasFont[slot] == true`), glyphs come from the live path. Classic UO
fonts (unicode 0–19, ASCII) are untouched and continue through the existing
legacy path.

## Non-Goals (explicitly deferred / out of scope)

- **Extending `OverrideAllFonts` to bypassed sites** (input dialogs, map
  labels, skill/trade width calcs, status/paperdoll/party, etc.). Separate
  follow-up spec — it changes classic-text behavior and carries different risk.
- **Arbitrary font sizes / new size-selection UI.** Keep the fixed 12-slot
  model (4 families × 14/18/22) and its existing picker. Live rasterization
  simply happens at the slot's fixed pixel size.
- **Any change to classic ASCII / unicode bitmap fonts**, the measure/layout
  API, `RenderedText` layout, caret, cropping, HTML layout, or hit-testing.
- **Moving login off its existing FSS path** (`FontStashLoginFont`). Untouched.
  (Sharing one FSS provider — approach "C" — was considered and rejected for
  now to keep scope minimal.)

## Background (current state)

- Slots 20–31 are synthetic unicode slots. `AtlasFontRegistry.Entries`
  (`src/ClassicUO.Client/Resources/Loader.cs:133-147`) maps each slot to a
  display name; `GetAtlasBytes(slot)` (`Loader.cs:154-173`) returns embedded
  baked-atlas bytes via source-generated partials `GetAtlasCinzel14()` etc.
  (`Loader.cs:79`). `RegisterAll` (`Loader.cs:180-186`) is called once from
  `Client.cs:204`.
- `FontsLoader.RegisterAtlasFont(int slot, string name, ReadOnlySpan<byte> atlasBytes)`
  (`FontsLoader.cs:319`) currently does `AtlasFontFile.Read(atlasBytes)`
  (`FontsLoader.cs:321`) and fills `_fontDataUNICODE[slot, c]` with per-glyph
  offsets/advance/coverage, then sets `_isAtlasFont[slot]=true`.
- `_fontDataUNICODE` is `FontCharacterDataUnicode[_unicodeFontAddress.Length, 0x10000]`
  (`FontsLoader.cs:76,213`) — a full-BMP-per-font array. Glyph rasterization for
  atlas slots reads `ch.Coverage` in `RenderSingleGlyphUnicode`
  (`FontsLoader.cs:4093-4110`), and the same per-char data feeds measure
  (`GetInfoUnicode`, `GetWidthUnicode`, …).
- `AtlasFontFile.Read` is used **only** at `FontsLoader.cs:321` (plus the class
  itself and its unit tests) — no other consumers.
- The offline baker lives at `tools/FontAtlasBaker` and already depends on
  StbTrueTypeSharp (1.26.12).

## Architecture

Three focused changes; everything downstream is untouched.

### 1. Registry supplies TTF bytes + pixel size (not baked atlases)

`AtlasFontRegistry` (`Loader.cs`): each `Entry` provides the **raw TTF bytes**
and the **pixel size** for its slot, instead of baked-atlas bytes. The four TTF
files (Cinzel SemiBold, Cormorant, Inter, SourceSans — the same faces already
used) are embedded once each; the 12 baked `.bin` atlas resources and their
`GetAtlas<Family><Size>()` partials are removed and replaced by four
`GetTtf<Family>()` (one per face, shared across that face's three sizes).
`RegisterAll` passes `(slot, name, ttfBytes, pixelSize)`.

### 2. `RegisterAtlasFont` opens a StbTrueType font for the slot

New signature:
`RegisterAtlasFont(int slot, string name, ReadOnlySpan<byte> ttfBytes, int pixelSize)`.
Instead of parsing an `AtlasFontFile`, it initializes a StbTrueType font handle
for the slot (retaining the TTF byte buffer for the process lifetime), records
the pixel size, sets `_isAtlasFont[slot]=true` and `_atlasFontNames[slot]=name`.
It does **not** rasterize glyphs up front.

A new per-slot table holds the StbTrueType font handle + pixel size +
scale/ascent/descent/lineHeight (queried once from StbTrueType). This is the
only new state.

### 3. Lazy per-glyph rasterization into the existing `_fontDataUNICODE`

The first time a character `c` is needed for an atlas slot (from either the
measure path or `RenderSingleGlyphUnicode`), rasterize it via StbTrueType:
compute advance/bearing/width/height and the 8-bit coverage buffer, and
**populate `_fontDataUNICODE[slot, c]`** with exactly the fields the existing
code already reads (offsets, advance, width, height, coverage; flagged
antialiased/8bpp so `RenderSingleGlyphUnicode` takes its existing coverage
branch). Populate-once, then it behaves identically to a baked entry.

- Covers any BMP codepoint on demand (Slavic č/š/ž etc.) — no bake step.
- No startup cost; cost is amortized per first-seen glyph, then cached in the
  array (and the GPU glyph is cached by `FontGlyphAtlas` as today).
- Downstream is byte-for-byte the same code: `BlendCoverage` /
  `StrengthenCoverage` (interior-solidify fix stays) / border / solid / italic /
  hue / premultiplied composite in `RenderSingleGlyphUnicode`, the
  `FontGlyphAtlas` GPU cache, `RenderedText.DrawGlyphs`, the shader hue path,
  and measure/layout via `GetInfoUnicode` (reads the same `_fontDataUNICODE`).

### Data flow (unchanged except the fill source)

```
text with font∈20..31
   → GetInfoUnicode / GetWidth* (measure)  ─┐
   → RenderSingleGlyphUnicode (raster)      ─┴─ read _fontDataUNICODE[slot,c]
                                                 │ (populated lazily by
                                                 │  StbTrueType on first use)
   → existing coverage/border/solid/hue/premultiplied composite
   → FontGlyphAtlas GPU cache → RenderedText.DrawGlyphs → shader → screen
```

## Removals

- `tools/FontAtlasBaker` (offline baker) — no longer needed.
- The 12 embedded baked-atlas resources + their `GetAtlas<Family><Size>()`
  partials in `Loader.cs`.
- `src/ClassicUO.Assets/AtlasFontFile.cs` (reader/writer) and
  `tests/ClassicUO.UnitTests/AtlasFontFileTests.cs` — `AtlasFontFile.Read` has
  no consumer once `RegisterAtlasFont` stops using it (verified: only used at
  `FontsLoader.cs:321`). Remove both. (If a residual consumer surfaces during
  implementation, keep the file and note it.)

## New dependency

`StbTrueTypeSharp` **1.26.12** as a `PackageReference` in
`src/ClassicUO.Assets/ClassicUO.Assets.csproj` (matches the version the baker
used; pure-managed, NativeAOT-safe).

## Error Handling

- **Missing glyph in the TTF** (codepoint not in face): rasterize returns
  empty → populate a zero/space entry so measure advances and draw skips, same
  as a missing baked glyph today. Optionally log once per (slot, codepoint) at
  trace.
- **Corrupt/unloadable TTF at registration**: log a warning, leave
  `_isAtlasFont[slot]=false`; `SlotUsable` returns false → callers fall back to
  the default/classic font (existing behavior for an unregistered slot).
- Rasterization must never throw into the render/measure path — wrap StbTrueType
  calls and fall back to an empty glyph on failure.

## Testing

Unit (`ClassicUO.UnitTests`, xUnit):
- **Rasterization metrics**: for a known face+size+glyph (e.g. Inter 22 'M'),
  StbTrueType yields width/height/advance > 0 and a coverage buffer whose
  interior pixels are fully opaque (255) — guards the patchy-infill regression.
- **Slot registration**: after `RegisterAtlasFont(slot, name, ttf, size)`,
  `IsAtlasFont(slot)` and `SlotUsable(slot)` are true and `GetFontDisplayName`
  matches.
- **Lazy fill**: first access to a char populates `_fontDataUNICODE[slot,c]`
  with nonzero metrics; second access is a cache hit (no re-raster) — assert via
  an observable counter or by state.
- **Codepoint coverage**: Slavic glyphs (č, š, ž) rasterize with nonzero
  metrics (the on-demand path handles arbitrary BMP).
- **Corrupt TTF**: `RegisterAtlasFont` with garbage bytes leaves the slot
  unusable and does not throw.

Manual (user, in-client on Vulkan/phoenix):
- Overhead world text, gump labels, tooltips, journal set to a TTF slot render
  solid (no see-through infill) with smooth AA edges; border/solid/hue look
  correct; SK/CZ text correct; classic fonts unchanged.

## Files touched

- **New:** per-slot StbTrueType state + the lazy rasterizer (likely a small new
  type in `ClassicUO.Assets`, e.g. `TtfGlyphSource.cs`, owned by `FontsLoader`).
  `tests/ClassicUO.UnitTests/…/TtfGlyphSourceTests.cs`.
- **Modify:** `src/ClassicUO.Assets/FontsLoader.cs` (`RegisterAtlasFont` new
  signature + lazy fill hook in the atlas-slot glyph/measure path),
  `src/ClassicUO.Assets/ClassicUO.Assets.csproj` (StbTrueTypeSharp ref),
  `src/ClassicUO.Client/Resources/Loader.cs` (`Entries` carry TTF bytes+size;
  `GetTtf<Family>()`; `RegisterAll` passes ttf+size), and the client csproj
  embedded-resource list (4 TTFs in, 12 atlases out).
- **Remove:** `tools/FontAtlasBaker`, the 12 baked-atlas resources,
  `src/ClassicUO.Assets/AtlasFontFile.cs`,
  `tests/ClassicUO.UnitTests/AtlasFontFileTests.cs`.

## Implementation Order

1. Add StbTrueTypeSharp ref; new `TtfGlyphSource` (open font, query
   vmetrics/scale, rasterize one glyph → coverage+metrics). Unit tests.
2. Rework `RegisterAtlasFont` to the TTF signature + per-slot handle; wire lazy
   fill of `_fontDataUNICODE[slot,c]` on first access. Unit tests (registration,
   lazy fill, codepoints, corrupt TTF).
3. Rework `AtlasFontRegistry`/`Loader.cs` + csproj: embed 4 TTFs, drop 12
   atlases, `RegisterAll` passes ttf+size. Build.
4. Remove `AtlasFontFile` + its tests and `tools/FontAtlasBaker`. Build + full
   unit suite.
5. Manual in-client verification (user, Vulkan): TTF text solid + correct across
   overhead/gump/tooltip/journal + SK/CZ; classic fonts unchanged.

## Risk

Localized to glyph coverage/metric parity vs the old baked data. Measure,
layout, caret, cropping, HTML, hit-testing, and classic fonts are untouched, so
regressions (if any) are confined to TTF glyph appearance and are directly
visible in-client.
