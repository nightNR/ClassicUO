# Permanent Anchor Groups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users define permanent, on-screen anchor groups (button widget + label + grid) in client Options; plugin/script status bars opened with a matching `groupId` auto-snap into that group's grid, and each group gets its own capacity (fixing the global-limit overflow bug).

**Architecture:** Approach A — a standalone draggable widget gump (`PluginAnchorGroupGump`) owns the group's screen position and grid config (stored in a new `Profile.PluginAnchorGroups` list of `PluginAnchorGroupDef`). Status bars form a normal healthbar-only `AnchorManager.AnchorGroup` seeded relative to the widget; grid layout math in `PluginStatusBars` is generalized to per-group rows/columns and a per-group fill order. Undefined group ids keep today's global-fallback behavior.

**Tech Stack:** C# `net10.0`, FNA-XNA, xUnit tests (`tests/ClassicUO.UnitTests`), source-generated JSON (`ProfileJsonContext`), existing `AnchorManager` / `UIManager` gump tree.

## Global Constraints

- Target framework `net10.0`, `LangVersion=Preview`, `AllowUnsafeBlocks=true`.
- Every new source file carries the project BSD-2 license header: first line `// SPDX-License-Identifier: BSD-2-Clause` (matches `PluginStatusBars.cs`).
- Profile settings are plain auto-properties serialized via `ProfileJsonContext`; any **new element type** placed in a serialized `List<T>` MUST be registered with `[JsonSerializable(typeof(T), GenerationMode = JsonSourceGenerationMode.Metadata)]` on `ProfileJsonContext` (`Profile.cs:22-27`).
- `ClassicUO.Client` internals are visible to `ClassicUO.UnitTests` via `InternalsVisibleTo` — test classes/methods reach `internal` members directly.
- Keyboard modifiers are read from the static `ClassicUO.Input.Keyboard` (`Keyboard.Shift`, etc.). Mouse buttons are `ClassicUO.Input.MouseButtonType`.
- Match surrounding style: no allocations in hot loops; mirror existing gump/options patterns rather than inventing new ones.
- Build check: `dotnet build ClassicUO.sln -c Debug`. Test: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~<Name>"`.

---

### Task 1: Data model — `FillOrder`, `PluginAnchorGroupDef`, `Profile.PluginAnchorGroups`

**Files:**
- Create: `src/ClassicUO.Client/Configuration/PluginAnchorGroupDef.cs`
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs` (register type in `ProfileJsonContext` ~`:22-27`; add list property near `:251-252`)
- Test: `tests/ClassicUO.UnitTests/PluginAnchorGroupDefTests.cs`

**Interfaces:**
- Produces:
  - `enum ClassicUO.Configuration.FillOrder { ColumnMajor = 0, RowMajor = 1 }`
  - `class ClassicUO.Configuration.PluginAnchorGroupDef` with public auto-properties: `int Id`, `string Label`, `int Columns`, `int Rows`, `FillOrder Fill`, `int X`, `int Y`, `bool Locked`. Parameterless ctor (defaults: `Label = ""`, `Columns = 1`, `Rows = 1`, `Fill = ColumnMajor`).
  - `Profile.PluginAnchorGroups` → `List<PluginAnchorGroupDef>` (defaults to empty list).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ClassicUO.UnitTests/PluginAnchorGroupDefTests.cs
// SPDX-License-Identifier: BSD-2-Clause
using System.Text.Json;
using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class PluginAnchorGroupDefTests
    {
        [Fact]
        public void Defaults_AreSane()
        {
            var def = new PluginAnchorGroupDef();
            Assert.Equal("", def.Label);
            Assert.Equal(1, def.Columns);
            Assert.Equal(1, def.Rows);
            Assert.Equal(FillOrder.ColumnMajor, def.Fill);
        }

        [Fact]
        public void SerializationRoundTrip_PreservesAllFields()
        {
            var def = new PluginAnchorGroupDef
            {
                Id = 42, Label = "Enemies", Columns = 3, Rows = 5,
                Fill = FillOrder.RowMajor, X = 100, Y = 200, Locked = true
            };

            string json = JsonSerializer.Serialize(def);
            var round = JsonSerializer.Deserialize<PluginAnchorGroupDef>(json);

            Assert.Equal(def.Id, round.Id);
            Assert.Equal(def.Label, round.Label);
            Assert.Equal(def.Columns, round.Columns);
            Assert.Equal(def.Rows, round.Rows);
            Assert.Equal(def.Fill, round.Fill);
            Assert.Equal(def.X, round.X);
            Assert.Equal(def.Y, round.Y);
            Assert.Equal(def.Locked, round.Locked);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginAnchorGroupDefTests"`
Expected: FAIL — `PluginAnchorGroupDef` / `FillOrder` do not exist (compile error).

- [ ] **Step 3: Create the data model file**

```csharp
// src/ClassicUO.Client/Configuration/PluginAnchorGroupDef.cs
// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.Configuration
{
    internal enum FillOrder
    {
        ColumnMajor = 0,
        RowMajor = 1
    }

    internal sealed class PluginAnchorGroupDef
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;
        public FillOrder Fill { get; set; } = FillOrder.ColumnMajor;
        public int X { get; set; }
        public int Y { get; set; }
        public bool Locked { get; set; }
    }
}
```

Note: the test serializes with a plain `JsonSerializer` (not the source-gen context), so `internal` types work in-test via `InternalsVisibleTo`. If the compiler rejects `internal` for the generic `JsonSerializer.Deserialize<T>` in the test assembly, that is fine — `InternalsVisibleTo` grants access.

- [ ] **Step 4: Add the Profile list property + register the type**

In `src/ClassicUO.Client/Configuration/Profile.cs`, add the property next to the plugin status-bar settings (after line 252):

```csharp
        public int PluginStatusBarMaxRows { get; set; } = 10;
        public int PluginStatusBarMaxColumns { get; set; } = 1;
        public List<PluginAnchorGroupDef> PluginAnchorGroups { get; set; } = new List<PluginAnchorGroupDef>();
```

Register the element type on `ProfileJsonContext` (add alongside the existing `[JsonSerializable]` lines ~`:22-27`):

```csharp
    [JsonSerializable(typeof(ClassicUO.Configuration.PluginAnchorGroupDef), GenerationMode = JsonSourceGenerationMode.Metadata)]
```

(Confirm `System.Collections.Generic` is already imported in `Profile.cs` — it is, given existing `List<>` usage.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginAnchorGroupDefTests"`
Expected: PASS (both tests).

- [ ] **Step 6: Build the client to confirm the source-gen context compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded (no source-generator error about `PluginAnchorGroupDef`).

- [ ] **Step 7: Commit**

```bash
git add src/ClassicUO.Client/Configuration/PluginAnchorGroupDef.cs src/ClassicUO.Client/Configuration/Profile.cs tests/ClassicUO.UnitTests/PluginAnchorGroupDefTests.cs
git commit -m "feat(anchor-groups): add PluginAnchorGroupDef model and profile list"
```

---

### Task 2: Layout helpers — per-group grid resolution + row/column-major `GridCell`

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs`
- Test: `tests/ClassicUO.UnitTests/PluginStatusBarLayoutTests.cs` (create; if an existing test file already covers `GridCell`, add to it instead)

**Interfaces:**
- Consumes: `PluginAnchorGroupDef`, `FillOrder` (Task 1); `ProfileManager.CurrentProfile.PluginAnchorGroups`.
- Produces (all `internal static` on `PluginStatusBars`):
  - `PluginAnchorGroupDef GetDef(int groupId)` — first def in `ProfileManager.CurrentProfile?.PluginAnchorGroups` with matching `Id`, else `null`.
  - `int ResolveMaxRows(int groupId)` — def's `Rows` if def exists, else `Profile.PluginStatusBarMaxRows` (clamped ≥1).
  - `int ResolveMaxColumns(int groupId)` — def's `Columns` if def exists, else `Profile.PluginStatusBarMaxColumns` (clamped ≥1).
  - `FillOrder ResolveFill(int groupId)` — def's `Fill` if def exists, else `FillOrder.ColumnMajor`.
  - `(int column, int row) GridCell(int index, int rows, int cols, FillOrder fill)` — new overload.
  - Existing no-arg `ResolveMaxRows()`/`ResolveMaxColumns()` and `GridCell(index, maxRows)` remain (used by fallback path); do NOT delete them.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ClassicUO.UnitTests/PluginStatusBarLayoutTests.cs
// SPDX-License-Identifier: BSD-2-Clause
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class PluginStatusBarLayoutTests
    {
        [Theory]
        // column-major, rows=3, cols=2 → fill down column 0, then column 1
        [InlineData(0, FillOrder.ColumnMajor, 0, 0)]
        [InlineData(1, FillOrder.ColumnMajor, 0, 1)]
        [InlineData(2, FillOrder.ColumnMajor, 0, 2)]
        [InlineData(3, FillOrder.ColumnMajor, 1, 0)]
        [InlineData(5, FillOrder.ColumnMajor, 1, 2)]
        // row-major, rows=3, cols=2 → fill across row 0, then row 1
        [InlineData(0, FillOrder.RowMajor, 0, 0)]
        [InlineData(1, FillOrder.RowMajor, 1, 0)]
        [InlineData(2, FillOrder.RowMajor, 0, 1)]
        [InlineData(3, FillOrder.RowMajor, 1, 1)]
        [InlineData(5, FillOrder.RowMajor, 1, 2)]
        public void GridCell_PlacesByFillOrder(int index, FillOrder fill, int expCol, int expRow)
        {
            var (col, row) = PluginStatusBars.GridCell(index, rows: 3, cols: 2, fill);
            Assert.Equal(expCol, col);
            Assert.Equal(expRow, row);
        }

        [Fact]
        public void IsCapacityReached_UsesPerGroupDims()
        {
            // 2x2 = 4 cells
            Assert.False(PluginStatusBars.IsCapacityReached(3, 2, 2));
            Assert.True(PluginStatusBars.IsCapacityReached(4, 2, 2));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusBarLayoutTests"`
Expected: FAIL — the 4-arg `GridCell` overload does not exist (compile error). (`IsCapacityReached` already exists and its test should compile.)

- [ ] **Step 3: Add the new overload + per-group resolvers**

In `PluginStatusBars.cs`, add `using ClassicUO.Configuration;` if not present, then add these members (near the existing helpers ~`:273-306`):

```csharp
        /// <summary>Cell for a 0-based insertion index under a given fill order.</summary>
        internal static (int column, int row) GridCell(int index, int rows, int cols, FillOrder fill)
        {
            if (rows < 1) rows = 1;
            if (cols < 1) cols = 1;

            if (fill == FillOrder.RowMajor)
            {
                return (index % cols, index / cols);
            }

            // ColumnMajor (default, matches legacy GridCell(index, maxRows))
            return (index / rows, index % rows);
        }

        internal static PluginAnchorGroupDef GetDef(int groupId)
        {
            var groups = ProfileManager.CurrentProfile?.PluginAnchorGroups;

            if (groups == null)
            {
                return null;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null && groups[i].Id == groupId)
                {
                    return groups[i];
                }
            }

            return null;
        }

        internal static int ResolveMaxRows(int groupId)
        {
            PluginAnchorGroupDef def = GetDef(groupId);
            return NormalizeDimension(def?.Rows ?? (ProfileManager.CurrentProfile?.PluginStatusBarMaxRows ?? DefaultMaxRows));
        }

        internal static int ResolveMaxColumns(int groupId)
        {
            PluginAnchorGroupDef def = GetDef(groupId);
            return NormalizeDimension(def?.Columns ?? (ProfileManager.CurrentProfile?.PluginStatusBarMaxColumns ?? DefaultMaxColumns));
        }

        internal static FillOrder ResolveFill(int groupId)
        {
            return GetDef(groupId)?.Fill ?? FillOrder.ColumnMajor;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusBarLayoutTests"`
Expected: PASS (all theory cases + capacity fact).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs tests/ClassicUO.UnitTests/PluginStatusBarLayoutTests.cs
git commit -m "feat(anchor-groups): per-group grid resolution and row/column-major layout"
```

---

### Task 3: Wire per-group config into `OpenStatusBar` / `AddToGroup`

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs` (`OpenStatusBar` `:179`, `AddToGroup` `:232`)
- Test: `tests/ClassicUO.UnitTests/PluginStatusBarLayoutTests.cs` (extend — pure neighbor helper)

**Interfaces:**
- Consumes: Task 2 resolvers, `GetDef`, `ResolveFill`.
- Produces:
  - `(int neighborIndex, bool startNewLine) NeighborFor(int index, int rows, int cols, FillOrder fill)` — `internal static`. For `index > 0`: if the new cell starts a new column (ColumnMajor) or new row (RowMajor), returns the index of the first bar of the previous line and `startNewLine = true`; otherwise returns `index - 1` and `startNewLine = false`.
- Behavior changes (not directly unit-tested — verified by build + manual): capacity + seed position now per-group; neighbor selection + fill honor `ResolveFill`.

- [ ] **Step 1: Write the failing test for the neighbor helper**

```csharp
        [Theory]
        // column-major rows=3: index 1,2 continue column (prev = index-1, no new line);
        // index 3 starts new column (prev = index-3 = 0, new line)
        [InlineData(1, 3, 2, FillOrder.ColumnMajor, 0, false)]
        [InlineData(2, 3, 2, FillOrder.ColumnMajor, 1, false)]
        [InlineData(3, 3, 2, FillOrder.ColumnMajor, 0, true)]
        // row-major cols=2: index 1 continues row (prev 0); index 2 starts new row (prev = index-2 = 0)
        [InlineData(1, 3, 2, FillOrder.RowMajor, 0, false)]
        [InlineData(2, 3, 2, FillOrder.RowMajor, 0, true)]
        [InlineData(3, 3, 2, FillOrder.RowMajor, 2, false)]
        public void NeighborFor_PicksAnchorAndLineBreak(int index, int rows, int cols, FillOrder fill, int expNeighbor, bool expNewLine)
        {
            var (neighbor, newLine) = PluginStatusBars.NeighborFor(index, rows, cols, fill);
            Assert.Equal(expNeighbor, neighbor);
            Assert.Equal(expNewLine, newLine);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~NeighborFor"`
Expected: FAIL — `NeighborFor` not defined (compile error).

- [ ] **Step 3: Add the neighbor helper**

In `PluginStatusBars.cs`:

```csharp
        /// <summary>
        /// For insertion index > 0, decides which existing member the new bar
        /// anchors to and whether it opens a new line (column for ColumnMajor,
        /// row for RowMajor). ColumnMajor: lines are columns of length `rows`.
        /// RowMajor: lines are rows of length `cols`.
        /// </summary>
        internal static (int neighborIndex, bool startNewLine) NeighborFor(int index, int rows, int cols, FillOrder fill)
        {
            int lineLength = fill == FillOrder.RowMajor ? (cols < 1 ? 1 : cols) : (rows < 1 ? 1 : rows);
            bool startNewLine = index % lineLength == 0;
            int neighbor = startNewLine ? index - lineLength : index - 1;
            return (neighbor, startNewLine);
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~NeighborFor"`
Expected: PASS.

- [ ] **Step 5: Rewrite `OpenStatusBar` capacity check + seed position (per-group)**

Replace the capacity block in `OpenStatusBar` (`:201-217`) so capacity uses per-group dims and, for a **defined** group, the first bar seeds at the widget origin. Current code:

```csharp
            if (groupId != 0 &&
                IsCapacityReached(
                    PluginStatusBarGroups.GetLiveMembers(groupId).Count,
                    ResolveMaxRows(),
                    ResolveMaxColumns()))
            {
                return;
            }

            // Plugin-opened bars are always the custom bar so priority-overlay tint works.
            HealthBarGumpCustom bar = new HealthBarGumpCustom(world, serial)
            {
                X = x,
                Y = y
            };
```

New code:

```csharp
            if (groupId != 0 &&
                IsCapacityReached(
                    PluginStatusBarGroups.GetLiveMembers(groupId).Count,
                    ResolveMaxRows(groupId),
                    ResolveMaxColumns(groupId)))
            {
                return;
            }

            // A defined group seeds its first bar at the widget's anchor origin,
            // ignoring the plugin-supplied x/y; undefined groups seed at x/y.
            PluginAnchorGroupDef def = GetDef(groupId);
            int seedX = x;
            int seedY = y;

            if (def != null && PluginStatusBarGroups.GetLiveMembers(groupId).Count == 0)
            {
                seedX = def.X;
                seedY = def.Y + PluginAnchorGroupGump.WidgetHeight;
            }

            // Plugin-opened bars are always the custom bar so priority-overlay tint works.
            HealthBarGumpCustom bar = new HealthBarGumpCustom(world, serial)
            {
                X = seedX,
                Y = seedY
            };
```

Note: `PluginAnchorGroupGump.WidgetHeight` is an `internal const int` defined in Task 4. If Task 4 is not yet implemented when you build, temporarily use the literal `24` and replace with the const in Task 4. Prefer implementing Task 4 first if executing out of order.

- [ ] **Step 6: Rewrite `AddToGroup` to honor per-group rows/cols + fill order**

Replace `AddToGroup` (`:232-271`) with:

```csharp
        private static void AddToGroup(int groupId, BaseHealthBarGump bar)
        {
            int rows = ResolveMaxRows(groupId);
            int cols = ResolveMaxColumns(groupId);
            FillOrder fill = ResolveFill(groupId);

            List<BaseHealthBarGump> members = PluginStatusBarGroups.GetLiveMembers(groupId);
            int index = members.Count; // cell this new bar will occupy

            AnchorManager.AnchorGroup group = PluginStatusBarGroups.GetGroup(groupId);

            if (index == 0 || group == null || group.IsEmpty)
            {
                group = new AnchorManager.AnchorGroup(bar);
                UIManager.AnchorManager[bar] = group;
                PluginStatusBarGroups.Track(groupId, group);
                PluginStatusBarGroups.AddMember(groupId, bar);

                return;
            }

            (int neighborIndex, bool startNewLine) = NeighborFor(index, rows, cols, fill);
            BaseHealthBarGump neighbor = members[neighborIndex];

            if (!startNewLine)
            {
                if (fill == FillOrder.RowMajor)
                {
                    // continue the row: place east of the previous bar
                    bar.X = neighbor.X + neighbor.GroupMatrixWidth;
                    bar.Y = neighbor.Y;
                }
                else
                {
                    // continue the column: place south of the previous bar
                    bar.X = neighbor.X;
                    bar.Y = neighbor.Y + neighbor.GroupMatrixHeight;
                }
            }
            else
            {
                if (fill == FillOrder.RowMajor)
                {
                    // new row below the first bar of the previous row
                    bar.X = neighbor.X;
                    bar.Y = neighbor.Y + neighbor.GroupMatrixHeight;
                }
                else
                {
                    // new column right of the top bar of the previous column
                    bar.X = neighbor.X + neighbor.GroupMatrixWidth;
                    bar.Y = neighbor.Y;
                }
            }

            UIManager.AnchorManager.DropControl(bar, neighbor);
            PluginStatusBarGroups.AddMember(groupId, bar);
        }
```

- [ ] **Step 7: Build to confirm everything compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded. (If `PluginAnchorGroupGump.WidgetHeight` is unresolved, implement Task 4 or use the literal `24` per the note in Step 5.)

- [ ] **Step 8: Run the full layout test file**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusBarLayoutTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs tests/ClassicUO.UnitTests/PluginStatusBarLayoutTests.cs
git commit -m "feat(anchor-groups): per-group capacity, seed position, and fill-order layout in OpenStatusBar"
```

---

### Task 4: `PluginAnchorGroupGump` widget

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Gumps/PluginAnchorGroupGump.cs`
- Modify: none (referenced by Tasks 3 and 5)

**Interfaces:**
- Consumes: `PluginAnchorGroupDef` (Task 1); `PluginStatusBarGroups.GetLiveMembers` / `PluginStatusBars.CloseStatusBar` (existing); `AnchorManager.AnchorGroup.UpdateLocation` (existing `AnchorManager.cs:366`).
- Produces:
  - `class PluginAnchorGroupGump : Gump`
  - `internal const int WidgetHeight` (used by Task 3 seed math) — set to the pic height, use `24`.
  - Ctor `PluginAnchorGroupGump(World world, PluginAnchorGroupDef def)`.
  - `int GroupId => _def.Id`.
  - Reads/writes `_def.X`, `_def.Y`, `_def.Locked`.

Graphics: normal `0x25F8`, hover `0x25F9`.

**Implementation (no separate test — UI interaction is not unit-tested, matching repo pattern). Mirror `UseSpellButtonGump.cs` (GumpPic + `SetTooltip`) and `MacroButtonGump.cs` (`OnMouseEnter/Exit`, `OnMouseUp`, `CanMove`).**

- [ ] **Step 1: Create the widget gump**

```csharp
// src/ClassicUO.Client/Game/UI/Gumps/PluginAnchorGroupGump.cs
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Input;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    internal sealed class PluginAnchorGroupGump : Gump
    {
        internal const int WidgetHeight = 24;

        private const ushort GRAPHIC_NORMAL = 0x25F8;
        private const ushort GRAPHIC_HOVER = 0x25F9;

        private readonly PluginAnchorGroupDef _def;
        private readonly Controls.GumpPic _pic;
        private readonly Controls.Label _label;
        private int _lastX, _lastY;

        public PluginAnchorGroupGump(World world, PluginAnchorGroupDef def)
            : base(world, 0, 0)
        {
            _def = def;

            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;

            X = def.X;
            Y = def.Y;
            _lastX = X;
            _lastY = Y;

            Add(_pic = new Controls.GumpPic(0, 0, GRAPHIC_NORMAL, 0)
            {
                AcceptMouseInput = false
            });

            Add(_label = new Controls.Label(def.Label ?? "", true, 0x0481, 0, 1)
            {
                X = _pic.Width + 4,
                Y = 2
            });

            Width = _pic.Width + 4 + _label.Width;
            Height = WidgetHeight;

            RefreshTooltip();
            RefreshLockCue();
        }

        public int GroupId => _def.Id;

        private void RefreshTooltip()
        {
            int count = PluginStatusBarGroups.GetLiveMembers(_def.Id).Count;
            int cap = (_def.Columns < 1 ? 1 : _def.Columns) * (_def.Rows < 1 ? 1 : _def.Rows);
            SetTooltip($"{_def.Label}: {count} / {cap}");
        }

        private void RefreshLockCue()
        {
            // Tint the pic while locked so the state is visible. Hue 0 = normal.
            _pic.Hue = _def.Locked ? (ushort)0x0021 : (ushort)0;
        }

        protected override void OnMouseEnter(int x, int y)
        {
            _pic.Graphic = GRAPHIC_HOVER;
            RefreshTooltip();
            base.OnMouseEnter(x, y);
        }

        protected override void OnMouseExit(int x, int y)
        {
            _pic.Graphic = GRAPHIC_NORMAL;
            base.OnMouseExit(x, y);
        }

        public override void Update()
        {
            base.Update();

            // Drag-follow: when the widget itself moved, shift the group's bars
            // by the same delta so the whole cluster tracks the header.
            if (X != _lastX || Y != _lastY)
            {
                if (_def.Locked)
                {
                    // Locked: snap back, do not move.
                    X = _lastX;
                    Y = _lastY;
                }
                else
                {
                    int dx = X - _lastX;
                    int dy = Y - _lastY;

                    foreach (BaseHealthBarGump bar in PluginStatusBarGroups.GetLiveMembers(_def.Id))
                    {
                        bar.X += dx;
                        bar.Y += dy;
                    }

                    _def.X = X;
                    _def.Y = Y;
                    _lastX = X;
                    _lastY = Y;
                }
            }
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            base.OnMouseUp(x, y, button);

            if (button == MouseButtonType.Left && Keyboard.Shift)
            {
                _def.Locked = !_def.Locked;
                RefreshLockCue();
                return;
            }

            if (button == MouseButtonType.Right && Keyboard.Shift)
            {
                var members = new List<BaseHealthBarGump>(PluginStatusBarGroups.GetLiveMembers(_def.Id));

                foreach (BaseHealthBarGump bar in members)
                {
                    PluginStatusBars.CloseStatusBar(bar.LocalSerial);
                }

                RefreshTooltip();
            }
        }
    }
}
```

Notes for the implementer:
- Verify the actual namespaces: `GumpPic` and `Label` live in `ClassicUO.Game.UI.Controls` — if the file already `using ClassicUO.Game.UI.Controls;`, drop the `Controls.` qualifier. Check `UseSpellButtonGump.cs` for the exact `using`s and copy them.
- `GumpPic` must expose a settable `Graphic` and `Hue`. Confirm against the `GumpPic` class; if `Graphic` is not settable, keep two `GumpPic`s (normal + hover) and toggle their `IsVisible` in enter/exit instead.
- `Label` ctor args are `(text, isunicode, hue, maxwidth, font, style, align)` in existing code — match the real signature from `MacroButtonGump.cs`/other `Label` usages; the version above uses the common `(text, isunicode, hue, font, style)` overload. Adjust to whichever overload compiles.
- `base.Update()` + the `X/Y` delta approach is the pragmatic drag-follow. If `AnchorManager` fights per-bar moves, switch to `UIManager.AnchorManager` group `UpdateLocation` — but per-bar delta is acceptable because plugin bars are only anchored within their own group.

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded. Fix any namespace/overload mismatches per the notes above until it compiles.

- [ ] **Step 3: Replace the temporary literal in Task 3 (if used)**

If Task 3 Step 5 used the literal `24`, change it to `PluginAnchorGroupGump.WidgetHeight` now and rebuild.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/PluginAnchorGroupGump.cs src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs
git commit -m "feat(anchor-groups): permanent group widget gump (drag-follow, lock, clear, tooltip)"
```

---

### Task 5: `PluginAnchorGroupManager` — build/rebuild widgets from defs + load hook

**Files:**
- Create: `src/ClassicUO.Client/Game/Managers/PluginAnchorGroupManager.cs`
- Modify: the world/gump load site that calls `Profile.ReadGumps` (search for `ReadGumps(` — likely `Game/Scenes/GameScene.cs`); add a `Rebuild` call after gumps are restored.
- Test: none (integration — verified by manual run).

**Interfaces:**
- Consumes: `Profile.PluginAnchorGroups`, `PluginAnchorGroupGump` (Task 4), `UIManager`.
- Produces (all `internal static`):
  - `void Rebuild(World world)` — dispose every existing `PluginAnchorGroupGump` in `UIManager`, then create one per def in `ProfileManager.CurrentProfile.PluginAnchorGroups` and `UIManager.Add` it.
  - `void DisposeAll()` — dispose all widget instances (used on logout, optional).

- [ ] **Step 1: Create the manager**

```csharp
// src/ClassicUO.Client/Game/Managers/PluginAnchorGroupManager.cs
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Owns the on-screen permanent anchor-group widgets. Rebuilt from
    /// Profile.PluginAnchorGroups on world load and after Options apply.
    /// </summary>
    internal static class PluginAnchorGroupManager
    {
        public static void Rebuild(World world)
        {
            DisposeAll();

            var defs = ProfileManager.CurrentProfile?.PluginAnchorGroups;

            if (defs == null)
            {
                return;
            }

            foreach (PluginAnchorGroupDef def in defs)
            {
                if (def == null || def.Id == 0)
                {
                    continue;
                }

                UIManager.Add(new PluginAnchorGroupGump(world, def));
            }
        }

        public static void DisposeAll()
        {
            List<PluginAnchorGroupGump> existing = UIManager.Gumps.OfType<PluginAnchorGroupGump>().ToList();

            foreach (PluginAnchorGroupGump g in existing)
            {
                g.Dispose();
            }
        }
    }
}
```

Note: confirm `UIManager.Gumps` is enumerable and that `System.Linq` is needed for `OfType`/`ToList` — add `using System.Linq;`. If `UIManager` exposes a different collection accessor (e.g. `UIManager.Gumps` is a `LinkedList<Gump>`), adapt the enumeration; `OfType<PluginAnchorGroupGump>()` works on any `IEnumerable`.

- [ ] **Step 2: Hook `Rebuild` into the load path**

Find the call to `ReadGumps(` (Grep the client for `ReadGumps(`). At that call site, after the returned gumps are added to `UIManager`, add:

```csharp
            PluginAnchorGroupManager.Rebuild(world);
```

Use the `world` instance already in scope at that site. If the site has no `world` local, use `Client.Game.UO.World` (matches `PluginStatusBars.OpenStatusBar`).

- [ ] **Step 3: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginAnchorGroupManager.cs <the-modified-load-site-file>
git commit -m "feat(anchor-groups): manager to build permanent widgets on world load"
```

---

### Task 6: Options UI — "Anchor Groups" editor list

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs`
- Create: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.AnchorGroupRow.cs` (a small row control; or nest the class inside `OptionsGump` — follow whichever the file already does for per-row controls like `AliasEntryControl`)
- Test: none (UI — verified by manual run).

**Interfaces:**
- Consumes: `Profile.PluginAnchorGroups`, `PluginAnchorGroupDef`, `FillOrder`, `PluginAnchorGroupManager.Rebuild` (Task 5).
- Produces: a new options subsection with add/edit/delete rows; on Apply writes the edited list back to the profile and calls `PluginAnchorGroupManager.Rebuild`.

**Mirror the aliases editor (`OptionsGump.cs:3424-3450`): a `ScrollArea` + `DataBox` populated with one row control per def, an `Add` `NiceButton`, per-row delete.**

- [ ] **Step 1: Add the row control**

Create a row control that edits one `PluginAnchorGroupDef` in place. Model it on `AliasEntryControl` (referenced at `OptionsGump.cs:3446`). Each row holds: an `InputField` for `Id` (numeric), an `InputField` for `Label`, numeric `InputField`s for `Columns` and `Rows`, a `Combobox` for `Fill` (values `new[] { "Column", "Row" }`, `SelectedIndex = (int)def.Fill`), and a delete `NiceButton`. Wire each control's change back onto the `def` (parse on Apply — see Step 3). Layout with fixed X offsets on one row, `Y = databox.Children.Count * 26` like the aliases loop.

Use the existing helpers where possible:
- `AddInputField(area, x, y, width, height, label, maxWidth, set_down, numbersOnly, maxCharCount)` (`OptionsGump.cs:4963`).
- `AddCombobox(area, values, currentIndex, x, y, width)` (`OptionsGump.cs:5048`).

- [ ] **Step 2: Build the subsection in the options tab that hosts plugin status-bar settings**

Locate the section that currently builds `_pluginStatusBarMaxRows` (`OptionsGump.cs:1179-1196`). Immediately after those two fields:
- Relabel the two existing fields to indicate fallback, e.g. change the label text at `:1179` from `"Plugin status bar max rows"` to `"Plugin status bar max rows (fallback)"`, and similarly for columns.
- Add a header label `"Anchor groups"`.
- Add a `DataBox _anchorGroupsBox` and populate it from `_currentProfile.PluginAnchorGroups` (one row control per def), exactly like the aliases loop:

```csharp
            DataBox anchorBox = new DataBox(0, 0, 0, 0) { WantUpdateSize = true };
            _anchorGroupsBox = anchorBox;

            foreach (PluginAnchorGroupDef def in _currentProfile.PluginAnchorGroups)
            {
                anchorBox.Add(new AnchorGroupRow(this, def) { Y = anchorBox.Children.Count * 26 });
            }

            section3.Add(anchorBox);
```

- Add an "Add group" `NiceButton`; on `MouseUp` create a new `PluginAnchorGroupDef`, add it to `_currentProfile.PluginAnchorGroups`, add a matching row to `anchorBox`, and re-layout rows' `Y`:

```csharp
            NiceButton addAnchor = new NiceButton(0, 0, 130, 25, ButtonAction.Activate, "Add group");
            addAnchor.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtonType.Left) return;
                var def = new PluginAnchorGroupDef();
                _currentProfile.PluginAnchorGroups.Add(def);
                _anchorGroupsBox.Add(new AnchorGroupRow(this, def) { Y = _anchorGroupsBox.Children.Count * 26 });
                _anchorGroupsBox.WantUpdateSize = true;
            };
            section3.Add(addAnchor);
```

Declare `private DataBox _anchorGroupsBox;` with the other option fields (~`:130`).

- [ ] **Step 3: Persist on Apply + validate + rebuild**

In the Apply path (near `:4291-4304` where the plugin bar fields are parsed), after the existing fallback parse:
- For each `AnchorGroupRow`, commit its edited values onto its `def` (parse `Id`/`Columns`/`Rows` with `int.TryParse`, clamp `Columns`/`Rows` to ≥1, read `Fill` from the combobox `SelectedIndex`, copy `Label`).
- Validate: drop defs with `Id == 0`; if two defs share an `Id`, keep the first and drop the rest (or surface a message — minimal: keep first). Delete-flagged rows remove their def from the list.
- After writing back, rebuild widgets:

```csharp
            PluginAnchorGroupManager.Rebuild(World);
```

(Use the `World` reference already available in `OptionsGump` — it derives from `Gump` which exposes `World`.)

- [ ] **Step 4: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 5: Manual smoke test**

Run the client (`-ClassicLogin` opt-out not needed). In Options → the plugin status-bar section: add a group (Id 1, Label "Test", 2 cols, 3 rows, Column fill), Apply. Confirm a `0x25F8` button with "Test" label appears on screen, hovers to `0x25F9`, tooltip shows `Test: 0 / 6`. Have a plugin/script call `OpenStatusBar(serial, 0, 0, true, 1)` for several mobiles → bars fill the 2×3 grid at the widget, reject the 7th. Shift+left-click toggles lock (drag disabled), shift+right-click clears bars.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.AnchorGroupRow.cs
git commit -m "feat(anchor-groups): Options editor for permanent anchor groups"
```

---

### Task 7: Full build + test sweep

**Files:** none (verification).

- [ ] **Step 1: Full solution build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeded, no warnings-as-errors from new files.

- [ ] **Step 2: Full unit test run**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: All pass (including `PluginAnchorGroupDefTests`, `PluginStatusBarLayoutTests`).

- [ ] **Step 3: Confirm no regression in existing plugin-bar tests**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatus"`
Expected: All pass.

---

## Self-Review

**Spec coverage:**
- Data model (id/label/cols/rows/fill/x/y/locked) → Task 1. ✔
- Widget `0x25F8`/`0x25F9` + label, always visible, drag, shift+lclick lock, shift+rclick clear, hover tooltip count → Task 4. ✔
- Per-group grid + row/column-major fill + reject-when-full → Tasks 2, 3. ✔
- Seed at widget origin → Task 3. ✔
- Global settings kept as fallback for undefined groups → Task 2 resolvers (def-first, else global). ✔
- Options dynamic list (add/edit/delete) + relabel globals as fallback → Task 6. ✔
- Lifecycle: widgets built on world load + rebuilt after Options apply → Task 5, Task 6 Step 3. ✔
- Edge cases (dup/zero id, prune on death, def deleted) → Task 6 validation, existing `GetLiveMembers` prune, Task 5 rebuild disposes stale widgets. ✔
- Tests: GridCell both orders, per-group capacity, resolver fallback, serialization round-trip → Tasks 1–3. ✔

**Placeholder scan:** UI tasks (4, 6) intentionally give pattern-anchored instructions with real helper signatures + snippets rather than full literal code, because they must be reconciled against exact control-ctor overloads in the codebase; each such step names the exact file/line pattern to copy and the fallback if an overload differs. No "TBD"/"handle edge cases"-style gaps in logic tasks.

**Type consistency:** `GridCell(index, rows, cols, fill)`, `ResolveMaxRows(int)`, `ResolveMaxColumns(int)`, `ResolveFill(int)`, `GetDef(int)`, `NeighborFor(int,int,int,FillOrder)`, `PluginAnchorGroupGump.WidgetHeight`, `PluginAnchorGroupManager.Rebuild(World)` used consistently across tasks.

## Known follow-ups (out of scope for this plan)
- If `AnchorManager` interferes with per-bar drag-follow, migrate Task 4's `Update` to group `UpdateLocation`.
- Persisting `Locked`/position happens via the def object being the same reference the profile serializes; a profile save after drag/lock persists it. If the profile is only saved on certain events, a follow-up may add an explicit save-on-change.
