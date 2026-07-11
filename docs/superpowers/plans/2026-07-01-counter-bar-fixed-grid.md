# Fixed-Grid Counter Bar (Opt-In) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in "fixed grid" mode to the counter bar that sizes the gump to exactly `rows × columns` cells, locks free resize, and scrolls overflow vertically — leaving default (free-resize) behavior unchanged.

**Architecture:** Pure grid math lives in a static, UI-free helper (`CounterBarGridMath`) so it is unit-testable without instantiating gumps. `ResizableGump` gains a `ResizeEnabled` lock. `CounterBarGump` gets grid state + `ApplyGridSettings` that recomputes size, toggles the lock, and drives layout/scroll. `OptionsGump` wires a checkbox + rows/cols inputs (resolving CS0649). Grid settings persist per profile and per saved gump.

**Tech Stack:** C# / .NET 10, FNA-XNA, xUnit. Client internals visible to `ClassicUO.UnitTests` via `InternalsVisibleTo`.

## Global Constraints

- Toolchain: `.NET 10` (`net10.0`), `LangVersion=Preview`, `AllowUnsafeBlocks=true`.
- License header on every new source file: `// SPDX-License-Identifier: BSD-2-Clause`.
- Test framework: **xUnit** in `tests/ClassicUO.UnitTests`.
- Defaults (verbatim from spec): `rows = 3`, `columns = 10`, `useFixedGrid = false`.
- Border size: `BoderSize` (note existing misspelling) returns `4`. Grid pixel size formula: `width = cols * cellSize + BoderSize * 2`, `height = rows * cellSize + BoderSize * 2` — matches the existing `SnapToGrid` math.
- Non-goals: no change to free-resize when grid off; no horizontal scroll; no auto-grow of rows/cols.

---

### Task 1: Grid math helper (pure, TDD)

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Gumps/CounterBarGridMath.cs`
- Test: `tests/ClassicUO.UnitTests/Game/UI/CounterBarGridMathTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (used by Tasks 4 and this test):
  - `static (int width, int height) CounterBarGridMath.GridPixelSize(int rows, int cols, int cellSize, int border)`
  - `static int CounterBarGridMath.GridCapacity(int rows, int cols)`
  - `static int CounterBarGridMath.MaxScroll(int itemCount, int rows, int cols)`

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/UI/CounterBarGridMathTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class CounterBarGridMathTests
    {
        [Fact]
        public void GridPixelSize_UsesColsForWidthRowsForHeight()
        {
            var (w, h) = CounterBarGridMath.GridPixelSize(3, 10, 40, 4);
            Assert.Equal(10 * 40 + 4 * 2, w); // 408
            Assert.Equal(3 * 40 + 4 * 2, h);  // 128
        }

        [Fact]
        public void GridPixelSize_ClampsRowsAndColsToAtLeastOne()
        {
            var (w, h) = CounterBarGridMath.GridPixelSize(0, 0, 40, 4);
            Assert.Equal(1 * 40 + 8, w);
            Assert.Equal(1 * 40 + 8, h);
        }

        [Fact]
        public void GridCapacity_IsRowsTimesCols()
        {
            Assert.Equal(30, CounterBarGridMath.GridCapacity(3, 10));
        }

        [Theory]
        [InlineData(0, 3, 10, 0)]   // no items
        [InlineData(30, 3, 10, 0)]  // exactly full
        [InlineData(31, 3, 10, 1)]  // one over -> one extra row
        [InlineData(50, 3, 10, 2)]  // ceil(50/10)=5 rows, 5-3=2
        public void MaxScroll_CountsOverflowRows(int items, int rows, int cols, int expected)
        {
            Assert.Equal(expected, CounterBarGridMath.MaxScroll(items, rows, cols));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~CounterBarGridMathTests"`
Expected: FAIL — `CounterBarGridMath` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `src/ClassicUO.Client/Game/UI/Gumps/CounterBarGridMath.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Pure grid math for the fixed-grid counter bar. UI-free so it can be
    /// unit-tested without instantiating gumps.
    /// </summary>
    internal static class CounterBarGridMath
    {
        public static (int width, int height) GridPixelSize(int rows, int cols, int cellSize, int border)
        {
            rows = Math.Max(1, rows);
            cols = Math.Max(1, cols);
            return (cols * cellSize + border * 2, rows * cellSize + border * 2);
        }

        public static int GridCapacity(int rows, int cols)
        {
            return Math.Max(1, rows) * Math.Max(1, cols);
        }

        public static int MaxScroll(int itemCount, int rows, int cols)
        {
            rows = Math.Max(1, rows);
            cols = Math.Max(1, cols);
            int totalRows = (Math.Max(0, itemCount) + cols - 1) / cols; // ceil
            return Math.Max(0, totalRows - rows);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~CounterBarGridMathTests"`
Expected: PASS (6 cases).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/CounterBarGridMath.cs tests/ClassicUO.UnitTests/Game/UI/CounterBarGridMathTests.cs
git commit -m "feat(counters): pure grid-math helper for fixed-grid counter bar"
```

---

### Task 2: Profile fields

**Files:**
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs:231`

**Interfaces:**
- Consumes: nothing.
- Produces (used by Tasks 4, 5): `Profile.CounterBarUseFixedGrid` (bool), `Profile.CounterBarRows` (int), `Profile.CounterBarColumns` (int).

No unit test: these are data-only auto-properties serialized by the existing profile mechanism; correctness is covered by the build and by Task 5 wiring.

- [ ] **Step 1: Add the properties**

In `src/ClassicUO.Client/Configuration/Profile.cs`, immediately after line 231 (`public int CounterBarCellSize { get; set; } = 40;`) add:

```csharp
        public bool CounterBarUseFixedGrid { get; set; } = false;
        public int CounterBarRows { get; set; } = 3;
        public int CounterBarColumns { get; set; } = 10;
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Client/Configuration/Profile.cs
git commit -m "feat(counters): profile fields for fixed-grid rows/columns"
```

---

### Task 3: Resize lock on ResizableGump

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/ResizableGump.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (used by Task 4): `protected bool ResizableGump.ResizeEnabled { get; set; }` — default `true`. Setting `false` hides the resize grip and makes drag-resize a no-op.

No dedicated unit test (requires UI instantiation); verified via build and Task 4 behavior. Default `true` guarantees no behavior change for other `ResizableGump` subclasses.

- [ ] **Step 1: Add the `ResizeEnabled` property**

In `src/ClassicUO.Client/Game/UI/Gumps/ResizableGump.cs`, add a backing field next to the other fields (after line 16, `private int _minW;`):

```csharp
        private bool _resizeEnabled = true;
```

Then add the property just below the `ShowBorder` property (after line 83):

```csharp
        protected bool ResizeEnabled
        {
            get => _resizeEnabled;
            set
            {
                _resizeEnabled = value;
                _button.IsVisible = value;
            }
        }
```

- [ ] **Step 2: Guard the drag-resize branch**

In `Update()`, change the resize condition (line 146) from:

```csharp
            if (_clicked && offset != Point.Zero)
```

to:

```csharp
            if (_clicked && _resizeEnabled && offset != Point.Zero)
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/ResizableGump.cs
git commit -m "feat(ui): add ResizeEnabled lock to ResizableGump"
```

---

### Task 4: CounterBarGump grid mode

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/CounterBarGump.cs`

**Interfaces:**
- Consumes: `CounterBarGridMath.GridPixelSize/MaxScroll` (Task 1); `ResizableGump.ResizeEnabled` (Task 3).
- Produces (used by Task 5): `public void CounterBarGump.ApplyGridSettings(bool useFixed, int rows, int cols)`.

This task changes gump internals only; there is no headless way to instantiate a gump, so it is covered by build + the Task 1 math tests + manual verification. Add each edit, then build.

- [ ] **Step 1: Add grid state fields**

In `CounterBarGump.cs`, after the existing fields block (after line 32, `private ScissorControl _scissor;`) add:

```csharp
        private bool _useFixedGrid;
        private int _rows = 3;
        private int _cols = 10;
        private int _scrollOffset;
        private int _freeWidth = 200;
        private int _freeHeight = 80;
```

- [ ] **Step 2: Make `SnapToGrid` a no-op in fixed mode**

At the top of `SnapToGrid()` (after line 54, `private void SnapToGrid()`), add:

```csharp
            if (_useFixedGrid)
            {
                return;
            }
```

- [ ] **Step 3: Add `ApplyGridSettings`**

Add this method to the class (e.g. after `SnapToGrid`, before `ConfigureContextMenu`):

```csharp
        public void ApplyGridSettings(bool useFixed, int rows, int cols)
        {
            rows = Math.Max(1, rows);
            cols = Math.Max(1, cols);

            // Entering fixed mode: remember the current free-resize size so we
            // can restore it when the grid is turned back off.
            if (useFixed && !_useFixedGrid)
            {
                _freeWidth = Width;
                _freeHeight = Height;
            }

            _useFixedGrid = useFixed;
            _rows = rows;
            _cols = cols;

            if (useFixed)
            {
                ResizeEnabled = false;

                var (w, h) = CounterBarGridMath.GridPixelSize(rows, cols, _rectSize, BoderSize);
                Point size = ResizeWindow(new Point(w, h));
                Width = size.X;
                Height = size.Y;
            }
            else
            {
                ResizeEnabled = true;
                _scrollOffset = 0;

                int w = _freeWidth > 0 ? _freeWidth : 200;
                int h = _freeHeight > 0 ? _freeHeight : 80;
                Point size = ResizeWindow(new Point(w, h));
                Width = size.X;
                Height = size.Y;
            }

            OnResize(); // calls SetupLayout
        }
```

- [ ] **Step 4: Recompute grid size when cell size changes**

In `SetCellSize` (lines 118-138), replace the trailing `SnapToGrid(); SetupLayout();` (lines 135-136) so fixed mode recomputes exact size. Change:

```csharp
                SnapToGrid();
                SetupLayout();
```

to:

```csharp
                if (_useFixedGrid)
                {
                    ApplyGridSettings(true, _rows, _cols);
                }
                else
                {
                    SnapToGrid();
                    SetupLayout();
                }
```

- [ ] **Step 5: Fixed-mode layout in `SetupLayout`**

In `SetupLayout()`, replace the item-placement loop (lines 234-254, the `for` loop that wraps by pixel width) with a branch on grid mode:

```csharp
            if (_useFixedGrid)
            {
                for (int i = 0; i < _dataBox.Children.Count; i++)
                {
                    CounterItem c = _dataBox.Children[i] as CounterItem;
                    if (c == null || c.IsDisposed)
                    {
                        continue;
                    }

                    int col = i % _cols;
                    int row = i / _cols;

                    c.X = col * _rectSize + BORDER_LEFT;
                    c.Y = (row - _scrollOffset) * _rectSize + BORDER_TOP;
                    c.Width = _rectSize - BORDER_LEFT - BORDER_RIGHT;
                    c.Height = _rectSize - BORDER_TOP - BORDER_BOTTOM;
                }

                return;
            }

            for (int i = 0; i < _dataBox.Children.Count; i++)
            {
                CounterItem c = _dataBox.Children[i] as CounterItem;
                if (c != null && !c.IsDisposed)
                {
                    c.X = x + BORDER_LEFT;
                    c.Y = y + BORDER_TOP;
                    c.Width = _rectSize - BORDER_LEFT - BORDER_RIGHT;
                    c.Height = _rectSize - BORDER_TOP - BORDER_BOTTOM;

                    x += _rectSize;

                    if (x + _rectSize > width)
                    {
                        x = 0;
                        y += _rectSize;
                    }

                    continue;
                }
            }
```

(The `_scissor` set earlier in `SetupLayout` already clips rows scrolled above/below the window.)

- [ ] **Step 6: Add mouse-wheel scroll (fixed mode only)**

Add this override to the class (e.g. after `OnMouseUp`, near line 288). `MouseEventType` comes from the already-imported `ClassicUO.Input` namespace:

```csharp
        protected override void OnMouseWheel(Input.MouseEventType delta)
        {
            if (!_useFixedGrid)
            {
                return;
            }

            int maxScroll = CounterBarGridMath.MaxScroll(_dataBox.Children.Count, _rows, _cols);

            if (delta == Input.MouseEventType.WheelScrollUp)
            {
                _scrollOffset--;
            }
            else if (delta == Input.MouseEventType.WheelScrollDown)
            {
                _scrollOffset++;
            }
            else
            {
                return;
            }

            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
            SetupLayout();
        }
```

- [ ] **Step 7: Persist grid settings in `Save`**

In `Save` (after line 319, `writer.WriteAttributeString("readonly", ReadOnly.ToString());`) add:

```csharp
            writer.WriteAttributeString("usefixedgrid", _useFixedGrid.ToString());
            writer.WriteAttributeString("rows", _rows.ToString());
            writer.WriteAttributeString("cols", _cols.ToString());
            writer.WriteAttributeString("freewidth", _freeWidth.ToString());
            writer.WriteAttributeString("freeheight", _freeHeight.ToString());
```

- [ ] **Step 8: Restore grid settings and fold the legacy parse**

In `Restore`, after the `readonly` parse block (lines 371-374) and before `BuildGump();` (line 376), read the grid attributes:

```csharp
            bool.TryParse(xml.GetAttribute("usefixedgrid"), out bool useFixedGrid);

            if (!int.TryParse(xml.GetAttribute("rows"), out int gridRows))
            {
                gridRows = 3;
            }

            if (!int.TryParse(xml.GetAttribute("cols"), out int gridCols))
            {
                gridCols = 10;
            }

            if (!int.TryParse(xml.GetAttribute("freewidth"), out int freeWidth))
            {
                freeWidth = width;
            }

            if (!int.TryParse(xml.GetAttribute("freeheight"), out int freeHeight))
            {
                freeHeight = height;
            }
```

Then, at the end of `Restore`, replace the final two lines (lines 408-410):

```csharp
            // resize only after items have been added
            // because an empty counter bar will always receive a minimum width for the help text
            ResizeWindow(new Point(width, height));

            SetupLayout();
```

with:

```csharp
            // resize only after items have been added
            // because an empty counter bar will always receive a minimum width for the help text
            Point size = ResizeWindow(new Point(width, height));
            Width = size.X;
            Height = size.Y;

            _freeWidth = freeWidth;
            _freeHeight = freeHeight;

            ApplyGridSettings(useFixedGrid, gridRows, gridCols);
```

Note: the existing legacy `columns`/`rows` → width/height fallback (lines 349-363) is retained as-is; it only fires for old saves that lack `width`/`height`, and now feeds the same `width`/`height` locals used above.

- [ ] **Step 9: Verify it builds**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeds.

- [ ] **Step 10: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/CounterBarGump.cs
git commit -m "feat(counters): fixed-grid mode with locked resize and overflow scroll"
```

---

### Task 5: Options UI wiring (resolves CS0649)

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs:56` (field), `:3143` (BuildCounters), `:3772-3783` (reset defaults), `:4188-4238` (Apply)
- Modify: `src/ClassicUO.Client/Resources/ResGumps.resx`
- Modify: `src/ClassicUO.Client/Resources/ResGumps.Designer.cs`

**Interfaces:**
- Consumes: `Profile.CounterBarUseFixedGrid/CounterBarRows/CounterBarColumns` (Task 2); `CounterBarGump.ApplyGridSettings` (Task 4); existing `_rows`, `_columns` `InputField` fields (line 123).
- Produces: user-visible controls; assigning `_rows`/`_columns` is what clears warning CS0649.

- [ ] **Step 1: Add the three resource strings (resx)**

In `src/ClassicUO.Client/Resources/ResGumps.resx`, after the `CellSize` entry (line 657-659) add:

```xml
  <data name="CounterUseFixedGrid" xml:space="preserve">
    <value>Use fixed grid (locks resize)</value>
  </data>
  <data name="CounterRows" xml:space="preserve">
    <value>Rows:</value>
  </data>
  <data name="CounterColumns" xml:space="preserve">
    <value>Columns:</value>
  </data>
```

- [ ] **Step 2: Add the matching designer accessors**

In `src/ClassicUO.Client/Resources/ResGumps.Designer.cs`, after the `CellSize` property (ends line ~575), add three properties mirroring the existing generated pattern:

```csharp
        /// <summary>
        ///   Looks up a localized string similar to Use fixed grid (locks resize).
        /// </summary>
        public static string CounterUseFixedGrid {
            get {
                return ResourceManager.GetString("CounterUseFixedGrid", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to Rows:.
        /// </summary>
        public static string CounterRows {
            get {
                return ResourceManager.GetString("CounterRows", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to Columns:.
        /// </summary>
        public static string CounterColumns {
            get {
                return ResourceManager.GetString("CounterColumns", resourceCulture);
            }
        }
```

- [ ] **Step 3: Add the checkbox field**

In `OptionsGump.cs` at line 56, add `_useFixedGridCheckbox` to the counters checkbox declaration:

```csharp
        private Checkbox _enableCounters, _highlightOnChange, _highlightOnAmount, _enableAbbreviatedAmount, _useFixedGridCheckbox;
```

- [ ] **Step 4: Build the checkbox + rows/cols inputs**

In `BuildCounters`, replace line 3145 (`Add(rightArea, PAGE);`) with the new controls followed by the `Add`:

```csharp
            startX = 5;
            startX += 40;
            startY += 40;

            _useFixedGridCheckbox = AddCheckBox
            (
                rightArea,
                ResGumps.CounterUseFixedGrid,
                _currentProfile.CounterBarUseFixedGrid,
                startX,
                startY
            );

            startY += _useFixedGridCheckbox.Height + 2;

            _rows = AddInputField
            (
                rightArea,
                startX,
                startY,
                50,
                TEXTBOX_HEIGHT,
                ResGumps.CounterRows,
                50,
                false,
                true,
                3
            );
            _rows.SetText(_currentProfile.CounterBarRows.ToString());

            startX += 120;

            _columns = AddInputField
            (
                rightArea,
                startX,
                startY,
                50,
                TEXTBOX_HEIGHT,
                ResGumps.CounterColumns,
                50,
                false,
                true,
                3
            );
            _columns.SetText(_currentProfile.CounterBarColumns.ToString());

            Add(rightArea, PAGE);
```

- [ ] **Step 5: Fix the reset-defaults block (case 9)**

In the reset switch, `case 9: // counters` (lines 3772-3783), change the two orphaned `SetText` lines and add the checkbox reset. Replace:

```csharp
                    _columns.SetText("1");
                    _rows.SetText("1");
```

with:

```csharp
                    _useFixedGridCheckbox.IsChecked = false;
                    _columns.SetText("10");
                    _rows.SetText("3");
```

- [ ] **Step 6: Apply grid settings on save**

In the counters Apply section, after line 4208 (`_currentProfile.CounterBarDisplayAbbreviatedAmount = _enableAbbreviatedAmount.IsChecked;`) and before line 4210 (`CounterBarGump counterGump = UIManager.GetGump<CounterBarGump>();`), add:

```csharp
            bool useFixedGrid = _useFixedGridCheckbox.IsChecked;

            if (!int.TryParse(_rows.Text, out int gridRows) || gridRows < 1)
            {
                gridRows = 3;
                _rows.SetText("3");
            }

            if (!int.TryParse(_columns.Text, out int gridCols) || gridCols < 1)
            {
                gridCols = 10;
                _columns.SetText("10");
            }

            _currentProfile.CounterBarUseFixedGrid = useFixedGrid;
            _currentProfile.CounterBarRows = gridRows;
            _currentProfile.CounterBarColumns = gridCols;
```

Then, in the same block, update the two places that touch an existing `counterGump` so the grid is applied. Change line 4232:

```csharp
                    counterGump.IsEnabled = counterGump.IsVisible = _currentProfile.CounterBarEnabled;
```

to:

```csharp
                    counterGump.IsEnabled = counterGump.IsVisible = _currentProfile.CounterBarEnabled;
                    counterGump.ApplyGridSettings(useFixedGrid, gridRows, gridCols);
```

and change the `else if` tail (lines 4235-4238):

```csharp
            else if (counterGump != null)
            {
                counterGump.SetCellSize(_currentProfile.CounterBarCellSize);
            }
```

to:

```csharp
            else if (counterGump != null)
            {
                counterGump.SetCellSize(_currentProfile.CounterBarCellSize);
                counterGump.ApplyGridSettings(useFixedGrid, gridRows, gridCols);
            }
```

- [ ] **Step 7: Verify build and that CS0649 is gone**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeds; no `CS0649` for `OptionsGump._rows` / `_columns`.

Confirm the warning is absent:
Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug 2>&1 | grep -c CS0649`
Expected: `0`

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS (including `CounterBarGridMathTests`).

- [ ] **Step 9: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs src/ClassicUO.Client/Resources/ResGumps.resx src/ClassicUO.Client/Resources/ResGumps.Designer.cs
git commit -m "feat(counters): options UI for fixed-grid mode; resolve CS0649"
```

---

## Manual Verification (after Task 5)

Not automatable (needs a running client + UO data dir). Perform once:

1. Options → Counters: check "Use fixed grid", set rows=3 cols=10, Apply. Counter bar sizes to `10*cell+8` × `3*cell+8`; resize grip hidden; dragging the corner does nothing.
2. Add > 30 items; mouse-wheel over the bar scrolls rows vertically; scroll clamps at top and bottom.
3. Uncheck "Use fixed grid", Apply. Bar returns to the pre-grid free size (or 200×80 if none); grip returns; drag-resize works.
4. Save profile, restart client: grid mode, rows, cols persist; existing (pre-feature) saved counter bars load unchanged.

## Self-Review Notes

- **Spec coverage:** Req 1 (opt-in checkbox, default off) → T2+T5; Req 2 (exact size) → T1+T4; Req 3 (resize lock) → T3+T4; Req 4 (unbounded + vertical scroll) → T4 step 5/6; Req 5 (persist) → T2+T4 step 7/8+T5 step 6; Req 6 (restore free size) → T4 `_freeWidth/_freeHeight`. CS0649 resolved in T5 step 3/4. Testing component → T1.
- **Warnings other than CS0649** (CS8632, CS0628, CS4014, CS0168, CS0169, IL3050, CA2022) are explicitly *handled separately* per the spec and are out of scope here.
- **Type consistency:** `ApplyGridSettings(bool, int, int)`, `GridPixelSize/GridCapacity/MaxScroll`, and `ResizeEnabled` names are used identically across tasks.
