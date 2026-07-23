# TTF in-game fonts (pre-generated atlas) + rune-font removal — design

Date: 2026-07-23
Status: approved
Branch: explore/ingame-fonts

## Problem

Two related asks against the Options → Fonts font picker:

1. **Remove rune fonts.** The `FontSelector` in Options → Fonts lists every UO
   unicode font (`unifontN.mul`) present in the client data dir. Indices **7–12**
   render as decorative rune glyphs and are not useful as a readable UI font;
   hide them from the picker.
2. **Add the project's TTF fonts as selectable in-game fonts.** The custom login
   scene recently added four TTF families under `Assets/fonts/` — **Cinzel**,
   **Cormorant Garamond**, **Inter**, **Source Sans 3** — rendered via
   FontStashSharp (`ILoginFont`/`FontStashLoginFont`). Make these usable as the
   in-game font selected in Options → Fonts (chat/speech, tooltip, override-all).

## Background: the two text pipelines

- **UO bitmap/unicode pipeline** (all in-game text: chat, journal, tooltips,
  gump labels, `StbTextBox` editing). Text is addressed by a `byte font` index.
  Unicode fonts live in `ClassicUO.Assets/FontsLoader.cs`, loaded from
  `unifont0..19.mul` into `_unicodeFontAddress[20]`; glyphs are cached in
  `_fontDataUNICODE[20, 0x10000]` as `FontCharacterDataUnicode`.
  - Glyph data is **1 bit per pixel**: `Data` is a bitmask sized
    `(((Width-1)/8)+1) * Height`. The rasterizer expands it hard-edged — see
    `FontsLoader.cs:1992` (`byte cl = (byte)(@char.Data[scanLineOff + c] & (1 << (7 - j)))`)
    and the second draw at `FontsLoader.cs:3915`. When a bit is set the pixel is
    written fully opaque: `pData[block] = charcolor;` (`FontsLoader.cs:1997`).
    `charcolor` already carries the hue/color; black-border and solid are
    post-passes over the binary result.
  - `RenderedText` / `TextRenderer` drive layout (`GenerateUnicode`, width,
    word-wrap, caret) purely through `FontsLoader` glyph metrics.
- **FontStashSharp TTF pipeline** (`ClassicUO.Renderer/fonts/FontStashLoginFont.cs`,
  `ILoginFont`). Anti-aliased dynamic TTF rasterization, used only by the login
  scene today. `FontStashSharp 1.3.10` is referenced **only** by
  `ClassicUO.Renderer.csproj` (transitively bundling StbTrueTypeSharp +
  StbImageSharp). `ClassicUO.Assets` has no TTF rasterizer, and Renderer depends
  on Assets — so a TTF rasterizer cannot run inside `FontsLoader` without
  inverting the dependency.

## Approach (decided)

**Extend the UO unicode pipeline to optionally carry 8bpp coverage glyphs
("Approach A"), and feed the TTF fonts in as pre-generated, build-time-baked
atlases loaded into new synthetic font slots.**

Why: reuses `RenderedText`/`TextRenderer`/word-wrap/measurement/hue **unchanged**
(they operate on `FontsLoader` glyph metrics), keeps the TTF rasterizer out of
the runtime/Assets layer, and is additive — original UO fonts render exactly as
today.

Rejected: **B (separate FontStashSharp draw path for in-game text)** — would
require routing every text draw site by font and reimplementing word-wrap /
caret / hue / HTML for the TTF path (two text engines). **A0 (bake TTF to 1bpp)**
— zero pipeline change but no anti-aliasing, defeating the point of TTF.

### Backward-compatibility guarantee

The 8bpp path is a **per-glyph capability gated to the new TTF slots only**.
Original unicode fonts (slots 0–19) keep 1bpp `Data` and take the existing
rasterizer branch, byte-for-byte unchanged — no visual change, no perf change.
Only glyphs that carry a coverage buffer take the new alpha-blend branch.

## Decisions

| Decision | Value |
| --- | --- |
| Rendering approach | A — 8bpp coverage extension of the UO unicode pipeline |
| Atlas generation | Build-time; shipped as an asset. Assets stays TTF-free. |
| Rune fonts to hide | Unicode slots **7–12** (inclusive), hidden from the picker |
| TTF families | All four: Cinzel, Cormorant Garamond, Inter, Source Sans 3 |
| Sizes per family | Three: **14, 18, 22 px** → 4 × 3 = **12 new font slots** |
| Char coverage | **U+0020 – U+017F** (Basic Latin + Latin-1 Supplement + Latin Extended-A). Chosen to cover the full **Slovak and Czech** alphabets — dĺžeň/acute (á é í ó ú ý), vokáň/circumflex (ô), dvojbodka/diaeresis (ä ö ü), and mäkčeň/caron (č ď ě ľ ň ř š ť ž, plus ĺ ŕ ů) — all of which fall at or below U+017F (ž = U+017E). |
| Slot model | Extend `_unicodeFontAddress`/`_fontDataUNICODE` to `20 + 12`; replace the `font >= 20` magic guards with a `MAX_UNICODE_FONTS` constant |

## Components / phases

Each phase ends with an independently testable deliverable. Phase 0 is
independent of the rest and can merge on its own.

### Phase 0 — Hide rune fonts from the picker

`OptionsGump.FontSelector` (`OptionsGump.cs:5428`) currently adds a radio button
for every `i` where `UnicodeFontExists(i)`. Add a filter that skips the rune
range 7–12 so they never appear.

- The filter is on the **picker only**. A profile that already stored
  `ChatFont`/`TooltipFont` in 7–12 keeps rendering (the render path is
  untouched); it just cannot be re-selected. Acceptable — runes were never a
  sensible UI font.
- Define the rune range as a named constant (e.g. `RUNE_FONT_FIRST = 7`,
  `RUNE_FONT_LAST = 12`) in `FontSelector`, not bare literals.

### Phase 1 — 8bpp coverage in the glyph struct + rasterizer

- `FontCharacterDataUnicode` gains an optional coverage buffer and a flag, e.g.
  `byte[] Coverage` (8bpp, one byte per pixel, row-major `Width*Height`) and
  `bool IsAntiAliased` (true iff `Coverage != null`). Existing fields
  (`OffsetX/OffsetY/Width/Height/Data`) keep their meaning; `Data` stays null for
  TTF glyphs, `Coverage` stays null for original glyphs.
- Both unicode rasterization sites branch on `IsAntiAliased`:
  - **`GenerateUnicode` inner loop** (`FontsLoader.cs:1949-2001`): when
    `IsAntiAliased`, replace the 1bpp bit-test + `pData[block] = charcolor` with
    an alpha blend of `charcolor`'s RGB against the destination weighted by
    `Coverage[y*Width + x] / 255`. When not, run the existing bit-test path
    verbatim.
  - **Second draw path** (`FontsLoader.cs:3899-3920`): same branch.
- **Border / solid / outline post-passes** (which assume binary pixels, e.g.
  `isSolid`/`UOFONT_BLACK_BORDER` around `FontsLoader.cs:2003-2116`): for
  coverage glyphs, derive the edge from a coverage threshold (a pixel counts as
  "set" when `Coverage >= T`, T ≈ 128) so border/solid behave sensibly on
  anti-aliased edges. Original fonts keep the exact existing behavior.
- Measurement/layout (`GetWidthUnicode`, word-wrap, caret) is **unchanged**: it
  reads `Width/OffsetX/advance`, which TTF glyphs supply identically.

### Phase 2 — Build-time atlas baker + atlas format

- New tool project `tools/FontAtlasBaker` (console, may reference FontStashSharp /
  StbTrueTypeSharp — it is build-time only, never shipped in the client).
- For each (family, size) it rasterizes glyphs over U+0020–U+017F to 8bpp
  coverage and writes one atlas file. **Atlas format** (little-endian binary):
  - Header: magic, version, `pixelSize`, `ascent`, `descent`, `lineHeight`,
    `firstChar` (0x20), `lastChar` (0x17F), glyph count.
  - Per glyph, in code-point order: `offsetX (int8)`, `offsetY (int8)`,
    `width (uint8)`, `height (uint8)`, `advance (int16)`, then `width*height`
    coverage bytes.
  - Missing glyphs in the font are emitted with `width=height=0` (renders as
    blank, like an absent UO glyph). The baker **warns** (build log) for any code
    point in range that a given TTF lacks, so missing Slovak/Czech diacritics are
    caught at bake time rather than showing blank in-game. The four chosen
    families (Cinzel, Cormorant Garamond, Inter, Source Sans 3) all ship Latin
    Extended-A, so full SK/CZ coverage is expected.
- Output committed under `Assets/fonts/atlas/<family>-<size>.uofont` (or embedded
  via the existing `FileEmbed` mechanism if that is how login assets ship — match
  the login-scene precedent). An MSBuild target regenerates them when a TTF or
  the baker changes; committing the baked artifacts keeps normal builds from
  needing the baker to run.

### Phase 3 — Load baked atlases into synthetic slots

- Replace the literal `20` bound in `FontsLoader` (`UnicodeFontExists`, the many
  `font >= 20` guards enumerated across `FontsLoader.cs`) with a
  `const int MAX_UNICODE_FONTS`. Size `_unicodeFontAddress` and the
  `_fontDataUNICODE` first dimension to `MAX_UNICODE_FONTS = 20 + 12 = 32`.
- After the existing `unifontN.mul` load loop, load the 12 baked atlases into
  slots 20–31. A new `LoadAtlasFont(slot, path)` parses the atlas header and
  eagerly (or lazily, mirroring `LoadChar`) fills `_fontDataUNICODE[slot, c]`
  with `Coverage`-backed `FontCharacterDataUnicode` for each baked code point.
- `UnicodeFontExists(slot)` returns true for a populated atlas slot. A small
  registry maps slot → display name ("Cinzel 18") and family/size for the picker.

### Phase 4 — Expose in the picker + persist

- `FontSelector` lists: original unicode fonts (minus runes 7–12, Phase 0) **plus**
  the 12 TTF slots, each labelled from the Phase 3 registry. The existing
  `max_font` cap widens to `MAX_UNICODE_FONTS`.
- `ChatFont`, `TooltipFont`, and `OverrideAllFonts`/`OverrideAllFontsIsUnicode`
  already store a `byte`/int index and persist through `Profile`; a TTF slot is
  just a higher index — no schema change. Confirm `OverrideAllFonts` (which
  forces a single font everywhere) works with a TTF slot end to end.

### Phase 5 — Verify in the running client

Manual verification (needs UO data dir + login): rune fonts gone from the picker;
each TTF family/size selectable and rendering anti-aliased in chat/journal/
tooltips; hue/color still applies; black-border/solid text still legible;
override-all-fonts works; original UO fonts visually unchanged. **Type/observe a
Slovak+Czech pangram covering á ä č ď ě é í ĺ ľ ň ó ô ŕ ř š ť ú ů ý ž (and
uppercase) in each TTF family and confirm every diacritic renders, none blank.**

## Risks / edge handling

- **Border/solid on AA edges** (Phase 1): the threshold approach may look slightly
  different from bitmap fonts on outlines. Verify in Phase 5; tune T if needed.
- **Atlas size**: 12 slots × ~350 glyphs × up to ~22×22 coverage bytes is a few
  MB uncompressed. If size matters, the atlas format can gzip the coverage blocks
  (out of scope unless Phase 5 flags it).
- **Slot-count guards**: every `font >= 20` in `FontsLoader.cs` must move to
  `MAX_UNICODE_FONTS`; a missed one would reject the new slots. Enumerate them all
  in Phase 3 (grep `>= 20` and `< 20` in `FontsLoader.cs`).
- **NativeAOT**: the client is AOT; the baker is a separate build-time tool, so
  its FontStashSharp/Stb dependency never enters the AOT image. Confirm the
  MSBuild wiring runs the baker with the SDK host, not under AOT.
- **Non-baked characters** (outside U+0020–U+017F, e.g. Cyrillic): a TTF slot
  renders them blank. Acceptable for the initial families/range; document it.

## Testing

Pure/unit-testable pieces:
- Phase 0: a pure `IsRuneFont(index)` / picker-inclusion predicate — unit test the
  7–12 exclusion and that 0–6 and ≥13 are included.
- Phase 1: an alpha-blend helper `BlendCoverage(dstRgba, srcColor, coverage)` and a
  coverage→edge threshold helper — unit test blend endpoints (coverage 0 → dst,
  255 → src, mid → interpolated) and the threshold.
- Phase 2: atlas round-trip — bake a tiny synthetic font (or a fixture) and assert
  the reader reproduces header + a known glyph's metrics/coverage.
- Phase 3: `MAX_UNICODE_FONTS` guard coverage — a test that `UnicodeFontExists`
  and the metric methods accept an index in 20–31 without throwing.

Rasterizer-integration and picker wiring are verified in the running client
(Phase 5), matching the codebase convention that gump/GPU-touching text code is
not unit-tested.

## Out of scope

- Per-draw dynamic TTF sizing (would require threading a size param through the
  whole unicode render path); sizes are fixed per slot instead.
- Additional scripts/code points beyond Latin Extended-A.
- Replacing the login scene's FontStashSharp path (unaffected).
