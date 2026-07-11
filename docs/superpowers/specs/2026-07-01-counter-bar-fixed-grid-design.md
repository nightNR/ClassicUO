# Fixed-Grid Counter Bar (Opt-In) — Design

Date: 2026-07-01
Status: Approved
Branch: feat/new-features

## Summary

Add an opt-in "fixed grid" mode to the counter bar. When enabled, the user
specifies an exact number of rows and columns; the counter bar sizes itself to
exactly `rows × columns` cells and can no longer be freely drag-resized. When
disabled (the default), the counter bar keeps its current free-resize behavior,
so existing users see no change.

This work also resolves compiler warning CS0649 in `OptionsGump.cs`, where the
`_rows` and `_columns` `InputField` members are declared and referenced
(`_columns.SetText("1")` / `_rows.SetText("1")` at lines 3776-3777) but never
assigned. Those fields are leftovers from an older fixed-grid design that was
replaced by free resize; this feature re-introduces the grid concept as an
explicit, opt-in mode and wires the fields up properly.

## Background

- `CounterBarGump` (`src/ClassicUO.Client/Game/UI/Gumps/CounterBarGump.cs`)
  derives from `ResizableGump`. It is a free-resize window that snaps its size
  to a multiple of the cell size (`_rectSize`) via `SnapToGrid`. Item layout in
  `SetupLayout` wraps by pixel width.
- The counter bar previously supported explicit `rows`/`columns`. That model was
  removed in favor of free resize; only legacy-parsing remnants survive in
  `CounterBarGump.Restore` (lines 349-363) and the orphaned `_rows`/`_columns`
  fields in `OptionsGump`.
- Resize is driven by `ResizableGump`'s `_button` grip and the `_clicked` branch
  in `ResizableGump.Update`. There is currently no way to lock resizing.

## Requirements

1. Fixed grid is **opt-in** via a new checkbox in Options → Counters. Default
   off; no behavior change for existing users or their saved gumps.
2. When on, the user enters rows and columns. The gump sizes to exactly
   `columns × _rectSize (+ border)` wide and `rows × _rectSize (+ border)` tall.
3. When on, free drag-resize is **locked** (grip hidden, resize input ignored).
   The grid is authoritative.
4. Item count is unbounded. When items exceed `rows × columns`, excess items
   **scroll vertically** within the fixed window (content is already clipped by
   the existing scissor control).
5. Grid mode, rows, and columns persist per profile and per saved gump.
6. Toggling fixed grid **off** restores the last saved free-resize size
   (fallback `200 × 80` if none).

Defaults: `rows = 3`, `columns = 10`, `useFixedGrid = false`.

## Components

### 1. Profile — `Configuration/Profile.cs`

Add after `CounterBarCellSize` (line 231):

```csharp
public bool CounterBarUseFixedGrid { get; set; } = false;
public int CounterBarRows { get; set; } = 3;
public int CounterBarColumns { get; set; } = 10;
```

### 2. Resize lock — `Game/UI/Gumps/ResizableGump.cs`

Add:

```csharp
protected bool ResizeEnabled { get; set; } = true;
```

- When set, toggle the grip `_button.IsVisible` accordingly.
- In `Update()`, guard the `_clicked` resize branch with `ResizeEnabled` so a
  locked gump ignores drag input. No behavior change while `true` (the default).

### 3. Counter gump — `Game/UI/Gumps/CounterBarGump.cs`

New state: `bool _useFixedGrid`, `int _rows`, `int _cols`, `int _scrollOffset`.

`ApplyGridSettings(bool useFixed, int rows, int cols)`:
- Clamp `rows`/`cols` to at least 1.
- Store state.
- If `useFixed`:
  - `ResizeEnabled = false`.
  - Compute exact size: `width = cols * _rectSize + BoderSize * 2`,
    `height = rows * _rectSize + BoderSize * 2`; `ResizeWindow` to it.
  - `SnapToGrid` short-circuits (no-op) while fixed.
- Else:
  - `ResizeEnabled = true`.
  - Restore free-resize; reset `_scrollOffset = 0`.
- Call `SetupLayout()`.

`SetupLayout`:
- When fixed, wrap items by column count (`_cols`) rather than pixel width, and
  offset vertically by `_scrollOffset`.
- When not fixed, unchanged (wrap by width).

Overflow scroll (fixed mode only):
- Compute `maxScroll` from total rows of items vs visible `_rows`.
- Add a mouse-wheel handler that adjusts `_scrollOffset`, clamped to
  `[0, maxScroll]`, then re-lays out. Scissor clips as today.

`Save`/`Restore`:
- Persist `usefixedgrid`, `rows`, `cols` attributes.
- `Restore` reads them (the existing legacy `rows`/`columns` parse at 349-363 is
  folded into this), applies via `ApplyGridSettings`.

### 4. Options UI — `Game/UI/Gumps/OptionsGump.cs`

In `BuildCounters` (starts line 3023):
- Add `_useFixedGridCheckbox` bound to `CounterBarUseFixedGrid`.
- Create and **assign** the `_rows` and `_columns` `InputField`s (numeric),
  seeded from `CounterBarRows` / `CounterBarColumns`. This assignment is what
  resolves CS0649. The reset-defaults lines at 3776-3777 become valid.

In the counters Apply block (~4188):
- Read `_useFixedGridCheckbox.IsChecked`, parse `_rows.Text` / `_columns.Text`
  (fallback to defaults on parse failure), write to profile.
- Call `counterGump.ApplyGridSettings(useFixed, rows, cols)` on the active
  `CounterBarGump`.

### 5. Testing — `tests/ClassicUO.UnitTests`

Extract grid math into a small pure helper (static methods), e.g.:
- `GridPixelSize(rows, cols, cellSize, border) -> (w, h)`
- `GridCapacity(rows, cols) -> int`
- `MaxScroll(itemCount, rows, cols) -> int`

Add xUnit cases covering: exact pixel size, capacity, and overflow/max-scroll
(including 0 items, exactly full, and over-capacity). This keeps the tested
logic free of UI instantiation.

## Warnings

- **Resolved by this feature:** CS0649 (`OptionsGump._rows` / `_columns`).
- **Handled separately** (mechanical, per the warning-fix plan): CS8632
  (NetClient), CS0628 (UltimaLive), CS4014 (WebSocketWrapper), CS0168
  (LocationGoGump), CS0169 (WorldMapGump `_mapLoadingTime`), IL3050
  (ResizableJournal `Enum.GetValues`), CA2022 (WorldMapGump `Stream.Read`).
- **Out of scope:** IL2046 / IL2067 in `external/FNA` (upstream, gitignored).

## Non-Goals

- No change to free-resize behavior when fixed grid is off.
- No horizontal scroll (columns are fixed; only vertical overflow scrolls).
- No auto-grow of rows/columns.
