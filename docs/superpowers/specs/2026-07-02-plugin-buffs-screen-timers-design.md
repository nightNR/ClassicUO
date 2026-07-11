# Plugin Buffs & Screen Timers — Design

**Date:** 2026-07-02
**Status:** Approved (design)
**Branch:** feat/new-features

## Overview

Two plugin-facing features exposed through the v2 plugin contract
(`ClassicUO.PluginApi` → `ClassicUO.BootstrapHost` → `cuo`):

- **Feature A — Plugin buffs:** let a plugin add/update/remove buff icons in the
  existing `BuffGump` so they render alongside server buffs. Each plugin buff
  carries an image (gump graphic), a timer, and a buff/debuff kind.
- **Feature B — Screen timers:** a plugin-driven on-screen overlay of timers.
  Each timer has an id, a shape (circle / bar / numeric), a duration, a color
  (UO hue), an absolute screen position, and a placement mode (lone or
  stacking). Stacking timers share an explicit group and lay out in a
  configured direction. Setting an existing id updates the running timer.
  On expiry (and on removal) an event is dispatched back to the plugin.

Both features expose their own PluginApi surface (`IPluginBuffs`,
`IScreenTimers`), mirroring the existing `IStatusBars` pattern.

## Cross-boundary constraint (must-hold invariant)

The plugin → cuo call path crosses a native function-pointer boundary
(`delegate* unmanaged[Cdecl]`). cuo is NativeAOT and **never** sees a managed
class/struct across that boundary. Only scalars cross: primitives, plus strings
marshaled as a `nint` pointer (pattern: `SetWindowTitleFn`, `GetClilocFn`).

Config classes (`BuffConfig`, `TimerConfig`, `TimerGroupConfig`) live in
`ClassicUO.PluginApi` — the shared, reflection-free contract referenced by both
plugin and host. They are managed convenience on the plugin↔host side only. The
host `*Impl` flattens each config into scalar arguments before the native call.

```
plugin  --TimerConfig-->  IScreenTimers (PluginApi)
host ScreenTimersImpl  -- flatten to scalars -->  AddTimerFn(int, int, ...)  --> cuo
```

Event direction (cuo → plugin) uses the reverse channel: cuo invokes a stored
`[UnmanagedCallersOnly]` callback (pattern: `ConnectedFn`, `WalkProgressFn` in
`HostBridge.cs`); the host fans out to plugins via `RaiseEachPlugin`.

New function-pointer fields are appended to the **end** of the shared
`CuoHostExportedFunctions` / `HostBindings` / `ClientBindings` structs to keep
ABI compatibility with both hosts. The legacy net472 `ClassicUO.Bootstrap`
(v1) host does not set the new event callbacks; cuo null-checks every event
pointer before invoking.

## Threading & lifecycle

- All plugin → cuo calls auto-marshal to the game thread (pattern: `IStatusBars`
  impls posting via `_bridge.PostToGameThread` when off-thread).
- No persistence. The plugin owns lifecycle; after a relog it re-creates its
  buffs/timers. Server buffs are cleared on relog as today.
- A single central tick, `PluginTimersManager.Update()`, is called each frame
  from `World.Update`. It handles expiry for **both** plugin buffs and screen
  timers independently of whether any gump is open, and dispatches events.

---

## Feature A — Plugin buffs

### Data model

- `Game/Data/BuffIcon.cs`: add `BuffDisplayKind Kind` (enum `None` / `Buff` /
  `Debuff`), default `None`. The server path (`PlayerMobile.AddBuff`) leaves it
  `None`, so server buffs get no tint and behave exactly as today.
- Plugin buffs live in a separate collection — new static
  `Game/Managers/PluginBuffs.cs` (pattern: `PluginStatusOverlays`):
  `Dictionary<int, PluginBuffEntry>` keyed by the plugin's int id.

`PluginBuffEntry`: `Id (int)`, `Graphic (ushort)`, `ExpiryTicks (long)`,
`Kind (BuffDisplayKind)`, `Text (string)`.

`DurationMs == 0` means infinite (no expiry), consistent with the server
convention (`Timer == 0xFFFF_FFFF`).

### Render

- `BuffGump.BuildGump` iterates **both** `World.Player.BuffIcons` (server) and
  `PluginBuffs` (plugin), producing one list of icon entries.
- `BuffControlEntry` is generalized to accept a small input carrying `Graphic`,
  expiry, `Text`, `Kind`, and (for plugin buffs) the plugin `Id`, instead of
  taking a `BuffIcon` directly.
- In `AddToRenderLists`: `Debuff` → red frame/tint, `Buff` → green frame/tint,
  `None` → current appearance unchanged.

### Lifecycle & expiry

- `PluginTimersManager.Update()` checks each plugin buff; on elapsed expiry it
  removes the entry, dispatches `BuffEvent(id, reason=Expired)`, and calls
  `RequestUpdateContents()` on the open `BuffGump` if present.
- `Remove(id)` from the plugin → `reason=RemovedByPlugin`.
- User-driven removal (e.g. closing/clearing the buff gump) →
  `reason=RemovedByUser`.

### Plugin API

`ClassicUO.PluginApi/IPluginBuffs.cs`:

```csharp
public sealed class BuffConfig {
    public int Id { get; init; }              // required
    public ushort Graphic { get; init; }      // required
    public int DurationMs { get; init; }      // 0 = infinite
    public BuffDisplayKind Kind { get; init; }
    public string? Text { get; init; }
}

public enum BuffDisplayKind { None, Buff, Debuff }
public enum BuffRemoveReason { Expired, RemovedByPlugin, RemovedByUser }

public interface IPluginBuffs {
    void AddOrUpdate(BuffConfig config);      // same id updates in place
    void Remove(int id);
    void ClearAll();
    event Action<int> Expired;                // id
    event Action<int, BuffRemoveReason> Removed;
}
```

---

## Feature B — Screen timers

### Data model

New static `Game/Managers/ScreenTimers.cs` (pattern: `PluginStatusOverlays`):
`Dictionary<int, ScreenTimerEntry>` keyed by plugin int id, plus a
`Dictionary<int, GroupEntry>` for groups.

`ScreenTimerEntry`:
- `Id (int)`
- `Shape` — enum `Circle` / `Bar` / `Numeric`
- `StartTicks (long)`, `DurationMs (int)` → `remaining = Duration - (Now - Start)`
- `Hue (ushort)`
- `GroupId (int)` — `0` = lone
- `X, Y (int)` — used only when `GroupId == 0`
- `Width, Height (int)` — `0` = default per shape
- `Label (string?)`, `ShowTime (bool)`

`GroupEntry`: `GroupId`, `X`, `Y`, `Direction (StackDirection)`, `Gap`.

### AddOrUpdate semantics

If `Id` already exists, all fields are updated and the timer is **restarted**
with the new `DurationMs` (repeated set = update of the running timer). A new
`Id` is added. Insertion order within a group determines stack index.

### Render

A single overlay gump `Game/UI/Gumps/ScreenTimersGump.cs` is added to
`UIManager` at the top layer, with `AcceptMouseInput = false` and non-draggable.
Each frame it iterates `ScreenTimers` and draws each entry via `RenderLists`
(pattern: `BuffControlEntry.AddToRenderLists`):

- `Circle` — radial fill (depleting arc/ring) by remaining %.
- `Bar` — rectangle filled proportionally to remaining %.
- `Numeric` — remaining-seconds text.
- Optional `Label` (if set) and time (if `ShowTime`) drawn near the shape. Color
  = `Hue`.

Sizes default per shape when `Width`/`Height` are `0`; otherwise plugin-supplied.

### Stacking layout

Grouping is by explicit `GroupId` (not pixel match). Direction and anchor are
defined **per group** via `DefineGroup`:

- Anchor = the group's `X, Y`.
- Each member is offset by `index * (extent + Gap)` in `Direction`
  (`Down` / `Up` / `Right` / `Left`), where `index` is insertion order within
  the group and `extent` is the member's height (vertical dirs) or width
  (horizontal dirs).
- `Lone` timers (`GroupId == 0`) draw at their own `X, Y`, ignoring groups.

The layout computation is a pure function (position from group anchor +
direction + gap + index + extents), unit-testable without rendering.

### Lifecycle & expiry

The same `PluginTimersManager.Update()` removes an entry on elapse and
dispatches `TimerEvent(id, reason=Expired)`. `Remove(id)` / `RemoveGroup` /
`ClearAll` → `RemovedByPlugin`.

### Plugin API

`ClassicUO.PluginApi/IScreenTimers.cs`:

```csharp
public enum TimerShape { Circle, Bar, Numeric }
public enum StackDirection { Down, Up, Right, Left }
public enum PlacementMode { Lone, Stacking }   // reserved; GroupId==0 implies Lone
public enum TimerRemoveReason { Expired, RemovedByPlugin, RemovedByUser }

public sealed class TimerConfig {
    public int Id { get; init; }              // required
    public TimerShape Shape { get; init; }    // required
    public int DurationMs { get; init; }      // required
    public ushort Hue { get; init; }
    public int GroupId { get; init; }         // 0 = lone
    public int X { get; init; }               // used only when GroupId == 0
    public int Y { get; init; }
    public int Width { get; init; }           // 0 = default per shape
    public int Height { get; init; }
    public string? Label { get; init; }
    public bool ShowTime { get; init; }
}

public sealed class TimerGroupConfig {
    public int GroupId { get; init; }         // required
    public int X { get; init; }
    public int Y { get; init; }
    public StackDirection Direction { get; init; }
    public int Gap { get; init; }
}

public interface IScreenTimers {
    void DefineGroup(TimerGroupConfig group);
    void AddOrUpdate(TimerConfig timer);      // same id restarts running timer
    void Remove(int id);
    void RemoveGroup(int groupId);
    void ClearAll();
    event Action<int> Expired;                // id
    event Action<int, TimerRemoveReason> Removed;
}
```

---

## Plumbing (files to touch)

### `ClassicUO.PluginApi` (contract, new)

- `IPluginBuffs.cs`, `IScreenTimers.cs`
- `BuffConfig`, `TimerConfig`, `TimerGroupConfig`
- enums: `BuffDisplayKind`, `BuffRemoveReason`, `TimerShape`, `PlacementMode`,
  `StackDirection`, `TimerRemoveReason`
- `IPluginContext.cs` — add `IPluginBuffs Buffs` and `IScreenTimers ScreenTimers`

### `ClassicUO.Client` (cuo)

- `Game/Data/BuffIcon.cs` — add `Kind`
- `Game/Managers/PluginBuffs.cs`, `ScreenTimers.cs`, `PluginTimersManager.cs`
  (new) — storage + central `Update()` (expiry detection + event dispatch)
- `Game/UI/Gumps/BuffGump.cs` — merge plugin buffs, generalize
  `BuffControlEntry`, tint by `Kind`
- `Game/UI/Gumps/ScreenTimersGump.cs` (new) — overlay render
- `PluginHost.cs` — new command fns (`AddBuffFn`, `RemoveBuffFn`,
  `ClearBuffsFn`, `AddTimerFn`, `RemoveTimerFn`, `RemoveTimerGroupFn`,
  `DefineTimerGroupFn`, `ClearTimersFn`) + event fns (`BuffEventFn`,
  `TimerEventFn`); bind static targets; store event pointers for cuo to invoke
- `World.cs` — call `PluginTimersManager.Update()` each tick

### `ClassicUO.BootstrapHost`

- `PluginContextImpl.cs` — `BuffsImpl : IPluginBuffs`,
  `ScreenTimersImpl : IScreenTimers` (flatten config → scalar native calls;
  string via `nint` pointer freed after call), plus `RaiseBuffEvent` /
  `RaiseTimerEvent`
- `ClientBindings` — new command fn fields
- `HostBridge.cs` — `OnBuffEvent(int id, int reason)` /
  `OnTimerEvent(int id, int reason)` `[UnmanagedCallersOnly]` →
  `RaiseEachPlugin` → maps `reason` to `Expired` vs `Removed(reason)`

### Event direction detail

cuo's `PluginTimersManager`, on expiry/remove, invokes the stored
`BuffEventFn` / `TimerEventFn` (null-checked — the legacy v1 host leaves them
unset). New struct fields are appended at the end to preserve ABI for both
hosts.

## Testing (xUnit, InternalsVisibleTo)

Pure logic, no rendering:

- `PluginBuffs`: add / update-same-id (overwrite) / remove / clear / expiry →
  event fired (injected fake dispatch)
- `ScreenTimers`: add, update = timer restart, `DefineGroup`, **stack layout
  math** (offset = `index * (extent + gap)` per direction) as a pure function,
  lone vs grouped positioning, remaining-% computation, expiry dispatch
- `BuffIcon.Kind` defaults to `None` on the server path

Rendering (`BuffGump` / `ScreenTimersGump`) is not unit-tested (consistent with
current test coverage).

## Out of scope (YAGNI)

- Persistence of plugin buffs/timers across sessions.
- User dragging of screen timers (overlay is fixed-position, non-interactive).
- Packet-level or server-side integration; both features are purely client/
  plugin-side.
- v1 (net472 `ClassicUO.Bootstrap`) plugin support for these surfaces.
