# Plugin-controlled status-bar priority ordering — design

Date: 2026-07-22
Depends on: `feat/anchor-groups` branch (per-group grid, `PluginAnchorGroupGump`,
`PluginStatusBarGroups`). This feature stacks on that work.

## Summary

Add a single new plugin→client primitive:

```csharp
void SetStatusBarPriority(uint serial, int priority);
```

The client keeps a per-serial priority value (default `0`). Within an anchor
group, member status bars are ordered by **priority descending, then insertion
order ascending** (stable tiebreak). Changing a bar's priority — or opening a
new bar — reflows its group so cells reflect the new order. Priority is only
meaningful **within the bar's own group**; a serial not currently in a group
just has its value stored for when it next joins one.

This one mechanism covers all three ordering strategies the plugin might want,
with no client-side "mode" and no need for the plugin to know anything about the
anchor's configuration:

- **As-is** — never call `SetStatusBarPriority`; everything stays `0`, so order
  is pure insertion order (today's behavior). Fully backward-compatible.
- **Exact order** — the plugin assigns explicit priorities to place bars exactly
  where it wants (relative order is exact; if the plugin sets priorities on all
  members, positions are fully determined).
- **Dynamic priority** — the plugin updates priorities as game state changes
  (HP%, distance, threat) and the group re-sorts automatically.

Because members are always sorted into a dense fill, **there are never holes in
the grid** — sparse-slot gaps are impossible by construction.

## Why priority (not slot index or explicit-order array)

Decided during design:
- Enemy bars are highly dynamic (appear / die / HP changes). A priority value
  per serial lets the plugin nudge one bar without resending a full order.
- Reuses the existing per-serial mental model already established by
  `SetOverlay(serial, …)`.
- Dense re-sort eliminates the sparse-grid and slot-collision edge cases that a
  slot-index API would require policies for.
- One new ABI binding; append-only, so `Phoenix`'s mirrored ABI is unaffected.

## Decided parameters

- Members **without** a priority set → default **`0`**.
- Sort is always **priority descending** (higher priority = higher/earlier in
  the list = grid cell 0 onward).
- **Reflow** whenever a group's membership or any member's priority changes
  (i.e. on open and on `SetStatusBarPriority`). Implementation may skip the
  reflow when the resulting order is unchanged, but correctness only requires
  "reflow if order could have changed."
- Priority is scoped to the serial's **own group**; outside a group it has no
  layout effect (value is still stored).
- **Reset**: call `SetStatusBarPriority(serial, 0)` to return a bar to default
  precedence.

## Current state (from `feat/anchor-groups`)

- `IStatusBars` (`src/ClassicUO.PluginApi/IStatusBars.cs`) — `SetOverlay` is the
  last method (line 36); same shape as the new primitive (serial + values,
  auto-marshaled to game thread).
- Host marshaling (`src/ClassicUO.BootstrapHost/PluginContextImpl.cs`) —
  `StatusBarsImpl.SetOverlay` (lines 224-259) is the template: check
  `fn == 0`, call the `delegate* unmanaged[Cdecl]` directly on the game thread
  else `PostToGameThread`.
- Host binding struct (`src/ClassicUO.BootstrapHost/HostBridge.cs`) —
  `ClientBindings` ends at `ClearCharactersFn` (line 387), no `ClientVersion`.
- Client binding struct (`src/ClassicUO.Client/PluginHost.cs`) — `ClientBindings`
  ends with `ClientVersion` (line 80, MUST stay last); the append point for new
  function pointers is **after `ClearCharactersFn` (line 77) and before
  `ClientVersion`**. Delegates + assignments mirror `SetOverlay`
  (`dSetOverlay` ~:301, assignment ~:457).
- Client target (`src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs`) —
  `SetOverlay` static method (:154), `PluginStatusOverlays` per-serial store
  (:15-56, template for the priority store), `PluginStatusBarGroups` with
  `_members` per-group list + `GetLiveMembers` (:64-143), `AddToGroup` (:246-303)
  and layout helpers `NeighborFor` (:403), `ResolveMaxRows/Columns/Fill`.
- Tests: `tests/ClassicUO.BootstrapHost.Tests/StatusBarsTests.cs` exercises the
  marshaling for the existing status-bar fns.

## Design

### 1. ABI — new binding `SetStatusBarPriorityFn` (append-only)

Native signature: `void(uint serial, int priority)` — delegate
`delegate* unmanaged[Cdecl]<uint, int, void>`.

- `HostBridge.cs`: append `public nint SetStatusBarPriorityFn;` immediately after
  `ClearCharactersFn` (the last field).
- `PluginHost.cs`: append
  `public IntPtr /*delegate*<uint, int, void>*/ SetStatusBarPriorityFn;`
  **after `ClearCharactersFn` and before `ClientVersion`** (same relative
  position → the two structs stay prefix-compatible).
- Both structs must keep byte-exact prefix compatibility; the append position is
  identical in both.

### 2. Public API — `IStatusBars.SetStatusBarPriority`

Append after `SetOverlay` (line 37):

```csharp
/// <summary>
/// Sets the ordering priority for the status bar of <paramref name="serial"/>.
/// Within its anchor group, bars are ordered by priority descending, then by
/// the order they were opened. Default priority is 0; pass 0 to reset. Priority
/// only affects layout while the serial's bar belongs to a group. Auto-marshals
/// to the game thread.
/// </summary>
void SetStatusBarPriority(uint serial, int priority);
```

### 3. Host marshaling — `StatusBarsImpl.SetStatusBarPriority`

Mirror `SetOverlay` exactly (game-thread check + `PostToGameThread`), using
`ClientBindings.SetStatusBarPriorityFn` and signature `<uint, int, void>`.

### 4. Client binding registration — `PluginHost.cs`

- Delegate type: `delegate void dSetStatusBarPriority(uint serial, int priority);`
- Field + target: `private readonly dSetStatusBarPriority _setStatusBarPriority =
  Game.Managers.PluginStatusBars.SetStatusBarPriority;` (near `_setOverlay`).
- Assignment in `Initialize`: `cuoHost.SetStatusBarPriorityFn =
  Marshal.GetFunctionPointerForDelegate(_setStatusBarPriority);` (near :457).

### 5. Client — priority store + group reflow (`PluginStatusBars.cs`)

**5a. Per-serial priority store** — new `internal static class
PluginStatusPriorities` modeled on `PluginStatusOverlays`:
- `Dictionary<uint,int> _priorities`.
- `Set(uint serial, int priority)` — store; if `priority == 0`, `Remove` (0 is
  the default, so absence == 0, keeping the dict small).
- `int Get(uint serial)` — value or `0`.
- `Clear(uint serial)`, `Reset()` (test-only).

**5b. Binding target**:
```csharp
public static void SetStatusBarPriority(uint serial, int priority)
{
    PluginStatusPriorities.Set(serial, priority);
    int groupId = PluginStatusBarGroups.FindGroupOf(serial);
    if (groupId != 0)
    {
        ReflowGroup(groupId);
    }
}
```

**5c. `PluginStatusBarGroups` additions**:
- `int FindGroupOf(uint serial)` — scans `_members` for a live bar whose
  `LocalSerial == serial`; returns its groupId or `0`.
- The `_members` list stays **insertion-ordered** (it is the stable tiebreak
  source). Sorting is computed on demand, never by mutating `_members` order,
  so insertion order is preserved across reflows. (If a stored, already-sorted
  list is preferred for indexing, add a parallel accessor — but insertion order
  MUST remain recoverable for the tiebreak.)

**5d. Sorted order helper** (pure, unit-testable):
```csharp
// returns the group's live members ordered by (priority desc, insertion asc)
internal static List<BaseHealthBarGump> OrderedMembers(int groupId)
```
Implementation: take `GetLiveMembers(groupId)` (insertion order), pair each with
its index, `OrderByDescending(priority).ThenBy(insertionIndex)`. Priority read
from `PluginStatusPriorities.Get(bar.LocalSerial)`.

Provide an even purer helper for tests, decoupled from gumps:
```csharp
// indices into the input arrays, ordered by (priority desc, position asc)
internal static int[] OrderByPriority(int[] priorities)
```

**5e. `ReflowGroup(int groupId)`** — re-lay the group into grid cells in sorted
order, anchored at the group's origin:
1. `ordered = OrderedMembers(groupId)`; if empty, return.
2. Determine `originX/originY` = **group origin** (see 5f).
3. Detach every member from the anchor system
   (`UIManager.AnchorManager.DetachControl(bar)`), then rebuild: seed
   `ordered[0]` as a fresh `AnchorManager.AnchorGroup` at the origin; place each
   subsequent `ordered[i]` relative to its neighbor using the **existing**
   `NeighborFor(i, rows, cols, fill)` + fill-order X/Y math from `AddToGroup`,
   calling `DropControl(bar, neighbor)` per bar.
4. Re-`Track` the rebuilt `AnchorGroup` for the groupId.

Reusing the same `NeighborFor` + placement code guarantees reflow geometry is
identical to incremental open. Factor the per-cell placement out of `AddToGroup`
into a shared `PlaceInGrid(orderedList, rows, cols, fill, originX, originY)` so
open and reflow share one implementation (DRY).

**5f. Group origin** — the fixed top-left the grid grows from:
- If the group has a def (`GetDef(groupId) != null`): origin =
  `(def.X, def.Y + PluginAnchorGroupGump.WidgetHeight)` (stable; matches the
  seed rule already in `OpenStatusBar`).
- Else (undefined group): origin = the current top-left of live members —
  smallest `Y`, tie smallest `X` — captured **before** detaching, or `(0,0)`
  if none. This keeps an undefined group visually where it currently sits.

**5g. `OpenStatusBar` / `AddToGroup` integration**:
- `AddToGroup` adds the member (as today) then calls `ReflowGroup(groupId)`
  instead of positioning only the new bar. This makes a freshly opened bar land
  in its priority-sorted cell rather than always at the end. (First member still
  seeds the group; reflow of a single-member group is a no-op beyond seeding.)
- The capacity check in `OpenStatusBar` is unchanged (still per-group
  `rows*cols`); priority reorders within capacity, it does not change how many
  fit.

**5h. Cleanup**: on `CloseStatusBar`, after detach+dispose+`PruneEmpty`, the
remaining members keep their relative order; no explicit reflow is required
(closing removes a cell; the neighbors' existing positions remain valid). A
reflow on close is optional and out of scope unless a visible gap appears — with
`DropControl`-based anchoring the surviving bars retain their positions.
`PluginStatusPriorities` entries for closed serials may be left as-is (harmless)
or cleared; clearing on `CloseStatusBar` is preferred to avoid unbounded growth.

### 6. Edge cases

- **Priority set before the bar is open**: stored; honored when `OpenStatusBar`
  later adds the serial and reflows.
- **Priority set for a serial not in any group** (`FindGroupOf == 0`): stored,
  no layout change.
- **Ties** (equal priority): insertion order — deterministic.
- **All default (0)**: `OrderByPriority` is a stable no-op → insertion order →
  identical to today.
- **groupId 0 / ungrouped bars**: unaffected (never grouped, never reflowed).
- **Capacity**: unchanged; if full, `OpenStatusBar` still drops the new bar
  before it would be added/reflowed.

## Testing (xUnit, `ClassicUO.UnitTests` + `ClassicUO.BootstrapHost.Tests`)

- `OrderByPriority` (pure): desc ordering; stable tiebreak on equal priorities;
  all-zero → identity; negative priorities sort below zero; mixed.
- `PluginStatusPriorities`: `Set`/`Get`/reset-to-0 removes entry; default 0.
- `PluginStatusBarGroups.FindGroupOf`: returns correct groupId; 0 when absent.
- Host marshaling (`StatusBarsTests.cs`): `SetStatusBarPriority` invokes
  `SetStatusBarPriorityFn` with the right serial/priority, on and off the game
  thread (mirror the existing `SetOverlay` marshaling test).
- Grid reflow geometry (via the shared `PlaceInGrid`/`NeighborFor` helpers,
  which are already unit-tested) — add a case asserting that a reordered input
  yields the expected cell coordinates.
- Live gump reflow (detach/rebuild through `UIManager`/`AnchorManager`) is
  verified by manual run, matching the repo pattern.

## Out of scope

- Optional `priority` parameter on `OpenStatusBar` (would need a second new
  binding) — YAGNI; add later if a one-call convenience is wanted.
- Cross-group / global priority — priority is per-group only.
- Slot-index / explicit-order-array API — superseded by priority.
- Persisting priorities to profile — priority is plugin-driven runtime state.
