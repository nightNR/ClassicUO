# Custom Login Scene (1280×720) — Design

Date: 2026-07-16
Branch target: TBD (feat)
Depends on: `2026-07-16-autologin-reconnect-gump-design.md` (reuses `LoginReconnectPolicy`)

## Goal

Rebuild the login screen as a custom-art, 1280×720 scene matching the staged
"Dark Paradise" mockup: a fullscreen background image, a centered ornate
9-slice form, PNG-based multi-state buttons, PNG text inputs, and TrueType
(Cinzel / Cormorant Garamond) text. The scene is selected at **compile time** —
building with `-p:CustomLoginScene=true` compiles the custom scene; the default
build keeps the original UO login untouched.

The scene has two states, driven by the existing `LoginReconnectPolicy`:
- **State A (full login):** Account Name + Password inputs, LOGIN (priority red)
  + QUIT (dark) buttons.
- **State B (reconnect):** in autologin mode with saved credentials, the form is
  replaced by a centered priority-red RECONNECT button + QUIT and a status
  label. No inputs.

(The mockup shows both a form and a bottom RECONNECT row — that is a mockup
error. Real design: A **or** B, never both.)

## Staged assets (in `Assets/`)

- `background-image.png` (1672×941) — fullscreen background.
- `button-neutral/hover/down.png` (2172×724) — dark button states.
- `button-prio-neutral/hover/mdown.png` (2172×724) — priority (red) button states.
- `text-input-neutral/focus/disabled.png` (2172×724) — input field backgrounds.
- `form-border/` — 9-slice pieces: `frame_corner_{top,bottom}_{left,right}.png`,
  `frame_side_{left,right}_repeat.png`, `frame_{top,bottom}_repeat_slice.png`,
  `frame_{top,bottom}_center_ornament.png`, `frame_center_stretch.png`.
  (`form-border.png` and `frame_full_transparent.png` are full composites; the
  sliced pieces are the ones used.)
- `fonts/` — TTF families. Used: `Cinzel/static/Cinzel-SemiBold.ttf`,
  `Cormorant_Garamond/static/CormorantGaramond-SemiBold.ttf`. (Inter,
  Source_Sans_3 available, not required.)

Button/input PNGs are ~1.7× the target render size; they will be scaled down at
draw time. Consider downscaling the source art later (asset task, out of scope
here).

## Typography

- **Account/Password text + labels:** Cormorant Garamond SemiBold, ~18–22 px,
  color `#D3C2A1`, ~90% opacity.
- **Button labels:** Cinzel SemiBold, UPPERCASE, letter-spacing 2–4 px, 24–30 px.

## Technical capabilities (verified read-only)

- **PNG → Texture2D at runtime:** exists via `Texture2D.FromStream`
  (`GameController.cs:126-128`, `OptionsGump.cs:478-479`,
  `WorldMapGump.cs:1377`). Existing PNGs are embedded via the `FileEmbed` source
  generator (`Resources/Loader.cs:11-15`).
- **Drawing arbitrary Texture2D:** `UltimaBatcher2D.Draw` / `DrawTiled`
  overloads take a raw `Texture2D` (`Batcher2D.cs:479,586,597`). No ready-made
  "image control" exists — `GumpPic` is bound to UO gump ids; `Line`/`HitBox`
  hold only solid-color textures. A small new control is required.
- **9-slice:** `ResizePic.cs` (`DrawInternal` at `:263-397`, `GetTexture` at
  `:399-422`) is a full 9-slice over UO art; the draw logic is reusable with
  custom textures by swapping the `GetTexture` lookup.
- **TTF fonts:** **do not exist.** No FontStashSharp / FreeType / StbTrueType /
  font NuGet anywhere in the client. Only bitmap XNB `SpriteFont`
  (`Renderer/SpriteFont.cs`, `Renderer/Fonts.cs`) and UO `.mul` bitmap fonts
  (`FontsLoader`/`RenderedText`). Adding TTF requires a new dependency or
  pre-baking.
- **Login window size:** forced 640×480 at `LoginScene.cs:94-97` via
  `ScaleWithDpi` + `SetWindowSize` + `SDL_SetWindowMinimumSize`. DPI-scaled.
- **Compile-time gating:** csproj already uses `PropertyGroup Condition` +
  `DefineConstants` (`ClassicUO.Client.csproj:24,47-56`, BootstrapHostMode
  pattern). Same mechanism defines `CUSTOM_LOGIN_SCENE`.

## Architecture

### 1. Font rendering (FontStashSharp, with a bitmap-atlas fallback)

Primary approach: add the `FontStashSharp.FNA` NuGet. FontStashSharp rasterizes
TTF glyphs into a texture atlas at runtime.

Bridge concern: ClassicUO renders through a custom `UltimaBatcher2D`, not XNA
`SpriteBatch`. FontStashSharp draws via `IFontStashRenderer2` (+
`ITexture2DManager`). We implement those against the FNA `GraphicsDevice` /
`UltimaBatcher2D`, or draw FontStashSharp's atlas quads through the existing
batcher.

**AOT contingency (user-directed):** the default `cuo` artifact is NativeAOT
(`PublishAot=true`). The first task is a spike that validates FontStashSharp
rasterizing + drawing under a NativeAOT publish. **If FontStashSharp fails AOT**,
fall back to **pre-baking**: a build-time tool renders the required TTFs at the
required sizes into a bitmap glyph atlas (texture + glyph-metrics table), shipped
as embedded assets and drawn through the existing bitmap-`SpriteFont`-style path.
The rest of the design (controls, gump, scene) consumes a small font-drawing
abstraction so the backend can be swapped without touching call sites.

Abstraction: `ILoginFont` (or a thin `LoginText` helper) with
`Measure(text, size)` and `Draw(batcher, text, x, y, size, color, opacity,
letterSpacing)`. Both FontStashSharp and the baked-atlas backend implement it.

### 2. Asset loading (FileEmbed)

Add `[FileEmbed]` partial methods in `Resources/Loader.cs` for every PNG and the
two TTFs (paths relative to the Client project dir, e.g.
`..\..\Assets\background-image.png`). A new static `LoginAssets` holds
lazily-created `Texture2D` (via `FromStream`) and the font backend (`FontSystem`
or baked atlas). All under `#if CUSTOM_LOGIN_SCENE` so the default build embeds
nothing extra.

### 3. New UI controls (`Game/UI/Controls`, under `#if CUSTOM_LOGIN_SCENE`)

- **`TextureImage`** — draws an arbitrary `Texture2D` scaled to `Width×Height`
  via `batcher.Draw(texture, destRect, sourceRect, hueVector, depth)` with hue 0.
- **`NineSliceFrame`** — custom 9-slice from the `form-border/` pieces. Corners
  via `Draw`, edges + center via `DrawTiled`, mirroring `ResizePic.DrawInternal`
  but sourcing the custom textures. Configurable `Width×Height`.
- **`ImageButton`** — 3-state PNG button (neutral / hover / down) with a centered
  `ILoginFont` label. Two style presets: `Priority` (button-prio-*) and `Dark`
  (button-*). Fires an action id on click, same `OnButtonClick` contract as
  existing buttons.
- **`TtfLabel`** — static text via `ILoginFont` (size, color, opacity,
  letter-spacing).
- **`TtfTextBox`** — text input. Reuses `StbTextBox` editing/caret logic
  (internals: `InternalsVisibleTo` already exists for tests, but this is the same
  assembly). Overrides `Draw` to render: background = text-input PNG
  (neutral / focus by keyboard focus), typed text + caret via `ILoginFont`,
  masking for the password field.

### 4. `CustomLoginGump` (`Game/UI/Gumps/Login/CustomLoginGump.cs`, `#if`)

- Base `Gump(world, 0, 0)`, spans 1280×720, `CanCloseWithRightClick = false`.
- Fullscreen `TextureImage(background-image)` at 0,0 scaled to 1280×720.
- Centered `NineSliceFrame` for the form panel.
- Branch on `LoginReconnectPolicy.UseReconnectGump(AutoLogin, Username,
  Password, scene.ForceFullLogin)`:
  - **State A:** "Account Name" + "Password" `TtfLabel`s (Cormorant), two
    `TtfTextBox` (account, password-masked), LOGIN `ImageButton` (Priority) →
    `Connect(account, password)`, QUIT `ImageButton` (Dark) → `Client.Game.Exit()`.
    Pending `scene.PopupMessage` shown as an error `TtfLabel`.
  - **State B:** status `TtfLabel` (`Reconnecting as <username>` or
    `PopupMessage`), RECONNECT `ImageButton` (Priority) →
    `Connect(Username, Decrypt(Password))`, QUIT `ImageButton` (Dark).
- Exact coordinates derived from the mockup proportions (finalized during
  implementation; mockup is 1672×941, scale ×0.766 to 1280×720).

### 5. `LoginScene` changes (all `#if CUSTOM_LOGIN_SCENE`)

- `Load()`: window `ScaleWithDpi(1280)×ScaleWithDpi(720)`, min-size to match;
  add a single `CustomLoginGump` instead of `LoginBackground` + `LoginGump`.
- `GetGumpForStep()` `case Main`: return `CustomLoginGump`.
- `_forceFullLogin` / `ForceFullLogin` / `HandleErrorCode` fatal-auth routing:
  identical to the reconnect spec — shared, not duplicated.
- Server-selection / character-selection / loading gumps: **unchanged** for now
  (mockup covers login only; those screens reuse the same frame art later, out
  of scope here).

## Relationship to the reconnect spec

`LoginReconnectPolicy` (`UseReconnectGump`, `IsFatalAuthRejection`) is authored in
the reconnect plan and reused verbatim. The reconnect plan targets the **default**
UO login (`LoginGump` / `ReconnectGump`); this scene provides a **custom** login
under `#if CUSTOM_LOGIN_SCENE` with its own gump but the same policy + fatal-auth
fallback. If this custom scene lands first, `LoginReconnectPolicy` must be
implemented as its own task here (Task from the reconnect plan, Task 1).

## Units & boundaries

- `ILoginFont` — swappable font backend (FontStashSharp | baked atlas). Isolates
  the AOT risk to one seam.
- `LoginAssets` — embedded-asset access + texture/font lifetime. Testable for
  presence/lazy-init logic (not pixel output).
- `TextureImage` / `NineSliceFrame` / `ImageButton` / `TtfLabel` / `TtfTextBox` —
  self-contained controls, each drawing-only, depend on `LoginAssets` +
  `ILoginFont`. Not unit-tested (graphics); verified manually.
- `CustomLoginGump` — composition + state selection via `LoginReconnectPolicy`
  (pure, unit-tested).
- `LoginScene` `#if` block — window sizing + gump wiring.

## Error handling

- Fatal auth (bad password / blocked / invalid creds, `0x82` codes 0/2/3) →
  `ForceFullLogin`, stop retry, State A with error label. (Reconnect spec §3.)
- Non-fatal (in-use / network) → stays State B, retry loop unchanged.
- Missing embedded asset / font backend init failure → log + fall back to the
  original login gump path (do not crash the login screen).
- FontStashSharp AOT failure → resolved at build time by the baked-atlas backend
  (contingency above), not at runtime.

## Testing

- Unit: `LoginReconnectPolicy` truth table + fatal-auth classification (shared
  with reconnect plan).
- Unit: `LoginAssets` lazy-init / embed-presence logic (no graphics).
- Manual smoke (custom build, `-p:CustomLoginScene=true`):
  1. Fresh launch, no autologin → State A full form, 1280×720, correct fonts/art.
  2. LOGIN connects; QUIT exits.
  3. autologin + valid saved creds → State B centered RECONNECT; connects.
  4. Wrong saved password → State A with "Incorrect password".
  5. Account-in-use → State B keeps retrying.
  6. Default build (no flag) → original UO login unchanged.

## Out of scope

- Server-selection / character-selection / loading screens re-skin (later; reuse
  frame art).
- Downscaling / optimizing source art.
- Runtime theme toggle (gating is compile-time by decision).
- Legacy v1 plugin path.
- Localization of new static strings (use existing `ResGumps` where available).

## Open implementation risks (resolved during build, not blocking design)

1. FontStashSharp ↔ `UltimaBatcher2D` renderer bridge (Task 1 spike).
2. FontStashSharp NativeAOT compatibility → baked-atlas fallback if it fails.
3. `TtfTextBox` editing + TTF caret rendering (most complex control).
