# Container View Modes (GridView / Standard / Toggle) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-profile setting that renders container gumps as the current art-based view, a paged item grid, or a per-container toggle between the two, with the toggle choice persisted per serial across relogin.

**Architecture:** `ContainerGump` keeps its single type and `GumpType.Container` identity (so every `GetGump<ContainerGump>` integration point keeps working) but branches its child layout on an effective-view resolver. Standard branch is the existing art path, unchanged. Grid branch delegates to a new `GridContainerView : Control` (paged cells) that owns all grid layout and interaction. Pure decision/layout logic is factored into small static helpers (`ContainerViewModeResolver`, `GridContainerLayout`) so it is unit-testable without a graphics context, mirroring the existing `CounterBarGridMath` pattern.

**Tech Stack:** C# (`net10.0`, `LangVersion=Preview`), FNA-XNA UI controls, xUnit tests. UI localization via `ResGumps.resx` + `ResGumps.Designer.cs`.

## Global Constraints

- Target framework `net10.0`; `AllowUnsafeBlocks=true`; match surrounding performance-sensitive style (no gratuitous allocation in hot paths).
- Every new source file carries the project BSD header: `// SPDX-License-Identifier: BSD-2-Clause` as the first line.
- Tests use **xUnit** (`[Fact]` / `[Theory]`), project `tests/ClassicUO.UnitTests`.
- `ClassicUO.Client` internals are visible to `ClassicUO.UnitTests` (`InternalsVisibleTo`), so `internal static` helpers are directly testable.
- New user-facing strings go in **both** `ResGumps.resx` (the `<data>` entry) and `ResGumps.Designer.cs` (the generated property). Do not run a resx code generator; edit both by hand to match the existing entries.
- Profile settings serialize automatically with the rest of `Profile`; no serializer changes needed (same mechanism as `JournalTabs` and `GridLootType`).
- Container items always have `Z == 0`, so "draw/stack order" == linked-list order of `container.Items`. Iterate that list directly; do not sort.

---

### Task 1: Profile settings

**Files:**
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs` (add three properties near the other container settings, ~line 286)
- Test: `tests/ClassicUO.UnitTests/Game/UI/ProfileContainerViewDefaultsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `int Profile.ContainerViewMode { get; set; }` — 0 = Standard (default), 1 = Grid, 2 = Toggle.
  - `bool Profile.ContainerToggleDefaultGrid { get; set; }` — default `false`.
  - `Dictionary<uint, bool> Profile.ContainerGridStates { get; set; }` — default empty, non-null.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/UI/ProfileContainerViewDefaultsTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class ProfileContainerViewDefaultsTests
    {
        [Fact]
        public void NewProfile_HasContainerViewDefaults()
        {
            var p = new Profile();

            Assert.Equal(0, p.ContainerViewMode);
            Assert.False(p.ContainerToggleDefaultGrid);
            Assert.NotNull(p.ContainerGridStates);
            Assert.Empty(p.ContainerGridStates);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ProfileContainerViewDefaultsTests"`
Expected: FAIL to compile — `Profile` does not contain `ContainerViewMode` / `ContainerToggleDefaultGrid` / `ContainerGridStates`.

- [ ] **Step 3: Add the properties**

In `src/ClassicUO.Client/Configuration/Profile.cs`, immediately after the existing `public bool AllowItemsOutsideContainerBounds { get; set; }` line (~286), add:

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

(`System.Collections.Generic` is already imported in this file — it declares other dictionaries such as `JournalTabs`.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ProfileContainerViewDefaultsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Configuration/Profile.cs tests/ClassicUO.UnitTests/Game/UI/ProfileContainerViewDefaultsTests.cs
git commit -m "feat(containers): add container view mode profile settings"
```

---

### Task 2: Effective-view resolver + toggle helper

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Gumps/ContainerViewModeResolver.cs`
- Test: `tests/ClassicUO.UnitTests/Game/UI/Gumps/ContainerViewModeResolverTests.cs`

**Interfaces:**
- Consumes: `Profile.ContainerViewMode`, `Profile.ContainerToggleDefaultGrid`, `Profile.ContainerGridStates` (Task 1) at the call sites, but the helper itself takes plain values.
- Produces:
  - `static bool ContainerViewModeResolver.Resolve(int viewMode, bool toggleDefaultGrid, IReadOnlyDictionary<uint, bool> gridStates, uint serial)`
  - `static bool ContainerViewModeResolver.ComputeToggleValue(bool toggleDefaultGrid, IReadOnlyDictionary<uint, bool> gridStates, uint serial)` — the value to store into `ContainerGridStates[serial]` when the corner button is clicked (inverse of the currently-resolved value).

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/UI/Gumps/ContainerViewModeResolverTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI.Gumps
{
    public class ContainerViewModeResolverTests
    {
        private const uint Serial = 0x4000_0001;

        [Fact]
        public void Standard_AlwaysFalse()
        {
            Assert.False(ContainerViewModeResolver.Resolve(0, true,
                new Dictionary<uint, bool> { [Serial] = true }, Serial));
        }

        [Fact]
        public void Grid_AlwaysTrue()
        {
            Assert.True(ContainerViewModeResolver.Resolve(1, false,
                new Dictionary<uint, bool> { [Serial] = false }, Serial));
        }

        [Fact]
        public void Toggle_Miss_UsesDefault_False()
        {
            Assert.False(ContainerViewModeResolver.Resolve(2, false,
                new Dictionary<uint, bool>(), Serial));
        }

        [Fact]
        public void Toggle_Miss_UsesDefault_True()
        {
            Assert.True(ContainerViewModeResolver.Resolve(2, true,
                new Dictionary<uint, bool>(), Serial));
        }

        [Fact]
        public void Toggle_Hit_UsesStoredValue_OverDefault()
        {
            Assert.True(ContainerViewModeResolver.Resolve(2, false,
                new Dictionary<uint, bool> { [Serial] = true }, Serial));
            Assert.False(ContainerViewModeResolver.Resolve(2, true,
                new Dictionary<uint, bool> { [Serial] = false }, Serial));
        }

        [Fact]
        public void Resolve_NullDictionary_FallsBackToDefault()
        {
            Assert.True(ContainerViewModeResolver.Resolve(2, true, null, Serial));
            Assert.False(ContainerViewModeResolver.Resolve(2, false, null, Serial));
        }

        [Fact]
        public void ComputeToggleValue_AbsentKey_ReturnsInverseOfDefault()
        {
            Assert.True(ContainerViewModeResolver.ComputeToggleValue(false,
                new Dictionary<uint, bool>(), Serial));
            Assert.False(ContainerViewModeResolver.ComputeToggleValue(true,
                new Dictionary<uint, bool>(), Serial));
        }

        [Fact]
        public void ComputeToggleValue_PresentKey_ReturnsInverseOfStored()
        {
            Assert.False(ContainerViewModeResolver.ComputeToggleValue(false,
                new Dictionary<uint, bool> { [Serial] = true }, Serial));
            Assert.True(ContainerViewModeResolver.ComputeToggleValue(true,
                new Dictionary<uint, bool> { [Serial] = false }, Serial));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ContainerViewModeResolverTests"`
Expected: FAIL to compile — `ContainerViewModeResolver` does not exist.

- [ ] **Step 3: Write the resolver**

Create `src/ClassicUO.Client/Game/UI/Gumps/ContainerViewModeResolver.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;

namespace ClassicUO.Game.UI.Gumps
{
    // Pure decision logic for whether a given container serial renders as a grid.
    // Kept free of Profile/UIManager references so it is unit-testable.
    internal static class ContainerViewModeResolver
    {
        // viewMode: 0 = Standard, 1 = Grid, 2 = Toggle.
        public static bool Resolve(
            int viewMode,
            bool toggleDefaultGrid,
            IReadOnlyDictionary<uint, bool> gridStates,
            uint serial
        )
        {
            switch (viewMode)
            {
                case 1:
                    return true;

                case 2:
                    if (gridStates != null && gridStates.TryGetValue(serial, out bool v))
                    {
                        return v;
                    }

                    return toggleDefaultGrid;

                default: // 0 (Standard) and any unknown value
                    return false;
            }
        }

        // Value to store into ContainerGridStates[serial] when the corner toggle is
        // clicked: the inverse of the value currently resolved for this serial.
        public static bool ComputeToggleValue(
            bool toggleDefaultGrid,
            IReadOnlyDictionary<uint, bool> gridStates,
            uint serial
        )
        {
            bool current =
                gridStates != null && gridStates.TryGetValue(serial, out bool v)
                    ? v
                    : toggleDefaultGrid;

            return !current;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ContainerViewModeResolverTests"`
Expected: PASS (9 assertions across 8 facts).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/ContainerViewModeResolver.cs tests/ClassicUO.UnitTests/Game/UI/Gumps/ContainerViewModeResolverTests.cs
git commit -m "feat(containers): add container view mode resolver"
```

---

### Task 3: Grid layout math

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/GridContainerLayout.cs`
- Test: `tests/ClassicUO.UnitTests/Game/UI/GridContainerLayoutTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (all `internal static` on `GridContainerLayout`):
  - Consts: `CELL_SIZE = 50`, `CELL_MARGIN = 4`, `MAX_WIDTH = 300`, `MAX_HEIGHT = 420`, `RESERVED_BOTTOM = 30` (space for page buttons/label).
  - `int Columns()` — columns that fit in `MAX_WIDTH`.
  - `int RowsPerPage()` — rows that fit in `MAX_HEIGHT - RESERVED_BOTTOM`.
  - `int PerPage()` — `Columns() * RowsPerPage()`.
  - `int PageCount(int itemCount)` — at least 1.
  - `(int x, int y, int page) CellPosition(int index)` — pixel position within a page and the 0-based page for the item at `index`.
  - `int GridWidth()` / `int GridHeight()` — pixel size of the cell area (excluding reserved bottom).

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/UI/GridContainerLayoutTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Controls;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class GridContainerLayoutTests
    {
        // cell 50 + margin 4 = 54 stride; MAX_WIDTH 300 -> (300-4)/54 = 5 cols
        [Fact]
        public void Columns_FitWithinMaxWidth()
        {
            Assert.Equal(5, GridContainerLayout.Columns());
        }

        // (MAX_HEIGHT 420 - RESERVED_BOTTOM 30 - margin 4)/54 = 386/54 = 7 rows
        [Fact]
        public void RowsPerPage_FitWithinMaxHeight()
        {
            Assert.Equal(7, GridContainerLayout.RowsPerPage());
        }

        [Fact]
        public void PerPage_IsColumnsTimesRows()
        {
            Assert.Equal(35, GridContainerLayout.PerPage());
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(35, 1)]   // exactly one page
        [InlineData(36, 2)]   // one over -> second page
        [InlineData(70, 2)]
        [InlineData(71, 3)]
        public void PageCount_CountsPages(int itemCount, int expected)
        {
            Assert.Equal(expected, GridContainerLayout.PageCount(itemCount));
        }

        [Fact]
        public void CellPosition_FirstCell_TopLeftPageZero()
        {
            var (x, y, page) = GridContainerLayout.CellPosition(0);
            Assert.Equal(4, x);   // CELL_MARGIN
            Assert.Equal(4, y);
            Assert.Equal(0, page);
        }

        [Fact]
        public void CellPosition_SecondColumn_SameRow()
        {
            var (x, y, page) = GridContainerLayout.CellPosition(1);
            Assert.Equal(4 + 54, x);
            Assert.Equal(4, y);
            Assert.Equal(0, page);
        }

        [Fact]
        public void CellPosition_WrapsToNextRow_AfterLastColumn()
        {
            // index 5 is the 6th cell -> row 1, col 0 (5 columns per row)
            var (x, y, page) = GridContainerLayout.CellPosition(5);
            Assert.Equal(4, x);
            Assert.Equal(4 + 54, y);
            Assert.Equal(0, page);
        }

        [Fact]
        public void CellPosition_WrapsToNextPage_AfterPerPage()
        {
            // index 35 is the 36th cell -> page 1, back to top-left
            var (x, y, page) = GridContainerLayout.CellPosition(35);
            Assert.Equal(4, x);
            Assert.Equal(4, y);
            Assert.Equal(1, page);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~GridContainerLayoutTests"`
Expected: FAIL to compile — `GridContainerLayout` does not exist.

- [ ] **Step 3: Write the layout helper**

Create `src/ClassicUO.Client/Game/UI/Controls/GridContainerLayout.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.Game.UI.Controls
{
    // Pure paged-grid geometry for GridContainerView. No graphics dependencies so
    // it can be unit-tested directly (mirrors CounterBarGridMath).
    internal static class GridContainerLayout
    {
        public const int CELL_SIZE = 50;
        public const int CELL_MARGIN = 4;
        public const int MAX_WIDTH = 300;
        public const int MAX_HEIGHT = 420;

        // Vertical space reserved at the bottom for Prev/Next buttons and the page label.
        public const int RESERVED_BOTTOM = 30;

        private const int STRIDE = CELL_SIZE + CELL_MARGIN;

        public static int Columns()
        {
            int cols = (MAX_WIDTH - CELL_MARGIN) / STRIDE;
            return cols < 1 ? 1 : cols;
        }

        public static int RowsPerPage()
        {
            int rows = (MAX_HEIGHT - RESERVED_BOTTOM - CELL_MARGIN) / STRIDE;
            return rows < 1 ? 1 : rows;
        }

        public static int PerPage()
        {
            return Columns() * RowsPerPage();
        }

        public static int PageCount(int itemCount)
        {
            int perPage = PerPage();
            int pages = (itemCount + perPage - 1) / perPage;
            return pages < 1 ? 1 : pages;
        }

        public static (int x, int y, int page) CellPosition(int index)
        {
            int cols = Columns();
            int perPage = PerPage();

            int page = index / perPage;
            int inPage = index % perPage;

            int col = inPage % cols;
            int row = inPage / cols;

            int x = CELL_MARGIN + col * STRIDE;
            int y = CELL_MARGIN + row * STRIDE;

            return (x, y, page);
        }

        public static int GridWidth()
        {
            return CELL_MARGIN + Columns() * STRIDE;
        }

        public static int GridHeight()
        {
            return CELL_MARGIN + RowsPerPage() * STRIDE;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~GridContainerLayoutTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/GridContainerLayout.cs tests/ClassicUO.UnitTests/Game/UI/GridContainerLayoutTests.cs
git commit -m "feat(containers): add grid container layout math"
```

---

### Task 4: `GridContainerItem` cell control

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/GridContainerItem.cs`

**Interfaces:**
- Consumes: `GridContainerLayout.CELL_SIZE` (Task 3); `GridContainerView` host type (Task 5 — forward reference, this task compiles once Task 5's type exists; build both before running). It only needs `GridContainerView.World` (a `World`) and `GridContainerView.ContainerSerial` (a `uint`).
- Produces: `internal sealed class GridContainerItem : Control` with `GridContainerItem(GridContainerView view, uint serial, int size)`.

**Interaction parity source (do not invent behavior — copy from these):**
- Double-click loot/use: `ItemGump.OnMouseDoubleClick` (`ItemGump.cs:270-299`).
- Pickup + split handling: `ItemGump.Update` / `CanPickup` / `AttemptPickUp` (`ItemGump.cs:72-104, 243-339`).
- Drop into container: `GameActions.DropItem(serial, 0xFFFF, 0xFFFF, 0, containerSerial)` (`ContainerGump.cs:446`, `GameActions.cs:508`).
- Rendering an art cell with hover highlight + stack count: `GridLootGump.GridLootItem.AddToRenderLists` (`GridLootGump.cs:484-588`).

> **Note on duplication:** the spec permits duplicating the minimal `ItemGump` pickup/double-click logic when a clean shared helper is impractical. `ItemGump`'s pickup path is tightly coupled to its own `Graphic`/`_is_gump` fields and `Contains` pixel-checks; extracting a shared helper would touch `ItemGump`'s hot path. We duplicate the minimal logic here and leave a `// Mirrors ItemGump.<method>` comment on each duplicated block so the two do not silently diverge.

- [ ] **Step 1: Write the control**

Create `src/ClassicUO.Client/Game/UI/Controls/GridContainerItem.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Controls
{
    // A single grid cell: centered item art, full-cell hitbox, stack-count badge,
    // and the same interactions ItemGump exposes inside a standard container.
    internal sealed class GridContainerItem : Control
    {
        private readonly GridContainerView _view;
        private readonly HitBox _hit;
        private readonly Label _count;

        public GridContainerItem(GridContainerView view, uint serial, int size)
        {
            _view = view;
            LocalSerial = serial;

            Item item = _view.World.Items.Get(serial);

            if (item == null)
            {
                Dispose();

                return;
            }

            CanMove = false;
            AcceptMouseInput = true;
            WantUpdateSize = false;

            Width = size;
            Height = size;

            AlphaBlendControl background = new AlphaBlendControl { Width = size, Height = size };
            Add(background);

            _hit = new HitBox(0, 0, size, size, null, 0f);
            Add(_hit);

            if (_view.World.ClientFeatures.TooltipsEnabled)
            {
                _hit.SetTooltip(item);
            }

            _count = new Label(
                item.Amount > 1 && item.ItemData.IsStackable ? item.Amount.ToString() : string.Empty,
                true,
                0x0481,
                align: TEXT_ALIGN_TYPE.TS_LEFT
            )
            {
                X = 1,
                Y = size - 14
            };

            Add(_count);
        }

        public override void Update()
        {
            if (IsDisposed)
            {
                return;
            }

            base.Update();

            if (!_view.World.InGame)
            {
                return;
            }

            // Mirrors ItemGump.Update: begin a pickup once the drag threshold or the
            // double-click window is passed while the left button is held over this cell.
            if (
                !Client.Game.UO.GameCursor.ItemHold.Enabled
                && Mouse.LButtonPressed
                && UIManager.LastControlMouseDown(MouseButtonType.Left) == this
                && (
                    Mouse.LastLeftButtonClickTime != 0xFFFF_FFFF
                        && Mouse.LastLeftButtonClickTime != 0
                        && Mouse.LastLeftButtonClickTime + Mouse.MOUSE_DELAY_DOUBLE_CLICK < Time.Ticks
                    || CanPickup()
                )
            )
            {
                AttemptPickUp();
            }
            else if (_hit.MouseIsOver)
            {
                SelectedObject.Object = _view.World.Get(LocalSerial);
            }
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button != MouseButtonType.Left)
            {
                base.OnMouseUp(x, y, button);
                return;
            }

            // Held item released over a cell -> drop into this container (grid has no
            // meaningful slot coordinates; 0xFFFF/0xFFFF lets the server place it).
            if (
                Client.Game.UO.GameCursor.ItemHold.Enabled
                && !Client.Game.UO.GameCursor.ItemHold.IsFixedPosition
            )
            {
                GameActions.DropItem(
                    Client.Game.UO.GameCursor.ItemHold.Serial,
                    0xFFFF,
                    0xFFFF,
                    0,
                    _view.ContainerSerial
                );

                Mouse.CancelDoubleClick = true;
                return;
            }

            // Single click: route through the delayed-click manager, same as ItemGump.
            SelectedObject.Object = _view.World.Get(LocalSerial);
            base.OnMouseUp(x, y, button);
        }

        protected override void OnMouseOver(int x, int y)
        {
            SelectedObject.Object = _view.World.Get(LocalSerial);
        }

        protected override bool OnMouseDoubleClick(int x, int y, MouseButtonType button)
        {
            // Mirrors ItemGump.OnMouseDoubleClick.
            if (button != MouseButtonType.Left || _view.World.TargetManager.IsTargeting)
            {
                return false;
            }

            Item item = _view.World.Items.Get(LocalSerial);
            Item container;

            if (
                !Keyboard.Ctrl
                && ProfileManager.CurrentProfile.DoubleClickToLootInsideContainers
                && item != null
                && !item.IsDestroyed
                && !item.ItemData.IsContainer
                && item.IsEmpty
                && (container = _view.World.Items.Get(item.RootContainer)) != null
                && container != _view.World.Player.FindItemByLayer(Layer.Backpack)
            )
            {
                GameActions.GrabItem(_view.World, LocalSerial, item.Amount);
            }
            else
            {
                GameActions.DoubleClick(_view.World, LocalSerial);
            }

            return true;
        }

        // Mirrors ItemGump.CanPickup (drag threshold + split-menu handoff).
        private bool CanPickup()
        {
            Point offset = Mouse.LDragOffset;

            if (
                Math.Abs(offset.X) < Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS
                && Math.Abs(offset.Y) < Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS
            )
            {
                return false;
            }

            SplitMenuGump split = UIManager.GetGump<SplitMenuGump>(LocalSerial);

            if (split == null)
            {
                return true;
            }

            split.X = Mouse.LClickPosition.X - 80;
            split.Y = Mouse.LClickPosition.Y - 40;
            UIManager.AttemptDragControl(split, true);
            split.BringOnTop();

            return false;
        }

        // Mirrors ItemGump.AttemptPickUp (honors RelativeDragAndDropItems /
        // ScaleItemsInsideContainers offsets). is_gump is always false for container art.
        private void AttemptPickUp()
        {
            Item item = _view.World.Items.Get(LocalSerial);

            if (item == null)
            {
                return;
            }

            ref readonly var spriteInfo = ref Client.Game.UO.Arts.GetArt(item.DisplayedGraphic);

            int centerX = spriteInfo.UV.Width >> 1;
            int centerY = spriteInfo.UV.Height >> 1;

            if (
                ProfileManager.CurrentProfile != null
                && ProfileManager.CurrentProfile.ScaleItemsInsideContainers
            )
            {
                float scale = UIManager.ContainerScale;
                centerX = (int)(centerX * scale);
                centerY = (int)(centerY * scale);
            }

            if (
                ProfileManager.CurrentProfile != null
                && ProfileManager.CurrentProfile.RelativeDragAndDropItems
            )
            {
                Point p = new Point(
                    centerX - (Mouse.Position.X - ScreenCoordinateX),
                    centerY - (Mouse.Position.Y - ScreenCoordinateY)
                );

                GameActions.PickUp(_view.World, LocalSerial, centerX, centerY, offset: p);
            }
            else
            {
                GameActions.PickUp(_view.World, LocalSerial, centerX, centerY);
            }
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            float layerDepth = layerDepthRef;

            Item item = _view.World.Items.Get(LocalSerial);

            if (item == null)
            {
                return true;
            }

            ref readonly var artInfo = ref Client.Game.UO.Arts.GetArt(item.DisplayedGraphic);
            var rect = Client.Game.UO.Arts.GetRealArtBounds(item.DisplayedGraphic);

            Vector3 hueVector = ShaderHueTranslator.GetHueVector(
                item.Hue,
                item.ItemData.IsPartialHue,
                1f
            );

            // Center the art within the cell (mirrors GridLootItem centering).
            Point size = new Point(_hit.Width, _hit.Height);
            Point point = new Point();

            if (rect.Width < _hit.Width)
            {
                size.X = rect.Width;
                point.X = (_hit.Width >> 1) - (size.X >> 1);
            }

            if (rect.Height < _hit.Height)
            {
                size.Y = rect.Height;
                point.Y = (_hit.Height >> 1) - (size.Y >> 1);
            }

            var texture = artInfo.Texture;
            var sourceRectangle = artInfo.UV;

            if (texture != null)
            {
                renderLists.AddGumpWithAtlas(
                    batcher =>
                    {
                        batcher.Draw(
                            texture,
                            new Rectangle(x + point.X, y + point.Y, size.X, size.Y),
                            new Rectangle(
                                sourceRectangle.X + rect.X,
                                sourceRectangle.Y + rect.Y,
                                rect.Width,
                                rect.Height
                            ),
                            hueVector,
                            layerDepth
                        );
                        return true;
                    }
                );
            }

            if (_hit.MouseIsOver)
            {
                Vector3 hoverHue = ShaderHueTranslator.GetHueVector(0);
                hoverHue.Z = 0.2f;

                renderLists.AddGumpNoAtlas(
                    batcher =>
                    {
                        batcher.Draw(
                            SolidColorTextureCache.GetTexture(Color.Yellow),
                            new Rectangle(x + 1, y + 1, Width - 1, Height - 1),
                            hoverHue,
                            layerDepth
                        );
                        return true;
                    }
                );
            }

            return true;
        }
    }
}
```

- [ ] **Step 2: Note — no standalone build yet**

This file references `GridContainerView` (Task 5), which does not exist yet, so it will not compile alone. That is expected. Do **not** attempt to build here; build happens at the end of Task 5. Commit the file so the two land as a pair.

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/GridContainerItem.cs
git commit -m "feat(containers): add grid container item cell control"
```

---

### Task 5: `GridContainerView` control

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/GridContainerView.cs`

**Interfaces:**
- Consumes: `GridContainerLayout` (Task 3), `GridContainerItem` (Task 4).
- Produces: `internal sealed class GridContainerView : Control` with:
  - `GridContainerView(ContainerGump host)`
  - `World World { get; }` (from host)
  - `uint ContainerSerial { get; }` (host `LocalSerial`)
  - `void Rebuild()` — dispose cells, re-enumerate `container.Items` applying the standard skip rules, lay out cells via `GridContainerLayout`, size self, wire Prev/Next paging. Called from `ContainerGump.BuildGump` (Task 6).

**Eligibility rules (copy from `ContainerGump.ItemsOnAdded`, `ContainerGump.cs:558-588`):**
- Skip `item.Amount <= 0`.
- For corpses (`container.Graphic == 0x2006`): skip when `item.Layer > 0 && !Constants.BAD_CONTAINER_LAYERS[(int)itemDataLayer]`.
- Skip wearables on `Layer.Face | Layer.Beard | Layer.Hair`.
- Use `(Layer)item.ItemData.Layer` (tiledata), not `item.Layer`.

- [ ] **Step 1: Write the control**

Create `src/ClassicUO.Client/Game/UI/Controls/GridContainerView.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Linq;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Controls
{
    // Paged grid replacement for a container's item area. Owns all grid layout so
    // ContainerGump stays lean. Populated in container linked-list (draw/stack) order.
    internal sealed class GridContainerView : Control
    {
        private readonly ContainerGump _host;
        private readonly AlphaBlendControl _background;
        private readonly NiceButton _buttonPrev;
        private readonly NiceButton _buttonNext;
        private readonly Label _pageLabel;

        private int _currentPage;
        private int _pageCount = 1;

        public GridContainerView(ContainerGump host)
        {
            _host = host;

            CanMove = true;
            AcceptMouseInput = true;
            WantUpdateSize = false;

            Width = GridContainerLayout.GridWidth();
            Height = GridContainerLayout.GridHeight() + GridContainerLayout.RESERVED_BOTTOM;

            _background = new AlphaBlendControl { Width = Width, Height = Height };
            Add(_background);

            _buttonPrev = new NiceButton(
                4,
                Height - GridContainerLayout.RESERVED_BOTTOM + 5,
                40,
                20,
                ButtonAction.Activate,
                ResGumps.Prev
            )
            {
                ButtonParameter = 0,
                IsSelectable = false,
                IsVisible = false
            };

            _buttonNext = new NiceButton(
                Width - 44,
                Height - GridContainerLayout.RESERVED_BOTTOM + 5,
                40,
                20,
                ButtonAction.Activate,
                ResGumps.Next
            )
            {
                ButtonParameter = 1,
                IsSelectable = false,
                IsVisible = false
            };

            _buttonPrev.MouseUp += (s, e) => { if (e.Button == MouseButtonType.Left) ChangePageBy(-1); };
            _buttonNext.MouseUp += (s, e) => { if (e.Button == MouseButtonType.Left) ChangePageBy(1); };

            Add(_buttonPrev);
            Add(_buttonNext);

            _pageLabel = new Label("1", true, 999, align: TEXT_ALIGN_TYPE.TS_CENTER)
            {
                X = Width / 2 - 5,
                Y = Height - GridContainerLayout.RESERVED_BOTTOM + 5
            };

            Add(_pageLabel);
        }

        public World World => _host.World;

        public uint ContainerSerial => _host.LocalSerial;

        public void Rebuild()
        {
            foreach (GridContainerItem cell in Children.OfType<GridContainerItem>().ToList())
            {
                cell.Dispose();
            }

            Entity container = World.Get(ContainerSerial);

            if (container == null)
            {
                return;
            }

            bool isCorpse = container.Graphic == 0x2006;

            int index = 0;

            for (LinkedObject i = container.Items; i != null; i = i.Next)
            {
                Item item = (Item)i;

                if (item.Amount <= 0)
                {
                    continue;
                }

                var layer = (Layer)item.ItemData.Layer;

                if (isCorpse && item.Layer > 0 && !Constants.BAD_CONTAINER_LAYERS[(int)layer])
                {
                    continue;
                }

                if (
                    item.ItemData.IsWearable
                    && (layer == Layer.Face || layer == Layer.Beard || layer == Layer.Hair)
                )
                {
                    continue;
                }

                var (cx, cy, page) = GridContainerLayout.CellPosition(index);

                GridContainerItem cell = new GridContainerItem(this, item.Serial, GridContainerLayout.CELL_SIZE)
                {
                    X = cx,
                    Y = cy
                };

                Add(cell, page + 1);

                index++;
            }

            _pageCount = GridContainerLayout.PageCount(index);

            if (_currentPage >= _pageCount)
            {
                _currentPage = _pageCount - 1;
            }

            if (_currentPage < 0)
            {
                _currentPage = 0;
            }

            ApplyPage();
        }

        private void ChangePageBy(int delta)
        {
            _currentPage += delta;

            if (_currentPage < 0)
            {
                _currentPage = 0;
            }
            else if (_currentPage >= _pageCount)
            {
                _currentPage = _pageCount - 1;
            }

            ApplyPage();
        }

        private void ApplyPage()
        {
            ActivePage = _currentPage + 1;

            _buttonPrev.IsVisible = _currentPage > 0;
            _buttonNext.IsVisible = _currentPage < _pageCount - 1;

            _pageLabel.Text = ActivePage.ToString();
            _pageLabel.X = Width / 2 - _pageLabel.Width / 2;
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button != MouseButtonType.Left)
            {
                base.OnMouseUp(x, y, button);
                return;
            }

            // Empty grid background is a drop target for a held item.
            if (
                Client.Game.UO.GameCursor.ItemHold.Enabled
                && !Client.Game.UO.GameCursor.ItemHold.IsFixedPosition
            )
            {
                GameActions.DropItem(
                    Client.Game.UO.GameCursor.ItemHold.Serial,
                    0xFFFF,
                    0xFFFF,
                    0,
                    ContainerSerial
                );

                Mouse.CancelDoubleClick = true;
                return;
            }

            base.OnMouseUp(x, y, button);
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            float layerDepth = layerDepthRef;

            Vector3 hueVector = ShaderHueTranslator.GetHueVector(0);

            renderLists.AddGumpNoAtlas(
                batcher =>
                {
                    batcher.DrawRectangle(
                        SolidColorTextureCache.GetTexture(Color.Gray),
                        x,
                        y,
                        Width,
                        Height,
                        hueVector,
                        layerDepth
                    );
                    return true;
                }
            );

            return true;
        }
    }
}
```

- [ ] **Step 2: Build the client to verify Tasks 3-5 compile together**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded (0 errors). `GridContainerItem` + `GridContainerView` now resolve each other.

If errors reference `GameActions`, `NiceButton`, `AlphaBlendControl`, `Label`, `HitBox`, or `ResGumps.Prev`/`ResGumps.Next`, confirm the `using` set matches the files those types live in (all already used by `GridLootGump.cs` / `ItemGump.cs` — cross-check import lists).

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/GridContainerView.cs
git commit -m "feat(containers): add paged grid container view control"
```

---

### Task 6: `ContainerGump` grid branch + corner toggle

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/ContainerGump.cs`

**Interfaces:**
- Consumes: `ContainerViewModeResolver.Resolve` / `.ComputeToggleValue` (Task 2), `GridContainerView` (Task 5), Profile settings (Task 1).
- Produces: `ContainerGump` that renders standard or grid based on the resolver, with a top-right toggle button in Toggle mode. `GumpType.Container` and `LocalSerial` unchanged in both branches.

- [ ] **Step 1: Add fields and the resolver wrapper**

In `ContainerGump.cs`, add fields alongside the existing private fields (after `private bool _isMinimized;`, ~line 26):

```csharp
        private GridContainerView _gridView;
        private Control _toggleButton;
```

Add the resolver wrapper method (place it just above `BuildGump()`, ~line 147):

```csharp
        private static bool ResolveGridView(uint serial)
        {
            var profile = ProfileManager.CurrentProfile;

            if (profile == null)
            {
                return false;
            }

            return ContainerViewModeResolver.Resolve(
                profile.ContainerViewMode,
                profile.ContainerToggleDefaultGrid,
                profile.ContainerGridStates,
                serial
            );
        }
```

- [ ] **Step 2: Branch `BuildGump()`**

Replace the body of `BuildGump()` (currently `ContainerGump.cs:148-199`) with the branching version. The standard branch is the existing code verbatim; the grid branch is new:

```csharp
        private void BuildGump()
        {
            CanMove = true;
            CanCloseWithRightClick = true;
            WantUpdateSize = false;

            Item item = World.Items.Get(LocalSerial);

            if (item == null)
            {
                Dispose();

                return;
            }

            _data = World.ContainerManager.Get(Graphic);

            _gridView = null;
            _toggleButton = null;

            if (ResolveGridView(LocalSerial))
            {
                BuildGridBranch();
            }
            else
            {
                BuildStandardBranch(item);
            }

            AddToggleButtonIfNeeded();
        }

        private void BuildStandardBranch(Item item)
        {
            float scale = GetScale();
            ushort g = _data.Graphic;

            _gumpPicContainer?.Dispose();
            _hitBox?.Dispose();

            _hitBox = new HitBox(
                (int)(_data.MinimizerArea.X * scale),
                (int)(_data.MinimizerArea.Y * scale),
                (int)(_data.MinimizerArea.Width * scale),
                (int)(_data.MinimizerArea.Height * scale)
            );

            _hitBox.MouseUp += HitBoxOnMouseUp;
            Add(_hitBox);

            Add(_gumpPicContainer = new GumpPicContainer(0, 0, g, 0));
            _gumpPicContainer.MouseDoubleClick += GumpPicContainerOnMouseDoubleClick;

            if (Graphic == CORPSES_GUMP)
            {
                _eyeGumpPic?.Dispose();
                Add(_eyeGumpPic = new GumpPic((int)(45 * scale), (int)(30 * scale), 0x0045, 0));

                _eyeGumpPic.Width = (int)(_eyeGumpPic.Width * scale);
                _eyeGumpPic.Height = (int)(_eyeGumpPic.Height * scale);
            }
            else if (ProfileManager.CurrentProfile.HueContainerGumps)
            {
                _gumpPicContainer.Hue = item.Hue;
            }

            Width = _gumpPicContainer.Width = (int)(_gumpPicContainer.Width * scale);
            Height = _gumpPicContainer.Height = (int)(_gumpPicContainer.Height * scale);
        }

        private void BuildGridBranch()
        {
            // Grid mode has no minimizer hitbox, no container art, and no corpse eye.
            _gumpPicContainer = null;
            _hitBox = null;
            _eyeGumpPic = null;

            _gridView = new GridContainerView(this) { X = 0, Y = 0 };
            Add(_gridView);
            _gridView.Rebuild();

            Width = _gridView.Width;
            Height = _gridView.Height;
        }

        private void AddToggleButtonIfNeeded()
        {
            if (ProfileManager.CurrentProfile == null
                || ProfileManager.CurrentProfile.ContainerViewMode != 2)
            {
                return;
            }

            NiceButton btn = new NiceButton(
                Width - 22,
                2,
                20,
                20,
                ButtonAction.Activate,
                _gridView != null ? "S" : "G"
            )
            {
                IsSelectable = false
            };

            btn.MouseUp += ToggleButtonOnMouseUp;

            _toggleButton = btn;
            Add(btn);
        }

        private void ToggleButtonOnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtonType.Left)
            {
                return;
            }

            var profile = ProfileManager.CurrentProfile;

            if (profile == null)
            {
                return;
            }

            profile.ContainerGridStates[LocalSerial] = ContainerViewModeResolver.ComputeToggleValue(
                profile.ContainerToggleDefaultGrid,
                profile.ContainerGridStates,
                LocalSerial
            );

            RequestUpdateContents();
        }
```

The `NiceButton` label `"G"`/`"S"` is a plain glyph (Grid / Standard) and intentionally not localized. `MouseEventArgs` / `MouseButtonType` are already imported (`ClassicUO.Input`).

- [ ] **Step 3: Guard `IsMinimized` and the corpse eye for grid mode**

In the `IsMinimized` setter (`ContainerGump.cs:116-140`), the body dereferences `_gumpPicContainer`, which is null in grid mode. Wrap the body:

```csharp
        public bool IsMinimized
        {
            get => _isMinimized;
            set
            {
                if (_gumpPicContainer == null)
                {
                    // Grid mode: no iconized artwork, minimize is disabled.
                    _isMinimized = false;
                    return;
                }

                {
                    _isMinimized = value;
                    _gumpPicContainer.Graphic = value ? _data.IconizedGraphic : Graphic;
                    float scale = GetScale();

                    Width = _gumpPicContainer.Width = (int)(_gumpPicContainer.Width * scale);
                    Height = _gumpPicContainer.Height = (int)(_gumpPicContainer.Height * scale);

                    foreach (Control c in Children)
                    {
                        c.IsVisible = !value;
                    }

                    _gumpPicContainer.IsVisible = true;

                    SetInScreen();
                }
            }
        }
```

In `Update()` the corpse-eye block (`ContainerGump.cs:501-509`) dereferences `_eyeGumpPic`, null in grid mode. Change the guard from `if (Graphic == CORPSES_GUMP && _corpseEyeTicks < Time.Ticks)` to:

```csharp
            if (Graphic == CORPSES_GUMP && _eyeGumpPic != null && _corpseEyeTicks < Time.Ticks)
```

- [ ] **Step 4: Branch `UpdateContents()`**

Replace `UpdateContents()` (`ContainerGump.cs:512-518`) with:

```csharp
        protected override void UpdateContents()
        {
            Clear();
            BuildGump();

            if (_gridView == null)
            {
                IsMinimized = IsMinimized;
                ItemsOnAdded();
            }
            // Grid branch: BuildGump already created and populated _gridView.
        }
```

- [ ] **Step 5: Guard the debug bounds overlay**

`AddToRenderLists` draws the debug container bounds using `_data.Bounds` only when `CUOEnviroment.Debug && !IsMinimized`. `_data` is set in both branches, so no null risk, but the red rectangle is meaningless in grid mode. Change the condition (`ContainerGump.cs:683`) to skip it in grid mode:

```csharp
            if (CUOEnviroment.Debug && !IsMinimized && _gridView == null)
```

- [ ] **Step 6: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded (0 errors).

- [ ] **Step 7: Run the full unit suite to confirm no regressions**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS (all existing tests plus Tasks 1-3 tests).

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/ContainerGump.cs
git commit -m "feat(containers): render container gump as grid and add corner toggle"
```

---

### Task 7: Options UI + strings + live refresh

**Files:**
- Modify: `src/ClassicUO.Client/Resources/ResGumps.resx` (5 new `<data>` entries)
- Modify: `src/ClassicUO.Client/Resources/ResGumps.Designer.cs` (5 new properties)
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` (field decls, container-section UI, Apply)

**Interfaces:**
- Consumes: Profile settings (Task 1), `ContainerGump.RequestUpdateContents` (existing).
- Produces: a "Container view" combobox + "Default toggled containers to grid" checkbox in the Containers options page; `Apply()` writes both settings and force-refreshes open `ContainerGump`s when either changes.

- [ ] **Step 1: Add the resource strings (resx)**

In `src/ClassicUO.Client/Resources/ResGumps.resx`, add these five entries (place them near the `GridLoot_*` entries, ~line 221, keeping the file's alphabetical-ish grouping is fine but not required):

```xml
  <data name="ContainerViewMode" xml:space="preserve">
    <value>Container view:</value>
  </data>
  <data name="ContainerViewMode_Standard" xml:space="preserve">
    <value>Standard</value>
  </data>
  <data name="ContainerViewMode_Grid" xml:space="preserve">
    <value>Grid</value>
  </data>
  <data name="ContainerViewMode_Toggle" xml:space="preserve">
    <value>Toggle</value>
  </data>
  <data name="ContainerToggleDefaultGrid" xml:space="preserve">
    <value>Default toggled containers to grid</value>
  </data>
```

- [ ] **Step 2: Add the generated properties (Designer)**

In `src/ClassicUO.Client/Resources/ResGumps.Designer.cs`, add five properties following the exact pattern of `GridLoot_None` (near ~line 1855). For example:

```csharp
        /// <summary>
        ///   Looks up a localized string similar to Container view:.
        /// </summary>
        public static string ContainerViewMode {
            get {
                return ResourceManager.GetString("ContainerViewMode", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Standard.
        /// </summary>
        public static string ContainerViewMode_Standard {
            get {
                return ResourceManager.GetString("ContainerViewMode_Standard", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Grid.
        /// </summary>
        public static string ContainerViewMode_Grid {
            get {
                return ResourceManager.GetString("ContainerViewMode_Grid", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Toggle.
        /// </summary>
        public static string ContainerViewMode_Toggle {
            get {
                return ResourceManager.GetString("ContainerViewMode_Toggle", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Default toggled containers to grid.
        /// </summary>
        public static string ContainerToggleDefaultGrid {
            get {
                return ResourceManager.GetString("ContainerToggleDefaultGrid", resourceCulture);
            }
        }
```

- [ ] **Step 3: Declare the option controls**

In `OptionsGump.cs`, add fields next to the existing container option fields. After line 52 (`private Checkbox _hueContainerGumps;`) add:

```csharp
        private Checkbox _containerToggleDefaultGrid;
```

Next to the other container comboboxes (after line 121 `private Combobox _overrideContainerLocationSetting;`) add:

```csharp
        private Combobox _containerViewMode;
```

- [ ] **Step 4: Build the container-section UI**

In `OptionsGump.cs`, in the Containers page builder, insert the new controls just before the "RebuildContainers" `NiceButton` block (currently `OptionsGump.cs:3601-3620`, right after `startX = 5; startY += _overrideContainerLocation.Height + 2 + 10;`). Insert:

```csharp
            // Container view mode (Standard / Grid / Toggle)
            Label containerViewLabel = AddLabel(rightArea, ResGumps.ContainerViewMode, startX, startY);
            startX += containerViewLabel.Width + 5;

            _containerViewMode = AddCombobox
            (
                rightArea,
                new[]
                {
                    ResGumps.ContainerViewMode_Standard,
                    ResGumps.ContainerViewMode_Grid,
                    ResGumps.ContainerViewMode_Toggle
                },
                _currentProfile.ContainerViewMode,
                startX,
                startY,
                200
            );

            startX = 5;
            startY += _containerViewMode.Height + 2;

            _containerToggleDefaultGrid = AddCheckBox
            (
                rightArea,
                ResGumps.ContainerToggleDefaultGrid,
                _currentProfile.ContainerToggleDefaultGrid,
                startX,
                startY
            );

            // Only meaningful in Toggle mode.
            _containerToggleDefaultGrid.IsEnabled = _currentProfile.ContainerViewMode == 2;
            _containerViewMode.OnOptionSelected += (s, index) =>
            {
                _containerToggleDefaultGrid.IsEnabled = index == 2;
            };

            startY += _containerToggleDefaultGrid.Height + 2 + 10;
```

(`AddLabel` returns a `Label`; `AddCombobox`/`AddCheckBox` are the existing helpers at `OptionsGump.cs:4627` and `:4648`. `Combobox.OnOptionSelected` is `EventHandler<int>`, `Checkbox.IsEnabled` exists on the base `Control`.)

- [ ] **Step 5: Wire Apply + live refresh**

In `Apply()`, in the containers block (after line 4494 `_currentProfile.HueContainerGumps = _hueContainerGumps.IsChecked;`), add:

```csharp
            if (_currentProfile.ContainerViewMode != _containerViewMode.SelectedIndex
                || _currentProfile.ContainerToggleDefaultGrid != _containerToggleDefaultGrid.IsChecked)
            {
                _currentProfile.ContainerViewMode = _containerViewMode.SelectedIndex;
                _currentProfile.ContainerToggleDefaultGrid = _containerToggleDefaultGrid.IsChecked;

                foreach (ContainerGump containerGump in UIManager.Gumps.OfType<ContainerGump>())
                {
                    containerGump.RequestUpdateContents();
                }
            }
```

(`System.Linq` and `ClassicUO.Game.UI.Gumps` are already imported in `OptionsGump.cs` — the existing container-scale apply block at line 4483 already uses `UIManager.Gumps.OfType<ContainerGump>()`.)

- [ ] **Step 6: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded (0 errors). If a string property is unresolved, re-check the Designer.cs property name matches the resx `name=` exactly.

- [ ] **Step 7: Run the full unit suite**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Resources/ResGumps.resx src/ClassicUO.Client/Resources/ResGumps.Designer.cs src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(containers): add container view mode options and live refresh"
```

---

### Task 8: Manual verification

**Files:** none (runtime verification against a real UO data directory).

This feature is UI/graphics-heavy; the pure logic is covered by Tasks 1-3 unit tests, but the rendering and interaction paths require a running client. Perform these checks with a legally-obtained client data directory configured in `settings.json`.

- [ ] **Step 1: Build a runnable client**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeded.

- [ ] **Step 2: Standard mode (regression)**

Set Container view = Standard in Options. Open backpack, a chest, and a corpse. Confirm behavior is identical to before this change (art view, minimize, drag in/out, double-click, tooltips).

- [ ] **Step 3: Grid mode — full interaction set**

Set Container view = Grid. Open backpack, chest, corpse. Confirm each opens as a grid. Then verify:
- Drag an item out to the cursor and drop it in the world / paperdoll.
- Drop a held item onto the grid (cell and empty background) — lands in the container.
- Double-click an item to use/equip/open.
- Split a stack (drag a stackable slowly to trigger `SplitMenuGump`), confirm the split menu appears.
- Hover an item — tooltip shows.
- Open a container with more items than one page — Prev/Next buttons page correctly and the page label updates.
- Empty container — grid renders empty and still accepts a dropped item.

- [ ] **Step 4: Toggle mode + persistence**

Set Container view = Toggle. Open a container; confirm the top-right "G"/"S" button appears. Click it — the same window switches between grid and standard in place (same position). Toggle a specific container to grid, log out, log back in, reopen it — confirm it reopens as grid (per-serial state persisted via `ContainerGridStates`). Toggle "Default toggled containers to grid" and confirm never-toggled containers follow the default.

- [ ] **Step 5: Live options refresh**

With containers open, change Container view in Options and click Apply. Confirm open containers immediately re-render in the new mode without needing to reopen.

- [ ] **Step 6: Commit (docs/notes only, if any)**

No code change expected. If verification surfaces a defect, fix under the owning task, re-run its tests, and amend the notes.

---

## Self-Review

**Spec coverage:**
- Profile settings (spec §1) → Task 1. ✔
- Effective-view resolver truth table (spec §2, Testing) → Task 2. ✔
- `ContainerGump` internal mode, grid branch, corner toggle, minimize-disabled, UpdateContents delegation (spec §3) → Task 6 (+ grid view Task 5). ✔
- `GridContainerView` paged layout, list-order population, skip rules, drop target (spec §4) → Tasks 3 (math) + 5. ✔
- `GridContainerItem` interactions: double-click, pickup/split, drop, tooltip, single-click (spec §5) → Task 4. ✔
- Options UI dropdown + conditional checkbox + strings (spec §6) → Task 7. ✔
- Live refresh on options change (spec, Error handling) → Task 7 Step 5. ✔
- Corpse in grid (eye absent) (spec, Error handling) → Task 6 Step 3 (eye guard) + Task 5 corpse skip rules. ✔
- Toggle button flip semantics (spec, Testing) → Task 2 `ComputeToggleValue` + tests. ✔
- Persistence across relogin → Task 1 (dictionary serializes) + Task 8 Step 4 manual check. ✔

**Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to Task N" — all code blocks are complete. ✔

**Type consistency:**
- `ContainerViewModeResolver.Resolve(int, bool, IReadOnlyDictionary<uint,bool>, uint)` and `.ComputeToggleValue(bool, IReadOnlyDictionary<uint,bool>, uint)` — same signatures in Task 2 definition, Task 2 tests, and Task 6 call sites. ✔
- `GridContainerLayout` members (`Columns`, `RowsPerPage`, `PerPage`, `PageCount`, `CellPosition`, `GridWidth`, `GridHeight`, `CELL_SIZE`, `RESERVED_BOTTOM`) — defined Task 3, used Tasks 3 tests + 5. ✔
- `GridContainerView.World` / `.ContainerSerial` / `.Rebuild()` — produced Task 5, consumed by `GridContainerItem` (Task 4) and `ContainerGump` (Task 6). ✔
- `Profile.ContainerViewMode` / `ContainerToggleDefaultGrid` / `ContainerGridStates` — one spelling throughout Tasks 1, 2 (via call), 6, 7. ✔

Notes: `Dictionary<uint,bool>` is passed where `IReadOnlyDictionary<uint,bool>` is expected — `Dictionary` implements `IReadOnlyDictionary`, so this is valid with no cast.

---

**Plan complete and saved to `docs/superpowers/plans/2026-07-02-container-view-modes.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
