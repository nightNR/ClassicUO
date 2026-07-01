# Plugin v2 Status Bars — Priority overlay, open-at-position, modern party bars

**Date:** 2026-06-30
**Branch context:** `feat/cuo_plugin_v2`
**Status:** Approved design, ready for implementation planning
**Sibling spec:** `2026-06-30-plugin-walkto-design.md` (sub-project D)

## Context

This folds sub-projects **A**, **C**, and **B** of the Plugin v2 effort into one
spec, because all three touch the same files (`HealthBarGump.cs`, the anchor
system) and the same `CustomBarsToggled` modern-bar path:

| # | Feature |
|---|---------|
| **A** | Priority-list highlight: recolor an NPC's status bar based on plugin rules |
| **C** | Plugin opens a status bar at a specific position, optionally grouped |
| **B** | When modern UI (`CustomBarsToggled`) is on, party member bars also use the modern custom bar |

Guiding constraint (user): **minimize client changes; keep policy in the
plugin.** The priority-list logic (which NPC by graphic+hue gets which highlight)
lives entirely in the plugin — it already reconstructs world state (serial,
graphic, hue, notoriety, hits, position) from the packet stream via
`IPacketPipeline.Incoming`. The client exposes thin, generic primitives keyed by
serial.

### What already exists

- **Health bars** (`src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs`):
  abstract `BaseHealthBarGump : AnchorableGump` with two subclasses —
  `HealthBarGump` (classic art-based) and `HealthBarGumpCustom` (modern
  line-drawn). Modern is selected when `ProfileManager.CurrentProfile.CustomBarsToggled`
  is true. The custom bar is hue-aware: notoriety background hue, plus state
  borders (red on low HP, poison green, war mode). It has a separate `_outline`
  ring (`HPB_OUTLINESIZE`) distinct from the state `_border[]` lines. It already
  has an `inparty` layout branch (line ~378).
- **Lookup by serial:** `UIManager.GetGump<BaseHealthBarGump>(serial)` returns an
  existing bar for a mobile; `UIManager.Add(gump)` registers a new one.
  Positioning is by `gump.X` / `gump.Y`.
- **Anchor/grouping** (`src/ClassicUO.Client/Game/Managers/AnchorManager.cs`):
  `AnchorManager` holds `AnchorGroup` objects (a 2D matrix of `AnchorableGump`),
  keyed to members via a `reverseMap`. Grouping today is drag-driven
  (`DropControl(dragged, host)`); there is **no named-group concept**. Helpers:
  `AnchorGroup.AddControlToMatrix(x, y, control)`,
  `GetCandidateDropLocation(dragged, host)`.
- **Party members are already `BaseHealthBarGump`** keyed by serial
  (`PartyManager` line ~59). The bar's `inparty` branch renders party layout
  (heal buttons, etc.).

## Goals

- **A** A plugin can highlight a mobile's status bar (priority hue) by serial,
  and clear it. Rules/colors are the plugin's; the client only renders.
- **C** A plugin can open a status bar for a serial at a screen position, choose
  whether to move an existing one, and optionally anchor bars into a shared
  group.
- **B** When `CustomBarsToggled` is on, party member bars render with the modern
  custom bar (the same style the player uses), not the classic art bar.
- Client footprint stays small and generic; no plugin policy in the client.

## Non-goals

- Badge / icon strip on bars (phase 2 — see "Deferred").
- Any `IWorld` state-query surface (plugin already has world state).
- New named-group UI for end users (grouping is a plugin-facing primitive that
  reuses the existing anchor machinery).
- Recoloring the classic `HealthBarGump` overlay (v1 overlay is custom-bar only).

## Architecture

Two new action primitives plus one new plugin service, threaded through the same
4-layer boundary as `CastSpell` (see the WalkTo spec for the boundary recipe):

```
PluginApi (contract)              BootstrapHost (net10)              cuo (client)
──────────────────────            ─────────────────────             ─────────────────────────────
IStatusBars.OpenStatusBar()  ─▶   StatusBarsImpl.OpenStatusBar() ─▶ HostBindings.OpenStatusBarFn ─▶ client open helper
IStatusBars.CloseStatusBar() ─▶   StatusBarsImpl.CloseStatusBar()─▶ HostBindings.CloseStatusBarFn─▶ client close helper
IStatusBars.SetOverlay()     ─▶   StatusBarsImpl.SetOverlay()    ─▶ HostBindings.SetOverlayFn    ─▶ overlay registry
```

All three are plugin → cuo actions only (no cuo → plugin event needed). cuo fills
the function pointers at `Initialize` (mirroring `CastSpellFn`); the host calls
them from a new `StatusBarsImpl`. **B has no boundary surface at all** — it is a
pure client behavior change gated on `CustomBarsToggled`.

## Public contract (PluginApi)

New interface `src/ClassicUO.PluginApi/IStatusBars.cs`:

```csharp
public interface IStatusBars
{
    /// <summary>
    /// Opens a status (health) bar for <paramref name="serial"/> at screen
    /// (<paramref name="x"/>, <paramref name="y"/>). If a bar already exists for
    /// that serial: when <paramref name="moveIfExists"/> is true it is moved to
    /// (x, y); otherwise the call is a no-op. When <paramref name="groupId"/> is
    /// non-zero the bar is anchored into the shared group with that id.
    /// Auto-marshals to the game thread.
    /// </summary>
    void OpenStatusBar(uint serial, int x, int y, bool moveIfExists = true, int groupId = 0);

    /// <summary>Closes the status bar for <paramref name="serial"/> if present.
    /// Auto-marshals to the game thread.</summary>
    void CloseStatusBar(uint serial);

    /// <summary>
    /// Sets a priority-highlight hue on the status bar for
    /// <paramref name="serial"/>. <paramref name="hue"/> = 0 clears the
    /// highlight. The highlight persists until cleared or the mobile is removed;
    /// re-apply as needed. Auto-marshals to the game thread.
    /// Badge support is deferred (see spec).
    /// </summary>
    void SetOverlay(uint serial, ushort hue);
}
```

`IPluginContext` gains: `IStatusBars StatusBars { get; }`.

All methods touch `UIManager` (not thread-safe) and auto-marshal to the game
thread (like `CastSpell`), so they are safe to call from any thread.

## Feature A — Priority overlay rendering

- The client keeps a small registry of priority hues keyed by serial:
  `Dictionary<uint, ushort>` (e.g. a static `PluginStatusOverlays` helper or a
  field on `World`). `SetOverlay(serial, hue)` writes it; `hue == 0` removes the
  entry.
- `HealthBarGumpCustom.Update` consults the registry for its `LocalSerial` and,
  when a non-zero hue is present, tints its **`_outline` ring** to that hue. The
  state `_border[]` lines are left untouched, so low-HP red / poison green / war
  mode coloring still reads correctly. The priority hue is an *additional* outer
  ring, not a replacement for survival info.
- Classic `HealthBarGump`: **no-op in v1** (documented). The classic bar has no
  equivalent line ring; adding one is out of scope for the first iteration.
- Lifetime: the registry entry is cleared by `SetOverlay(serial, 0)` and when the
  bar/mobile is disposed. The plugin re-applies highlights on its own cadence
  (e.g. on the packets that told it a mobile appeared/changed).

Why the plugin holds the policy: the plugin already knows each mobile's graphic
and hue from packets, so "graphic X + hue Y ⇒ priority hue H" is a plugin-side
lookup. It calls `SetOverlay(serial, H)`. The client never sees a rule.

## Feature C — Open at position + grouping

- **Open / move / ignore.** `OpenStatusBar`:
  - If no bar exists for `serial`: create `new HealthBarGumpCustom(world, serial)`,
    set `X`/`Y`, `UIManager.Add(...)`.
  - If a bar exists and `moveIfExists` is true: set its `X`/`Y` to (x, y).
  - If a bar exists and `moveIfExists` is false: no-op.
  - The opened bar is `HealthBarGumpCustom` (modern). Floating (not auto-snapped)
    unless `groupId` is set.
- **Grouping (`groupId`).** The client maintains
  `Dictionary<int, AnchorGroup>` mapping plugin group ids to anchor groups:
  - First bar opened with a given `groupId` seeds a new `AnchorGroup` (this bar
    becomes the host at matrix (0, 0)), positioned at (x, y).
  - Subsequent bars with the same `groupId` are attached into that group's matrix
    at the next free cell via `AnchorGroup.AddControlToMatrix`, with position
    computed from the host + matrix offset (reuse the math in
    `GetCandidateDropLocation`). The existing group-drag code then moves all
    members together.
  - `groupId == 0` means "no group" (floating).
  - When a grouped bar closes, it is detached via the existing
    `AnchorManager.DetachControl`. Empty groups are removed from the registry.
- This reuses the existing anchor machinery; the only new client state is the
  `groupId → AnchorGroup` registry and a small "add bar to group" helper.

## Feature B — Modern party bars

Requirement: when `ProfileManager.CurrentProfile.CustomBarsToggled` is true,
party member status bars render with `HealthBarGumpCustom` (the bar the player
already uses in modern mode), exercising its existing `inparty` layout branch —
instead of falling back to the classic `HealthBarGump`.

- **Audit** every health-bar creation site and ensure the custom-vs-classic
  choice honors `CustomBarsToggled` for party members. Known creation sites:
  `Game/Scenes/GameSceneInputHandler.cs` (~213/217, ~1091/1103),
  `Game/UI/Gumps/NameOverheadGump.cs` (~245/257),
  `Game/UI/Gumps/OptionsGump.cs` (~4295/4306),
  `Game/Managers/MacroManager.cs` (~707/711). The party-member auto-spawn path
  (the one that currently produces the "original" party bar) is located during
  implementation and routed through the same toggle.
- **Verify** `HealthBarGumpCustom`'s `inparty` branch (line ~378) renders the
  party layout correctly (party heal buttons, name color, death handling that
  keeps the party bar alive — mirrors the classic bar's `inparty` behavior).
- No plugin/boundary surface: B is purely a client default change.

## Testing

- **A** (`ClassicUO.UnitTests`): `SetOverlay(serial, hue)` populates the registry;
  `HealthBarGumpCustom.Update` tints `_outline` to that hue and leaves
  `_border[]` state colors unchanged; `SetOverlay(serial, 0)` clears it; classic
  bar is a no-op.
- **C** (`ClassicUO.UnitTests`): open creates a custom bar at (x, y);
  `moveIfExists` true moves an existing bar, false leaves it; a second
  `OpenStatusBar` with the same `groupId` attaches into the group matrix at the
  expected offset; close detaches and prunes an empty group.
- **B** (`ClassicUO.UnitTests`): with `CustomBarsToggled` true, the party-member
  spawn path produces `HealthBarGumpCustom`; with it false, `HealthBarGump`.
- **Host** (`ClassicUO.BootstrapHost.Tests`): each `IStatusBars` method invokes
  the correct binding (mirror existing `TestRaise*` plumbing tests).
- **Manual** (`HelloPlugin` via `tools/stage-bootstraphost.ps1`): highlight a
  mobile (confirm outline tint), open two bars in one `groupId` (confirm they
  snap together and drag as a unit), toggle modern UI in-party (confirm party
  bars switch to the custom visual).

## Files touched (estimate)

| Layer | File | Change |
|-------|------|--------|
| Contract | `src/ClassicUO.PluginApi/IStatusBars.cs` (new) | `IStatusBars` interface |
| Contract | `src/ClassicUO.PluginApi/IPluginContext.cs` | add `StatusBars` property |
| Host | `src/ClassicUO.BootstrapHost/HostBridge.cs` | add `OpenStatusBarFn`/`CloseStatusBarFn`/`SetOverlayFn` |
| Host | `src/ClassicUO.BootstrapHost/PluginContextImpl.cs` | new `StatusBarsImpl`; expose on context |
| cuo | `src/ClassicUO.Client/PluginHost.cs` | add fn-pointer fields; wire to client helpers |
| cuo | `src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs` | overlay registry consult + `_outline` tint (A); open/move helper (C); custom `inparty` verify (B) |
| cuo | `src/ClassicUO.Client/Game/Managers/AnchorManager.cs` | `groupId → AnchorGroup` registry + add-to-group helper (C) |
| cuo | client party-bar spawn site (located in impl) | honor `CustomBarsToggled` for party (B) |
| cuo | `src/ClassicUO.Client/Network/Plugin.cs` | legacy `HostBindings` struct-layout parity if required |
| tests | `tests/ClassicUO.UnitTests`, `tests/ClassicUO.BootstrapHost.Tests` | as above |
| sample | `samples/HelloPlugin` | demo overlay + grouped open |

## Deferred (phase 2)

- **Badge strip.** Add `void SetBadges(uint serial, ReadOnlySpan<int> graphics)`
  to `IStatusBars`; the custom bar draws the supplied gump/art ids as a small
  horizontal strip next to the bar. Deferred because multi-icon layout, hit
  area, and per-icon tooltips need their own design pass. The v1 `SetOverlay`
  hue is sufficient on its own and does not change shape when badges are added.
- **Classic-bar overlay.** Extend the priority highlight to the classic
  `HealthBarGump` if users want it without modern bars.

## Open questions / risks

- **`_outline` availability.** The priority tint assumes `HealthBarGumpCustom`
  always builds an `_outline` ring. Implementation must confirm it exists in both
  single-line and multi-line party layouts; if not, add a dedicated 1px priority
  ring rather than reusing a state element.
- **Legacy `HostBindings` layout.** New fn-pointer fields must be appended (not
  reordered) so the legacy net472 `Bootstrap` apphost still loads the shared
  struct. Verify the legacy path after the change.
- **Group lifetime.** Bars can also be closed by the user or the client (mobile
  out of range, death). The `groupId → AnchorGroup` registry must prune entries
  when their host/last member is disposed, not only on plugin `CloseStatusBar`.
