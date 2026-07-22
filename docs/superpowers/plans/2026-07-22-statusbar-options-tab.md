# "Status Bars" Options Tab + anchor-editor redraw fix — plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or executing-plans. Checkbox steps track progress.

**Goal:** Move all status-bar / health-bar configuration out of the General page into a new dedicated "Status Bars" tab (placed right under "Status Bar Colors"), and in doing so fix the anchor-groups editor bug where adding a group row does not re-layout the settings window (rows overlap / clip / become non-interactive).

**Architecture:** The bug's root cause is that the anchor-groups `DataBox` sits three containers deep inside a `SettingsSection` (whose height is frozen at build time), and the "Add group" handler never calls `ReArrangeChildren()`. The working editors (Aliases `BuildAliases`, `BuildStatusbarColors`) instead put their `DataBox` as a **direct child of the page ScrollArea** and call `ReArrangeChildren()` on every add. Giving status bars their own tab lets the anchor editor follow that proven pattern, which fixes the bug structurally.

**Tech Stack:** C#, FNA, `OptionsGump.cs` (~5500 lines). No unit tests for UI (repo pattern) — verification is build + manual run.

## Global Constraints

- Single primary file: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs`. `Controls/AnchorGroupRow.cs` stays as-is (its delete already calls `((DataBox)Parent)?.ReArrangeChildren()`).
- Do NOT move field DECLARATIONS (top of class, e.g. `_customBars` :39, `_pluginStatusBarMaxRows`/`_anchorGroupsBox` :130-131). Only move the BUILD code (control construction + `Add(..., page)`) and keep the Apply/save handlers working — they reference fields by name and are location-independent, so they must keep compiling and running unchanged.
- Page numbers are bare int literals (no enum). Existing: General=1, …, Aliases=13, Status Bar Colors=14. New "Status Bars" = **15** (next free).
- Every Build method is invoked once in the constructor sequence (`~:448-461`), then `ChangePage(1)`. The new `BuildStatusBars()` MUST be added to that call list, or its fields never get constructed and Apply throws NRE.
- License header unchanged; match surrounding OptionsGump style.
- Build gate: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug` must succeed. Regression gate: `dotnet test tests/ClassicUO.UnitTests` unaffected (UI not unit-tested, but must still pass).
- Line numbers below are from the pre-change file and may drift — locate by symbol/label text, not line number.

## Inventory to move into the new "Status Bars" tab (from `BuildGeneral`)

All currently in `BuildGeneral` (page 1). Move construction into `BuildStatusBars` (page 15):

Health-bar core (in `SettingsSection section3`):
- `_customBars` Checkbox — `ResGumps.UseCustomHPBars`
- `_customBarsBBG` Checkbox (AddRight) — `ResGumps.UseBlackBackgr`
- `_statValuesOnBars` Checkbox — `ResGumps.ShowStatValuesOnBars`
- `_saveHealthbars` Checkbox — `ResGumps.SaveHPBarsOnLogout`
- `_healtbarType` Combobox — `ResGumps.CloseHPGumpWhen` (None/OutOfRange/Dead)

Plugin fallback grid (in `section3`):
- `_pluginStatusBarMaxRows` InputField — "Plugin status bar max rows (fallback)"
- `_pluginStatusBarMaxColumns` InputField — "Plugin status bar max columns (fallback)"

Anchor groups editor (in `section3` — the buggy block):
- "Anchor groups" label, "Add group" `NiceButton`, `_anchorGroupsBox` `DataBox` + seed loop

Anchor-behavior toggles (elsewhere in `BuildGeneral`, NOT in section3 — locate each by its resource label):
- `_holdDownKeyAlt` — `ResGumps.HoldDownKeyAltToCloseAnchored` (or similar "hold alt to close anchored")
- `_closeAllAnchoredGumpsWithRClick` — the "close all anchored gumps with right click" checkbox
- `_dragSelectAsAnchor` — `ResGumps.DragSelectAnchoredHB`

Leave everything else in General. "Status Bar Colors" (page 14) stays its own separate tab, untouched.

---

### Task 1: Add the empty "Status Bars" tab scaffold

**Files:** `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs`

- [ ] **Step 1: Add the nav button** — find the "Status Bar Colors" nav `NiceButton` (`ButtonParameter = 14`, label "Status Bar Colors", ~:252-263). Immediately AFTER it (before the next nav button, Macros), insert:

```csharp
            Add(new NiceButton(10, 10 + 30 * i++, 140, 25, ButtonAction.SwitchPage, "Status Bars") { ButtonParameter = 15 });
```

(Use the same `i++` vertical-increment idiom the surrounding nav buttons use, so following buttons shift down by one slot automatically.)

- [ ] **Step 2: Add the Build method** — add a new method modeled on `BuildStatusbarColors` (page-root recipe: a `ScrollArea` added via `Add(rightArea, PAGE)`):

```csharp
        private void BuildStatusBars()
        {
            const int PAGE = 15;

            ScrollArea rightArea = new ScrollArea(190, 20, WIDTH - 210, 420, true);

            // (controls added in Tasks 2-4)

            Add(rightArea, PAGE);
        }
```

- [ ] **Step 3: Register it in the constructor** — in the Build call list (`~:448-461`, where `BuildStatusbarColors()` is called), add `BuildStatusBars();` after `BuildStatusbarColors();`.

- [ ] **Step 4: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: succeeds; a new empty "Status Bars" tab appears (verified at runtime later).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(options): add empty Status Bars tab under Status Bar Colors"
```

---

### Task 2: Move health-bar core + plugin fallback controls into the new tab

**Files:** `OptionsGump.cs`

Move the construction of `_customBars`, `_customBarsBBG`, `_statValuesOnBars`, `_saveHealthbars`, `_healtbarType`, `_pluginStatusBarMaxRows`, `_pluginStatusBarMaxColumns` out of `BuildGeneral`'s `section3` and into `BuildStatusBars`.

- [ ] **Step 1:** In `BuildStatusBars`, add a `SettingsSection` for these fixed-height controls (fixed height is safe inside a section — only growable lists must avoid sections). Follow the `SettingsSection` construction + `.Add(...)` / `.AddRight(...)` idiom used in `BuildGeneral`. Reproduce each control's construction verbatim (same ctor args, same `SetText`, same labels/resource strings, same `AddRight` chaining for `_customBarsBBG`). Add the section to `rightArea`.

- [ ] **Step 2:** DELETE those same construction blocks from `BuildGeneral`'s `section3`. Preserve the surrounding section3 controls that are NOT being moved. After deletion, `section4.Y = section3.Bounds.Bottom + 40` still works (section3 is just shorter).

- [ ] **Step 3:** Verify the Apply path still references these fields (`_pluginStatusBarMaxRows`/`Columns` parse ~:4322-4335; `_healtbarType`/`_customBars`/`_saveHealthbars`/`_statValuesOnBars` in the save + custom-bar-swap logic ~:4837-4879). Those handlers are unchanged — just confirm they still compile (fields still exist, now assigned in `BuildStatusBars`).

- [ ] **Step 4: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(options): move health-bar and plugin fallback settings to Status Bars tab"
```

---

### Task 3: Move the anchor-groups editor into the tab AND fix the redraw bug

**Files:** `OptionsGump.cs`

This is the bug fix. Rebuild the anchor editor as a DIRECT child of the page `ScrollArea` (NOT inside a `SettingsSection`), mirroring `BuildAliases`.

- [ ] **Step 1:** In `BuildStatusBars`, below the Task-2 section, add (adapt exact ctor args to the real ones already used in the old block):

```csharp
            rightArea.Add(AddLabel(null, "Anchor groups", 0, 0)); // position appropriately within rightArea

            NiceButton addAnchorGroupButton = new NiceButton(0, 0, 130, 25, ButtonAction.Activate, "Add group") { ButtonParameter = 999 };
            addAnchorGroupButton.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtonType.Left)
                {
                    return;
                }

                PluginAnchorGroupDef newDef = new PluginAnchorGroupDef();
                _currentProfile.PluginAnchorGroups.Add(newDef);

                _anchorGroupsBox.Add(new AnchorGroupRow(this, newDef) { Y = _anchorGroupsBox.Children.Count * 26 });
                _anchorGroupsBox.ReArrangeChildren(); // <-- THE FIX: restack + flag size, was missing
            };
            rightArea.Add(addAnchorGroupButton);

            _anchorGroupsBox = new DataBox(0, 0, 0, 0) { WantUpdateSize = true };

            foreach (PluginAnchorGroupDef def in _currentProfile.PluginAnchorGroups)
            {
                _anchorGroupsBox.Add(new AnchorGroupRow(this, def) { Y = _anchorGroupsBox.Children.Count * 26 });
            }

            _anchorGroupsBox.ReArrangeChildren();
            rightArea.Add(_anchorGroupsBox); // DIRECT child of ScrollArea (not a SettingsSection) — grows correctly
```

Give the label / Add button / DataBox sensible Y offsets within `rightArea` so they sit below the Task-2 section (mirror how `BuildAliases` positions its `addButton` at `Y=35` and `databox` at `Y=70`, or place relative to the section's bottom).

- [ ] **Step 2:** DELETE the old anchor-groups block (label, `addAnchorGroupButton`, `_anchorGroupsBox` seed loop, `section3.Add(_anchorGroupsBox)`) from `BuildGeneral`.

- [ ] **Step 3:** Confirm the Apply loop that walks `_anchorGroupsBox.Children` (~:4337-4374, commits each `AnchorGroupRow` + rebuilds via `PluginAnchorGroupManager.Rebuild(World)`) is unchanged and still compiles.

- [ ] **Step 4: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "fix(options): move anchor-groups editor to Status Bars tab; ReArrangeChildren on add so rows relayout"
```

---

### Task 4: Move the anchor-behavior toggles into the tab

**Files:** `OptionsGump.cs`

Move `_holdDownKeyAlt`, `_closeAllAnchoredGumpsWithRClick`, `_dragSelectAsAnchor` construction from `BuildGeneral` into `BuildStatusBars` (into the Task-2 `SettingsSection` or a small dedicated section — they are fixed-height checkboxes).

- [ ] **Step 1:** Locate each by its resource label (`HoldDownKeyAltToCloseAnchored`, the close-all-anchored-with-rclick checkbox, `DragSelectAnchoredHB`). Reproduce each construction verbatim in `BuildStatusBars`; DELETE from `BuildGeneral`. Watch for `AddRight` chaining or ordering dependencies with neighbors that stay in General — only move the three named controls, keep their non-anchor neighbors in place.

- [ ] **Step 2:** Confirm Apply/defaults references still compile (`_dragSelectAsAnchor` ~:4823, `_closeAllAnchoredGumpsWithRClick` default ~:4059, `_saveHealthbars`/`_dragSelectAsAnchor` defaults ~:4108-4122). Unchanged — just relocated construction.

- [ ] **Step 3: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(options): move anchored-gump behavior toggles to Status Bars tab"
```

---

### Task 5: Build + test sweep + manual verification checklist

- [ ] **Step 1:** `dotnet build ClassicUO.sln -c Debug` — succeeds.
- [ ] **Step 2:** `dotnet test tests/ClassicUO.UnitTests` — all pass (no regression).
- [ ] **Step 3:** Manual checklist (document for the user to run):
  - Options → "Status Bars" tab appears directly under "Status Bar Colors".
  - All moved controls render, are interactive, and their values load from the profile.
  - "Add group" adds a row that appears correctly stacked; the window/scroll re-lays-out; multiple adds do not overlap or clip; rows remain interactable; delete restacks. Scroll works when rows exceed the visible area.
  - Apply persists everything (plugin fallback rows/cols, anchor groups, HP-bar toggles, anchor-behavior toggles) and rebuilds the on-screen anchor widgets.
  - General page no longer shows the moved controls and its remaining layout is intact.

## Self-Review
- New tab plumbing (nav button + Build method + constructor call) → Task 1.
- All inventory items relocated → Tasks 2 (HP core + plugin fallback), 3 (anchor editor), 4 (behavior toggles).
- Bug fix (direct ScrollArea child + `ReArrangeChildren` on add) → Task 3.
- Apply/persistence untouched, fields still constructed → verified each task.
- Scope per user: anchor-behavior toggles included; Status Bar Colors stays separate. ✔

## Risks
- Cutting blocks from the large `BuildGeneral` can disturb neighbor layout (Y chaining, `AddRight`). Mitigate: move only the named controls; after each task, build + eyeball the General page in the manual pass.
- If any moved control participated in `section.AddRight(previous)` chaining with a control that STAYS in General, moving it breaks the row — check each control's immediate neighbors before cutting.
