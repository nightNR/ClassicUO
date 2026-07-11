# Anchored screen timers — design

**Date:** 2026-07-10
**Status:** Approved (brainstorm), pending implementation plan
**Branch:** `feat/new-features`

## Summary

Extend the existing plugin-driven screen-timer system so a timer can be
**pinned to an in-game anchor** — a mobile/item serial, an absolute map tile,
or the player — instead of only a fixed screen position or stacking group.

When the anchor is off-screen the timer hides but **keeps counting**; when the
anchor scrolls back on-screen the timer shows again. When the anchor is *lost*
(entity destroyed / out of sync range) the timer keeps running for a grace
period, then auto-removes and raises a removal event.

This is driven entirely through the **plugin v2 API** (no new custom in-game UI,
no server packet). Timers are **not persisted** — a plugin re-adds them after
reload/relog, consistent with the current screen-timer overlay.

## Background — what already exists

The codebase already has a complete plugin-driven screen-timer system; the only
gap is world-anchoring. Reuse, don't rebuild.

- `ScreenTimers.cs` — store (`ScreenTimerEntry`) + pure layout math (groups,
  stacking, `RemainingFraction`, `ComputePosition`, `CollectExpired`). Timer
  shapes: `Circle`, `Bar`, `Numeric`.
- `PluginTimersManager.cs` — per-frame driver called from `World.Update`
  regardless of any open gump: detects expiry, removes, dispatches events to the
  plugin host. Also holds the plugin add/remove commands. **Because expiry lives
  here (not in the gump), a timer already keeps counting while nothing draws it.**
- `ScreenTimersGump.cs` — fixed-position, non-interactive overlay added at the
  top layer on game-scene load; turns each entry into draw calls. `GumpType.None`
  keeps it out of the saved-gump set.
- Plugin API chain: `TimerConfig` (managed, `IScreenTimers.cs`) → `AddTimerFn`
  function pointer (flat scalars, `HostBridge.cs`) → `PluginTimersManager.AddTimer`
  → `ScreenTimers.AddOrUpdate` → `ScreenTimerEntry` → `ScreenTimersGump`.

Prior art for world-anchoring + off-screen hide (the recipe to copy):

- `NameOverheadGump.cs:554-648` — follows a mobile/item, converts to screen via
  `Camera.WorldToScreen`, returns false (hides) when outside camera bounds;
  refreshes/disposes on lost entity via `World.Get(serial)`.
- `HealthLinesManager.cs:80-169` — same anchor math + off-screen `continue`.
- `GameObject.RealScreenPosition` + `Offset`; iso tile→pixel formula
  `((X-Y)*22, (X+Y)*22-(Z<<2))` (`GameObject.cs:150-154`).
- `World.Get(uint)` → mobile-or-item, null if destroyed (`World.cs:434-463`).

## Decisions (from brainstorm)

| Question | Decision |
|---|---|
| How is a timer created? | Plugin API action (no custom UI, no packet). |
| Anchor kinds | Serial (mobile/item) **+** Absolute tile **+** Self/player. |
| What it shows | Reuse existing shapes (Circle/Bar/Numeric + label + time). |
| Anchor lost before expiry | **Grace period**: keep running N ms; if the anchor returns, show again; if grace elapses, auto-remove + event. |
| Multiple timers on one anchor | **One anchor = one timer** (new replaces old). |
| Persistence | **None** — plugin re-adds after reload/relog. |

Key distinction throughout: **off-screen ≠ lost.**
- *Off-screen*: anchor exists but outside the camera → hide, no timeout, runs to
  natural expiry.
- *Lost*: `World.Get` returns null → start grace countdown.

## Design

### 1. Data model

New anchor concept, added to `ScreenTimerEntry` and mirrored in the managed
`TimerConfig` and the ABI:

```
enum AnchorKind { None = 0, Serial = 1, Absolute = 2, Self = 3 }
```

New fields on `ScreenTimerEntry`:

```
AnchorKind AnchorKind;      // None => existing fixed/group behavior, unchanged
uint   AnchorSerial;        // Serial kind: mobile-or-item serial
ushort AnchorX, AnchorY;    // Absolute kind: tile coords
sbyte  AnchorZ;
short  AnchorOffsetX;       // pixel nudge from resolved anchor point, default 0
short  AnchorOffsetY;
int    AnchorGraceMs;       // Serial/Self lost-grace; default 5000, 0 => default
long   MissingSinceTicks;   // runtime state; 0 = currently resolvable
```

Resolution per kind:
- `None` → unchanged: fixed `X/Y` or stacking group via `ComputePosition`.
- `Serial` → `World.Get(AnchorSerial)` (mobile or item).
- `Absolute` → tile `(AnchorX, AnchorY, AnchorZ)`, never "lost".
- `Self` → `World.Player`.

Anchored timers (`AnchorKind != None`) ignore `GroupId`/stacking — position comes
from the anchor. The group path remains only for `None`.

### 2. Anchor resolution + off-screen cull (render — `ScreenTimersGump`)

Per anchored entry, inside `AddToRenderLists`:

1. Resolve a world screen position (copy the `NameOverheadGump` /
   `HealthLinesManager` recipe):
   - `Serial`: `World.Get(serial)`. Null → skip draw (grace handled by the
     manager, §3). Mobile → `RealScreenPosition + Offset` + anim height above
     head; Item → item variant.
   - `Self`: `World.Player`, treated as a mobile.
   - `Absolute`: synthetic iso `((x-y)*22, (x+y)*22-(z<<2))` minus the current
     draw offset (no entity needed).
2. `Camera.WorldToScreen` (`Client.Game.Scene.Camera`); center the timer
   horizontally on the anchor, place it above the head, then apply
   `AnchorOffsetX/Y`.
3. Cull: if the placement is outside `camera.Bounds` → **skip draw**. The entry
   stays alive and keeps counting (off-screen = hidden but running).
4. On-screen → `DrawEntry(...)`, identical shape rendering to today
   (Bar / Circle / Numeric + label + time).

The gump holds **no** new anchor state — pure resolve → cull → draw each frame.
Where practical, extract a pure helper `TryResolveAnchorScreen(...)` so the
math is unit-testable apart from the batcher.

### 3. Grace + lost-anchor (authoritative — `PluginTimersManager.Update`)

`PluginTimersManager.Update(now)` runs from `World.Update` regardless of the
gump, so lost/grace lives here (not in the render path). New pass over anchored
entries:

- `Serial` / `Self`: resolve via `World`.
  - Resolvable → `MissingSinceTicks = 0`.
  - Null → if `MissingSinceTicks == 0` set it to `now`; if
    `now - MissingSinceTicks >= AnchorGraceMs` → `Remove(id)` +
    `RaiseTimerEvent(id, ReasonAnchorLost)`.
  - Anchor reappears within grace → `MissingSinceTicks = 0`, timer resumes and
    draws again once on-screen.
- `Absolute` → never lost.
- Natural expiry (existing `CollectExpired`) still fires first when the duration
  elapses → `ReasonExpired`. Both paths remove + dispatch.

New reason code: `ReasonAnchorLost = 3` (client) ↔ `TimerRemoveReason.AnchorLost`
(PluginApi) ↔ mapped in `HostBridge` / `ScreenTimersImpl.RaiseEvent`.

### 4. API surface + ABI + replace-by-anchor

**Managed `TimerConfig`** — add:

```
AnchorKind Anchor;                // default None
uint   AnchorSerial;
ushort AnchorX, AnchorY;
sbyte  AnchorZ;
short  AnchorOffsetX, AnchorOffsetY;
int    AnchorGraceMs;             // default 5000; 0 => use default
```

`AnchorKind` enum added to `ClassicUO.PluginApi`. Default `None` keeps existing
plugins source-compatible.

**ABI** — `AddTimerFn` gains appended scalar params:

```
void(int id, int shape, int durationMs, ushort hue, int groupId,
     int x, int y, int width, int height, nint labelUtf8, byte showTime,
     int anchorKind, uint anchorSerial, ushort ax, ushort ay, sbyte az,
     short offX, short offY, int graceMs)
```

This is a breaking function-pointer signature change; host and client rebuild
together (same repo; plugin v2 contract is still evolving). Thread the new args
through: `HostBridge` declaration → `ScreenTimersImpl.AddOrUpdate` →
`PluginTimersManager.AddTimer` → `ScreenTimers.AddOrUpdate`.

**Replace-by-anchor** (one anchor = one timer): when `AddOrUpdate` runs with
`Anchor == Serial`, purge any *other* entry sharing the same `AnchorSerial`
before insert. A same-`Id` update remains a restart-in-place (existing behavior).

### 5. Testing

Unit tests (xUnit, in the `PluginTimersManagerTests` style — pure logic via the
`TimerEventSink` seam, no render):

- `AddOrUpdate` with a Serial anchor stores the anchor fields.
- Second add on the same serial (different id) purges the first (replace-by-anchor).
- Grace: anchor null `< grace` → alive; `>= grace` → removed + `ReasonAnchorLost`.
- Anchor reappears within grace → `MissingSinceTicks` resets, no removal.
- Absolute / Self are never lost.
- Natural expiry still fires `ReasonExpired` while anchored.

Anchor→screen math (`RealScreenPosition` / iso / camera cull) is render-path;
extract `TryResolveAnchorScreen(...)` as a pure helper to test where feasible,
otherwise cover by manual in-game verification.

**In-game verification:** a plugin pins a timer on a mobile serial → walk it
off-screen (hidden, still running) → return (visible again); kill the mobile →
grace elapses → auto-remove event fires.

### Files touched

| File | Change |
|---|---|
| `Game/Managers/ScreenTimers.cs` | anchor fields on entry, replace-by-anchor purge, `MissingSinceTicks` |
| `Game/Managers/PluginTimersManager.cs` | anchor grace/lost pass, extend `AddTimer` signature, `ReasonAnchorLost` |
| `Game/UI/Gumps/ScreenTimersGump.cs` | resolve anchor → cull off-screen → draw; `TryResolveAnchorScreen` helper |
| `PluginApi/IScreenTimers.cs` | `AnchorKind` enum, `TimerConfig` anchor fields, `AnchorLost` reason |
| `BootstrapHost/PluginContextImpl.cs` | `ScreenTimersImpl.AddOrUpdate` new args + reason map |
| `BootstrapHost/HostBridge.cs` | `AddTimerFn` signature + reason plumb |
| `PluginApi/README.md` | document anchor usage |
| `tests/ClassicUO.UnitTests/.../PluginTimersManagerTests.cs` | cases above |

## Out of scope

- Custom in-game UI to create timers (plugin-only trigger).
- Server-packet-driven timers.
- Persistence across sessions.
- Stacking multiple timers on one anchor (one anchor = one timer).
