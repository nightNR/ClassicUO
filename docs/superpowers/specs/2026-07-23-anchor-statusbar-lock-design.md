# Anchor status-bar lock + Alt-eject — design

Date: 2026-07-23
Status: approved

## Problem

Permanent plugin anchor groups render status bars in a priority-ordered grid
tied to an **anchor** widget (`PluginAnchorGroupGump`). Today the whole cluster
can be dragged by grabbing *any* member bar (`AnchorableGump.OnMove` calls
`AnchorGroup.UpdateLocation`), and `Alt`+drag detaches a bar but does **not**
reflow the group afterwards.

Desired: status bars that belong to a group **with an anchor** cannot be moved
individually. The cluster moves only via its anchor widget. The single escape
hatch is `Alt`+drag on a bar, which **ejects** that bar from the group and
reflows the remaining members by priority so the grid collapses densely.

## Terminology

- **anchor** — the draggable `PluginAnchorGroupGump` widget (label + pic) that
  represents a defined group and moves the whole cluster.
- **anchor group** — the status bars assigned to that anchor, tracked in
  `PluginStatusBarGroups` and defined by a `PluginAnchorGroupDef`.
- A group **has an anchor** iff `GetDef(groupId) != null` (only defined groups
  get a `PluginAnchorGroupGump` via `PluginAnchorGroupManager.Rebuild`).

## Scope

Applies **only** to status bars belonging to a group that has an anchor.

Out of scope, behaviour unchanged:
- Classic `AnchorManager` groups (spells, macros, buffs) — no anchor widget
  exists for them; blocking their drag would strand them. `Alt`-detach for those
  keeps working as before.
- Headerless / undefined status-bar groups (bars merely dragged together, no
  `PluginAnchorGroupDef`) — keep current drag-moves-the-group behaviour.
- Anchor drag-move of the cluster via `PluginAnchorGroupGump`.
- `def.Locked` (shift-click on the anchor) — still gates only the anchor's own
  group-move; it does not gate individual `Alt`-eject.

## Behaviour

Hook: `AnchorableGump.OnMove`. When the moved gump is a `BaseHealthBarGump`
whose group has an anchor:

| Input | Action |
| --- | --- |
| Drag **without** `Alt` | Do nothing — snap `X/Y` back to `_prevX/_prevY`. The bar stays put; the cluster moves only via its anchor widget. |
| Drag **with** `Alt` (and `!HoldAltToMoveGumps`) | Eject the bar: detach from the anchor matrix, drop group membership, reflow the remaining members by priority. The ejected bar becomes free and follows the cursor. |

All other gumps fall through to the existing `OnMove` logic unchanged.

## Components

### `PluginStatusBars` (new methods)

```csharp
// True only when the bar belongs to a tracked group that has an anchor
// (a PluginAnchorGroupDef exists). groupId is 0 otherwise.
public static bool TryGetHeaderedGroup(BaseHealthBarGump bar, out int groupId);

// Eject one bar from its anchor group without disposing it:
// detach from the anchor matrix, remove from membership, reflow the
// remainder by priority, prune the group if now empty. The ejected bar's
// PluginStatusPriorities entry is PRESERVED (unlike CloseStatusBar, which
// clears it) so a later re-join restores its ordering.
public static void LeaveGroup(int groupId, BaseHealthBarGump bar);
```

`LeaveGroup` steps: `UIManager.AnchorManager.DetachControl(bar)` →
`PluginStatusBarGroups.RemoveMember(groupId, bar)` → `ReflowGroup(groupId)` →
`PluginStatusBarGroups.PruneEmpty()`.

`DetachControl(bar)` is explicit because `ReflowGroup` only rebuilds the anchor
group for the *remaining* members; the ejected bar's stale
`UIManager.AnchorManager[bar]` mapping must be cleared separately.

Naming note: `TryGetHeaderedGroup` uses "headered" as the internal predicate
name for "has an anchor widget" to avoid overloading the word *anchor*, which
already names the widget, the group, and the matrix. Its doc comment states the
equivalence.

### `PluginStatusBarGroups` (new method)

```csharp
// Drop a live (non-disposed) bar from a group's tracked membership so a
// subsequent ReflowGroup will not replace it.
public static void RemoveMember(int groupId, BaseHealthBarGump bar);
```

Needed because `GetLiveMembers` only prunes disposed/null bars; an ejected bar
is still alive and would otherwise be re-placed on the next reflow.

### `AnchorableGump.OnMove` (modified)

```csharp
protected override void OnMove(int x, int y)
{
    if (this is BaseHealthBarGump bar &&
        PluginStatusBars.TryGetHeaderedGroup(bar, out int gid))
    {
        if (Keyboard.Alt && !ProfileManager.CurrentProfile.HoldAltToMoveGumps)
        {
            PluginStatusBars.LeaveGroup(gid, bar);
            // fall through: bar is now free, follow the cursor
        }
        else
        {
            // locked to the anchor group: individual drag is a no-op
            X = _prevX;
            Y = _prevY;
            base.OnMove(x, y);
            return;
        }
    }

    // existing behaviour
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

The snap-back pattern (`X = _prevX; Y = _prevY`) mirrors the locked-group path
already used in `PluginAnchorGroupGump.Update`. After a `LeaveGroup` eject the
bar falls through to the `Alt` branch (`DetachControl` is now a harmless no-op)
and updates `_prevX/_prevY`, so it tracks the cursor freely.

## Error / edge handling

- Bar not in any group, or in a group without an anchor → `TryGetHeaderedGroup`
  returns false, existing behaviour unchanged.
- `LeaveGroup` on a group of one → after removal the group is empty;
  `ReflowGroup` no-ops and `PruneEmpty` drops it.
- `HoldAltToMoveGumps = true` (Alt required for *any* gump move): the eject
  condition `!HoldAltToMoveGumps` is false, so a grouped bar can be neither
  moved nor ejected — it always snaps back. Consistent with the existing
  Alt-detach semantics, which is likewise disabled in that mode. Documented as a
  known limitation, not fixed here.

## Testing

Pure/logic-level unit tests (no `Keyboard`/UI):

- `PluginStatusBarGroups.RemoveMember` drops the given bar from membership and
  leaves the others untouched.
- `PluginStatusBars.LeaveGroup`:
  - the ejected bar is no longer a member and is detached from the anchor
    matrix;
  - remaining members collapse to the group origin, reflowed by priority
    (assert positions / order);
  - the ejected bar's `PluginStatusPriorities` entry is preserved;
  - a one-member group becomes empty and is pruned.
- `PluginStatusBars.TryGetHeaderedGroup` is true only when a `PluginAnchorGroupDef`
  exists for the bar's group; false for undefined groups and non-members.

The `AnchorableGump.OnMove` wiring is a thin dispatch over the tested methods and
is not unit-tested (depends on `Keyboard`/drag state).
