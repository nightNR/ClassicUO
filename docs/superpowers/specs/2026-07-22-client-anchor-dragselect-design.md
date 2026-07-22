# Pure-client anchors: drag-select routing + drag-into-anchor — design

Date: 2026-07-22
Depends on: the anchor-group system (`PluginAnchorGroupDef`, `PluginStatusBarGroups`,
`PluginAnchorGroupGump`, `PluginStatusBars.OpenStatusBar`) already in `main`, and
the "Status Bars" Options tab + `AnchorGroupRow` editor (PR #8, branch
`feat/statusbar-options-tab`). This feature stacks on that editor.

## Summary

Make user-defined anchor groups usable from the **pure client**, not just plugins:

1. **Drag-select routing** — extend the existing "drag a rectangle to open health
   bars" feature so the held modifier keys + each mobile's allegiance route the
   opened bars into specific anchor groups. Each anchor group can be bound to a
   modifier combination (Ctrl/Shift/Alt checkboxes) and a target filter
   (Any/Allied/Hostile). A single drag can populate several anchors at once.
2. **Drag-into-anchor** — dragging a free (unanchored) health bar onto the bars of
   an anchor group automatically adds it to that group (tracked membership: grid,
   capacity, reflow, priority), not just an ad-hoc AnchorManager attach.

Both reuse the existing group machinery (`PluginStatusBars.OpenStatusBar`,
`PluginStatusBarGroups`), which is `public static` in the client assembly and
already reachable without a live plugin.

## Current state (from code map)

- Drag-select: `Game/Scenes/GameSceneInputHandler.cs` — `DragSelectModifierActive()`
  (~:99, a single global gate: 0=always, 1=Ctrl, 2=Shift; Ctrl+Shift disables),
  `DoDragSelect()` (~:126-267) builds the rectangle, filters mobiles
  (`DragSelectHumanoidsOnly`, `DragSelectHostileOnly`), skips serials that already
  have a bar, creates each bar via `HealthBarFactory.Create` (~:209), cascades
  X/Y, and — only if `DragSelectAsAnchor` — calls `hbgc.TryAttacheToExist()`
  (~:252, ad-hoc AnchorManager attach, **no group id**).
- Settings (`Configuration/Profile.cs` ~:210-222): `EnableDragSelect`,
  `DragSelectModifierKey`, `DragSelectHumanoidsOnly`, `DragSelectHostileOnly`,
  `DragSelectStartX/Y`, `DragSelectAsAnchor`.
- Anchor groups: `PluginStatusBars.OpenStatusBar(uint serial, int x, int y, byte
  moveIfExists, int groupId)` (`Game/Managers/PluginStatusBars.cs` ~:252) —
  routes into a tracked group (`AddToGroup` → `ReflowGroup`). **Forces
  `HealthBarGumpCustom`** regardless of `CustomBarsToggled`. `PluginStatusBarGroups`
  (`FindGroupOf`, `GetLiveMembers`, `AddMember`, `Track`, capacity via
  `ResolveMaxRows/Columns`) is `public static`.
- Manual anchor drop: `Game/UI/Gumps/AnchorableGump.cs` — `Attache()` (~:91) →
  `AnchorManager.DropControl(this, host)` builds an **untracked** `AnchorGroup`
  with no group id; never notifies `PluginStatusBarGroups`.
- Group defs live in `Profile.PluginAnchorGroups`; edited via `AnchorGroupRow` in
  `BuildStatusBars` (PR #8); widgets spawned by `PluginAnchorGroupManager.Rebuild`.

## Design

### 1. Data model — extend `PluginAnchorGroupDef`

Add (append; keep JSON round-trip):
- `bool DragCtrl`, `bool DragShift`, `bool DragAlt` — modifier checkboxes. A group
  has a drag-select binding iff at least one is true; all-false = no binding.
- `DragTargetFilter DragTarget` — new enum `{ Any = 0, Allied = 1, Hostile = 2 }`.

A binding is the tuple `(modifiers = {Ctrl?,Shift?,Alt?}, target)`.

### 2. Matching — the pure routing decision

New pure, unit-tested helper (in `PluginStatusBars` or a small new static class):
```
// Given the modifier set held at drag time and a mobile's allegiance, pick the
// target group id, or 0 for "no anchor → default unanchored".
int ResolveDragAnchor(ModifierSet held, Allegiance mob, IReadOnlyList<PluginAnchorGroupDef> defs)
```
Rules:
- Consider only defs with a binding (≥1 modifier true) whose modifier set **exactly
  equals** `held`.
- Among those, pick the one whose `DragTarget` matches `mob`: `Any` matches any;
  `Allied` matches allied mobiles; `Hostile` matches hostile mobiles.
- Preference when both a specific filter and `Any` match: the **more specific**
  (Allied/Hostile) wins over `Any`.
- If none match → return 0 (mobile falls to default unanchored behavior).

`Allegiance` (Allied/Hostile/Neutral) is derived from the mobile's notoriety,
reusing the SAME classification the existing `DragSelectHostileOnly` filter uses
(`GameSceneInputHandler` already distinguishes hostile). "Allied" = the allied/
party/innocent side of that classification; anything neither is Neutral (matches
only `Any`).

### 3. Drag-select integration (`DoDragSelect`)

- At drag **start**, capture the held modifier set `M` (Ctrl/Shift/Alt).
- Replace the single `DragSelectModifierActive()` gate: drag-select **activates**
  if `EnableDragSelect` AND (`M` equals the configured default set OR `M` equals
  some group's binding). The old `DragSelectModifierKey` becomes the **default
  set** selector (backward compatible: its current values map to the default
  binding for unanchored drag-select).
- At drag **end**, per selected mobile:
  - `gid = ResolveDragAnchor(M, allegianceOf(mobile), CurrentProfile.PluginAnchorGroups)`.
  - If `gid != 0`: `PluginStatusBars.OpenStatusBar(mobile.Serial, x, y, moveIfExists:1, gid)`
    — the group's grid placement wins (the drag cascade X/Y is ignored for anchored bars).
  - If `gid == 0`: existing default path (create via `HealthBarFactory`, cascade
    position, optional `TryAttacheToExist` per `DragSelectAsAnchor`).
- One drag thus fills multiple anchors (e.g. Ctrl+drag → Hostile enemies into
  anchor A and Allied into anchor B simultaneously), plus default for the rest.
- Existing global `DragSelectHumanoidsOnly`/`DragSelectHostileOnly` still pre-filter
  which mobiles are considered at all.

### 4. Validation (Options Apply)

A **conflict** exists only when two groups share the **same modifier set** AND
their target filters **overlap** (same filter, or either is `Any`). Non-overlapping
(e.g. Ctrl+Hostile vs Ctrl+Allied) is allowed. On conflict at Apply: surface a
warning and keep the first (drop the later binding, i.e. clear its modifiers).

### 5. Drag-into-anchor (feature 2)

Hook in `AnchorableGump.Attache()` after the existing `DropControl`:
- `gid = PluginStatusBarGroups.FindGroupOf(host.LocalSerial)` where `host` is the
  drop target (`_anchorCandidate`). Only bars are `BaseHealthBarGump`.
- If `gid != 0` and this bar is not already a member:
  - Respect capacity: if `GetLiveMembers(gid).Count >= ResolveMaxRows*Columns`,
    **do not join** (leave the ad-hoc attach as-is, or reject the drop) — no
    overfilling the grid.
  - Else `PluginStatusBarGroups.AddMember(gid, this)` + `PluginStatusBars.ReflowGroup(gid)`
    so the dropped bar becomes a tracked member and the grid re-lays-out densely.
- Dragging a member OUT (existing detach path) already removes it via
  `GetLiveMembers` pruning on next read; no extra work needed beyond confirming
  `FindGroupOf`/reflow stay consistent.

### 6. Respect the profile's bar style (not forced-custom)

Generalize `PluginStatusBars.OpenStatusBar`'s bar creation to use
`HealthBarFactory.Create(world, entity)` (honoring `CustomBarsToggled`) instead of
always `new HealthBarGumpCustom`. Plugin callers keep working; client-opened
anchored bars now match the user's chosen bar style.

**Follow-up (optional, out of core scope):** the priority-overlay tint
(`PluginStatusOverlays`/`SetOverlay`) currently renders only on the custom bar.
Making the classic `HealthBarGump` honor the overlay hue (classic bars already
support multiple colors) is a separate, optional enhancement — this feature does
not depend on it.

### 7. Options UI — extend `AnchorGroupRow`

Add per row: three checkboxes `Ctrl` / `Shift` / `Alt` and a target `Combobox`
(`Any`/`Allied`/`Hostile`), wired to `def.DragCtrl/Shift/Alt/DragTarget`. Row grows
wider or wraps to a second sub-line per group; commit on Apply alongside the
existing id/label/cols/rows/fill fields. Add a small header/legend so the columns
are understandable. Update the default-set control (former `DragSelectModifierKey`)
label to reflect its new "default (unanchored) drag-select" meaning.

## Testing

- **Pure**: `ResolveDragAnchor` — exact modifier match; Allied/Hostile/Any
  selection and Any-vs-specific preference; no-match → 0; empty-binding groups
  ignored. Conflict-detection helper (same modifiers + overlapping filter).
- Allegiance classification maps notoriety → Allied/Hostile/Neutral consistently
  with the existing hostile filter.
- `PluginAnchorGroupDef` JSON round-trip includes the new fields.
- Drag-select end-to-end, drag-into-anchor drop, and the Options row are UI —
  manual verification (repo pattern).

## Out of scope

- Overlay/priority tint on classic bars (noted as optional follow-up in §6).
- Target filters beyond Any/Allied/Hostile (Humanoid/Monster) — can be added later
  to the same enum if wanted.
- Changing how the classic vs custom bar looks; only which one is created.
- Non-drag ways of assigning client bars to groups (macros, etc.).
