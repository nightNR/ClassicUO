# Asset Overrides — Design

**Date:** 2026-07-24
**Status:** Approved (design), pending spec review
**Branch (current):** `main`

## Goal

Let users replace built-in UO client assets with their own image/sound files
dropped into an `Overrides/` folder next to the client — matching the pattern
Orion offers. Drop `Overrides/Gump/1234.png` and gump id 1234 renders your PNG
instead of the MUL/UOP art. No asset repacking, no tools.

## Scope

All asset families, in one design (implemented in phases — see Implementation
Order):

| Family    | Built-in loader        | Renderer wrapper hook             | Difficulty |
| --------- | ---------------------- | --------------------------------- | ---------- |
| Gump      | `GumpsLoader`          | `Gump.GetGump` (Gump.cs:20)       | easy       |
| Art       | `ArtLoader` (static)   | `Art.Get`/`GetArt` (Art.cs:36)    | easy       |
| Land      | `ArtLoader` (land)     | `Art.GetLand` (Art.cs:30)         | easy       |
| Texmap    | `TexmapsLoader`        | `Texmap.GetTexmap` (Texmap.cs:20) | easy       |
| Light     | `LightsLoader`         | `Light.GetLight` (Light.cs:32)    | easy       |
| Animation | `AnimationsLoader`     | frame loop (Animation.cs:354-381) | hard       |
| Sound     | `SoundsLoader`         | `Sound.GetSound` (Sound.cs:29-36) | hard       |

Out of scope: hue/variation-specific overrides, hot reload / file watching,
per-asset scaling metadata beyond a center offset, MP3/OGG sound overrides.

## Discovery & Enablement

- **Root:** `Overrides/` resolved against `CUOEnviroment.ExecutablePath`
  (CUOEnviroment.cs:27) — the same base used for the existing `Data/` folder
  (e.g. `ProfileManager.cs:25`, `WorldMapGump.cs:48`).
- **Auto-enable:** if `Overrides/` exists, the system is active. A
  `settings.json` flag `enable_asset_overrides` (default `true`) can force it
  off. New key added to `Settings.cs` alongside existing keys.
- **Timing:** a single recursive scan at startup builds an in-memory index. PNG
  (and WAV) files are decoded lazily on first request, matching the existing
  lazy `SpriteInfo[]` caching. No `FileSystemWatcher`, no per-request disk
  probing.

## Folder Layout

```
Overrides/
  Gump/     <id>.png
  Art/      <id>.png            # static item art (natural static graphic id)
  Land/     <id>.png            # land tiles (natural land id)
  Texmap/   <id>.png
  Light/    <id>.png
  Anim/     <id>/<group>_<dir>_<frame>.png
  Sound/    <id>.wav
```

- **id parsing:** accept decimal (`1234`) OR `0x`-prefixed hex (`0x04D2`),
  case-insensitive. Non-parseable filenames are skipped with a warning log.
- **Art vs Land:** two folders keyed by the *natural* game id (the id a user
  sees in art tables), not the internal `+0x4000` static offset. `Art.GetArt`
  already adds `0x4000` internally and `Art.GetLand` does not — the override
  index is keyed the same way each wrapper keys its lookup, so the folder maps
  1:1 to the wrapper call.
- **Animation naming:** nested per body id. `<group>` = animation action/group
  index, `<dir>` = direction, `<frame>` = frame number (all decimal). A user
  overrides only the frames they supply; every other (group, dir, frame) falls
  back to the built-in frame. Filenames that don't match the
  `<group>_<dir>_<frame>` triplet are skipped with a warning.

### Optional center sidecar

Alongside any override image, an optional `<id>.json` (for single-image
families) or `<group>_<dir>_<frame>.json` (animations) may set the draw center:

```json
{ "centerX": 12, "centerY": 40 }
```

If absent, center is derived (see Center Derivation). Sidecar parsing uses the
JSON support already present; a malformed sidecar logs a warning and falls back
to derived center.

## Architecture

One new focused unit plus thin hooks in the existing renderer wrappers.

### `AssetOverrides` (new)

New file in `ClassicUO.Assets` (`AssetOverrides.cs`). One instance, owned by
`UOFileManager` (constructed at UOFileManager.cs:31-50, exposed as a public
prop like the loaders). Responsibilities:

- **Index build:** recursive scan of `Overrides/` at load time → nested
  dictionaries keyed by asset family, then by id (and by
  `(group,dir,frame)` for animations). Value = absolute file path (+ optional
  sidecar path). Pure function of a directory tree — unit-testable against a
  temp dir.
- **Lookup:** `bool TryGet(AssetKind kind, uint id, out string path, out string sidecar)`
  and an animation-specific overload keyed by `(id, group, dir, frame)`.
- **Decode helpers:**
  - `bool TryLoadPixels(string path, out uint[] pixels, out int w, out int h)`
    — PNG → BGRA/RGBA `uint[]`, reusing the `Texture2D.FromStream` + `GetData`
    pattern from `LoginAssets.cs:29-44`. (Decode needs a `GraphicsDevice`; the
    helper is handed the device the wrappers already hold.)
  - `bool TryLoadSidecarCenter(string path, out int cx, out int cy)`.
  - `bool TryLoadSoundPcm(string path, out byte[] pcm)` — parse WAV, extract the
    `data` chunk as raw PCM (see Sounds).

The manager does **no** GPU work and holds no textures; it produces pixel
buffers and metadata. Wrappers own the atlas upload and caching, unchanged.

### Wrapper hooks (existing files, minimal edits)

Each renderer wrapper's `GetX(id)` already: (1) checks its `SpriteInfo[]` cache
slot, (2) on miss calls the built-in loader for pixels, (3) uploads via
`TextureAtlas.AddSprite`, (4) stores the slot. The hook inserts one branch at
step 2:

```
if (overrides.TryGet(kind, id, out path, out sidecar)
    && overrides.TryLoadPixels(path, out px, out w, out h))
{
    center = TryLoadSidecarCenter(sidecar) ?? DeriveCenter(kind, w, h);
    spriteInfo.Texture = atlas.AddSprite(px, w, h, out spriteInfo.UV);
    spriteInfo.Center = center;
    picker.Set(id-key, w, h, px);   // keep hit-testing correct
    return spriteInfo;
}
// else: existing built-in path, untouched
```

`PixelPicker` (`_picker`, Gump.cs:10 / Art.cs:16) must be fed the override
pixels so mouse hit-testing (`PixelCheck`) matches what's drawn.

Wrappers touched: `Gump.cs`, `Art.cs`, `Texmap.cs`, `Light.cs`,
`Animations/Animation.cs`, `Sounds/Sound.cs`.

## Center Derivation

- **Gump, Texmap, Light:** center = `(0,0)` (built-in semantics; gumps draw from
  top-left).
- **Art (static), Animation frame:** derive from the override's own dimensions
  the way the loader does — the built-in art center is stored per frame
  (`frame.CenterX/Y`, Animation.cs:373). For a differently-sized override with
  no sidecar, derive `centerX = w/2`, `centerY = h` (art baseline convention);
  confirm exact formula against `ArtLoader` during planning.
- **Sidecar always wins** when present.

## Animations (hard path)

Injection at the per-frame loop, Animation.cs:354-381. For each frame being
built for `(id, action/group, dir, frame.Num)`:

- If an override triplet file exists, decode it and replace
  `frame.Pixels / Width / Height / CenterX / CenterY` before `AddSprite`
  (Animation.cs:375). Feed `_picker` (Animation.cs:371) the override pixels.
- If not, the built-in frame is used unchanged. Partial overrides work: any
  frame without a file keeps its MUL/UOP pixels.
- Key space matches Animation.cs:367-369 (`action | dir<<8 | uop<<16` upper,
  `id | frame<<16` lower) — the override lookup is by the human-facing
  `(id, group, dir, frame)`, resolved inside this loop where those values are
  already in scope.

## Sounds (hard path)

`Sound.GetSound` (Sound.cs:29-36) calls `SoundsLoader.TryGetSound`, which returns
**raw PCM** with the 40-byte name header stripped (SoundsLoader.cs:216), then
builds `new UOSound(name, index, data)`. UO sound is raw PCM (~22050 Hz, 16-bit,
mono).

Hook in `Sound.GetSound` before the `TryGetSound` call: if
`overrides.TryGet(Sound, id, ...)`, run `TryLoadSoundPcm(path)` to parse the
user WAV — strip the RIFF/`fmt `/`data` headers and hand the raw PCM bytes to
`new UOSound(name, index, pcm)`.

**Risk / planning item:** if the WAV's format (sample rate / bit depth /
channels) differs from what `UOSound` assumes, playback pitch/speed will be
wrong. Planning must decide between (a) documenting "WAV must be 22050 Hz 16-bit
mono" and validating the `fmt ` chunk (reject + warn on mismatch), or (b)
resampling. Recommendation: (a) for the first cut — validate and warn.

## Error Handling

- Missing/blank `Overrides/` → system inert, zero overhead beyond one dir-exists
  check.
- Unparseable filename, malformed sidecar, undecodable image, WAV format
  mismatch → skip that one override, log a warning, fall back to built-in. Never
  throw into the render path.
- A decoded override that fails `AddSprite` (e.g. exceeds the 4096² atlas) →
  warn, fall back to built-in.

## Testing

- **`AssetOverrides` index build** — unit test against a temp directory tree:
  correct family/id keying, decimal + hex parsing, animation triplet parsing,
  bad filenames skipped, nested anim folders.
- **id parsing** — decimal, `0x` hex, mixed case, invalid → skipped.
- **Sidecar parse** — valid, missing, malformed.
- **WAV PCM extraction** — well-formed WAV → correct PCM bytes/length; format
  mismatch flagged.
- Renderer-wrapper injection is covered by manual in-client verification (needs
  a GPU/atlas); keep the wrapper edits tiny so logic lives in the testable
  `AssetOverrides` unit.

## Implementation Order

1. `AssetOverrides` manager: index build + id parsing + PNG decode + sidecar.
   Wire into `UOFileManager`. Unit tests. (No visible effect yet.)
2. Gump override hook — smallest, most visible; validates the end-to-end path.
3. Art + Land + Texmap + Light hooks (same shape as Gump).
4. Animation hook (per-frame loop + partial fallback).
5. Sound hook + WAV PCM extraction + format validation.
6. `enable_asset_overrides` setting + docs (README section, folder layout,
   id-format + sidecar examples).

## New / Touched Files

- **New:** `src/ClassicUO.Assets/AssetOverrides.cs`,
  `tests/ClassicUO.UnitTests/AssetOverridesTests.cs`.
- **Touched:** `UOFileManager.cs` (own + load the manager), `Settings.cs`
  (flag), `Gump.cs`, `Art.cs`, `Texmap.cs`, `Light.cs`, `Animation.cs`,
  `Sound.cs` (hooks), plus a README/docs note.
