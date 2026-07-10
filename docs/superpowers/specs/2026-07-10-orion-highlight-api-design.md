# Plugin-driven object/area highlighting (OrionUO parity) — design

**Date:** 2026-07-10
**Status:** Approved (brainstorm), pending implementation plan
**Branch:** `feat/new-features`

## Summary

Expose OrionUO's `AddHighlightArea` / `AddHighlightCharacter` family through the
plugin v2 API (`ClassicUO.PluginApi`) as a new `IHighlight` context surface.
A plugin can:

- Tint a specific mobile serial with a hue, with a priority flag that decides
  whether it wins over the client's own status coloring (poison/paralyze/
  attacked/notoriety) or loses to it.
- Paint a world-space area (fixed position, following the mouse, or pinned to
  a serial) that tints every matching object type (mobile/item/corpse/land/
  static/multi) inside it, for a duration or indefinitely.

This must stay cheap even with thousands of registered characters/areas —
cost has to scale with what's **on screen**, not with how much a plugin has
registered.

## Background — what already exists

- **Per-object hue override chain** — `Mobile.Draw` (`MobileView.cs:26-151`)
  computes `overridedHue`/`hueVec` through an ordered if/else chain: selected
  object → out-of-range → dead/blackout → hidden → dead-non-human → poison/
  paralyze/invul → attacked/under-mouse. This is the exact chain the new
  character/area (mobile) highlight slots into.
- **Mesh-rendered land/statics** — `RenderLists` builds a GPU instance mesh for
  on-screen land/statics (`RenderLists.cs:199-244`) which has no per-instance
  hue; only the CPU per-object `Draw()` path (used by mobiles/items/effects,
  `RenderLists.cs:278`) can inject a hue.
- **Chunk-based spatial index** — `Map`/`Chunk` (`Chunk.cs:15-54`) already
  partitions the world into 8×8 chunks with per-tile linked lists;
  `GameSceneDrawingSorting` tracks the current `_visibleChunks`
  (`GameSceneDrawingSorting.cs:50-75`). Reuse chunk coordinates as the bucket
  key for the area spatial index rather than inventing a new grid.
- **Existing "plugin paints a serial" precedent** — `PluginStatusOverlays`
  (`Game/Managers/PluginStatusBars.cs:14-55`) is a static
  `Dictionary<uint, OverlayHues>` keyed by serial, written by the plugin host,
  read by `HealthBarGump` at draw time. No per-frame scan, no `World`
  instance coupling, no auto-clear on disconnect (plugin owns the lifecycle
  and re-applies after relog). **This is the pattern to copy for character
  highlighting** — same shape, different read site (`MobileView.Draw`
  instead of `HealthBarGump`).
- **ABI wiring precedent** — `IStatusBars`/`IScreenTimers` (PluginApi) →
  `*Impl` classes in `PluginContextImpl.cs` (thread-check +
  `_bridge.PostToGameThread` when off the game thread, e.g.
  `StatusBarsImpl.SetOverlay`, `PluginContextImpl.cs:249`) → function pointer
  in `HostBridge.ClientBindings` (`HostBridge.cs:359-361`) → static delegate
  target in `PluginHost.cs` (`PluginHost.cs:282-292`) → static manager method
  in `Game/Managers/`. The new `IHighlight` follows this chain exactly.
- **Hue type** — client-wide convention is `ushort` hue-table index (0 = none),
  not RGB (`GameObject.cs:72`, `ShaderHueTranslator.cs`). Plugin API takes
  `ushort` hues, matching `IStatusBars.SetOverlay`/`IScreenTimers.TimerConfig.Hue`.

## Decisions (from brainstorm)

| Question | Decision |
|---|---|
| Plugin entry point | New `IHighlight` on `IPluginContext`, not a generic string/JSON command bus. Strongly typed. |
| Color parameter type | `ushort` hue-table index (matches existing convention), not RGB. |
| Area snap modes | All three: `Mouse`, `Position(x,y)`, `Serial` (follows a mobile/item/multi). |
| Area object-type filter | Full parity: `Mobile \| Item \| Corpse \| Land \| Static \| Multi \| All` flags enum. |
| Color resolution when character-highlight and status-hue both apply | OrionUO semantics: `priorityHighlight=true` always wins; `priorityHighlight=false` loses to an active status hue (poison/paralyze/invul/attacked/notoriety) but wins over the plain default hue. |
| Overlapping area highlights on the same tile | Last-added wins (insertion order), no extra priority parameter. |
| Land/static/multi rendering | **Direct mesh hue injection, not an overlay pass.** The mesh path already recomputes a per-instance hue every visible frame (`GameSceneDrawingSorting.ApplyMeshHue` → `MeshLayer.SetHue`); the area lookup slots into that existing call, same as the non-meshed `Draw()` hue chains. No extra draw call, no mesh exclusion. (Corrected during planning — the mesh already supports per-instance hue; the brainstorm's "GPU mesh can't be tinted" premise was inaccurate.) |
| Area membership test | **Linear scan over active areas** per queried tile/object (bounded by concurrently active area count — realistically tens), not a chunk-keyed spatial index. On-screen tile count is itself capped (~24×24), so `areas × visible-tiles` stays cheap without extra indexing machinery. (Simplified during planning.) |
| Character-highlight lifecycle | No timer (matches OrionUO — only `AddHighlightArea` takes a duration). Plugin owns add/remove/clear, mirroring the existing `PluginStatusOverlays` convention (no auto-clear on disconnect). |

## Design

### 1. Public API (`ClassicUO.PluginApi`)

New file `IHighlight.cs`:

```csharp
public interface IHighlight
{
    void AddArea(string id, HighlightAreaOptions options = default);
    void RemoveArea(string id);
    void ClearAreas();
    int GetAreaTimer(string id);   // remaining ms; 0 if id doesn't exist / expired

    void AddCharacter(uint serial, ushort hue, bool priorityHighlight = false);
    void RemoveCharacter(uint serial, bool priorityHighlight = false);
    void ClearCharacters(bool priorityHighlight = false);
}

public enum HighlightSnap { Mouse, Position, Serial }

[Flags]
public enum HighlightObjectTypes
{
    None   = 0,
    Mobile = 1 << 0,
    Item   = 1 << 1,
    Corpse = 1 << 2,
    Land   = 1 << 3,
    Static = 1 << 4,
    Multi  = 1 << 5,
    All    = Mobile | Item | Corpse | Land | Static | Multi
}

public readonly struct HighlightAreaOptions
{
    public int DurationMs { get; init; }                 // default -1 (never expires)
    public HighlightSnap Snap { get; init; }              // default Mouse
    public uint AnchorSerial { get; init; }               // used when Snap = Serial
    public int X { get; init; }                           // used when Snap = Position
    public int Y { get; init; }
    public ushort Hue { get; init; }                      // default 0x0386
    public int RangeX { get; init; }                      // default 3
    public int RangeY { get; init; }                      // default 3
    public HighlightObjectTypes ObjectTypes { get; init; } // default All
}
```

`IPluginContext` gains `IHighlight Highlight { get; }` next to `StatusBars`/
`ScreenTimers`.

### 2. Client-side stores (`Game/Managers/PluginHighlights.cs`)

Two independent static stores, mirroring `PluginStatusOverlays`'s shape —
not `World`-instance state, so the read sites (draw calls) don't need a
`World` reference beyond what they already have:

**`PluginHighlightCharacters`** — two `Dictionary<uint, ushort>` (priority
tier, normal tier). `Set`/`Clear`/`Reset` mirror `PluginStatusOverlays`
exactly. O(1) lookup, read once per visible mobile in `MobileView.Draw`; cost
is bounded by what's drawn, not by how many entries are registered.

**`PluginHighlightAreas`** — `Dictionary<string id, AreaEntry>` holding
snap kind/anchor, hue, range, object-type flags, and `expireAtTicks` (`-1` =
never). No spatial index: a once-per-frame pass (driven from the same place
`PluginTimersManager.Update` is called) recomputes `Mouse`/`Serial` snap
centers and drops expired areas — O(active areas). Draw-time membership test
for a tile/object is a **linear scan over the active-area dictionary**,
testing its resolved center ± range against the queried (x, y). On-screen
tile/object count is itself capped (isometric view is ~24×24 tiles), and
active area count is realistically tens, so this stays cheap without a
spatial index.

### 3. Read sites / hue resolution

**Mobile** (`MobileView.Draw`, `MobileView.cs:73-151`) — insert the
character/area lookup into the existing `overridedHue` chain in this order
(first match wins):

1. `PluginHighlightCharacters` priority tier → `overridedHue = hue` (full
   swap, `hueVec.Y` stays 0), skip the rest of the chain.
2. *(existing chain: selected/out-of-range/dead/hidden/poison/paralyze/
   invul/attacked — unchanged)*
3. If none of the existing chain set an override → `PluginHighlightCharacters`
   normal tier.
4. Else → `PluginHighlightAreas` lookup for this mobile's tile, filtered to
   `ObjectTypes.Mobile`, last-added-wins on tie.

**Item / Corpse** (`ItemView.Draw`) — same as steps 3-4 above but without a
character-highlight concept (items aren't addressable via
`AddHighlightCharacter`): area lookup only, filtered to `Item`/`Corpse`.

**Land / Static / Multi** — two call sites, both already recompute a hue every
visible frame, so the area lookup slots into each directly (no new draw call):

- **Meshed objects** (`obj.InChunkMesh == true`, the common case — baked map
  terrain/furniture) never call `Draw()`; their hue is set once per visible
  frame by `GameSceneDrawingSorting.ApplyMeshHue` (`GameSceneDrawingSorting.cs:644-686`)
  into `MeshLayer.SetHue(obj.MeshSpriteIndex, hueX, hueY)`. The area lookup is
  inserted into `ApplyMeshHue` alongside its existing out-of-range/dead-world
  hue overrides.
- **Non-meshed objects** (dynamic ground items masquerading as statics,
  animated/light-emitting statics, excluded tiles) go through the ordinary
  per-object `Draw()` hue chain in `LandView.cs`/`StaticView.cs`/`MultiView.cs`
  — same injection point pattern as `MobileView`/`ItemView`.

### 4. ABI wiring

Follows the `IStatusBars` chain exactly:

- `IPluginApi/IHighlight.cs` (new) — interface + enums/struct above.
- `PluginContextImpl.cs` — new `HighlightImpl : IHighlight`, same
  thread-check + `PostToGameThread` pattern as `StatusBarsImpl`.
- `HostBridge.cs` — new function pointers in `ClientBindings`
  (`AddAreaFn`, `RemoveAreaFn`, `ClearAreasFn`, `GetAreaTimerFn`,
  `AddCharacterFn`, `RemoveCharacterFn`, `ClearCharactersFn`), flat-scalar
  signatures (strings marshaled as UTF-8 `nint`, matching existing
  `TimerConfig` label handling).
- `PluginHost.cs` — static delegate fields bound to
  `Game.Managers.PluginHighlights.*` static methods (mirrors
  `_openStatusBar`/`_setOverlay`, `PluginHost.cs:282-292`).
- `Game/Managers/PluginHighlights.cs` (new) — the two stores from §2 plus the
  static methods the delegates bind to (`AddArea`, `RemoveArea`, `ClearAreas`,
  `GetAreaTimer`, `AddCharacter`, `RemoveCharacter`, `ClearCharacters`), and
  a `Tick()` entry point wired into the existing per-frame update (called
  from the same place `PluginTimersManager.Update` is called from
  `World.Update`).

### 5. Testing

Unit tests (`tests/ClassicUO.UnitTests`), pure logic, no render:

- `PluginHighlightCharacters`: set/clear per tier, priority tier shadows
  normal tier, `Reset()` clears both.
- `PluginHighlightAreas`: add/remove/clear-all; `GetAreaTimer` remaining-ms
  math and the `0` cases (unknown id, expired); membership test against
  resolved `Position`/`Serial` centers and range; last-added-wins when two
  areas overlap the same tile; object-type flag filtering.
- Priority-resolution order (§3 step order) as a table-driven test against a
  small fake "current hue chain state" input.

**Manual in-game verification** (render path, not automatable): area follows
mouse/serial/fixed position correctly; land/static/multi show a translucent
tint without corrupting the mesh batch elsewhere on screen; character
highlight full-swaps body hue and correctly loses to an active poison tint
when `priorityHighlight=false`.

### Files touched

| File | Change |
|---|---|
| `PluginApi/IHighlight.cs` | new — interface, `HighlightSnap`, `HighlightObjectTypes`, `HighlightAreaOptions` |
| `PluginApi/IPluginContext.cs` | add `IHighlight Highlight { get; }` |
| `BootstrapHost/PluginContextImpl.cs` | new `HighlightImpl` |
| `BootstrapHost/HostBridge.cs` | new function pointers in `ClientBindings` |
| `ClassicUO.Client/PluginHost.cs` | bind delegates to `PluginHighlights` statics |
| `ClassicUO.Client/Game/Managers/PluginHighlights.cs` | new — `PluginHighlightCharacters`, `PluginHighlightAreas`, `Tick()` |
| `Game/GameObjects/Views/MobileView.cs` | insert highlight lookup into `overridedHue` chain |
| `Game/GameObjects/Views/ItemView.cs` | area lookup for Item/Corpse |
| `Game/GameObjects/Views/LandView.cs`, `StaticView.cs`, `MultiView.cs` | area lookup in each `Draw()` hue chain (non-meshed path) |
| `Game/Scenes/GameSceneDrawingSorting.cs` | area lookup inside `ApplyMeshHue` (meshed path) |
| `Game/World.cs:287` | also call `PluginHighlights.Update(Time.Ticks)` next to `PluginTimersManager.Update` |
| `PluginApi/README.md` | document `IHighlight` |
| `tests/ClassicUO.UnitTests/.../PluginHighlightsTests.cs` | cases in §5 |

## Out of scope

- No new custom in-game UI to create highlights — plugin-only trigger, same
  as status bars/screen timers.
- No server-packet-driven highlighting.
- No persistence across sessions — plugin re-applies after reload/relog
  (matches `PluginStatusOverlays` convention).
- No explicit priority/z-order parameter for overlapping areas beyond
  insertion order.
- No removal-reason event for areas losing their `Serial` anchor (OrionUO has
  no such event either); the area is simply auto-removed.
