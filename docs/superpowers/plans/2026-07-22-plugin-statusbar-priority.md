# Plugin Status-Bar Priority Ordering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `SetStatusBarPriority(uint serial, int priority)` plugin primitive that orders status bars within an anchor group by priority (desc, insertion-order tiebreak), reflowing the group densely on open, close, and priority change.

**Architecture:** A per-serial priority store (mirroring `PluginStatusOverlays`) feeds a pure ordering helper. Grid placement is factored out of `AddToGroup` into a shared `PlaceInGrid` used by both incremental open and a new `ReflowGroup`. Open/close/priority-change all funnel through `ReflowGroup`, which detaches the group's members and rebuilds the anchor grid in sorted order. A new append-only ABI binding (`SetStatusBarPriorityFn`) plumbs the call from plugin → host → client, mirroring `SetOverlay` exactly.

**Tech Stack:** C# `net10.0`, FNA-XNA, xUnit (`tests/ClassicUO.UnitTests`, `tests/ClassicUO.BootstrapHost.Tests`), native function-pointer ABI (`ClientBindings`).

## Global Constraints

- Branch stacks on `feat/anchor-groups` (which provides `PluginStatusBarGroups`, per-group `ResolveMaxRows/Columns/Fill`, `NeighborFor`, `PluginAnchorGroupGump.WidgetHeight`, `GetDef`). Do NOT re-implement those.
- New source files carry the BSD-2 header: first line `// SPDX-License-Identifier: BSD-2-Clause`.
- **ABI is append-only.** New function-pointer fields are appended, never inserted:
  - `HostBridge.cs` `ClientBindings`: append after `ClearCharactersFn` (currently the last field; no `ClientVersion` in this struct).
  - `PluginHost.cs` `ClientBindings`: append after `ClearCharactersFn` and **before** `ClientVersion` (which MUST remain the last field). The two structs must stay byte-exact prefix-compatible — identical append position.
- Native signature of the new binding: `void(uint serial, int priority)` → `delegate* unmanaged[Cdecl]<uint, int, void>`.
- `ClassicUO.Client` internals are visible to `ClassicUO.UnitTests` via `InternalsVisibleTo`.
- A public `[Theory]` test method cannot take an `internal` type as a parameter (CS0051) — use primitive params in `InlineData` and cast inside the body (established in the anchor-groups work).
- Match surrounding style; no allocations in hot loops beyond what the sort requires.
- Build: `dotnet build ClassicUO.sln -c Debug`. Tests: `dotnet test tests/ClassicUO.UnitTests` and `dotnet test tests/ClassicUO.BootstrapHost.Tests`.

---

### Task 1: Priority store + pure ordering helper

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs` (add `PluginStatusPriorities` class near `PluginStatusOverlays` ~:15-56; add `OrderByPriority` helper near the other `internal static` layout helpers)
- Test: `tests/ClassicUO.UnitTests/PluginStatusPriorityTests.cs` (create)

**Interfaces:**
- Produces:
  - `internal static class PluginStatusPriorities` with `void Set(uint serial, int priority)` (store; `priority == 0` → `Remove`), `int Get(uint serial)` (value or `0`), `void Clear(uint serial)`, `void Reset()` (test-only).
  - `internal static int[] PluginStatusBars.OrderByPriority(int[] priorities)` — returns indices `0..n-1` ordered by priority **descending**, ties broken by **ascending original index** (stable).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ClassicUO.UnitTests/PluginStatusPriorityTests.cs
// SPDX-License-Identifier: BSD-2-Clause
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class PluginStatusPriorityTests
    {
        [Fact]
        public void Store_DefaultsToZero_AndResetsByZero()
        {
            PluginStatusPriorities.Reset();
            Assert.Equal(0, PluginStatusPriorities.Get(0x1234));
            PluginStatusPriorities.Set(0x1234, 5);
            Assert.Equal(5, PluginStatusPriorities.Get(0x1234));
            PluginStatusPriorities.Set(0x1234, 0); // reset removes entry
            Assert.Equal(0, PluginStatusPriorities.Get(0x1234));
            PluginStatusPriorities.Clear(0x1234);
            Assert.Equal(0, PluginStatusPriorities.Get(0x1234));
        }

        [Theory]
        // higher priority first; ties keep original (insertion) order
        [InlineData(new[] { 0, 0, 0 }, new[] { 0, 1, 2 })]                 // all equal -> identity
        [InlineData(new[] { 1, 5, 3 }, new[] { 1, 2, 0 })]                 // 5,3,1
        [InlineData(new[] { 5, 5, 1 }, new[] { 0, 1, 2 })]                 // tie 5,5 keeps 0<1
        [InlineData(new[] { -1, 0, -1 }, new[] { 1, 0, 2 })]               // 0 above -1; tie keeps 0<2
        public void OrderByPriority_SortsDescStableTiebreak(int[] priorities, int[] expected)
        {
            Assert.Equal(expected, PluginStatusBars.OrderByPriority(priorities));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusPriorityTests"`
Expected: FAIL — `PluginStatusPriorities` / `OrderByPriority` do not exist (compile error).

- [ ] **Step 3: Add the priority store**

In `PluginStatusBars.cs`, after the `PluginStatusOverlays` class (~line 56), add:

```csharp
    /// <summary>
    /// Plugin-driven per-serial ordering priority. Absence == priority 0
    /// (the default); a value of 0 is stored as removal to keep the map small.
    /// Policy lives in the plugin; the client only stores and sorts.
    /// </summary>
    internal static class PluginStatusPriorities
    {
        private static readonly Dictionary<uint, int> _priorities = new Dictionary<uint, int>();

        public static void Set(uint serial, int priority)
        {
            if (priority == 0)
            {
                _priorities.Remove(serial);
                return;
            }

            _priorities[serial] = priority;
        }

        public static int Get(uint serial)
        {
            return _priorities.TryGetValue(serial, out int p) ? p : 0;
        }

        public static void Clear(uint serial) => _priorities.Remove(serial);

        /// <summary>Test-only: drops every priority so tests start clean.</summary>
        public static void Reset() => _priorities.Clear();
    }
```

- [ ] **Step 4: Add the pure ordering helper**

In `PluginStatusBars.cs`, near the other layout helpers (e.g. after `NeighborFor`), add:

```csharp
        /// <summary>
        /// Indices into <paramref name="priorities"/> ordered by priority
        /// descending, ties broken by ascending original index (stable). This
        /// is the single source of truth for group member ordering.
        /// </summary>
        internal static int[] OrderByPriority(int[] priorities)
        {
            int[] order = new int[priorities.Length];

            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            // Stable insertion sort: small member counts, and it guarantees the
            // ascending-index tiebreak without extra key allocation.
            for (int i = 1; i < order.Length; i++)
            {
                int cur = order[i];
                int j = i - 1;

                while (j >= 0 && priorities[order[j]] < priorities[cur])
                {
                    order[j + 1] = order[j];
                    j--;
                }

                order[j + 1] = cur;
            }

            return order;
        }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusPriorityTests"`
Expected: PASS (store fact + 4 theory cases).

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs tests/ClassicUO.UnitTests/PluginStatusPriorityTests.cs
git commit -m "feat(statusbar-priority): per-serial priority store and stable ordering helper"
```

---

### Task 2: Group reflow + open/close/priority integration

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs` (`PluginStatusBarGroups`, `AddToGroup`, `OpenStatusBar`, `CloseStatusBar`; add `SetStatusBarPriority`, `ReflowGroup`, `PlaceInGrid`)
- Test: `tests/ClassicUO.UnitTests/PluginStatusBarLayoutTests.cs` (extend — pure `OrderedIndices` mapping if factored; see below)

**Interfaces:**
- Consumes: Task 1 (`PluginStatusPriorities`, `OrderByPriority`); existing `NeighborFor`, `ResolveMaxRows/Columns/Fill`, `GetDef`, `PluginAnchorGroupGump.WidgetHeight`, `AnchorManager` (`DropControl`, `DetachControl`), `PluginStatusBarGroups`.
- Produces:
  - `internal static int PluginStatusBarGroups.FindGroupOf(uint serial)` — groupId of the live member with that serial, else `0`.
  - `void PluginStatusBars.SetStatusBarPriority(uint serial, int priority)` — the binding target (store + reflow).
  - `private static void PluginStatusBars.ReflowGroup(int groupId)`.
  - `private static void PluginStatusBars.PlaceInGrid(IReadOnlyList<BaseHealthBarGump> ordered, int rows, int cols, FillOrder fill, int originX, int originY)` — shared placement used by open + reflow.

**Note:** live reflow drives `UIManager`/`AnchorManager` and is verified by build + manual run (repo pattern). Keep the pure ordering in Task 1's tested helper; this task wires it to gumps.

- [ ] **Step 1: Add `FindGroupOf` to `PluginStatusBarGroups`**

```csharp
        /// <summary>groupId of the live member whose serial matches, else 0.</summary>
        public static int FindGroupOf(uint serial)
        {
            foreach (KeyValuePair<int, List<BaseHealthBarGump>> kv in _members)
            {
                List<BaseHealthBarGump> list = kv.Value;

                for (int i = 0; i < list.Count; i++)
                {
                    BaseHealthBarGump b = list[i];

                    if (b != null && !b.IsDisposed && b.LocalSerial == serial)
                    {
                        return kv.Key;
                    }
                }
            }

            return 0;
        }
```

- [ ] **Step 2: Extract `PlaceInGrid` from `AddToGroup` (refactor, behavior-preserving)**

Replace the neighbor-placement block of `AddToGroup` so that both open and reflow share one implementation. Add:

```csharp
        // Positions an already-ordered member list into the group's grid,
        // anchoring the first member at (originX, originY) and snapping each
        // subsequent member to its neighbor via NeighborFor + fill-order math.
        // Rebuilds the AnchorGroup so the whole grid drags as one unit.
        private static void PlaceInGrid(
            IReadOnlyList<BaseHealthBarGump> ordered,
            int rows, int cols, FillOrder fill,
            int originX, int originY)
        {
            if (ordered.Count == 0)
            {
                return;
            }

            BaseHealthBarGump first = ordered[0];
            first.X = originX;
            first.Y = originY;

            AnchorManager.AnchorGroup group = new AnchorManager.AnchorGroup(first);
            UIManager.AnchorManager[first] = group;

            for (int i = 1; i < ordered.Count; i++)
            {
                BaseHealthBarGump bar = ordered[i];
                (int neighborIndex, bool startNewLine) = NeighborFor(i, rows, cols, fill);
                BaseHealthBarGump neighbor = ordered[neighborIndex];

                if (!startNewLine)
                {
                    if (fill == FillOrder.RowMajor)
                    {
                        bar.X = neighbor.X + neighbor.GroupMatrixWidth;
                        bar.Y = neighbor.Y;
                    }
                    else
                    {
                        bar.X = neighbor.X;
                        bar.Y = neighbor.Y + neighbor.GroupMatrixHeight;
                    }
                }
                else
                {
                    if (fill == FillOrder.RowMajor)
                    {
                        bar.X = neighbor.X;
                        bar.Y = neighbor.Y + neighbor.GroupMatrixHeight;
                    }
                    else
                    {
                        bar.X = neighbor.X + neighbor.GroupMatrixWidth;
                        bar.Y = neighbor.Y;
                    }
                }

                UIManager.AnchorManager.DropControl(bar, neighbor);
            }
        }
```

- [ ] **Step 3: Add `GroupOriginX/Y` and `ReflowGroup`**

```csharp
        // The fixed top-left the grid grows from. Defined groups derive it from
        // the anchor def (stable); undefined groups use the current top-left of
        // live members (smallest Y, then X), so the group stays where it sits.
        private static (int x, int y) GroupOrigin(int groupId, List<BaseHealthBarGump> members)
        {
            PluginAnchorGroupDef def = GetDef(groupId);

            if (def != null)
            {
                return (def.X, def.Y + PluginAnchorGroupGump.WidgetHeight);
            }

            if (members.Count == 0)
            {
                return (0, 0);
            }

            BaseHealthBarGump topLeft = members[0];

            for (int i = 1; i < members.Count; i++)
            {
                BaseHealthBarGump b = members[i];

                if (b.Y < topLeft.Y || (b.Y == topLeft.Y && b.X < topLeft.X))
                {
                    topLeft = b;
                }
            }

            return (topLeft.X, topLeft.Y);
        }

        // Re-lays a group's live members into the grid ordered by priority
        // (desc, insertion tiebreak). Detaches everything, then rebuilds via
        // PlaceInGrid so cells reflect the new order and collapse densely.
        private static void ReflowGroup(int groupId)
        {
            List<BaseHealthBarGump> members = PluginStatusBarGroups.GetLiveMembers(groupId);

            if (members.Count == 0)
            {
                return;
            }

            (int originX, int originY) = GroupOrigin(groupId, members);

            // Order indices by priority using the tested pure helper.
            int[] priorities = new int[members.Count];

            for (int i = 0; i < members.Count; i++)
            {
                priorities[i] = PluginStatusPriorities.Get(members[i].LocalSerial);
            }

            int[] order = OrderByPriority(priorities);

            List<BaseHealthBarGump> ordered = new List<BaseHealthBarGump>(members.Count);

            foreach (int idx in order)
            {
                ordered.Add(members[idx]);
            }

            // Detach all before rebuilding so the matrix starts clean.
            foreach (BaseHealthBarGump bar in members)
            {
                UIManager.AnchorManager.DetachControl(bar);
            }

            PlaceInGrid(ordered, ResolveMaxRows(groupId), ResolveMaxColumns(groupId), ResolveFill(groupId), originX, originY);

            PluginStatusBarGroups.Track(groupId, UIManager.AnchorManager[ordered[0]]);
        }
```

Note: `_members` insertion order is NOT reordered — it stays the stable-tiebreak source. `ReflowGroup` only reorders the transient `ordered` list used for placement.

- [ ] **Step 4: Route `AddToGroup` through reflow**

Change `AddToGroup` so that after adding the member it reflows (instead of positioning only the new bar). Keep the first-member seed path:

```csharp
        private static void AddToGroup(int groupId, BaseHealthBarGump bar)
        {
            PluginStatusBarGroups.AddMember(groupId, bar);
            ReflowGroup(groupId);
        }
```

(`ReflowGroup` seeds a fresh `AnchorGroup` for a single-member group, so the old explicit-seed branch is no longer needed. Verify `AddMember` still guards `groupId == 0` / null.)

- [ ] **Step 5: Add the binding target `SetStatusBarPriority`**

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

- [ ] **Step 6: Reflow on close**

In `CloseStatusBar`, capture the groupId before disposing, then reflow after prune:

```csharp
        public static void CloseStatusBar(uint serial)
        {
            BaseHealthBarGump bar = UIManager.GetGump<BaseHealthBarGump>(serial);

            if (bar == null)
            {
                return;
            }

            int groupId = PluginStatusBarGroups.FindGroupOf(serial);

            UIManager.AnchorManager.DetachControl(bar);
            bar.Dispose();

            PluginStatusPriorities.Clear(serial);
            PluginStatusBarGroups.PruneEmpty();

            if (groupId != 0)
            {
                ReflowGroup(groupId); // surviving bars collapse upward
            }
        }
```

- [ ] **Step 7: Build + run existing layout tests**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded.

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusBar"`
Expected: PASS (existing layout tests + Task 1 priority tests — no regression).

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs
git commit -m "feat(statusbar-priority): reflow groups by priority on open, close, and priority change"
```

---

### Task 3: ABI binding — plumb `SetStatusBarPriority` plugin → host → client

**Files:**
- Modify: `src/ClassicUO.PluginApi/IStatusBars.cs` (append method after `SetOverlay`, line 36)
- Modify: `src/ClassicUO.BootstrapHost/HostBridge.cs` (append `nint SetStatusBarPriorityFn;` after `ClearCharactersFn`, line 387)
- Modify: `src/ClassicUO.BootstrapHost/PluginContextImpl.cs` (`StatusBarsImpl` — add method mirroring `SetOverlay`, ~:224-259)
- Modify: `src/ClassicUO.Client/PluginHost.cs` (append struct field before `ClientVersion`; add delegate + field + assignment)
- Test: `tests/ClassicUO.BootstrapHost.Tests/StatusBarsTests.cs` (add marshaling test mirroring the `SetOverlay` one)

**Interfaces:**
- Consumes: `PluginStatusBars.SetStatusBarPriority` (Task 2).
- Produces: `IStatusBars.SetStatusBarPriority(uint serial, int priority)` reachable from a v2 plugin, marshaled to the game thread, invoking the client target.

- [ ] **Step 1: Write the failing host-marshaling test**

Open `tests/ClassicUO.BootstrapHost.Tests/StatusBarsTests.cs`, find the existing `SetOverlay` marshaling test, and add an analogous one. It must fill a fake `ClientBindings` with a function pointer for `SetStatusBarPriorityFn`, call `StatusBarsImpl.SetStatusBarPriority(serial, priority)` on the game thread, and assert the pointer was invoked with the exact `serial` and `priority`. Mirror the existing test's harness precisely (same fake-bridge/capture mechanism). Example shape (adapt to the file's actual harness):

```csharp
[Fact]
public void SetStatusBarPriority_InvokesBinding_WithSerialAndPriority()
{
    uint gotSerial = 0; int gotPriority = 0; bool called = false;
    // register a delegate* unmanaged[Cdecl]<uint,int,void> into the fake bindings'
    // SetStatusBarPriorityFn that sets called/gotSerial/gotPriority
    // ... (mirror the SetOverlay test's setup) ...

    statusBars.SetStatusBarPriority(0xDEADBEEF, 7);

    Assert.True(called);
    Assert.Equal(0xDEADBEEFu, gotSerial);
    Assert.Equal(7, gotPriority);
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~SetStatusBarPriority"`
Expected: FAIL — `SetStatusBarPriorityFn` / `StatusBarsImpl.SetStatusBarPriority` do not exist (compile error).

- [ ] **Step 3: Append the public API method**

In `src/ClassicUO.PluginApi/IStatusBars.cs`, after `SetOverlay` (line 36):

```csharp
        /// <summary>
        /// Sets the ordering priority for the status bar of <paramref name="serial"/>.
        /// Within its anchor group, bars are ordered by priority descending, then by
        /// the order they were opened; default is 0, and passing 0 resets. Priority
        /// only affects layout while the serial's bar belongs to a group. Auto-marshals
        /// to the game thread.
        /// </summary>
        void SetStatusBarPriority(uint serial, int priority);
```

- [ ] **Step 4: Append the host binding field**

In `src/ClassicUO.BootstrapHost/HostBridge.cs`, immediately after `ClearCharactersFn` (line 387, the last field of `ClientBindings`):

```csharp
    public nint SetStatusBarPriorityFn;      // void(uint serial, int priority)
```

- [ ] **Step 5: Add the host marshaling method**

In `src/ClassicUO.BootstrapHost/PluginContextImpl.cs`, in `StatusBarsImpl`, after `SetOverlay`:

```csharp
        public unsafe void SetStatusBarPriority(uint serial, int priority)
        {
            var fn = _bridge.ClientBindings.SetStatusBarPriorityFn;
            if (fn == 0) return;
            if (_bridge.IsGameThread)
                ((delegate* unmanaged[Cdecl]<uint, int, void>)fn)(serial, priority);
            else
                _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, int, void>)fn)(serial, priority));
        }
```

- [ ] **Step 6: Append the client struct field + delegate + assignment**

In `src/ClassicUO.Client/PluginHost.cs`:

(a) In `ClientBindings`, **after `ClearCharactersFn` (line 77) and before `ClientVersion` (line 80)**:

```csharp
        public IntPtr /*delegate*<uint, int, void>*/ SetStatusBarPriorityFn;
```

(b) Near the `dSetOverlay` delegate/field (~:300-303):

```csharp
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dSetStatusBarPriority(uint serial, int priority);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dSetStatusBarPriority _setStatusBarPriority = Game.Managers.PluginStatusBars.SetStatusBarPriority;
```

(c) In `Initialize`, near the `SetOverlayFn` assignment (~:457):

```csharp
        cuoHost.SetStatusBarPriorityFn = Marshal.GetFunctionPointerForDelegate(_setStatusBarPriority);
```

- [ ] **Step 7: Run the marshaling test to verify it passes**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~SetStatusBarPriority"`
Expected: PASS.

- [ ] **Step 8: Full build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeded (both `ClientBindings` structs compile; prefix-compatible).

- [ ] **Step 9: Commit**

```bash
git add src/ClassicUO.PluginApi/IStatusBars.cs src/ClassicUO.BootstrapHost/HostBridge.cs src/ClassicUO.BootstrapHost/PluginContextImpl.cs src/ClassicUO.Client/PluginHost.cs tests/ClassicUO.BootstrapHost.Tests/StatusBarsTests.cs
git commit -m "feat(statusbar-priority): append-only ABI binding for SetStatusBarPriority"
```

---

### Task 4: Full build + test sweep

**Files:** none (verification).

- [ ] **Step 1: Full solution build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeded.

- [ ] **Step 2: Both test suites**

Run: `dotnet test tests/ClassicUO.UnitTests`
Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests`
Expected: All pass (new priority tests, marshaling test, and no regression in existing status-bar/layout tests).

---

## Self-Review

**Spec coverage:**
- New primitive `SetStatusBarPriority` → Tasks 2 (target) + 3 (ABI). ✔
- Per-serial store, default 0, reset via 0 → Task 1. ✔
- Sort priority desc, insertion tiebreak → Task 1 `OrderByPriority`. ✔
- Reflow on open/close/priority-change, collapse upward → Task 2 (`AddToGroup`, `CloseStatusBar`, `SetStatusBarPriority` all call `ReflowGroup`). ✔
- Dense grid / no holes → `PlaceInGrid` fills contiguous cells from ordered list. ✔
- Group origin (defined vs undefined) → Task 2 `GroupOrigin`. ✔
- Priority scoped to group; ungrouped store-only → `SetStatusBarPriority` guards `FindGroupOf == 0`. ✔
- Priority before open honored → store read in `ReflowGroup`, invoked from `AddToGroup`. ✔
- Append-only ABI, both structs, host + client + test → Task 3, Global Constraints. ✔
- DRY placement shared by open + reflow → `PlaceInGrid`. ✔

**Placeholder scan:** Task 3 Step 1's test is shape-guided (must adapt to the actual `StatusBarsTests.cs` harness) — this is deliberate: the fake-bridge capture mechanism must mirror the existing `SetOverlay` test in that file, which the implementer reads directly. All logic tasks (1, 2) carry complete code.

**Type consistency:** `OrderByPriority(int[]) -> int[]`, `PluginStatusPriorities.Get/Set/Clear/Reset`, `FindGroupOf(uint) -> int`, `ReflowGroup(int)`, `PlaceInGrid(IReadOnlyList<BaseHealthBarGump>, int, int, FillOrder, int, int)`, `GroupOrigin(int, List<...>) -> (int,int)`, `SetStatusBarPriority(uint, int)`, native `<uint, int, void>` — consistent across tasks.

## Known follow-ups (out of scope)
- Optional `priority` param on `OpenStatusBar` (second binding) if a one-call convenience is later wanted.
- Reflow currently rebuilds the whole `AnchorGroup` on every change; if profiling shows cost with very large groups, add an "order unchanged → skip" short-circuit in `ReflowGroup`.
