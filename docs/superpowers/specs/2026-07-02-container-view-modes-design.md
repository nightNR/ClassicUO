# Container View Modes (GridView / Standard / Toggle)

**Date:** 2026-07-02
**Branch:** feat/new-features
**Status:** Approved design, ready for implementation planning

## Summary

Add a profile option controlling how container gumps are visualized. Three modes:

- **Standard** — current art-based `ContainerGump` (unchanged behavior).
- **Grid** — every container opens as a paged grid of item cells instead of the
  container artwork.
- **Toggle** — containers show a small button in the top-right corner that
  switches that specific container (by serial) between standard and grid. The
  choice is remembered per serial and persists across logout.

The grid must be a full-fidelity replacement of the standard container: it
supports double-click to use/equip/open, drag items out to the cursor, drop held
items into the container, hover tooltips, right-click/context actions, and stack
amount splitting.

## Goals / Non-goals

**Goals**
- Per-profile selection of container visualization mode.
- Grid view with the full interaction set listed above.
- Per-serial toggle state that survives relogin (stored in profile).
- Minimal disruption to existing container integration points.

**Non-goals (YAGNI)**
- Configurable column count.
- Freely resizable grid window.
- Custom/user-selectable sort orders, search, or filtering.
- Pruning stale per-serial toggle entries.

## Chosen approach

`ContainerGump` gains an internal view mode rather than introducing a separate
grid gump class.

Rationale: many call sites resolve the container window via
`UIManager.GetGump<ContainerGump>(serial)` — the drop-to-container logic in
`ContainerGump.OnMouseUp`, position saving on dispose/drag-end, `PacketHandlers`
open path, and `Profile` gump restore. A separate `GridContainerGump` type would
force dual-type handling at every one of those sites (high blast radius). Keeping
a single `ContainerGump` type that swaps its child layout preserves all of that
machinery. Grid-specific rendering/interaction code is isolated in new control
files so `ContainerGump` itself stays close to its current size.

Rejected alternatives:
- **Separate `GridContainerGump` class** — clean separation but breaks the
  `GetGump<ContainerGump>` integration points.
- **Grid as a floating overlay panel** — duplicate state, confusing UX.

## Components

### 1. Profile settings

Added to `src/ClassicUO.Client/Configuration/Profile.cs`. These serialize
automatically with the rest of the profile (same mechanism as the existing
`JournalTabs` dictionary and `GridLootType` int).

```csharp
// 0 = Standard (default), 1 = Grid, 2 = Toggle
public int ContainerViewMode { get; set; }

// In Toggle mode, the view used for a serial the user has never toggled.
// false = standard, true = grid.
public bool ContainerToggleDefaultGrid { get; set; }

// Explicit per-serial choice in Toggle mode. true = grid, false = standard.
// Absent key => fall back to ContainerToggleDefaultGrid.
public Dictionary<uint, bool> ContainerGridStates { get; set; } = new();
```

### 2. Effective-view resolver

A single static helper decides whether a given container serial should render as
a grid. Lives on `ContainerGump` (e.g. `static bool ResolveGridView(uint serial)`).

Logic:

| ContainerViewMode | Result |
| ----------------- | ------ |
| 0 (Standard)      | `false` |
| 1 (Grid)          | `true` |
| 2 (Toggle)        | `ContainerGridStates.TryGetValue(serial, out v) ? v : ContainerToggleDefaultGrid` |

Null-profile guard returns `false` (standard).

### 3. `ContainerGump` internal mode

File: `src/ClassicUO.Client/Game/UI/Gumps/ContainerGump.cs`.

- `BuildGump()` calls the resolver and branches:
  - **Standard branch** — the existing art path (`GumpPicContainer`, corpse eye,
    hue, minimizer hitbox) unchanged.
  - **Grid branch** — skip `GumpPicContainer` and the minimizer hitbox; add a
    single `GridContainerView` child sized to the paged grid, and set the gump
    `Width`/`Height` from it.
- The gump keeps `LocalSerial` and `GumpType.Container` in both branches, so
  `GetGump<ContainerGump>`, drop handling, position save, and restore work
  without change.
- **Corner toggle button**: added only when `ContainerViewMode == 2`. Positioned
  in the top-right of the current view. On click it flips
  `ContainerGridStates[LocalSerial]` (writing the resolved current value first if
  the key is absent, then inverting) and triggers `RequestUpdateContents()` so
  the gump rebuilds in place — same window instance, same screen position, no
  re-registration.
- `UpdateContents()` currently does `Clear(); BuildGump(); IsMinimized =
  IsMinimized; ItemsOnAdded();`. In grid mode, `BuildGump` produces the
  `GridContainerView` and item population is delegated to that control's own
  rebuild (see below) instead of `ItemsOnAdded`.
- **Minimize**: grid mode has no iconized artwork. Minimize is disabled while in
  grid view (the minimizer hitbox is not added in the grid branch).

### 4. `GridContainerView : Control`

New file: `src/ClassicUO.Client/Game/UI/Controls/GridContainerView.cs`.

Isolates all grid layout/rendering so `ContainerGump` stays lean. Modeled on the
existing `GridLootGump` paged layout.

- Fixed cell size and `MAX_WIDTH` / `MAX_HEIGHT` bounds; Prev/Next page buttons
  and a page-number label appear when items overflow one page.
- Populated from the container's `Items` linked list **in list order**, which is
  the item draw/stacking order. (Container items always have `Z == 0` — set
  explicitly in `PacketHandlers.AddItemToContainer`, line ~6089 — so "sort by z
  position" resolves to this linked-list/stack order rather than a numeric Z
  sort.)
- Applies the same skip rules as `ContainerGump.ItemsOnAdded`: `Amount <= 0`,
  corpse bad-container layers, and wearable items on Face/Beard/Hair layers.
- Rebuilds its cells whenever the host `ContainerGump.UpdateContents()` runs, so
  server-side add/remove/move refreshes the grid.
- The empty grid background is a drop target: when a held item is released over
  it, call `GameActions.DropItem(heldSerial, 0xFFFF, 0xFFFF, 0, containerSerial)`
  to drop into the container (grid has no meaningful slot coordinates).

### 5. `GridContainerItem : Control`

Grid cell, modeled on `GridLootGump.GridLootItem`. Either nested in
`GridContainerView` or its own file.

- Fixed-size cell, item art centered within it, a full-cell `HitBox` (so the
  entire cell is clickable, not just the art silhouette), and a stack-count
  badge for stackable items.
- Interactions reuse the same entry points `ItemGump` uses:
  - **Double-click** — replicate `ItemGump.OnMouseDoubleClick`: if
    `DoubleClickToLootInsideContainers` conditions hold, `GameActions.GrabItem`;
    otherwise `GameActions.DoubleClick`.
  - **Drag out / pickup** — `GameActions.PickUp` (mirroring
    `ItemGump.AttemptPickUp`, honoring `RelativeDragAndDropItems` /
    `ScaleItemsInsideContainers` offsets), including `SplitMenuGump` handling for
    stack splitting.
  - **Drop onto cell** — held-item release drops into the container (same call
    as the grid background drop target).
  - **Tooltip** — `SetTooltip(item)` when tooltips enabled.
  - **Context / single-click** — set `SelectedObject.Object` and route through
    `DelayedObjectClickManager`, same as `ItemGump.OnMouseUp` / `OnMouseOver`.

Where practical, factor the shared pickup/double-click logic so `ItemGump` and
`GridContainerItem` do not diverge; if a clean shared helper is impractical,
duplicate the minimal logic and note it.

### 6. Options UI

File: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs`, Containers section
(alongside the existing container options such as `GridLootType`,
`ScaleItemsInsideContainers`).

- A dropdown/combo **"Container view: Standard / Grid / Toggle"** bound to
  `ContainerViewMode`.
- A checkbox **"Default toggled containers to grid"** bound to
  `ContainerToggleDefaultGrid`, enabled only when the mode dropdown is set to
  Toggle.
- New user-facing strings added to the resource files (`ResGumps`), following the
  existing localization pattern.

## Data flow

1. Server sends open-container / container-contents packets → `PacketHandlers`
   creates or refreshes the `ContainerGump` (unchanged).
2. `BuildGump()` calls `ResolveGridView(serial)` to pick the branch.
3. Grid branch builds `GridContainerView`, which enumerates `container.Items` and
   creates a `GridContainerItem` per eligible item.
4. User interactions on cells call the same `GameActions` the standard container
   uses, so networking/side-effects are identical.
5. In Toggle mode, the corner button writes `ContainerGridStates[serial]` and
   rebuilds; the profile persists the dictionary on save (relogin-safe).

## Error handling / edge cases

- Container destroyed or out of range → existing `ContainerGump.Update()` dispose
  path handles both views (shared).
- Changing `ContainerViewMode` in options while containers are open → force a
  refresh of open `ContainerGump`s on apply (iterate `UIManager` container gumps
  and call `RequestUpdateContents`), so the new mode takes effect immediately.
- Empty container in grid mode → grid renders with zero cells but remains a valid
  drop target.
- Corpse gump (`CORPSES_GUMP`) in grid mode → grid view applies; the corpse-eye
  animation is a standard-only element and is simply absent in grid view. (This
  is independent of the separate `GridLootGump` corpse-loot feature.)
- `ContainerGridStates` growth is unbounded but small per entry; acceptable and
  consistent with how container positions already accumulate. Pruning is out of
  scope.

## Testing

**Unit tests** (`tests/ClassicUO.UnitTests`)
- `ResolveGridView` truth table: all three modes, dictionary hit/miss, and both
  values of `ContainerToggleDefaultGrid`.
- Toggle button state flip: absent key → writes inverse of default; present key →
  inverts stored value.

**Manual verification**
- Open backpack, a chest, and a corpse in each of the three modes.
- Toggle a container to grid, log out and back in, confirm it reopens as grid.
- In grid view: drag an item out, drop an item in, double-click to use/equip,
  split a stack, page through a container with more items than one page.
- Switch mode in options with containers open; confirm they refresh.

## Files touched

- `src/ClassicUO.Client/Configuration/Profile.cs` — new settings.
- `src/ClassicUO.Client/Game/UI/Gumps/ContainerGump.cs` — resolver, grid branch,
  toggle button.
- `src/ClassicUO.Client/Game/UI/Controls/GridContainerView.cs` — new.
- `src/ClassicUO.Client/Game/UI/Controls/GridContainerItem.cs` — new (or nested).
- `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` — options UI.
- `src/ClassicUO.Client/Resources/ResGumps*` — new strings.
- `tests/ClassicUO.UnitTests/...` — resolver tests.
