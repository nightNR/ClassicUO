# Permanent anchor groups for plugin status bars — design

Date: 2026-07-22
Branch: `feat/custom-login-scene` (feature to land here or a dedicated branch)

## Summary

Let users define **permanent, on-screen anchor groups** in client Options.
Each group renders as a persistent button widget (gump `0x25F8` normal /
`0x25F9` hover) with a game-font label. When a plugin/script opens a status bar
targeting that group's id (`IStatusBars.OpenStatusBar(serial, x, y, moveIfExists,
groupId)`), the bar automatically snaps into the group's grid
(columns × rows) laid out relative to the widget.

This also fixes an existing bug: the plugin status-bar grid capacity and column
count are currently **global** (single `Profile.PluginStatusBarMaxRows/Columns`
pair shared by every group), so with many enemies bars overlap or land outside
the intended row/column layout. Per-group grids give each group its own budget.

## Current state (as-built)

- Public API: `src/ClassicUO.PluginApi/IStatusBars.cs` —
  `OpenStatusBar(uint serial, int x, int y, bool moveIfExists = true, int groupId = 0)`.
- Client impl: `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs`
  - `OpenStatusBar` creates a `HealthBarGumpCustom`, `UIManager.Add`, then
    `AddToGroup(groupId, bar)` when `groupId != 0`.
  - `PluginStatusBarGroups` tracks `_groups` (id → `AnchorManager.AnchorGroup`)
    and `_members` (id → insertion-ordered `List<BaseHealthBarGump>`).
  - `AddToGroup` seeds the `AnchorGroup` at the **first bar's** plugin-supplied
    position, then stacks column-major via `GridCell(index, maxRows)` and
    `AnchorManager.DropControl`.
  - Grid dims come from `ResolveMaxRows()` / `ResolveMaxColumns()` →
    `Profile.PluginStatusBarMaxRows` (default 10) / `PluginStatusBarMaxColumns`
    (default 1). **Single global pair — the bug.**
  - `IsCapacityReached(liveCount, maxRows, maxColumns)` = `liveCount >= rows*cols`.
- Anchor machinery: `src/ClassicUO.Client/Game/Managers/AnchorManager.cs`
  (`AnchorGroup`, `controlMatrix`, `DropControl`, `UpdateLocation` drags the
  whole group as a unit). Cell sizes read from `AnchorableGump.GroupMatrixWidth/
  Height` — `BaseHealthBarGump` overrides these to `Width`/`Height`.
- Options: `OptionsGump.cs` renders `_pluginStatusBarMaxRows` /
  `_pluginStatusBarMaxColumns` input fields (`~:1181-1215`, parsed back
  `~:4291-4304`). Dynamic option lists use `ScrollArea` + section
  `Add`/`AddRight`.

## Chosen approach

**Approach A — widget owns position; bars form their own healthbar-only
`AnchorGroup` beside it.** The widget is a standalone draggable gump storing
`x,y`; it is **not** part of the anchor matrix, so grid cell math
(`GroupMatrixWidth/Height`) stays pure-healthbar and unchanged. Dragging the
widget moves the widget plus all its live member bars by the same delta.

Rejected: (B) widget as an `AnchorableGump` matrix cell — mixes a button-sized
cell into the healthbar grid, messy math. (C) purely visual widget, no live
`AnchorGroup` — loses drag-as-unit and existing snapping.

## Design

### 1. Data model — `PluginAnchorGroupDef`

New serializable class under `src/ClassicUO.Client/Configuration/`, stored as a
list on `Profile`:

| Field       | Type        | Notes |
| ----------- | ----------- | ----- |
| `Id`        | `int`       | User-entered; matches the script's `groupId`. Must be unique and non-zero. |
| `Label`     | `string`    | Drawn in game font beside the button. |
| `Columns`   | `int`       | Grid width, clamped `>= 1`. |
| `Rows`      | `int`       | Grid height, clamped `>= 1`. |
| `Fill`      | `FillOrder` enum | `ColumnMajor` (default) or `RowMajor`. Per-group. |
| `X`, `Y`    | `int`       | Widget screen position; persisted, rewritten on drag. |
| `Locked`    | `bool`      | Toggled by shift+left-click; blocks drag when true. |

- `Profile.PluginAnchorGroups` = `List<PluginAnchorGroupDef>` (new, defaults to
  empty). Serialized with the existing profile JSON, same mechanism as other
  profile collections.
- `FillOrder` enum lives near the def (Configuration namespace).
- Global `Profile.PluginStatusBarMaxRows/Columns` are **kept** as the fallback
  grid for any `groupId != 0` that has no matching def.

### 2. On-screen widget — `PluginAnchorGroupGump`

New gump class in `src/ClassicUO.Client/Game/UI/Gumps/`, one instance per def,
created on world-load for **every** def, **always visible** (even with 0 bars):

- `GumpPic` graphic `0x25F8` normal, swapped to `0x25F9` in
  `OnMouseEnter`/`OnMouseExit`.
- `Label` control in game font beside the pic (label text from the def).
- **Drag (no modifier):** moves the widget; on each move, shift every live
  member bar of the group by the same `(dx, dy)` so the grid follows. Write the
  final position back to `def.X/Y`. Suppressed when `def.Locked`.
- **Shift + left-click:** toggle `def.Locked`; apply a visual cue (e.g. hue tint
  on the pic) to show locked state.
- **Shift + right-click:** close every bar in the group
  (`PluginStatusBars.CloseStatusBar(serial)` per member), then `PruneEmpty`.
- **Tooltip on hover:** `"<Label>: N / (Columns×Rows)"`, N = live member count
  (`PluginStatusBarGroups.GetLiveMembers(id).Count`).
- Registered with `UIManager`; explicitly **excluded** from the anchor matrix.
- The widget is bound to its def by `Id`.

### 3. Layout engine — extend `PluginStatusBars` / `PluginStatusBarGroups`

- Resolve grid config **per group id**, def first then global fallback:
  - `ResolveMaxRows(int groupId)` → def's `Rows` if a def with that id exists,
    else `Profile.PluginStatusBarMaxRows` (existing default).
  - `ResolveMaxColumns(int groupId)` → def's `Columns`, else global.
  - Keep the existing no-arg resolvers as thin wrappers, or migrate call sites.
- `GridCell(int index, int rows, int cols, FillOrder fill)` — generalize the
  current column-major helper to also support row-major:
  - ColumnMajor: `column = index / rows`, `row = index % rows` (today's behavior).
  - RowMajor: `row = index / cols`, `column = index % cols`.
- `IsCapacityReached(liveCount, rows, cols)` unchanged (`>= rows*cols`), now
  evaluated with **per-group** dims → fixes the overflow/overlap bug.
- **Seed position:** in `AddToGroup`, when the group has a def, seed the first
  bar (and thus the `AnchorGroup`) at the widget's anchor origin
  (`def.X`, `def.Y + widgetHeight`) rather than the plugin-supplied `x,y`.
  Undefined groups keep seeding at plugin `x,y` (unchanged fallback path).
- Neighbor selection for `DropControl` follows the resolved `Fill`:
  ColumnMajor stacks south within a column then starts a new column east (as
  today); RowMajor stacks east within a row then starts a new row south.

### 4. Options UI — new "Anchor Groups" list in `OptionsGump.cs`

- New subsection near the existing plugin status-bar options.
- Dynamic `ScrollArea` list. Each row: input fields for `Id`, `Label`,
  `Columns`, `Rows`, a `Fill` dropdown (`Combobox`), and a **[X]** delete button.
- **[+ Add]** button appends a new default def (`Id = 0`, `Label = ""`,
  `1 × 1`, `ColumnMajor`, position defaulted to a visible spot).
- On **Apply**: validate — ids unique and non-zero, `Columns/Rows >= 1`; write
  the list back to `Profile.PluginAnchorGroups`; then **rebuild** on-screen
  widgets (dispose existing `PluginAnchorGroupGump`s, recreate from defs).
- The existing global `MaxRows`/`MaxColumns` inputs stay, relabeled to indicate
  they are the **fallback** grid for undefined groups.

### 5. Lifecycle / persistence

- Widgets created from defs on world-load, alongside the existing profile gump
  restore in `Profile.cs`. Disposed on logout.
- `def.X/Y/Locked` saved on profile save; edited defs re-materialize widgets
  (handled by the Options rebuild and by load).
- `PluginStatusBarGroups` stays keyed by id. A def-backed group binds its
  `AnchorGroup` to the def's widget for drag-follow.

### 6. Edge cases

- **Duplicate / zero id in Options:** validation rejects on Apply (surface a
  warning; keep the editor open for correction).
- **Bar closed by death / out-of-range:** member pruned
  (`GetLiveMembers` drops disposed), slot frees, next open refills it.
- **Group grid full:** new `OpenStatusBar` for that group is **dropped** (bar
  rejected) — no free-float, no replace. (Chosen behavior.)
- **Def deleted while bars open:** close its bars and dispose its widget.
- **groupId `0`:** never grouped (unchanged).
- **`groupId != 0` with no def:** falls back to global grid + plugin-supplied
  seed position (today's behavior preserved).

### 7. Testing (xUnit, `tests/ClassicUO.UnitTests`)

- `GridCell` — column-major and row-major cases across several indices.
- `IsCapacityReached` — per-group with distinct rows/cols per group id.
- `ResolveMaxRows(groupId)` / `ResolveMaxColumns(groupId)` — def-present vs
  fallback-to-global resolution.
- `PluginAnchorGroupDef` serialization round-trip.
- Widget UI interaction (drag/lock/clear/tooltip) is **not** unit-tested,
  matching the repo's gump-testing pattern.

## Out of scope

- Packet mutation, in-world overlays, or any plugin-API surface change beyond
  reusing the existing `groupId` parameter.
- Non-plugin (classic drag-to-open) health bars — unaffected.
- Migration UI for pre-existing profiles beyond defaulting
  `PluginAnchorGroups` to empty.
