# Visual FX: sprite filters + extended lighting/shadows (Phase 1)

Date: 2026-07-04
Branch base: `feat/new-features`
Status: approved design

## Goal

Improve perceived visual quality of ClassicUO's 2D sprite world **without
redrawing any art assets**, exposed strictly as opt-in Profile options (never
forced defaults). Two feature groups:

1. A selectable **sprite upscale filter** (dropdown of realtime GPU filters).
2. **Extended lighting/shadow** effects: blob shadows, colored dynamic point
   lights, soft light-buffer blur.

Out of scope for Phase 1 (deferred): directional object-cast shadows (Phase 2),
AI/super-resolution "HD asset pack" (later, separate discussion — blocked by the
runtime hue-recolor system that keys on grayscale `r==g==b`).

## Current state (verified)

- `src/ClassicUO.Renderer/Effects/XBREffect.cs`, `shaders/xBR.fx(.fxc)`, and
  `Profile.UseXBR` (default `true`) **all exist but are dead code** — `new
  XBREffect` is never called and `UseXBR` is never read anywhere. The world
  render target is composited to screen in `RenderTargets.Draw()` with a plain
  sampler, no post-process effect. Phase 1 must *wire up* a post pass, not just
  tune an existing one.
- Lighting already exists: `Light.mul` textures drawn additively into
  `LightRenderTarget` (`GameScene.PrepareLightsRendering`, ~`GameScene.cs:1063`),
  day/night via `_world.Light.IsometricLevel`, dark-nights, and directional
  terrain shading in-shader (`get_light`, `IsometricWorld.fx:60`). Per-light data
  is `LightData` (`GameScene.cs:451`), and the draw loop already passes a hue
  vector (color index / hued / lights modes).
- The isometric shader already has a `SHADOW` mode (`IsometricWorld.fx:158`):
  renders the sprite as flat black at 0.4 alpha. Blob shadows can reuse this.
- Shaders compile as SM3 (`fx_2_0` target, `vs_3_0`/`ps_3_0`) via `fxc` →
  MojoShader (`shaders/compile_shaders.bat`). Any new shader must fit the SM3
  instruction budget and run on DX11/OpenGL/Vulkan/browser backends.

## Architecture

Chosen approach: **post-process hook centralized in `RenderTargets`.** All final
compositing to the backbuffer already happens in `RenderTargets.Draw()`. Both the
world-upscale filter and the light-buffer blur belong there. `GameScene` reads
Profile and pushes configuration into `RenderTargets` through setters, mirroring
the existing `SetLightsConfiguration(...)` pattern — so the Renderer library stays
free of any Profile/Client dependency.

Rejected: doing filters in `GameScene.DrawWorld` — the world→screen blit lives in
`RenderTargets.Draw()`, so the filter must go there regardless; splitting it would
scatter the pipeline.

Feature ownership by layer:

| Feature | Where it lives | Data path |
| --- | --- | --- |
| Sprite filter dropdown | `RenderTargets` post pass + filter `Effect` classes | `GameScene` → `RenderTargets.SetSpriteFilter(Func<Effect>)` |
| Blob shadows | render-list draw pass (per entity view) | reuse `SHADOW` shader mode |
| Colored point lights | `LightData` + `PrepareLightsRendering` loop | extend struct + draw loop |
| Soft light blur | `RenderTargets` blur pass on `LightRenderTarget` | `GameScene` → `RenderTargets.SetLightBlur(...)` |

## Component 1 — Sprite filter dropdown

**What:** replace `Profile.UseXBR` (bool) with `Profile.SpriteFilter` enum:
`Off, xBR, xBRZ, ScaleFX, MMPX`. Migration: existing `UseXBR == true` → `xBR`,
`false` → `Off` (read old value once during profile load if present).

**How:**
- One `Effect` subclass per filter, each exposing `MatrixTransform` and
  `textureSize` parameters (xBR already has this shape). Wire up the existing
  `xBR` first (proves the hook), then add `xBRZ`, `ScaleFX`, `MMPX` `.fx`/`.fxc`
  pairs under `src/ClassicUO.Renderer/shaders/`, embedded via `FileEmbed` like
  `GetXBRShader()`.
- `RenderTargets` gains `SetSpriteFilter(Func<Effect> selector)` and applies the
  returned effect (or none) when drawing `WorldRenderTarget` in `Draw()`. Set
  `textureSize` from the world RT dimensions.
- Filter `Effect` instances are created lazily and cached (one per filter), owned
  by whatever owns the effects today (Client/GameController graphics init).
- OptionsGump: a combobox in a new "Visual FX" section bound to
  `Profile.SpriteFilter`.

**Risk / gate:** `ScaleFX` (and possibly `xBRZ`) are instruction-heavy. Each new
shader must compile under `ps_3_0` and be smoke-tested on GL and browser
backends. If one does not fit SM3 or fails on a backend, it is dropped from the
enum for Phase 1 (design does not block on all four shipping — xBR + at least one
new filter is the success bar).

## Component 2 — Blob shadows

**What:** a soft elliptical shadow rendered under mobiles (and optionally items),
opt-in. Fake contact shadow, not projected geometry.

**How:**
- New Profile: `bool BlobShadows` (default false) + `int BlobShadowOpacity`
  (maps to alpha) and `int BlobShadowSize` (scale). Optionally a separate
  `bool BlobShadowsItems` if item shadows prove noisy — default false.
- Drawn in the render-list pass into `WorldRenderTarget`, at the entity's ground
  depth so it sorts beneath the sprite and beneath other entities in front.
- Reuse the shader `SHADOW` mode (flat black + alpha) applied to a reusable soft
  radial-gradient texture (generated once, in code, or a small embedded asset),
  scaled to the entity footprint. No per-art work.
- Anchored at the mobile's feet, offset by facing where cheap; static/simple
  ellipse is acceptable for v1.

**Open detail to resolve in plan:** exact depth value so the blob sits above the
land tile but below the entity and is not double-drawn during multi-object
stacking. Reference existing `MobileView`/`ItemView` depth calc.

## Component 3 — Colored dynamic point lights

**What:** richer per-light appearance — color tint, radius scaling, and optional
flicker — layered on the existing additive light system. Backwards compatible:
defaults reproduce today's look.

**How:**
- Extend `LightData` (`GameScene.cs:451`) with `Color` (tint), `RadiusScale`,
  and `Flicker` (phase/intensity) fields.
- The draw loop in `PrepareLightsRendering` already emits a hue vector; extend it
  to modulate by per-light color and scale the drawn quad by `RadiusScale`.
  Flicker jitters intensity per frame with a cheap time-based noise.
- Profile: `bool EnhancedLights` (default false) master toggle, plus
  `int LightFlickerStrength`. When off, lights render exactly as today.
- Light sources (torches, spell effects) set the new fields where known;
  unknown/legacy lights keep neutral values.

## Component 4 — Soft light blur

**What:** blur the light buffer so light/dark transitions are gradients, not hard
pixel edges.

**How:**
- Add a separable gaussian blur `Effect` (new shader) run as a ping-pong pass on
  `LightRenderTarget` before it is composited in `RenderTargets.Draw()`.
- One extra transient RT of light-buffer size for the ping-pong; reused across
  frames, allocated in `RenderTargets.EnsureSizes`.
- `RenderTargets.SetLightBlur(bool enabled, float strength)`; `GameScene` feeds
  it from Profile.
- Profile: `bool SoftLights` (default false) + `int LightBlurStrength`.

## Options UI

All toggles/sliders/combobox live in a new **"Visual FX"** grouping in
`OptionsGump`, following the existing options patterns and `ResGumps` localized
strings (add new string entries; do not repurpose `UseXBREffectBETA`). Every
feature defaults to off / legacy behavior so no existing profile changes look
without opting in.

## Testing & verification

- Shaders compile via `compile_shaders.bat` (fxc) and are smoke-tested on at
  least GL + one other backend before a filter is offered in the enum.
- Visual before/after side-by-side per feature.
- Perf: watch `RENDER_FRAME_WORLD` % in `DebugGump` with each option on; document
  cost. Blur and heavy filters are the perf-sensitive ones.
- Graceful fallback: if a filter effect fails to load/compile at runtime, fall
  back to `Off` (plain blit) rather than crashing.
- Profile migration test: old `UseXBR` true/false maps to `xBR`/`Off`.

## Success criteria

- Sprite-filter dropdown works with xBR wired up plus at least one new filter.
- Blob shadows, colored/flickering lights, and soft light blur each toggle on/off
  cleanly and default off.
- No regression when all new options are off (byte-for-identical legacy path).
- No modification to UO data assets.

## Deferred (not this spec)

- **Phase 2:** directional object-cast shadows (needs isometric depth-sort work).
- **Later:** AI super-resolution HD asset pack — blocked by runtime hue recolor
  keying on grayscale `r==g==b`; would need luminance-only upscaling or a
  full-color-art-only scope plus a runtime asset-override layer and storage
  strategy. Separate spec when revisited.
