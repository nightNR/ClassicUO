# Pure-client anchors: drag-select routing + drag-into-anchor — plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or executing-plans. Checkbox steps track progress.

**Goal:** Let the pure client route drag-selected health bars into user-defined anchor groups by (held modifier set × mobile allegiance category), and auto-join a free bar dropped onto an anchor group's bars.

**Architecture:** Extend `PluginAnchorGroupDef` with modifier (Ctrl/Shift/Alt) and category (Allied/Hostile/Neutral) checkboxes. A pure, unit-tested router maps `(modifiers, allegiance) → groupId` against the profile's group defs. `DoDragSelect` captures the held modifiers, classifies each selected mobile's allegiance, and routes each bar via the existing `PluginStatusBars.OpenStatusBar(..., groupId)` (or the default unanchored path). `AnchorableGump.Attache()` gains a hook that registers a dropped free bar into the drop-target's tracked group. `OpenStatusBar` is generalized to honor the profile bar style via `HealthBarFactory`. The Options `AnchorGroupRow` gains the six checkboxes + Apply-time conflict validation.

**Tech Stack:** C#, FNA, xUnit. Depends on `feat/statusbar-options-tab` (PR #8): `AnchorGroupRow`, `BuildStatusBars`, `PluginAnchorGroupDef`, `PluginStatusBars`/`PluginStatusBarGroups`, `PluginStatusPriorities`, `ReflowGroup`.

## Global Constraints

- Branch stacks on `feat/statusbar-options-tab`. Do NOT reimplement anchor groups, reflow, priority, or the Options tab — extend them.
- New source files carry `// SPDX-License-Identifier: BSD-2-Clause`.
- New fields on `PluginAnchorGroupDef` are appended; keep JSON source-gen round-trip (the type is already registered on `ProfileJsonContext`).
- Public `[Theory]` methods cannot take an `internal` enum param (CS0051) — use `int` params cast inside the test body (established pattern).
- Existing behavior must stay intact: with no group bindings configured, drag-select behaves exactly as today (default set = former `DragSelectModifierKey`).
- Build: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`. Tests: `dotnet test tests/ClassicUO.UnitTests`.
- UI (drag-select, drop-join, Options row) has no unit tests (repo pattern) — verify by build + a documented manual checklist.
- Line numbers below are indicative — locate by symbol.

---

### Task 1: Data model — modifier + category fields on `PluginAnchorGroupDef`

**Files:**
- Modify: `src/ClassicUO.Client/Configuration/PluginAnchorGroupDef.cs`
- Test: `tests/ClassicUO.UnitTests/PluginAnchorGroupDefTests.cs` (extend existing round-trip test)

**Interfaces:**
- Produces on `PluginAnchorGroupDef`: `bool DragCtrl`, `bool DragShift`, `bool DragAlt`, `bool DragAllied`, `bool DragHostile`, `bool DragNeutral` (all default `false`).

- [ ] **Step 1: Extend the round-trip test** — add the six new fields to the existing `SerializationRoundTrip_PreservesAllFields` test (set each to a non-default `true`, assert preserved). Add them to the object initializer and the asserts.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginAnchorGroupDefTests"`
Expected: FAIL (compile — fields don't exist).

- [ ] **Step 3: Add the fields**

In `PluginAnchorGroupDef.cs`, append to the class:
```csharp
        public bool DragCtrl { get; set; }
        public bool DragShift { get; set; }
        public bool DragAlt { get; set; }
        public bool DragAllied { get; set; }
        public bool DragHostile { get; set; }
        public bool DragNeutral { get; set; }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginAnchorGroupDefTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Configuration/PluginAnchorGroupDef.cs tests/ClassicUO.UnitTests/PluginAnchorGroupDefTests.cs
git commit -m "feat(client-anchor): add drag modifier + category fields to PluginAnchorGroupDef"
```

---

### Task 2: Pure routing — `DragAnchorRouting`

**Files:**
- Create: `src/ClassicUO.Client/Game/Managers/DragAnchorRouting.cs`
- Test: `tests/ClassicUO.UnitTests/DragAnchorRoutingTests.cs`

**Interfaces:**
- Produces:
  - `internal enum Allegiance { Neutral = 0, Allied = 1, Hostile = 2 }`
  - `[Flags] internal enum DragModifier { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }`
  - `internal static class DragAnchorRouting` with:
    - `DragModifier ModifiersOf(PluginAnchorGroupDef def)`
    - `bool HasCategory(PluginAnchorGroupDef def, Allegiance a)`
    - `bool HasBinding(PluginAnchorGroupDef def)` (≥1 modifier AND ≥1 category)
    - `int ResolveDragAnchor(DragModifier held, Allegiance cat, IReadOnlyList<PluginAnchorGroupDef> defs)`
    - `List<int> ConflictingGroupIds(IReadOnlyList<PluginAnchorGroupDef> defs)` — ids of the LATER of any two bound defs sharing modifier set AND ≥1 category (for Apply validation).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ClassicUO.UnitTests/DragAnchorRoutingTests.cs
// SPDX-License-Identifier: BSD-2-Clause
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class DragAnchorRoutingTests
    {
        private static PluginAnchorGroupDef Def(int id, bool c, bool s, bool a, bool allied, bool hostile, bool neutral)
            => new PluginAnchorGroupDef { Id = id, DragCtrl = c, DragShift = s, DragAlt = a, DragAllied = allied, DragHostile = hostile, DragNeutral = neutral };

        [Fact]
        public void NoBinding_IsIgnored()
        {
            // modifiers set but no category -> not a binding
            var defs = new List<PluginAnchorGroupDef> { Def(1, true, false, false, false, false, false) };
            Assert.Equal(0, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Hostile, defs));
        }

        [Fact]
        public void ExactModifierMatch_Required()
        {
            var defs = new List<PluginAnchorGroupDef> { Def(1, true, false, false, false, true, false) }; // Ctrl+Hostile
            Assert.Equal(1, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Hostile, defs));
            // Ctrl+Shift held != Ctrl binding
            Assert.Equal(0, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl | DragModifier.Shift, Allegiance.Hostile, defs));
        }

        [Fact]
        public void OneModifier_SplitsByCategory_ToDifferentAnchors()
        {
            var defs = new List<PluginAnchorGroupDef>
            {
                Def(1, true, false, false, false, true, false),  // Ctrl -> Hostile -> group 1
                Def(2, true, false, false, true, false, true),   // Ctrl -> Allied|Neutral -> group 2
            };
            Assert.Equal(1, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Hostile, defs));
            Assert.Equal(2, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Allied, defs));
            Assert.Equal(2, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Neutral, defs));
        }

        [Fact]
        public void NoMatch_ReturnsZero()
        {
            var defs = new List<PluginAnchorGroupDef> { Def(1, true, false, false, false, true, false) };
            Assert.Equal(0, DragAnchorRouting.ResolveDragAnchor(DragModifier.Shift, Allegiance.Hostile, defs)); // wrong modifier
            Assert.Equal(0, DragAnchorRouting.ResolveDragAnchor(DragModifier.Ctrl, Allegiance.Allied, defs));   // wrong category
        }

        [Fact]
        public void Conflict_SameModifiers_OverlappingCategory()
        {
            var defs = new List<PluginAnchorGroupDef>
            {
                Def(1, true, false, false, true, true, false),   // Ctrl -> Allied|Hostile
                Def(2, true, false, false, false, true, false),  // Ctrl -> Hostile  (overlaps on Hostile)
                Def(3, true, false, false, false, false, true),  // Ctrl -> Neutral  (disjoint, ok)
            };
            var conflicts = DragAnchorRouting.ConflictingGroupIds(defs);
            Assert.Contains(2, conflicts);     // later of the overlapping pair
            Assert.DoesNotContain(1, conflicts);
            Assert.DoesNotContain(3, conflicts);
        }
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~DragAnchorRoutingTests"`
Expected: FAIL (types don't exist).

- [ ] **Step 3: Implement**

```csharp
// src/ClassicUO.Client/Game/Managers/DragAnchorRouting.cs
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;

namespace ClassicUO.Game.Managers
{
    internal enum Allegiance
    {
        Neutral = 0,
        Allied = 1,
        Hostile = 2
    }

    [Flags]
    internal enum DragModifier
    {
        None = 0,
        Ctrl = 1,
        Shift = 2,
        Alt = 4
    }

    /// <summary>
    /// Pure routing of a drag-selected mobile to an anchor group id, keyed by the
    /// held modifier set and the mobile's allegiance category. No UI/game state.
    /// </summary>
    internal static class DragAnchorRouting
    {
        public static DragModifier ModifiersOf(PluginAnchorGroupDef def)
        {
            DragModifier m = DragModifier.None;
            if (def.DragCtrl) m |= DragModifier.Ctrl;
            if (def.DragShift) m |= DragModifier.Shift;
            if (def.DragAlt) m |= DragModifier.Alt;
            return m;
        }

        public static bool HasCategory(PluginAnchorGroupDef def, Allegiance a)
        {
            switch (a)
            {
                case Allegiance.Allied: return def.DragAllied;
                case Allegiance.Hostile: return def.DragHostile;
                default: return def.DragNeutral;
            }
        }

        public static bool HasBinding(PluginAnchorGroupDef def)
        {
            return ModifiersOf(def) != DragModifier.None && (def.DragAllied || def.DragHostile || def.DragNeutral);
        }

        public static int ResolveDragAnchor(DragModifier held, Allegiance cat, IReadOnlyList<PluginAnchorGroupDef> defs)
        {
            if (defs == null)
            {
                return 0;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                PluginAnchorGroupDef d = defs[i];
                if (d != null && d.Id != 0 && HasBinding(d) && ModifiersOf(d) == held && HasCategory(d, cat))
                {
                    return d.Id;
                }
            }

            return 0;
        }

        public static List<int> ConflictingGroupIds(IReadOnlyList<PluginAnchorGroupDef> defs)
        {
            var conflicts = new List<int>();
            if (defs == null)
            {
                return conflicts;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                PluginAnchorGroupDef a = defs[i];
                if (a == null || !HasBinding(a))
                {
                    continue;
                }

                for (int j = 0; j < i; j++)
                {
                    PluginAnchorGroupDef b = defs[j];
                    if (b == null || !HasBinding(b) || ModifiersOf(b) != ModifiersOf(a))
                    {
                        continue;
                    }

                    bool overlap = (a.DragAllied && b.DragAllied) || (a.DragHostile && b.DragHostile) || (a.DragNeutral && b.DragNeutral);
                    if (overlap)
                    {
                        conflicts.Add(a.Id); // the later def loses
                        break;
                    }
                }
            }

            return conflicts;
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~DragAnchorRoutingTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/DragAnchorRouting.cs tests/ClassicUO.UnitTests/DragAnchorRoutingTests.cs
git commit -m "feat(client-anchor): pure drag-anchor router (modifier x allegiance -> group id) + conflict detection"
```

---

### Task 3: Allegiance classification from a mobile

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/DragAnchorRouting.cs`
- Test: `tests/ClassicUO.UnitTests/DragAnchorRoutingTests.cs`

**Interfaces:**
- Produces: `Allegiance DragAnchorRouting.ClassifyNotoriety(NotorietyFlag noto)` — pure mapping, plus a thin `Allegiance ClassifyMobile(Mobile m)` wrapper (not unit-tested).

- [ ] **Step 1: Determine the existing hostile rule** — read `GameSceneInputHandler.DoDragSelect`'s `DragSelectHostileOnly` filter and how it decides "hostile" (likely `mobile.NotorietyFlag` values or an `IsHostile`-style check). Mirror EXACTLY that definition of hostile so the two features agree. Note the actual `NotorietyFlag` enum values used.

- [ ] **Step 2: Write the failing test** — using the real `NotorietyFlag` enum (in `ClassicUO.Game.Data` or similar), assert the mapping. Because `Allegiance`/`NotorietyFlag` accessibility may block a `[Theory]` param, pass the notoriety as its underlying `byte`/`int` and cast inside:

```csharp
        [Theory]
        // Fill with the REAL NotorietyFlag numeric values discovered in Step 1:
        // hostile examples (Enemy/Murderer/Criminal/Gray) -> Hostile(2);
        // allied examples (Ally/Innocent/Invulnerable as friendly) -> Allied(1);
        // unknown/other -> Neutral(0)
        [InlineData(/*Enemy*/    5, 2)]
        [InlineData(/*Murderer*/ 6, 2)]
        [InlineData(/*Ally*/     2, 1)]
        [InlineData(/*Innocent*/ 1, 1)]
        [InlineData(/*Invalid*/  0, 0)]
        public void ClassifyNotoriety_MapsToAllegiance(int noto, int expected)
        {
            Assert.Equal(expected, (int)DragAnchorRouting.ClassifyNotoriety((ClassicUO.Game.Data.NotorietyFlag)noto));
        }
```

(Adjust the InlineData numeric values + expected to the REAL enum discovered in Step 1; do not guess — read the enum.)

- [ ] **Step 3: Run to verify fail**, then implement `ClassifyNotoriety` mirroring the hostile rule from Step 1 (hostile set → `Hostile`; friendly set → `Allied`; else `Neutral`), plus:

```csharp
        public static Allegiance ClassifyMobile(ClassicUO.Game.GameObjects.Mobile m)
            => m == null ? Allegiance.Neutral : ClassifyNotoriety(m.NotorietyFlag);
```

(Use the real namespaces for `Mobile`/`NotorietyFlag`.)

- [ ] **Step 4: Run to verify pass.**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ClassifyNotoriety"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/DragAnchorRouting.cs tests/ClassicUO.UnitTests/DragAnchorRoutingTests.cs
git commit -m "feat(client-anchor): classify mobile notoriety into allegiance category"
```

---

### Task 4: Generalize `OpenStatusBar` to honor the profile bar style

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs`
- Test: none (existing `PluginStatusBar*` tests must still pass)

- [ ] **Step 1:** In `OpenStatusBar`, replace the forced `new HealthBarGumpCustom(world, serial)` bar creation with the factory that honors `CustomBarsToggled`:
```csharp
            BaseHealthBarGump bar = HealthBarFactory.Create(world, world.Get(serial));
            bar.X = seedX;
            bar.Y = seedY;
```
(Confirm `HealthBarFactory.Create(World, Entity)` signature and that `world.Get(serial)` yields the entity — mirror how `DoDragSelect` calls the factory. Keep the existing seed-position logic; only change which type is constructed.)

- [ ] **Step 2:** Confirm nothing else in the group/priority/overlay path depends on the bar being specifically `HealthBarGumpCustom` (overlay tint is a separate concern noted in the spec as an optional follow-up; do NOT add classic-bar overlay here).

- [ ] **Step 3: Build + tests**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusBar"`
Expected: build ok; tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs
git commit -m "feat(client-anchor): OpenStatusBar honors CustomBarsToggled via HealthBarFactory"
```

---

### Task 5: Drag-select routing in `DoDragSelect`

**Files:**
- Modify: `src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs`
- Test: none (integration/UI — manual)

- [ ] **Step 1:** At drag start (where `_isSelectionActive` is set, ~:355-362), capture the held modifier set into a field `DragModifier _dragSelectModifiers` from `Keyboard.Ctrl/Shift/Alt`.

- [ ] **Step 2:** Replace the single `DragSelectModifierActive()` gate so drag-select activates when `EnableDragSelect` AND (`held == defaultSet` OR any group `HasBinding` with `ModifiersOf == held`). Map the former `DragSelectModifierKey` (0=none,1=Ctrl,2=Shift) to the `defaultSet` `DragModifier`. Keep Ctrl+Shift's old "disable" behavior ONLY for the default set; a group explicitly bound to Ctrl+Shift should still activate.

- [ ] **Step 3:** In `DoDragSelect`, at the per-mobile creation site (~:204-261), for each selected mobile:
```csharp
            int gid = DragAnchorRouting.ResolveDragAnchor(_dragSelectModifiers, DragAnchorRouting.ClassifyMobile(mobile), ProfileManager.CurrentProfile.PluginAnchorGroups);
            if (gid != 0)
            {
                PluginStatusBars.OpenStatusBar(mobile.Serial, finalX, finalY, 1, gid);
                continue; // group grid placement owns position; skip cascade + TryAttacheToExist
            }
            // else: existing default path (HealthBarFactory.Create + cascade + optional TryAttacheToExist)
```
Preserve the existing duplicate-skip (`UIManager.GetGump<BaseHealthBarGump>(mobile) != null`) BEFORE routing; keep `DragSelectHumanoidsOnly`/`DragSelectHostileOnly` pre-filters.

- [ ] **Step 4: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs
git commit -m "feat(client-anchor): route drag-selected bars into anchor groups by modifier x allegiance"
```

---

### Task 6: Drag-into-anchor auto-join

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/AnchorableGump.cs`
- Test: none (integration — manual)

- [ ] **Step 1:** In `Attache()`, after the existing `DropControl(this, _anchorCandidate)` succeeds and before clearing `_anchorCandidate`, add (guard to health bars only):
```csharp
            if (this is BaseHealthBarGump && _anchorCandidate is BaseHealthBarGump host)
            {
                int gid = PluginStatusBarGroups.FindGroupOf(host.LocalSerial);
                if (gid != 0 && PluginStatusBarGroups.FindGroupOf(LocalSerial) == 0)
                {
                    int cap = PluginStatusBars.ResolveMaxRows(gid) * PluginStatusBars.ResolveMaxColumns(gid);
                    if (PluginStatusBarGroups.GetLiveMembers(gid).Count < cap)
                    {
                        PluginStatusBarGroups.AddMember(gid, (BaseHealthBarGump)this);
                        PluginStatusBars.ReflowGroup(gid);
                    }
                }
            }
```
(Confirm `ResolveMaxRows/Columns(int)` and `ReflowGroup(int)` accessibility — make `ReflowGroup` `internal`/`public static` if it is currently `private`; the plan's Task requires it callable here. If exposing it is undesirable, add a thin `public static void PluginStatusBars.JoinGroup(int groupId, BaseHealthBarGump bar)` wrapper that does the capacity check + AddMember + ReflowGroup, and call that instead.)

- [ ] **Step 2:** Verify dragging a bar OUT still works (existing detach), and that a re-dropped bar re-joins. `GetLiveMembers` prunes disposed; no extra code.

- [ ] **Step 3: Build + tests**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusBar"`
Expected: build ok; tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/AnchorableGump.cs src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs
git commit -m "feat(client-anchor): dropping a free bar onto an anchor group joins the tracked group"
```

---

### Task 7: Options — extend `AnchorGroupRow` + conflict validation

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Controls/AnchorGroupRow.cs`
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` (Apply validation + default-set relabel)
- Test: none (UI — manual)

- [ ] **Step 1:** In `AnchorGroupRow`, add three modifier `Checkbox`es (Ctrl/Shift/Alt) and three category `Checkbox`es (Allied/Hostile/Neutral), initialized from `def.DragCtrl/Shift/Alt/DragAllied/DragHostile/DragNeutral`. Widen the row (increase `Width`) or add a second sub-line and bump `Height` (keep the DataBox `count * Height` layout in `BuildStatusBars` consistent — if `Height` changes, update the `Y = count * <H>` stride at both the seed loop and the Add handler in `BuildStatusBars`). In `Commit()`, write the six checkbox states back to the def.

- [ ] **Step 2:** Add a short header/legend row in `BuildStatusBars` labeling the new columns (Mods: C S A | Target: Al Ho Ne) so the checkboxes are understandable.

- [ ] **Step 3:** In `OptionsGump` Apply (the anchor-groups commit loop), after committing all rows, call `DragAnchorRouting.ConflictingGroupIds(_currentProfile.PluginAnchorGroups)`; for each conflicting id, clear that def's modifier bools (disable its binding) and surface a warning (mirror however the gump warns elsewhere, or a simple log + reset like the existing dup-id handling).

- [ ] **Step 4:** Relabel the former `DragSelectModifierKey` control (in `BuildGeneral`) to indicate it is the **default (unanchored) drag-select** trigger.

- [ ] **Step 5: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/AnchorGroupRow.cs src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(client-anchor): Options row modifier+category checkboxes and conflict validation"
```

---

### Task 8: Build + test sweep + manual checklist

- [ ] **Step 1:** `dotnet build ClassicUO.sln -c Debug` — succeeds.
- [ ] **Step 2:** `dotnet test tests/ClassicUO.UnitTests` — all pass (Data model, routing, allegiance, no regression).
- [ ] **Step 3:** Manual checklist for the user:
  - Options → Status Bars: each anchor row shows Ctrl/Shift/Alt + Allied/Hostile/Neutral checkboxes; values persist across Apply/reopen.
  - Configure e.g. group A = Ctrl + Hostile, group B = Ctrl + Allied+Neutral. Ctrl+drag over a mixed crowd → enemies land in A, allies/neutrals in B, both in their grids.
  - Plain drag (default set) still opens bars as before.
  - Conflicting bindings (same modifiers + overlapping category) get a warning on Apply and the later one is disabled.
  - Drag a free health bar onto an anchor group's bars → it joins the group (snaps into the grid, participates in reflow); dropping when full does not overfill.
  - Custom-bar OFF: client-opened anchored bars use the classic bar style.

## Self-Review
- Data model (modifier + category fields) → Task 1.
- Pure routing + conflict → Task 2; allegiance classification → Task 3.
- Bar-style respect → Task 4.
- Drag-select routing → Task 5; drag-into-anchor → Task 6.
- Options UI + validation + default relabel → Task 7.
- One drag → multiple anchors, no-match → default, exact modifier match, category split — all in Task 2 tests + Task 5 integration.
- Overlay-on-classic explicitly deferred (spec §6) — not implemented here.

## Risks
- `ReflowGroup` may be `private` — Task 6 requires it callable; expose it or add a `JoinGroup` wrapper (documented in Task 6 Step 1).
- Notoriety→allegiance must match the existing hostile filter exactly (Task 3 Step 1) or the two drag-select notions diverge.
- `AnchorGroupRow` height change ripples to the DataBox `count * Height` stride in `BuildStatusBars` — update both the seed loop and Add handler (Task 7 Step 1).
- Modifier capture must read the state at drag START, not drag END (keys may change mid-drag) — Task 5 Step 1.
