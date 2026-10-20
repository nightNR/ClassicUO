# Auto-avoid obstacles for mouse walking

## Goal
When walking with the mouse (right button held), the character should step
diagonally around a small obstacle one tile *before* hitting it, instead of
stopping against it. Optional, off by default.

## Scope
- Small obstacles only (1-2 blocked tiles: tree, rock, house corner).
- Mouse walking on foot only. Not keyboard, not boats, not `WalkTo` autowalk.
- Walls with no nearby gap: behaviour unchanged.

## Setting
`Profile.AutoAvoidObstacles` (bool, default `false`), checkbox in
`OptionsGump` next to `AllowDiagonalCornerCutting`.

## Logic
New pure static class `ObstacleAvoidance` (`Game/ObstacleAvoidance.cs`),
decoupled from `Pathfinder` through a `WalkProbe` delegate so it is unit
testable:

`Direction Steer(Direction want, int x, int y, sbyte z, int cursorDx, int cursorDy, ref int lastSide, WalkProbe probe)`

A probe is "clean" only if the step succeeds, keeps the requested direction
and lands on the geometrically expected tile (no corner-slide substitution).

1. `p1 = probe(want)`. If clean and `probe(want)` from `p1` is also clean
   (lookahead 2) -> return `want`.
2. Otherwise try candidates `want+1`, `want-1` (45 deg). (A 90 deg fallback was
   dropped: with the resume check below it can never succeed where the 45 deg
   candidate fails.) Candidates are ordered by closeness to the
   cursor (dot product of the candidate's screen vector with the cursor
   vector); on a tie the side used last (`lastSide`) wins.
3. A candidate `c` is accepted when `probe(c)` is clean and from its landing
   tile `probe(want)` is clean (so we can resume the original heading).
   Return `c` and remember its side in `lastSide`.
4. No candidate accepted -> return `want` (current behaviour).

Screen vector of a UO direction `d`: `(ox - oy, ox + oy)` using the
`_offsetX/_offsetY` tables.

## Wiring
`Pathfinder.SteerAround(...)` adapts `CanWalk` to `WalkProbe` and calls
`ObstacleAvoidance.Steer`. `MoveCharacterByMouseInput` calls it on the
non-boat branch when the setting is on, using the queue-end position the
same way `PlayerMobile.Walk` does, and passes the result to `Player.Walk`.
`lastSide` resets when the mouse is released.

## Cost
At most ~6 `CanWalk` calls per step, no allocations, only when enabled.
No protocol changes: only the direction passed to `Walk` differs.

## Tests (xUnit)
Free path, obstacle at 1 and 2 tiles, side by cursor, tie uses `lastSide`,
wall (unchanged), diagonal heading.
