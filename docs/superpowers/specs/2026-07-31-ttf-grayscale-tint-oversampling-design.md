# TTF Grayscale Atlas + Draw-Time Tint + Oversampling — Design

**Date:** 2026-07-31
**Status:** Approved (design), pending spec review
**Builds on:** the live-StbTrueType TTF font path (slots 20–31).

## Goal

Make colored in-game TTF text render solid and correctly hued (no washed /
see-through infill), and stop baking a separate atlas glyph per color. Two
separable improvements, both TTF-only (slots 20–31); classic MUL/ASCII fonts
are untouched.

- **B (the visual fix): oversampled rasterization.** Rasterize each TTF glyph
  at 3× the target pixel size and box-downsample to target. Small-size strokes
  gain full interior coverage plus smooth anti-aliasing, which is the actual
  cause of the washed colored infill. This lets us delete the
  `CoverageSolidAt`/`StrengthenCoverage` solidify hack.
- **A (architecture): color-independent grayscale atlas + tint at draw.** TTF
  glyphs are cached once as a white×coverage (premultiplied) mask and tinted at
  draw time via the existing `TEXT_RGB` shader path (the same mechanism the
  login FSS text already uses). Removes per-color glyph baking
  (`GetColoredEntry`) for TTF and the `baseColor` bake branch.

## Why both

Tinting a premultiplied white×coverage glyph yields `tint × coverage`
premultiplied — mathematically identical to the current color-baking (also
`color × coverage` premultiplied). So A alone changes no pixels. The washed
appearance comes from sub-255 interior coverage at 14–22px, which only B fixes.
A is the clean architecture (color-independent atlas, reuse of the proven login
path); B is the visible correctness fix. Doing both gives correct visuals on a
clean foundation.

## Trigger / scope boundary

Everything keys off `FontsLoader.IsAtlasFont(font)` (slots 20–31). Classic
unicode (0–19) and ASCII fonts keep their existing rasterization, baking, and
draw path with zero change. Measure/layout (`GetInfoUnicode`, `GetWidthUnicode`,
…), hit-testing, and HTML layout are unchanged for all fonts.

## Background (current state, verified)

- `TtfGlyphSource` (in `ClassicUO.Assets`) rasterizes one glyph at 1× via
  StbTrueType (`stbtt_GetCodepointBitmap(info, scale, scale, …)`), producing
  coverage + metrics that `FontsLoader.LoadAtlasChar` stores in
  `_fontDataUNICODE[slot, c]`.
- `FontsLoader.RenderSingleGlyphUnicode` (`FontsLoader.cs`, ~4110–4140) builds
  a glyph bitmap: for atlas (AA) glyphs it runs `cov = StrengthenCoverage(cov)`
  then `pData[block] = BlendCoverage(pData[block], charcolor, cov)` — all four
  channels blended against a zero background, so a covered pixel becomes
  premultiplied `charcolor × coverage`. Uncolored (`charcolor == 0` →
  `0xFEFFFFFF`) therefore yields a **color-independent premultiplied
  white×coverage** glyph.
- `FontGlyphAtlas.GetEntry` (uncolored) → `CreateEntry(…, 0)`;
  `GetColoredEntry` (colored) → `CreateEntry(…, color)`. Atlas is
  `SurfaceFormat.Color`.
- `RenderedText.DrawGlyphs` (`RenderedText.cs`, ~473–709): for hued Unicode it
  computes `baseColor = RgbaToArgb((Hues.GetPolygoneColor(Cell, Hue) << 8) | 0xFF)`
  and calls `GetColoredEntry` (baked color), drawing with `hueVector =
  (0, 0, alpha)` → shader mode NONE. `RenderedText.Hue` is a `ushort` hue index.
- Login FSS reference (works): `FontStashLoginFont` draws a premultiplied
  white×coverage glyph via `UltimaBatcher2D.DrawTextGlyph`
  (`Batcher2D.cs:636`), which puts the tint rgb in the vertex `Normal` channel
  and sets the hue vector to `(0, SHADER_TEXT_RGB, opacity)`. Shader
  `IsometricWorld.fx` `TEXT_RGB` (mode 11): `color.rgb *= IN.Normal;` then
  `return color * alpha;` — premultiplied tint, correct under the default
  premultiplied `BlendState.AlphaBlend` all text draws with.
- `TEXT_RGB` multiplying a premultiplied white×coverage sample by `tint` gives
  `tint × coverage` premultiplied. A baked-black border pixel (`≈0x010101`) ×
  tint stays ≈ black, so border + fill both render correctly in one pass.

## Architecture

### B — Oversampled rasterization (`TtfGlyphSource`)

- Add an oversample factor constant `Oversample = 3`.
- In `TryRasterize`, rasterize at `scale3 = stbtt_ScaleForPixelHeight(info, pixelSize * Oversample)`
  (equivalently `scale * Oversample`), producing a 3×-size coverage bitmap and
  its `(w3, h3, xoff3, yoff3)`.
- Box-downsample the 3× coverage to target: output pixel `(x,y)` = average of
  the `Oversample × Oversample` source block (rounded), producing target
  coverage `w = ceil(w3/3)`-ish. Compute exact target dimensions by
  downsampling the 3× bounding box; ensure `Width`/`Height` are the downsampled
  extents. A fully-covered 3×3 block → 255; a partially-covered block → its
  average (smooth AA).
- Metrics: `OffsetX = round(xoff3 / Oversample)`, `OffsetY =
  round((ascentPx3 + yoff3) / Oversample)` where `ascentPx3 = round(ascentRaw *
  scale3)`; `Width`/`Height` = downsampled coverage extents. Clamp to `sbyte`
  as today. (Net effect: same metric space as a 1× bake, just higher-quality
  coverage.)
- Missing glyph / empty bitmap → returns false as today.

This makes interiors reach full (255) coverage with smooth edges, so no
threshold solidify is needed.

### A — Grayscale atlas + draw-time tint (TTF only)

- **Remove the solidify hack:** `StrengthenCoverage` no longer thresholds;
  delete `CoverageSolidAt` and make coverage pass through unchanged (or delete
  the call in `RenderSingleGlyphUnicode`'s AA branch). Oversampling supplies the
  quality. This is TTF-only (only atlas glyphs take the AA/coverage branch).
- **Atlas entry:** TTF glyphs always use the uncolored `GetEntry`
  (white×coverage, incl. baked black border/solid/italic). `GetColoredEntry` is
  no longer used for TTF. (`GetColoredEntry` stays for any non-TTF use; if it
  has no remaining callers after this change, remove it.)
- **Draw branch in `RenderedText.DrawGlyphs`:** for `IsAtlasFont(font)` glyphs,
  draw via `UltimaBatcher2D.DrawTextGlyph(texture, position, sourceRect,
  tintRgb, opacity, depth)` instead of `batcher.Draw(..., hueVector, ...)`:
  - `tintRgb` (`Vector3`, 0..1) = the glyph's color:
    - HTML per-char color present → that color's rgb directly.
    - else a UO `Hue` (index) present → full-intensity hue rgb via
      `HuesLoader` (the managed equivalent of the shader `get_rgb(1.0, hue)`;
      e.g. `HuesHelper.Color16To32(Hues.GetColor(HUES_COUNT-1 or max cell, Hue))`
      — the exact call to produce a full-brightness hue color is pinned in the
      plan).
    - else (no hue) → white `(1,1,1)`.
  - `opacity` = the existing per-draw `alpha`.
  - The `drawHue > 0` highlight override maps to a highlight tint rgb the same
    way (full-intensity of `drawHue`).
- **Remove the TTF `baseColor` bake:** the `GetPolygoneColor`→`GetColoredEntry`
  branch runs only for classic fonts now.
- **Classic fonts:** unchanged — same `baseColor`, `GetColoredEntry`/`GetEntry`,
  `batcher.Draw(..., hueVector, ...)`, mode NONE path.

### Data flow

```
TTF text (font 20-31)
  measure: GetInfoUnicode → _fontDataUNICODE[slot,c]   (unchanged)
  glyph:   TtfGlyphSource 3x raster → downsample → coverage
           → RenderSingleGlyphUnicode (no StrengthenCoverage) → GetEntry
             = premultiplied white×coverage (color-independent), cached once
  draw:    tintRgb = hue/html/white → DrawTextGlyph → TEXT_RGB (rgb*=tint)
           → premultiplied AlphaBlend → screen
```

## Removals

- `FontsLoader.CoverageSolidAt` and the thresholding in `StrengthenCoverage`
  (pass-through or remove; remove the call site in the AA branch if removed).
- The TTF path's use of `GetColoredEntry` and the `baseColor` bake in
  `DrawGlyphs`. `GetColoredEntry`/`RenderSingleGlyphUnicode`'s `charcolor`
  parameter may become dead for TTF — remove if no classic caller needs it
  (classic still uses baked hue via ASCII path / `baseColor`).

## Error handling

Unchanged: missing glyph → space/empty; corrupt TTF → slot disabled (sfnt
header guard). Downsampling of a 1×1-or-smaller glyph handled by the general
average (no special case needed); zero-area glyph → no pixels as today.

## Testing

Unit (`ClassicUO.UnitTests`, xUnit):
- **Downsample math:** a synthetic 3×3-per-target coverage buffer where each
  block is fully 255 downsamples to 255; a block half-covered averages to ~127;
  target dimensions = source/3 (rounded). (Test the downsample helper directly
  with a hand-built input, no GPU.)
- **Oversampled glyph quality:** `TtfGlyphSource.Create(Inter, 22).TryRasterize('M')`
  interior has fully-opaque (255) pixels and nonzero edge-gradient values
  (some 0 < cov < 255), confirming solid interior + AA (regression guard for
  washed infill).
- **Color-independent atlas:** the uncolored glyph bytes for a char do not
  depend on hue (registering + rendering a glyph produces identical coverage
  regardless of any color) — i.e. the TTF path never bakes color.
- **hue→rgb tint:** the managed full-intensity hue conversion returns the
  expected rgb for a known hue index.
- Existing `TtfGlyphSourceTests` / `AtlasFontRegistrationTests` still pass
  (including the space-width regression test).

Manual (user, in-client on Vulkan):
- Colored overhead names / speech and colored gump text in a TTF font render
  **solid** and in the **correct hue**, with an intact black border and smooth
  edges; white/default TTF text still correct; SK/CZ glyphs correct; classic
  fonts visually unchanged.

## Files touched

- **Modify:** `src/ClassicUO.Assets/TtfGlyphSource.cs` (3× rasterize +
  downsample; a testable `internal static` downsample helper),
  `src/ClassicUO.Assets/FontsLoader.cs` (drop `CoverageSolidAt`/threshold in
  `StrengthenCoverage`; AA branch no longer solidifies),
  `src/ClassicUO.Client/Game/GameObjects/RenderedText.cs` (TTF draw branch via
  `DrawTextGlyph` with tintRgb; drop TTF `baseColor` bake / `GetColoredEntry`
  use).
- **Possibly modify/remove:** `src/ClassicUO.Renderer/FontGlyphAtlas.cs`
  (`GetColoredEntry` if it becomes unused).
- **Tests:** add to `tests/ClassicUO.UnitTests/Assets/TtfGlyphSourceTests.cs`
  (downsample + interior-opacity), and a small tint-conversion test.

## Implementation order

1. Oversampled rasterization in `TtfGlyphSource` (3× + downsample helper) +
   unit tests (downsample math, interior opacity). No visible change yet
   (StrengthenCoverage still runs).
2. Remove the `CoverageSolidAt` solidify (StrengthenCoverage pass-through);
   confirm coverage tests / suite still green. (Now oversampling is the sole
   coverage-quality mechanism.)
3. TTF draw branch: grayscale `GetEntry` + `DrawTextGlyph` tint (hue/html/white)
   in `RenderedText.DrawGlyphs`; drop TTF `baseColor` bake. Build.
4. Remove now-dead `GetColoredEntry`/`charcolor` for TTF if unused. Build + full
   suite.
5. Manual in-client verification (user, Vulkan).

## Risk

Localized to TTF glyph generation and the TTF draw branch. Classic fonts,
measure/layout, caret, cropping, HTML layout, and hit-testing are untouched.
The tint path reuses the already-shipping login `DrawTextGlyph`/`TEXT_RGB`
mechanism, so the shader/blend behavior is proven.
