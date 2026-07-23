# Anchor status-bar lock + Alt-eject Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Status bars in an anchor group (a group with a `PluginAnchorGroupGump` anchor widget) can no longer be dragged individually; only `Alt`+drag ejects a bar, which then reflows the remainder by priority.

**Architecture:** A pure decision helper (`ResolveGroupedDrag`) classifies a drag on a grouped bar into PassThrough / SnapBack / Eject from three booleans. `AnchorableGump.OnMove` computes membership via `TryGetHeaderedGroup` and dispatches: SnapBack pins the bar, Eject calls `LeaveGroup` (detach + drop membership + reflow), PassThrough runs the existing logic. Only the pure helper is unit-tested; the gump-wiring methods are verified by running the client, matching the codebase convention (`ReflowGroup`/`PlaceInGrid` have no unit tests).

**Tech Stack:** C# `net10.0`, xUnit, FNA-XNA. Existing anchor system (`AnchorManager`, `PluginStatusBars`, `PluginStatusBarGroups`).

## Global Constraints

- License header on every new source file: `// SPDX-License-Identifier: BSD-2-Clause` (first line).
- Never mention Claude/AI in commit messages.
- An anchor group = a tracked `PluginStatusBarGroups` group whose id has a `PluginAnchorGroupDef` (`PluginStatusBars.GetDef(id) != null`). Only these get a `PluginAnchorGroupGump` anchor widget.
- The eject modifier is `Keyboard.Alt`, gated by `!ProfileManager.CurrentProfile.HoldAltToMoveGumps` — identical to the existing anchor-detach condition in `AnchorableGump.OnMove`.
- Do not change classic `AnchorManager` groups, headerless/undefined status-bar groups, anchor drag-move via `PluginAnchorGroupGump`, or `def.Locked` semantics.
- Internal enum as a public test-method parameter triggers CS0051 even with `InternalsVisibleTo`; pass the expected value as `int` and cast, as `PluginStatusBarLayoutTests` already does for `FillOrder`.

---

### Task 1: Pure grouped-drag decision helper

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs` (add enum + method to the `PluginStatusBars` static class, near the other `internal static` pure helpers around line 493–630)
- Test: `tests/ClassicUO.UnitTests/AnchorStatusBarLockTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `internal enum PluginStatusBars.GroupedDragAction { PassThrough, SnapBack, Eject }`
  - `internal static GroupedDragAction PluginStatusBars.ResolveGroupedDrag(bool inAnchoredGroup, bool altHeld, bool holdAltToMoveGumps)`

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/AnchorStatusBarLockTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class AnchorStatusBarLockTests
    {
        // Expected passed as int (not GroupedDragAction) because an internal
        // enum parameter on a public [Theory] trips CS0051 even under
        // InternalsVisibleTo — same reason FillOrder is passed as int in
        // PluginStatusBarLayoutTests.
        [Theory]
        // not in an anchored group -> always fall through, Alt irrelevant
        [InlineData(false, false, false, (int)PluginStatusBars.GroupedDragAction.PassThrough)]
        [InlineData(false, true, false, (int)PluginStatusBars.GroupedDragAction.PassThrough)]
        // in an anchored group, Alt held, normal profile -> eject the bar
        [InlineData(true, true, false, (int)PluginStatusBars.GroupedDragAction.Eject)]
        // in an anchored group, no Alt -> locked, snap back
        [InlineData(true, false, false, (int)PluginStatusBars.GroupedDragAction.SnapBack)]
        // HoldAltToMoveGumps disables the eject modifier -> snap back even with Alt
        [InlineData(true, true, true, (int)PluginStatusBars.GroupedDragAction.SnapBack)]
        [InlineData(true, false, true, (int)PluginStatusBars.GroupedDragAction.SnapBack)]
        public void ResolveGroupedDrag_ClassifiesDrag(bool inAnchoredGroup, bool altHeld, bool holdAlt, int expected)
        {
            var action = PluginStatusBars.ResolveGroupedDrag(inAnchoredGroup, altHeld, holdAlt);
            Assert.Equal(expected, (int)action);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AnchorStatusBarLockTests"`
Expected: FAIL — build error, `PluginStatusBars` does not contain `ResolveGroupedDrag` / `GroupedDragAction`.

- [ ] **Step 3: Write minimal implementation**

In `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs`, inside the `PluginStatusBars` static class (with the other `internal static` pure helpers), add:

```csharp
/// <summary>
/// The three outcomes of dragging a status bar that belongs to a group
/// with an anchor widget.
/// </summary>
internal enum GroupedDragAction
{
    /// <summary>Not a grouped bar; run the normal anchor drag logic.</summary>
    PassThrough,

    /// <summary>Grouped and locked; pin the bar (the anchor moves the cluster).</summary>
    SnapBack,

    /// <summary>Grouped and Alt-dragged; eject the bar from the group.</summary>
    Eject
}

/// <summary>
/// Pure classification of a drag on a status bar. A bar in an anchored group
/// may only leave via Alt-drag (when the eject modifier is enabled, i.e.
/// HoldAltToMoveGumps is off); otherwise it is pinned. Bars not in an
/// anchored group pass through to the existing anchor logic.
/// </summary>
internal static GroupedDragAction ResolveGroupedDrag(bool inAnchoredGroup, bool altHeld, bool holdAltToMoveGumps)
{
    if (!inAnchoredGroup)
    {
        return GroupedDragAction.PassThrough;
    }

    if (altHeld && !holdAltToMoveGumps)
    {
        return GroupedDragAction.Eject;
    }

    return GroupedDragAction.SnapBack;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AnchorStatusBarLockTests"`
Expected: PASS (6 cases).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs tests/ClassicUO.UnitTests/AnchorStatusBarLockTests.cs
git commit -m "feat(anchor): pure grouped-drag decision helper"
```

---

### Task 2: Eject wiring — RemoveMember, TryGetHeaderedGroup, LeaveGroup

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs` — add `RemoveMember` to `PluginStatusBarGroups` (near `AddMember`, ~line 159); add `TryGetHeaderedGroup` and `LeaveGroup` to `PluginStatusBars` (near `CloseStatusBar`, ~line 211).

**Interfaces:**
- Consumes: `PluginStatusBarGroups.FindGroupOf(uint)`, `PluginStatusBars.GetDef(int)`, `UIManager.AnchorManager.DetachControl(AnchorableGump)`, `PluginStatusBars.ReflowGroup(int)` (private, same class), `PluginStatusBarGroups.PruneEmpty()`.
- Produces:
  - `public static void PluginStatusBarGroups.RemoveMember(int groupId, BaseHealthBarGump bar)`
  - `public static bool PluginStatusBars.TryGetHeaderedGroup(BaseHealthBarGump bar, out int groupId)`
  - `public static void PluginStatusBars.LeaveGroup(int groupId, BaseHealthBarGump bar)`

**Testing note:** These touch `UIManager`/gumps, which the unit-test harness cannot construct (no existing test builds a `BaseHealthBarGump`). Like `ReflowGroup`/`PlaceInGrid`/`AddToGroup`, they carry no unit test; they are exercised by the OnMove wiring (Task 3) and verified by running the client (Task 4). This task's gate is: solution builds and the full existing suite stays green.

- [ ] **Step 1: Add `RemoveMember` to `PluginStatusBarGroups`**

In `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs`, inside `PluginStatusBarGroups`, immediately after the `AddMember` method:

```csharp
/// <summary>
/// Drops a live (non-disposed) bar from a group's tracked membership so a
/// subsequent <see cref="PluginStatusBars"/> reflow will not replace it.
/// Used when a bar is ejected without being disposed. No-op if untracked.
/// </summary>
public static void RemoveMember(int groupId, BaseHealthBarGump bar)
{
    if (bar == null)
    {
        return;
    }

    if (_members.TryGetValue(groupId, out List<BaseHealthBarGump> list))
    {
        list.Remove(bar);
    }
}
```

- [ ] **Step 2: Add `TryGetHeaderedGroup` and `LeaveGroup` to `PluginStatusBars`**

In the `PluginStatusBars` static class, immediately after `CloseStatusBar`:

```csharp
/// <summary>
/// True when the bar belongs to a tracked group that has an anchor widget
/// (a <see cref="PluginAnchorGroupDef"/> exists for its id). "Headered" is
/// the internal predicate name for "has an anchor widget", chosen to avoid
/// overloading the word <em>anchor</em>. <paramref name="groupId"/> is the
/// group's id on success, else 0.
/// </summary>
public static bool TryGetHeaderedGroup(BaseHealthBarGump bar, out int groupId)
{
    groupId = 0;

    if (bar == null)
    {
        return false;
    }

    int gid = PluginStatusBarGroups.FindGroupOf(bar.LocalSerial);

    if (gid == 0 || GetDef(gid) == null)
    {
        return false;
    }

    groupId = gid;
    return true;
}

/// <summary>
/// Ejects one bar from its anchor group without disposing it: detaches it
/// from the anchor matrix, drops its membership, then reflows the remaining
/// members by priority so the grid collapses densely. The ejected bar's
/// <see cref="PluginStatusPriorities"/> entry is preserved (unlike
/// <see cref="CloseStatusBar"/>, which clears it) so a later re-join
/// restores its ordering. The explicit DetachControl clears the ejected
/// bar's own stale anchor mapping, which ReflowGroup (rebuilding only the
/// survivors) would otherwise leave behind.
/// </summary>
public static void LeaveGroup(int groupId, BaseHealthBarGump bar)
{
    if (groupId == 0 || bar == null)
    {
        return;
    }

    UIManager.AnchorManager.DetachControl(bar);
    PluginStatusBarGroups.RemoveMember(groupId, bar);
    ReflowGroup(groupId);
    PluginStatusBarGroups.PruneEmpty();
}
```

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds (no CS errors).

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS — all existing tests plus Task 1's 6 cases, no regressions.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs
git commit -m "feat(anchor): eject-from-group wiring (RemoveMember, LeaveGroup)"
```

---

### Task 3: Wire `AnchorableGump.OnMove`

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/AnchorableGump.cs:40-55` (the `OnMove` method)

**Interfaces:**
- Consumes: `PluginStatusBars.TryGetHeaderedGroup`, `PluginStatusBars.ResolveGroupedDrag`, `PluginStatusBars.GroupedDragAction`, `PluginStatusBars.LeaveGroup` (Tasks 1–2); existing `_prevX/_prevY`, `Keyboard.Alt`, `ProfileManager.CurrentProfile.HoldAltToMoveGumps`.
- Produces: nothing new (behavioral change only).

**Testing note:** Depends on `Keyboard`/drag state; not unit-tested. Gate: builds + existing suite green (Step 3), plus Task 4's client verification.

- [ ] **Step 1: Replace the `OnMove` body**

`src/ClassicUO.Client/Game/UI/Gumps/AnchorableGump.cs` already has `using ClassicUO.Game.Managers;` (line 4) and `using ClassicUO.Input;` (line 5), so no new usings. Replace:

```csharp
        protected override void OnMove(int x, int y)
        {
            if (Keyboard.Alt && !ProfileManager.CurrentProfile.HoldAltToMoveGumps)
            {
                UIManager.AnchorManager.DetachControl(this);
            }
            else
            {
                UIManager.AnchorManager[this]?.UpdateLocation(this, X - _prevX, Y - _prevY);
            }

            _prevX = X;
            _prevY = Y;

            base.OnMove(x, y);
        }
```

with:

```csharp
        protected override void OnMove(int x, int y)
        {
            // Status bars in a group with an anchor widget cannot be dragged
            // individually: the anchor moves the cluster, and only Alt-drag
            // ejects a single bar (then the group reflows by priority).
            if (this is BaseHealthBarGump bar &&
                PluginStatusBars.TryGetHeaderedGroup(bar, out int gid))
            {
                PluginStatusBars.GroupedDragAction action = PluginStatusBars.ResolveGroupedDrag(
                    inAnchoredGroup: true,
                    altHeld: Keyboard.Alt,
                    holdAltToMoveGumps: ProfileManager.CurrentProfile.HoldAltToMoveGumps);

                if (action == PluginStatusBars.GroupedDragAction.SnapBack)
                {
                    // Locked to the anchor group: pin the bar in place. _prevX/_prevY
                    // are left untouched so every drag frame snaps back to the same spot.
                    X = _prevX;
                    Y = _prevY;
                    base.OnMove(x, y);
                    return;
                }

                if (action == PluginStatusBars.GroupedDragAction.Eject)
                {
                    // Detaches from the matrix + drops membership + reflows the rest.
                    // Falls through to the free-move path below so the ejected bar
                    // follows the cursor (DetachControl there is now a no-op).
                    PluginStatusBars.LeaveGroup(gid, bar);
                }
            }

            if (Keyboard.Alt && !ProfileManager.CurrentProfile.HoldAltToMoveGumps)
            {
                UIManager.AnchorManager.DetachControl(this);
            }
            else
            {
                UIManager.AnchorManager[this]?.UpdateLocation(this, X - _prevX, Y - _prevY);
            }

            _prevX = X;
            _prevY = Y;

            base.OnMove(x, y);
        }
```

- [ ] **Step 2: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS — no regressions.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/AnchorableGump.cs
git commit -m "feat(anchor): lock grouped status bars, Alt-drag to eject"
```

---

### Task 4: Verify in the running client

**Files:** none (manual/behavioral verification).

**Interfaces:**
- Consumes: the full feature (Tasks 1–3).
- Produces: nothing.

- [ ] **Step 1: Invoke the `verify` skill and drive the client**

Use the `verify` skill (or `run` skill to launch). Log into a world with a defined plugin anchor group holding at least three status bars (distinct priorities). Confirm each behaviour:

- [ ] **Step 2: Drag a grouped bar WITHOUT Alt** — the bar does not move (snaps back); the group does not move.
- [ ] **Step 3: Drag the anchor widget (`PluginAnchorGroupGump`)** — the whole cluster moves together (unchanged); when the anchor is `Locked` (shift-click), it does not move.
- [ ] **Step 4: `Alt`+drag a grouped bar** — that bar leaves the group and follows the cursor; the remaining bars collapse to the anchor origin, re-sorted by priority (highest priority nearest the origin).
- [ ] **Step 5: `Alt`+drag the last remaining bar** — the group empties and is pruned (no leftover ghost anchor group; the anchor widget tooltip shows `0 / cap`).
- [ ] **Step 6: Confirm classic groups are unaffected** — drag a classic anchored gump group (e.g. spell icons or two plain health bars dragged together, not in a plugin anchor group); the whole classic group still moves by dragging any member, and `Alt`+drag still detaches as before.

- [ ] **Step 7: Commit any doc/notes** (if verification surfaces a fix, loop back to the relevant task; otherwise no commit).
