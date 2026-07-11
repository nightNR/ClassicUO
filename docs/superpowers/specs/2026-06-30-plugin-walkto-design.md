# Plugin v2 `WalkTo` — Reliable pathfinding walk/run to coordinates

**Date:** 2026-06-30
**Branch context:** `feat/cuo_plugin_v2`
**Status:** Approved design, ready for implementation planning

## Context

This is sub-project **D** of a larger effort to give Plugin v2 (BootstrapHost,
net10) richer control over ClassicUO. The full effort decomposes into
independent sub-projects, each with its own spec → plan → build cycle:

| # | Feature | Status |
|---|---------|--------|
| **D** | Plugin reliable walk/run to coords with pathfinding + dynamic-obstacle bypass | **this spec** |
| A+C | Status bar control: open at position + priority-list overlay/recolor | later spec |
| B | Modern look for party status bars (client toggle → `HealthBarGumpCustom`) | later spec |

Guiding constraint (user): **minimize changes to the client; keep the policy in
the plugin.** The client exposes thin, generic primitives; the plugin holds the
orchestration.

A prior candidate sub-project — an `IWorld` state-query API — was **dropped**:
the plugin already reconstructs world state (serial, graphic, hue, notoriety,
hits, position) by parsing the packet stream via `IPacketPipeline.Incoming`. It
does not need the client to hand it mobile data.

### What already exists

`src/ClassicUO.Client/Game/Pathfinder.cs`:

- `bool WalkTo(int x, int y, int z, int distance)` (line ~930) — runs A*
  (`FindPath`) once over the **current** map, including dynamic blockers
  (mobiles/items are consulted in `CanWalk` / `CalculateNewZ`), then starts
  `AutoWalking`. Returns whether a path was found.
- `void ProcessAutoWalk()` (line ~1005) — steps one path node per call; driven
  every frame by `GameScene.Update` (`Pathfinder.cs` is ticked at
  `GameScene.cs:755`). Calls `Player.Walk(dir, _run)`; on failure calls
  `StopAutoWalk()`. When `_pointIndex >= _pathSize`, the path is exhausted and
  it calls `StopAutoWalk()`.
- `void StopAutoWalk()` (line ~1032) — clears `AutoWalking`, resets `_run` to
  false and `_pathSize` to 0.

### Reliability gaps to close

1. **No `run` parameter.** `WalkTo` never sets `_run`; the only assignment is
   `StopAutoWalk` resetting it to false. Auto-walk therefore always walks.
2. **No completion/failure signal.** A plugin cannot tell *arrived* from
   *gave up*. Every exit funnels through `StopAutoWalk()`, which carries no
   reason.
3. **Path computed once.** If a mobile/item moves into the path *after*
   computation, the next `Player.Walk` fails → `StopAutoWalk` → the walk
   silently stalls. This is precisely the "bypass mobiles/items" requirement:
   the fix is to re-path on stall, because A* already routes around whatever is
   currently blocking.

Out of scope (user decision): teleporters, doors, stairs, and other special
tiles. "Bypass instanced obstacles" means dynamic mobiles and items only.

## Goals

- A plugin can request "walk/run to tile (x, y, z), stopping within `distance`
  tiles" and reliably reach it, re-routing around mobiles/items that move into
  the way.
- The plugin receives authoritative state transitions (not position guesses) so
  it can implement retry/give-up policy.
- The client gains only small, generic primitives. No retry policy, no plugin
  knowledge in the client.

## Non-goals

- Teleporter / door / stairs handling.
- Multi-step travel (recall, gates, boats).
- Any `IWorld` state-query surface (the plugin already has world state).
- Client-side retry/give-up policy (lives in the plugin).

## Architecture

D adds **two action primitives** (plugin → cuo) and **one event** (cuo →
plugin), each threaded through the existing 4-layer boundary used by
`CastSpell` and `UpdatePlayerPosition`:

```
PluginApi (contract)         BootstrapHost (net10)           cuo (client)
────────────────────         ─────────────────────           ───────────────────────────
IGameActions.WalkTo()    ─▶  GameActionsImpl.WalkTo()   ─▶   ClientBindings.WalkToFn   ─▶ Pathfinder.WalkTo
IGameActions.StopWalk()  ─▶  GameActionsImpl.StopWalk()  ─▶  ClientBindings.StopWalkFn ─▶ Pathfinder.StopAutoWalk
IGameActions.WalkProgress ◀─ OnWalkProgress (Unmanaged)  ◀─  HostBindings.WalkProgressFn ◀─ Pathfinder transition
```

Note on which binding table: the **action** pointers (plugin → cuo) live in the
cuo-populated `ClientBindings` table, alongside the existing `CastSpellFn` /
`RequestMoveFn` precedent. Only the **event** pointer (cuo → plugin),
`WalkProgressFn`, lives in `HostBindings` (host-populated, like `ConnectedFn` /
`UpdatePlayerPosFn`).

- **Actions (plugin → cuo).** cuo fills `WalkToFn` / `StopWalkFn` function
  pointers in `ClientBindings` at `Initialize` (mirroring how `CastSpellFn` is
  set in `PluginHost.cs` and `Network/Plugin.cs`). The host calls them from
  `GameActionsImpl`.
- **Event (cuo → plugin).** A new `WalkProgressFn` is added to `HostBindings`.
  cuo invokes `host.WalkProgress(state)` on real transitions; the host's
  `[UnmanagedCallersOnly] OnWalkProgress` static fans out to every loaded
  plugin context's `WalkProgress` event (mirroring the existing
  `UpdatePlayerPosition` / `Connected` fan-out in `HostBridge.cs` and
  `PluginContextImpl.cs`).

## Public contract (PluginApi)

Added to the existing `IGameActions` interface
(`src/ClassicUO.PluginApi/IGameActions.cs`):

```csharp
public interface IGameActions
{
    // existing: CastSpell, RequestMove, TryGetPlayerPosition ...

    /// <summary>
    /// Pathfinds to tile (x, y, z) and starts auto-walking toward it, stopping
    /// within <paramref name="distance"/> tiles of the goal. Pass
    /// <paramref name="run"/> = true to run instead of walk.
    /// Returns <c>false</c> if no path can be found right now.
    /// Must be called on the game thread; throws otherwise. Use
    /// <see cref="IPluginContext.Game"/>.<c>Post</c> to marshal.
    /// </summary>
    bool WalkTo(int x, int y, int z, int distance, bool run);

    /// <summary>
    /// Cancels any active auto-walk. Safe to call at any time and from any
    /// thread (auto-marshals to the game thread).
    /// </summary>
    void StopWalk();

    /// <summary>
    /// Raised on auto-walk state transitions. Fired on the game thread.
    /// </summary>
    event Action<WalkState>? WalkProgress;
}

/// <summary>State of an auto-walk requested via <see cref="IGameActions.WalkTo"/>.</summary>
public enum WalkState
{
    /// <summary>A path was found and the player started moving.</summary>
    Walking,
    /// <summary>The player reached within <c>distance</c> tiles of the goal.</summary>
    Arrived,
    /// <summary>A step failed (a dynamic mobile/item blocks the path). The
    /// plugin may re-issue WalkTo to re-route.</summary>
    Blocked,
    /// <summary>The walk was cancelled, no path existed, or the player object
    /// went away.</summary>
    Stopped,
}
```

Semantics:

- `WalkTo` returning `false` means no path was found at call time; no
  `WalkProgress` event is raised for that case (the bool return already says
  so).
- `WalkTo` returning `true` is followed by a `Walking` event, then exactly one
  terminal event: `Arrived`, `Blocked`, or `Stopped`.
- `WalkProgress` is raised for *any* auto-walk, including client-initiated
  click-to-walk. A plugin that only cares about its own requests should track
  whether it initiated the current walk and ignore others.

## Client-side behavior

The only new client logic is emitting the four states from transition points
the pathfinder already passes through. Today every exit funnels through
`StopAutoWalk()`, which cannot distinguish reasons.

**Change:** introduce a private `EndAutoWalk(WalkState reason)` that records the
reason, performs the existing teardown, and emits the event. Keep a separate
private no-emit reset for the internal "clear prior walk" case.

| Transition point (existing code) | Today | New emit |
|----------------------------------|-------|----------|
| `WalkTo` → `FindPath` succeeds | starts AutoWalk | `Walking` |
| `WalkTo` → `FindPath` fails | `AutoWalking = false`; returns false | none (bool return) |
| `WalkTo` start, clearing a prior walk (line ~982) | `StopAutoWalk()` | **none** (no-emit reset) |
| `ProcessAutoWalk` → `_pointIndex >= _pathSize` | `StopAutoWalk()` | `Arrived` |
| `ProcessAutoWalk` → `!Player.Walk(...)` | `StopAutoWalk()` | `Blocked` |
| public `StopWalk` / external cancel | `StopAutoWalk()` | `Stopped` |

Important nuance: `WalkTo` calls `StopAutoWalk()` at its start to clear any
prior walk. That call **must not emit** (it would spuriously fire `Stopped`
before `Walking`). Route it through the no-emit reset; only `EndAutoWalk` emits.
Public `StopWalk` → `EndAutoWalk(Stopped)`.

**`run`:** add the missing `_run = run;` in `WalkTo` (the signature gains the
`run` argument). This is the only behavior change to existing walk logic.

**Emitting to plugins:** the pathfinder raises an internal hook (e.g. a static
event or a direct call into the plugin host bridge, following the pattern used
for `UpdatePlayerPosition`). The cuo `UnmanagedAssistantHost.WalkProgress(state)`
invokes the plugin's `WalkProgressFn` pointer.

**Threading:** `WalkTo` touches `World` / `Pathfinder` (not thread-safe) and
returns `bool`, so it requires the game thread and throws otherwise — the same
contract as the existing `RequestMove`. `StopWalk` auto-marshals. `WalkProgress`
fires on the game thread (transitions happen inside `ProcessAutoWalk`, which
runs on the game thread via `GameScene.Update`).

## Plugin-side orchestration (illustrative — lives entirely in the plugin)

The client holds none of this. Example policy in a plugin:

```csharp
int _retries;
(int x, int y, int z, int dist, bool run) _goal;
const int MaxRetries = 5;

void GoTo(int x, int y, int z, int dist, bool run)
{
    _goal = (x, y, z, dist, run);
    _retries = 0;
    ctx.Actions.WalkTo(x, y, z, dist, run);   // on the game thread
}

// subscribed once in OnInitialize:
// ctx.Actions.WalkProgress += OnWalkProgress;
void OnWalkProgress(WalkState state)
{
    switch (state)
    {
        case WalkState.Arrived:
            // success
            break;
        case WalkState.Blocked:                       // dynamic mobile/item in the way
            if (_retries++ < MaxRetries)
                ctx.Actions.WalkTo(_goal.x, _goal.y, _goal.z, _goal.dist, _goal.run); // re-route
            else
                /* give up — failure */;
            break;
        case WalkState.Stopped:
            // cancelled or no path
            break;
    }
}
```

Re-issuing `WalkTo` on `Blocked` is the obstacle-bypass mechanism: A* recomputes
against current world state and routes around whatever moved in. Spacing retries
across a few ticks (backoff) is plugin policy — it handles a blocker that is
still passing through. The client does not need to know about any of this.

## Testing

- **Client unit tests** (`tests/ClassicUO.UnitTests`): `Pathfinder` emits the
  correct `WalkState` per transition — `Arrived` when the path exhausts,
  `Blocked` when a step fails, `Stopped` on cancel; `run` propagates to `_run`;
  the prior-walk-clearing reset at the top of `WalkTo` does **not** emit.
- **Host tests** (`tests/ClassicUO.BootstrapHost.Tests`): follow the existing
  `TestRaise*` pattern — `WalkTo` / `StopWalk` invoke the correct bindings, and
  `OnWalkProgress` fans out to every subscribed plugin context exactly once per
  transition.
- **Manual** (`HelloPlugin` staged via `tools/stage-bootstraphost.ps1`): request
  a walk to a tile behind a mobile, confirm re-path on `Blocked` and an eventual
  `Arrived` in the plugin event log (`CUO_PLUGIN_TEST_LOG`).

## Files touched (estimate)

| Layer | File | Change |
|-------|------|--------|
| Contract | `src/ClassicUO.PluginApi/IGameActions.cs` | add `WalkTo`, `StopWalk`, `WalkProgress`, `WalkState` |
| Host | `src/ClassicUO.BootstrapHost/HostBridge.cs` | add `WalkToFn`/`StopWalkFn`/`WalkProgressFn` to bindings; `OnWalkProgress` unmanaged fan-out |
| Host | `src/ClassicUO.BootstrapHost/PluginContextImpl.cs` | `GameActionsImpl.WalkTo`/`StopWalk`; raise `WalkProgress` on contexts |
| cuo | `src/ClassicUO.Client/PluginHost.cs` | add fn-pointer fields; wire `WalkToFn`/`StopWalkFn` to client methods; `WalkProgress(state)` emit |
| cuo | `src/ClassicUO.Client/Game/Pathfinder.cs` | `run` param + `_run` setter; `EndAutoWalk(reason)`; emit at transitions |
| cuo | `src/ClassicUO.Client/Network/Plugin.cs` | legacy binding parity if required for struct layout |
| tests | `tests/ClassicUO.UnitTests`, `tests/ClassicUO.BootstrapHost.Tests` | as above |
| sample | `samples/HelloPlugin` | demo walk-to for manual verification |

## Open questions / risks

- **Legacy `HostBindings` layout.** Both the legacy net472 `Bootstrap` and the
  v2 `BootstrapHost` consume the same `HostBindings` struct from cuo. Appending
  new fn-pointer fields must keep struct layout compatible with both apphosts.
  Implementation must append (not reorder) and verify the legacy path still
  loads.
- **`Blocked` chatter.** A blocker that re-blocks every re-path could spin the
  plugin's retry loop. Mitigated by the plugin's `MaxRetries` + backoff; the
  client stays dumb.
