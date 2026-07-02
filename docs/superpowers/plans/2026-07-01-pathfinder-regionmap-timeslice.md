# Pathfinder Region Map + Time-Sliced Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make long-range pathfinding cheap and non-freezing by (A) rejecting cross-island/continent walk requests in O(1) via a precomputed reachability region map, and (B) spreading the A* search itself across game ticks so no single frame stalls.

**Architecture:** Two independent subsystems layered on the existing heap+hashset A* (`AStarPathSearch`).
- **Part A — Region map:** an offline/first-run flood-fill of each facet's *static* passability into connected-component ids, cached to disk. `Pathfinder.WalkTo` short-circuits when start and goal fall in different components (they can never be reached on foot).
- **Part B — Time-sliced search:** refactor the one-shot A* into a resumable search object (`Begin`/`Step(budget)`/`GetPath`). `WalkTo` finishes short searches synchronously (a large first-call budget preserves today's behaviour for in-view walks) and spills long searches into per-tick continuations driven by `ProcessAutoWalk`.

The two parts are independent and independently shippable. Part B is lower-risk and self-contained; Part A is larger (asset reading + on-disk cache + background build). Recommended execution order: **Part B first, then Part A** — but either can go first.

**Tech Stack:** C# `net10.0`, xUnit, `System.Collections.Generic.PriorityQueue`, the existing `ClassicUO.Assets` loader (`MapLoader`, `TileDataLoader`).

## Global Constraints

- Target framework: `net10.0`, `LangVersion=Preview`, `AllowUnsafeBlocks=true` (from `src/Directory.Build.props`) — copy the project BSD license header (`// SPDX-License-Identifier: BSD-2-Clause`) into every new source file.
- New client code lives in `src/ClassicUO.Client` (namespace `ClassicUO.Game`); it is `internal` and visible to `ClassicUO.UnitTests` via the existing `InternalsVisibleTo`.
- Tests are xUnit under `tests/ClassicUO.UnitTests`. Run with `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~<Name>"`.
- Do NOT modify `CanWalk`, `CalculateNewZ`, `CreateItemList`, or `CalculateMinMaxZ` in `Pathfinder.cs` — those encode UO walkability and must stay byte-for-byte identical.
- Region connectivity must be **optimistic (over-connected)**: when unsure whether a tile is passable, treat it as passable. A false "reachable" only wastes an A* run; a false "unreachable" wrongly refuses a valid walk. Never introduce the latter.
- Direction/offset encoding matches `Pathfinder.GetNewXY`: `0=N,1=NE,2=E,3=SE,4=S,5=SW,6=W,7=NW`; odd index = diagonal (step cost 2, cardinal cost 1).
- Existing behaviour that MUST NOT regress: the 8 tests in `AStarPathSearchTests.cs` and the 4 in `WalkProgressTests.cs` stay green after every task.
- **Integration-test data:** a real UO data dir is available at `E:/Games/Ultima Online Classic` (has `client.exe`, `map0.mul`/`map0LegacyMUL.uop`, `staidx0.mul`, `statics0.mul`, `tiledata.mul`). Asset-backed tests resolve the dir from env var `CUO_UO_TEST_DIR`, falling back to that path, and **skip cleanly (return early) when the dir does not exist** so CI without assets stays green. Load headless via `new UOFileManager(version, dir)` + `fm.Load(false, "enu")` — the data loaders need no `GraphicsDevice`. Get `version` with `ClientVersionHelper.TryParseFromFile(Path.Combine(dir,"client.exe"), out var vtext)` then `ClientVersionHelper.IsClientVersionValid(vtext, out var version)` (both in `ClassicUO.Utility`, public); fall back to `ClientVersion.CV_70796` if parsing fails.

---

## Existing code reference (read before starting)

- `src/ClassicUO.Client/Game/AStarPathSearch.cs` — current one-shot static A* (`TryFindPath`, nested `Neighbor`/`Step`/`ExpandNeighbors`). Part B refactors this.
- `src/ClassicUO.Client/Game/Pathfinder.cs` — `WalkTo(x,y,z,distance,run)` (builds `ExpandWalkable`, calls `AStarPathSearch.TryFindPath`), `ProcessAutoWalk()` (walks one `_path` step per tick, called from `GameScene.Update`), `PATHFINDER_MAX_NODES = 300000`.
- `src/ClassicUO.Client/Game/Scenes/GameScene.cs:755` — `_world.Player.Pathfinder.ProcessAutoWalk();` (the once-per-tick call site).
- `src/ClassicUO.Assets/MapLoader.cs` — `MapBlocksSize[facet, 0|1]` (block dims; tile dims = `<<3`), `BlockData[facet][block]` (`IndexMap` with `MapFile`/`MapAddress`/`StaticFile`/`StaticAddress`/`StaticCount`), `struct StaticsBlock { ushort Color; byte X; byte Y; sbyte Z; ushort Hue; }`, `GetIndex(map,x,y)`. `MapBlock` cells give per-tile land graphic + Z (see `Map.GetTileZ` at `src/ClassicUO.Client/Game/Map/Map.cs:107`).
- `src/ClassicUO.Assets/TileDataLoader.cs` — `LandData[graphic].Flags` / `StaticData[graphic].Flags` with `.IsImpassable`, `.IsSurface`, `.IsWet`.

---

# PART B — Time-Sliced Search

### Task B1: Resumable A* search core

Refactor the one-shot search into a resumable object while keeping the existing static `TryFindPath` (and its 8 tests) working via a thin wrapper.

**Files:**
- Create: `src/ClassicUO.Client/Game/AStarSearch.cs`
- Modify: `src/ClassicUO.Client/Game/AStarPathSearch.cs` (turn `TryFindPath` into a wrapper over `AStarSearch`)
- Test: `tests/ClassicUO.UnitTests/Game/Pathfinder/AStarSearchTests.cs`

**Interfaces:**
- Consumes: `AStarPathSearch.Neighbor`, `AStarPathSearch.Step`, `AStarPathSearch.ExpandNeighbors` (unchanged nested types).
- Produces:
  - `internal sealed class AStarSearch`
  - `internal enum AStarSearch.Status { Running, Found, Failed }`
  - `void Begin(int startX, int startY, int startZ, int goalX, int goalY, int distance, AStarPathSearch.ExpandNeighbors expand)`
  - `Status Step(int nodeBudget)` — expands at most `nodeBudget` nodes this call; `Found` when a node within `distance` (Chebyshev) of the goal is popped, `Failed` when the frontier empties, `Running` when the budget is hit first. If the start is already within tolerance, the first `Step` returns `Found` with an empty path.
  - `void GetPath(List<AStarPathSearch.Step> path)` — clears then fills `path` start→goal; valid after `Found`.
  - `int ExpandedNodes { get; }` — total nodes expanded since `Begin`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ClassicUO.UnitTests/Game/Pathfinder/AStarSearchTests.cs
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class AStarSearchTests
    {
        private static readonly int[] OffX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] OffY = { -1, -1, 0, 1, 1, 1, 0, -1 };

        private static AStarPathSearch.ExpandNeighbors OpenField => (x, y, z, into) =>
        {
            for (int dir = 0; dir < 8; dir++)
            {
                int cost = (dir % 2 != 0) ? 2 : 1;
                into.Add(new AStarPathSearch.Neighbor(x + OffX[dir], y + OffY[dir], 0, dir, cost));
            }
        };

        [Fact]
        public void Stepping_in_small_budgets_finds_same_path_as_one_shot()
        {
            var oneShot = new List<AStarPathSearch.Step>();
            Assert.True(AStarPathSearch.TryFindPath(0, 0, 0, 10, 0, 0, 1_000_000, OpenField, oneShot));

            var s = new AStarSearch();
            s.Begin(0, 0, 0, 10, 0, 0, OpenField);
            AStarSearch.Status st;
            do
            {
                st = s.Step(3);
            }
            while (st == AStarSearch.Status.Running);

            Assert.Equal(AStarSearch.Status.Found, st);

            var resumed = new List<AStarPathSearch.Step>();
            s.GetPath(resumed);
            Assert.Equal(oneShot.Count, resumed.Count);
            Assert.Equal(oneShot[^1].X, resumed[^1].X);
            Assert.Equal(oneShot[^1].Y, resumed[^1].Y);
        }

        [Fact]
        public void Step_respects_the_node_budget()
        {
            var s = new AStarSearch();
            s.Begin(0, 0, 0, 500, 0, 0, OpenField);

            AStarSearch.Status st = s.Step(5);

            Assert.Equal(AStarSearch.Status.Running, st);
            Assert.True(s.ExpandedNodes <= 5, $"expanded {s.ExpandedNodes} > budget 5");
        }

        [Fact]
        public void Start_within_tolerance_returns_found_empty_path()
        {
            var s = new AStarSearch();
            s.Begin(3, 3, 0, 3, 3, 0, OpenField);

            Assert.Equal(AStarSearch.Status.Found, s.Step(100));

            var path = new List<AStarPathSearch.Step>();
            s.GetPath(path);
            Assert.Empty(path);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AStarSearchTests"`
Expected: FAIL to compile — `AStarSearch` does not exist.

- [ ] **Step 3: Write `AStarSearch` (move the search internals here)**

```csharp
// src/ClassicUO.Client/Game/AStarSearch.cs
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace ClassicUO.Game
{
    /// <summary>
    /// Resumable A* over an implicit 8-connected grid. Holds the frontier
    /// (binary-heap open set + hashed closed set) between <see cref="Step"/>
    /// calls so a long search can be spread across game ticks with a per-call
    /// node budget. Walkability comes from an injected
    /// <see cref="AStarPathSearch.ExpandNeighbors"/> delegate.
    /// </summary>
    internal sealed class AStarSearch
    {
        internal enum Status { Running, Found, Failed }

        private struct SearchNode
        {
            public int X, Y, Z, Direction, G, Parent;
        }

        private readonly List<SearchNode> _nodes = new List<SearchNode>();
        private readonly PriorityQueue<int, int> _open = new PriorityQueue<int, int>();
        private readonly Dictionary<long, int> _bestG = new Dictionary<long, int>();
        private readonly HashSet<long> _closed = new HashSet<long>();
        private readonly List<AStarPathSearch.Neighbor> _neighbors = new List<AStarPathSearch.Neighbor>();

        private int _goalX, _goalY, _distance;
        private AStarPathSearch.ExpandNeighbors _expand;
        private int _goalIndex = -1;
        private bool _startWithinTolerance;

        public int ExpandedNodes { get; private set; }

        private static long Key(int x, int y, int z)
        {
            return ((long)(uint)x << 32) | ((long)(uint)y << 8) | (byte)(sbyte)z;
        }

        private static int Heuristic(int x, int y, int goalX, int goalY)
        {
            int dx = Math.Abs(goalX - x);
            int dy = Math.Abs(goalY - y);

            return dx > dy ? dx : dy;
        }

        public void Begin(int startX, int startY, int startZ, int goalX, int goalY, int distance, AStarPathSearch.ExpandNeighbors expand)
        {
            _nodes.Clear();
            _open.Clear();
            _bestG.Clear();
            _closed.Clear();
            _neighbors.Clear();
            _goalX = goalX;
            _goalY = goalY;
            _distance = distance;
            _expand = expand;
            _goalIndex = -1;
            ExpandedNodes = 0;
            _startWithinTolerance = Heuristic(startX, startY, goalX, goalY) <= distance;

            if (_startWithinTolerance)
            {
                return;
            }

            _nodes.Add(new SearchNode { X = startX, Y = startY, Z = startZ, Direction = 0, G = 0, Parent = -1 });
            _bestG[Key(startX, startY, startZ)] = 0;
            _open.Enqueue(0, Heuristic(startX, startY, goalX, goalY));
        }

        public Status Step(int nodeBudget)
        {
            if (_startWithinTolerance)
            {
                _goalIndex = -1;
                return Status.Found;
            }

            int expandedThisCall = 0;

            while (_open.TryDequeue(out int idx, out _))
            {
                SearchNode cur = _nodes[idx];
                long curKey = Key(cur.X, cur.Y, cur.Z);

                if (!_closed.Add(curKey))
                {
                    continue;
                }

                if (_bestG.TryGetValue(curKey, out int recordedG) && recordedG < cur.G)
                {
                    continue;
                }

                if (Heuristic(cur.X, cur.Y, _goalX, _goalY) <= _distance)
                {
                    _goalIndex = idx;
                    return Status.Found;
                }

                ExpandedNodes++;

                _neighbors.Clear();
                _expand(cur.X, cur.Y, cur.Z, _neighbors);

                for (int i = 0; i < _neighbors.Count; i++)
                {
                    AStarPathSearch.Neighbor nb = _neighbors[i];
                    long nKey = Key(nb.X, nb.Y, nb.Z);

                    if (_closed.Contains(nKey))
                    {
                        continue;
                    }

                    int ng = cur.G + nb.Cost;

                    if (_bestG.TryGetValue(nKey, out int oldG) && oldG <= ng)
                    {
                        continue;
                    }

                    _bestG[nKey] = ng;
                    _nodes.Add(new SearchNode { X = nb.X, Y = nb.Y, Z = nb.Z, Direction = nb.Direction, G = ng, Parent = idx });
                    _open.Enqueue(_nodes.Count - 1, ng + Heuristic(nb.X, nb.Y, _goalX, _goalY));
                }

                if (++expandedThisCall >= nodeBudget)
                {
                    return Status.Running;
                }
            }

            return Status.Failed;
        }

        public void GetPath(List<AStarPathSearch.Step> path)
        {
            path.Clear();

            if (_goalIndex < 0)
            {
                return; // start already within tolerance: no steps
            }

            int count = 0;

            for (int i = _goalIndex; i >= 0 && _nodes[i].Parent != -1; i = _nodes[i].Parent)
            {
                count++;
            }

            for (int i = 0; i < count; i++)
            {
                path.Add(default);
            }

            int slot = count - 1;

            for (int i = _goalIndex; i >= 0 && _nodes[i].Parent != -1; i = _nodes[i].Parent)
            {
                SearchNode n = _nodes[i];
                path[slot--] = new AStarPathSearch.Step(n.X, n.Y, n.Z, n.Direction);
            }
        }
    }
}
```

- [ ] **Step 4: Rewrite `AStarPathSearch.TryFindPath` as a wrapper**

Replace the body of `TryFindPath` in `src/ClassicUO.Client/Game/AStarPathSearch.cs` (keep the nested `Neighbor`/`Step`/`ExpandNeighbors` types and the method signature exactly). Delete the old private `SearchNode`/`Key`/`Heuristic`/`Reconstruct` members now living in `AStarSearch`.

```csharp
        internal static bool TryFindPath(
            int startX, int startY, int startZ,
            int goalX, int goalY, int distance,
            int maxNodes,
            ExpandNeighbors expand,
            List<Step> path)
        {
            var search = new AStarSearch();
            search.Begin(startX, startY, startZ, goalX, goalY, distance, expand);

            AStarSearch.Status status = search.Step(maxNodes);

            if (status == AStarSearch.Status.Found)
            {
                search.GetPath(path);
                return true;
            }

            path.Clear();
            return false; // Running (budget exhausted) or Failed — matches legacy maxNodes cap
        }
```

- [ ] **Step 5: Run the new and existing search tests**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AStar"`
Expected: PASS — `AStarSearchTests` (3) and `AStarPathSearchTests` (8) all green.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/AStarSearch.cs src/ClassicUO.Client/Game/AStarPathSearch.cs tests/ClassicUO.UnitTests/Game/Pathfinder/AStarSearchTests.cs
git commit -m "feat(pathfinder): resumable AStarSearch (Begin/Step/GetPath) with one-shot wrapper"
```

---

### Task B2: Wire time-sliced search into Pathfinder

`WalkTo` finishes short searches in one call (large first-call budget = no behaviour change for in-view walks) and, for long searches, keeps the `AStarSearch` alive and advances it a bounded number of nodes per tick from `ProcessAutoWalk`. While searching, the player stands still.

**Files:**
- Create: `tests/ClassicUO.UnitTests/Game/Pathfinder/PathBudgetTests.cs`
- Modify: `src/ClassicUO.Client/Game/Pathfinder.cs`

**Interfaces:**
- Consumes: `AStarSearch` (Task B1); existing `Pathfinder.ExpandWalkable`, `_path`, `_pathSize`, `_pointIndex`, `EndAutoWalk`, `RaiseWalkProgress`.
- Produces:
  - `internal const int Pathfinder.FIRST_CALL_NODE_BUDGET` (value `8000`)
  - `internal const int Pathfinder.PER_TICK_NODE_BUDGET` (value `4000`)
  - `internal static int Pathfinder.FrameBudget(bool firstCall)` → returns `FIRST_CALL_NODE_BUDGET` when `firstCall`, else `PER_TICK_NODE_BUDGET`. (Extracted so the budget policy is unit-testable without World.)

- [ ] **Step 1: Write the failing test (pure budget-policy seam)**

```csharp
// tests/ClassicUO.UnitTests/Game/Pathfinder/PathBudgetTests.cs
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class PathBudgetTests
    {
        [Fact]
        public void First_call_budget_is_larger_than_per_tick_budget()
        {
            Assert.True(ClassicUO.Game.Pathfinder.FrameBudget(true) > ClassicUO.Game.Pathfinder.FrameBudget(false));
        }

        [Fact]
        public void First_call_budget_covers_a_typical_in_view_walk()
        {
            // A ~30-tile in-view walk expands well under the first-call budget,
            // so WalkTo still completes synchronously (no behaviour change).
            Assert.True(ClassicUO.Game.Pathfinder.FrameBudget(true) >= 8000);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PathBudgetTests"`
Expected: FAIL to compile — `Pathfinder.FrameBudget` does not exist.

- [ ] **Step 3: Add the budget constants + policy method**

Add to `Pathfinder` (near `PATHFINDER_MAX_NODES` in `src/ClassicUO.Client/Game/Pathfinder.cs`):

```csharp
        // Per-frame node budgets for the resumable search. The first call spends
        // a large budget so in-view walks finish synchronously (no behaviour
        // change); longer searches spill into per-tick continuations driven by
        // ProcessAutoWalk.
        internal const int FIRST_CALL_NODE_BUDGET = 8000;
        internal const int PER_TICK_NODE_BUDGET = 4000;

        internal static int FrameBudget(bool firstCall) => firstCall ? FIRST_CALL_NODE_BUDGET : PER_TICK_NODE_BUDGET;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PathBudgetTests"`
Expected: PASS.

- [ ] **Step 5: Add the search-state fields and rewrite `WalkTo` + `ProcessAutoWalk`**

Add fields to `Pathfinder`:

```csharp
        private readonly AStarSearch _search = new AStarSearch();
        private bool _searching;
```

Replace the `AStarPathSearch.TryFindPath(...)` call block in `WalkTo` (keep everything above it — the `Player`/`IsParalyzed` guard, the `distance==0 && IsBlocked` bump, `playerX/Y/Z`, `ResetAutoWalk`, the run heuristic, `_run = run`) with:

```csharp
            _search.Begin(playerX, playerY, playerZ, x, y, distance, ExpandWalkable);
            _searching = true;
            AutoWalking = true;

            AStarSearch.Status status = _search.Step(FrameBudget(firstCall: true));

            if (status == AStarSearch.Status.Found)
            {
                AdoptFoundPath();
                return _pathSize != 0;
            }

            if (status == AStarSearch.Status.Failed)
            {
                _searching = false;
                AutoWalking = false;
                return false;
            }

            // Still searching: keep going on later ticks. Report success (a walk
            // has been started) so click-to-path callers do not run their fallback.
            return true;
```

Add a helper (private) that turns a completed search into a walk:

```csharp
        private void AdoptFoundPath()
        {
            _searching = false;
            _search.GetPath(_path);
            _pathSize = _path.Count;
            _pointIndex = 0;

            if (_pathSize == 0)
            {
                // Already within tolerance: nothing to walk.
                AutoWalking = false;
                return;
            }

            RaiseWalkProgress(WalkState.Walking);
            ProcessAutoWalk();
        }
```

Modify `ProcessAutoWalk` — add a search-continuation branch at the very top of the method body, before the existing walking logic:

```csharp
        public void ProcessAutoWalk()
        {
            if (_searching)
            {
                if (!AutoWalking || !_world.InGame)
                {
                    return;
                }

                AStarSearch.Status status = _search.Step(FrameBudget(firstCall: false));

                if (status == AStarSearch.Status.Found)
                {
                    AdoptFoundPath();
                }
                else if (status == AStarSearch.Status.Failed)
                {
                    EndAutoWalk(WalkState.Blocked);
                }

                return; // do not walk on the tick the search advances/finishes
            }

            // ... existing body unchanged ...
        }
```

Also set `_searching = false;` inside `ResetAutoWalk()` so cancels/new requests drop any in-flight search:

```csharp
        internal void ResetAutoWalk()
        {
            AutoWalking = false;
            _run = false;
            _pathSize = 0;
            _searching = false;
        }
```

- [ ] **Step 6: Build and run the full suite (no regressions)**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug` then `dotnet test tests/ClassicUO.UnitTests`
Expected: Build succeeded; all tests pass (`AStarPathSearchTests`, `AStarSearchTests`, `PathBudgetTests`, `WalkProgressTests`, and the rest).

- [ ] **Step 7: Manual verification (requires a UO data dir + running client)**

Build/run the client, log in, and:
1. Right-click-hold to path-walk to a nearby in-view tile → walks immediately, exactly as before (first-call budget covers it).
2. Use a plugin/console `WalkTo` (or `PacketHandlers` walk) to a far same-continent tile hundreds of tiles away → the client keeps rendering (no multi-second freeze) and the character starts walking within a few frames.
3. Cancel a walk mid-search (movement key / `StopAutoWalk`) → search drops, no stuck state.

Expected: no frame stall on the long request; short requests unchanged.

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Game/Pathfinder.cs tests/ClassicUO.UnitTests/Game/Pathfinder/PathBudgetTests.cs
git commit -m "feat(pathfinder): time-slice long searches across ticks via resumable AStarSearch"
```

---

# PART A — Region Map (O(1) reachability reject)

### Task A1: Flood-fill core (connected components)

Pure 8-connected labelling of a passability grid. No map data.

**Files:**
- Create: `src/ClassicUO.Client/Game/RegionMap.cs` (the builder lives here alongside the map type it produces)
- Test: `tests/ClassicUO.UnitTests/Game/Pathfinder/RegionMapBuilderTests.cs`

**Interfaces:**
- Produces:
  - `internal static class RegionMapBuilder`
  - `int[] RegionMapBuilder.Build(int width, int height, System.Func<int, int, bool> passable)` — returns an array of length `width*height` indexed `x*height + y`; `0` = impassable, region ids number from `1`; 8-connected (diagonals join).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ClassicUO.UnitTests/Game/Pathfinder/RegionMapBuilderTests.cs
// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class RegionMapBuilderTests
    {
        private static int Id(int[] ids, int height, int x, int y) => ids[x * height + y];

        [Fact]
        public void Open_field_is_one_region()
        {
            int[] ids = RegionMapBuilder.Build(4, 4, (x, y) => true);

            int first = Id(ids, 4, 0, 0);
            Assert.True(first > 0);
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 4; y++)
                    Assert.Equal(first, Id(ids, 4, x, y));
        }

        [Fact]
        public void Impassable_tiles_get_region_zero()
        {
            int[] ids = RegionMapBuilder.Build(3, 1, (x, y) => x != 1);

            Assert.True(Id(ids, 1, 0, 0) > 0);
            Assert.Equal(0, Id(ids, 1, 1, 0));
            Assert.True(Id(ids, 1, 2, 0) > 0);
        }

        [Fact]
        public void A_full_height_wall_splits_into_two_regions()
        {
            // column x==2 impassable, walls off left (x<2) from right (x>2).
            Func<int, int, bool> passable = (x, y) => x != 2;
            int[] ids = RegionMapBuilder.Build(5, 5, passable);

            int left = Id(ids, 5, 0, 0);
            int right = Id(ids, 5, 4, 0);
            Assert.True(left > 0 && right > 0);
            Assert.NotEqual(left, right);
        }

        [Fact]
        public void Diagonally_touching_cells_are_the_same_region()
        {
            // Only (0,0) and (1,1) passable; they touch diagonally => same region.
            Func<int, int, bool> passable = (x, y) => (x == 0 && y == 0) || (x == 1 && y == 1);
            int[] ids = RegionMapBuilder.Build(2, 2, passable);

            Assert.Equal(Id(ids, 2, 0, 0), Id(ids, 2, 1, 1));
            Assert.True(Id(ids, 2, 0, 0) > 0);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~RegionMapBuilderTests"`
Expected: FAIL to compile — `RegionMapBuilder` does not exist.

- [ ] **Step 3: Implement the flood fill (iterative — no recursion)**

```csharp
// src/ClassicUO.Client/Game/RegionMap.cs
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace ClassicUO.Game
{
    internal static class RegionMapBuilder
    {
        private static readonly int[] OffX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] OffY = { -1, -1, 0, 1, 1, 1, 0, -1 };

        /// <summary>Label 8-connected components of the passability grid. Result
        /// is length width*height indexed x*height+y; 0 = impassable, ids from 1.</summary>
        public static int[] Build(int width, int height, Func<int, int, bool> passable)
        {
            int[] ids = new int[width * height];
            var stack = new Stack<int>(); // packed x*height+y
            int nextId = 0;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int start = x * height + y;

                    if (ids[start] != 0 || !passable(x, y))
                    {
                        continue;
                    }

                    nextId++;
                    ids[start] = nextId;
                    stack.Push(start);

                    while (stack.Count > 0)
                    {
                        int cur = stack.Pop();
                        int cx = cur / height;
                        int cy = cur % height;

                        for (int d = 0; d < 8; d++)
                        {
                            int nx = cx + OffX[d];
                            int ny = cy + OffY[d];

                            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            {
                                continue;
                            }

                            int nidx = nx * height + ny;

                            if (ids[nidx] != 0 || !passable(nx, ny))
                            {
                                continue;
                            }

                            ids[nidx] = nextId;
                            stack.Push(nidx);
                        }
                    }
                }
            }

            return ids;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~RegionMapBuilderTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/RegionMap.cs tests/ClassicUO.UnitTests/Game/Pathfinder/RegionMapBuilderTests.cs
git commit -m "feat(pathfinder): connected-component flood fill for region map"
```

---

### Task A2: `RegionMap` value type + `SameRegion` query

**Files:**
- Modify: `src/ClassicUO.Client/Game/RegionMap.cs` (add the `RegionMap` type)
- Test: `tests/ClassicUO.UnitTests/Game/Pathfinder/RegionMapTests.cs`

**Interfaces:**
- Consumes: `RegionMapBuilder.Build`.
- Produces:
  - `internal sealed class RegionMap`
  - `RegionMap(int facet, int width, int height, int[] ids)` — `ids` as produced by `RegionMapBuilder.Build`.
  - `int Facet { get; }`, `int Width { get; }`, `int Height { get; }`, `int[] Ids { get; }`
  - `int RegionOf(int x, int y)` — `0` when out of bounds or impassable.
  - `bool SameRegion(int x1, int y1, int x2, int y2)` — `true` only when both tiles are in-bounds, passable, and share a region id.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ClassicUO.UnitTests/Game/Pathfinder/RegionMapTests.cs
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class RegionMapTests
    {
        private static RegionMap Build(int w, int h, System.Func<int, int, bool> passable)
            => new RegionMap(0, w, h, RegionMapBuilder.Build(w, h, passable));

        [Fact]
        public void Same_component_returns_true()
        {
            var map = Build(4, 4, (x, y) => true);
            Assert.True(map.SameRegion(0, 0, 3, 3));
        }

        [Fact]
        public void Different_components_return_false()
        {
            var map = Build(5, 5, (x, y) => x != 2); // wall at x==2
            Assert.False(map.SameRegion(0, 0, 4, 4));
        }

        [Fact]
        public void Impassable_endpoint_returns_false()
        {
            var map = Build(3, 1, (x, y) => x != 1);
            Assert.Equal(0, map.RegionOf(1, 0));
            Assert.False(map.SameRegion(0, 0, 1, 0));
        }

        [Fact]
        public void Out_of_bounds_returns_zero_and_false()
        {
            var map = Build(2, 2, (x, y) => true);
            Assert.Equal(0, map.RegionOf(-1, 0));
            Assert.Equal(0, map.RegionOf(2, 2));
            Assert.False(map.SameRegion(0, 0, 99, 99));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~RegionMapTests"`
Expected: FAIL to compile — `RegionMap` does not exist.

- [ ] **Step 3: Implement `RegionMap`**

Add to `src/ClassicUO.Client/Game/RegionMap.cs`:

```csharp
    internal sealed class RegionMap
    {
        public RegionMap(int facet, int width, int height, int[] ids)
        {
            Facet = facet;
            Width = width;
            Height = height;
            Ids = ids;
        }

        public int Facet { get; }
        public int Width { get; }
        public int Height { get; }
        public int[] Ids { get; }

        public int RegionOf(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return 0;
            }

            return Ids[x * Height + y];
        }

        public bool SameRegion(int x1, int y1, int x2, int y2)
        {
            int a = RegionOf(x1, y1);

            return a != 0 && a == RegionOf(x2, y2);
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~RegionMapTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/RegionMap.cs tests/ClassicUO.UnitTests/Game/Pathfinder/RegionMapTests.cs
git commit -m "feat(pathfinder): RegionMap with SameRegion reachability query"
```

---

### Task A3: On-disk cache (RLE serialization)

Region maps are large; persist them so the flood fill runs once per facet. Format: header + run-length-encoded ids.

**Files:**
- Create: `src/ClassicUO.Client/Game/RegionMapCache.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Pathfinder/RegionMapCacheTests.cs`

**Interfaces:**
- Consumes: `RegionMap`.
- Produces:
  - `internal static class RegionMapCache`
  - `const uint RegionMapCache.Magic` (= `0x4E47_5243`, "CRGN"), `const int RegionMapCache.Version` (= `1`)
  - `void Write(System.IO.Stream stream, RegionMap map)` — writes magic, version, facet, width, height, then RLE pairs `(int id, int runLength)` over `Ids` in index order.
  - `bool TryRead(System.IO.Stream stream, int expectedFacet, out RegionMap map)` — returns `false` (and `map = null`) on magic/version/facet mismatch or truncation.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ClassicUO.UnitTests/Game/Pathfinder/RegionMapCacheTests.cs
// SPDX-License-Identifier: BSD-2-Clause

using System.IO;
using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class RegionMapCacheTests
    {
        [Fact]
        public void Roundtrip_preserves_ids_and_dims()
        {
            var original = new RegionMap(2, 5, 5, RegionMapBuilder.Build(5, 5, (x, y) => x != 2));

            using var ms = new MemoryStream();
            RegionMapCache.Write(ms, original);
            ms.Position = 0;

            Assert.True(RegionMapCache.TryRead(ms, 2, out RegionMap read));
            Assert.Equal(original.Width, read.Width);
            Assert.Equal(original.Height, read.Height);
            Assert.Equal(original.Facet, read.Facet);
            Assert.Equal(original.Ids, read.Ids);
        }

        [Fact]
        public void Wrong_facet_is_rejected()
        {
            var original = new RegionMap(0, 3, 3, RegionMapBuilder.Build(3, 3, (x, y) => true));
            using var ms = new MemoryStream();
            RegionMapCache.Write(ms, original);
            ms.Position = 0;

            Assert.False(RegionMapCache.TryRead(ms, 1, out RegionMap read));
            Assert.Null(read);
        }

        [Fact]
        public void Garbage_stream_is_rejected()
        {
            using var ms = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            Assert.False(RegionMapCache.TryRead(ms, 0, out RegionMap read));
            Assert.Null(read);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~RegionMapCacheTests"`
Expected: FAIL to compile — `RegionMapCache` does not exist.

- [ ] **Step 3: Implement the cache**

```csharp
// src/ClassicUO.Client/Game/RegionMapCache.cs
// SPDX-License-Identifier: BSD-2-Clause

using System.IO;

namespace ClassicUO.Game
{
    internal static class RegionMapCache
    {
        public const uint Magic = 0x4E475243; // 'C','R','G','N'
        public const int Version = 1;

        public static void Write(Stream stream, RegionMap map)
        {
            using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            w.Write(Magic);
            w.Write(Version);
            w.Write(map.Facet);
            w.Write(map.Width);
            w.Write(map.Height);

            int[] ids = map.Ids;
            int i = 0;

            while (i < ids.Length)
            {
                int value = ids[i];
                int run = 1;

                while (i + run < ids.Length && ids[i + run] == value)
                {
                    run++;
                }

                w.Write(value);
                w.Write(run);
                i += run;
            }
        }

        public static bool TryRead(Stream stream, int expectedFacet, out RegionMap map)
        {
            map = null;

            try
            {
                using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

                if (r.ReadUInt32() != Magic || r.ReadInt32() != Version)
                {
                    return false;
                }

                int facet = r.ReadInt32();
                int width = r.ReadInt32();
                int height = r.ReadInt32();

                if (facet != expectedFacet || width <= 0 || height <= 0)
                {
                    return false;
                }

                long total = (long)width * height;
                int[] ids = new int[total];
                int i = 0;

                while (i < total)
                {
                    int value = r.ReadInt32();
                    int run = r.ReadInt32();

                    if (run <= 0 || i + run > total)
                    {
                        return false;
                    }

                    for (int j = 0; j < run; j++)
                    {
                        ids[i + j] = value;
                    }

                    i += run;
                }

                map = new RegionMap(facet, width, height, ids);
                return true;
            }
            catch (EndOfStreamException)
            {
                map = null;
                return false;
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~RegionMapCacheTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/RegionMapCache.cs tests/ClassicUO.UnitTests/Game/Pathfinder/RegionMapCacheTests.cs
git commit -m "feat(pathfinder): RLE region-map disk cache (read/write)"
```

---

### Task A4: WalkTo reachability gate (pure decision + integration)

Short-circuit `WalkTo` when start and goal are in different components. The decision is a pure helper (unit-tested); the `WalkTo` hook is a one-line integration.

**Files:**
- Modify: `src/ClassicUO.Client/Game/RegionMap.cs` (add `RegionGate`)
- Modify: `src/ClassicUO.Client/Game/Pathfinder.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Pathfinder/RegionGateTests.cs`

**Interfaces:**
- Consumes: `RegionMap`.
- Produces:
  - `internal static class RegionGate`
  - `bool RegionGate.IsUnreachable(RegionMap map, int startX, int startY, int goalX, int goalY)` — `false` when `map` is null (no map = never reject); otherwise `true` only when both endpoints are known-passable and in different regions. If either endpoint is region `0` (impassable/unknown in the static map), returns `false` (stay optimistic — let the live A* decide).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ClassicUO.UnitTests/Game/Pathfinder/RegionGateTests.cs
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    public class RegionGateTests
    {
        private static RegionMap Wall5x5() => new RegionMap(0, 5, 5, RegionMapBuilder.Build(5, 5, (x, y) => x != 2));

        [Fact]
        public void Null_map_never_rejects()
        {
            Assert.False(RegionGate.IsUnreachable(null, 0, 0, 999, 999));
        }

        [Fact]
        public void Different_regions_are_unreachable()
        {
            Assert.True(RegionGate.IsUnreachable(Wall5x5(), 0, 0, 4, 4));
        }

        [Fact]
        public void Same_region_is_reachable()
        {
            Assert.False(RegionGate.IsUnreachable(Wall5x5(), 0, 0, 1, 4));
        }

        [Fact]
        public void Unknown_endpoint_stays_optimistic()
        {
            // goal on the impassable wall (region 0) => do not reject.
            Assert.False(RegionGate.IsUnreachable(Wall5x5(), 0, 0, 2, 2));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~RegionGateTests"`
Expected: FAIL to compile — `RegionGate` does not exist.

- [ ] **Step 3: Implement `RegionGate`**

Add to `src/ClassicUO.Client/Game/RegionMap.cs`:

```csharp
    internal static class RegionGate
    {
        /// <summary>True only when the region map proves the goal cannot be
        /// reached from the start on foot. Optimistic: a null map, or an
        /// endpoint the static map marks impassable/unknown (region 0), never
        /// rejects — the live A* is left to decide.</summary>
        public static bool IsUnreachable(RegionMap map, int startX, int startY, int goalX, int goalY)
        {
            if (map == null)
            {
                return false;
            }

            int a = map.RegionOf(startX, startY);
            int b = map.RegionOf(goalX, goalY);

            if (a == 0 || b == 0)
            {
                return false;
            }

            return a != b;
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~RegionGateTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Hook the gate into `WalkTo`**

In `src/ClassicUO.Client/Game/Pathfinder.cs`, in `WalkTo`, immediately after the existing `if (_world.Player == null ... IsParalyzed) return false;` guard and before the `distance==0 && IsBlocked` bump, add:

```csharp
            if (RegionGate.IsUnreachable(_world.RegionMap, _world.Player.X, _world.Player.Y, x, y))
            {
                return false;
            }
```

`_world.RegionMap` is provided by Task A6. Until A6 lands, add a temporary `internal RegionMap RegionMap => null;` property to `World` (removed/replaced in A6) so this task builds and behaves as a no-op. (If executing A6 before A4's integration, skip this stub.)

- [ ] **Step 6: Build + run the full suite**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug` then `dotnet test tests/ClassicUO.UnitTests`
Expected: Build succeeded; all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/ClassicUO.Client/Game/RegionMap.cs src/ClassicUO.Client/Game/Pathfinder.cs tests/ClassicUO.UnitTests/Game/Pathfinder/RegionGateTests.cs
git commit -m "feat(pathfinder): reject cross-region WalkTo via RegionGate"
```

---

### Task A5: Static passability extraction (asset read + integration test)

Extract optimistic land+statics passability straight from the map files and prove, against the real `E:/Games/Ultima Online Classic` data, that the resulting region map separates water-split landmasses. This is the one task that reads UO assets; it is covered by a guarded integration test (skips when no data dir), not a pure unit test.

**Files:**
- Create: `src/ClassicUO.Client/Game/StaticMapPassability.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Pathfinder/StaticMapPassabilityIntegrationTests.cs`

**Interfaces:**
- Consumes: `ClassicUO.Assets.UOFileManager` (`Maps.MapBlocksSize`, `Maps.BlockData` → `IndexMap` with `MapFile`/`MapAddress`/`StaticFile`/`StaticAddress`/`StaticCount`, `struct MapBlock`, `struct StaticsBlock`), `ClassicUO.Assets.TileDataLoader` (`LandData[g].Flags`, `StaticData[g].Flags` with `.IsImpassable`/`.IsSurface`).
- Produces:
  - `internal static class StaticMapPassability`
  - `int StaticMapPassability.Width(ClassicUO.Assets.UOFileManager fm, int facet)` → `MapBlocksSize[facet,0] << 3`
  - `int StaticMapPassability.Height(ClassicUO.Assets.UOFileManager fm, int facet)` → `MapBlocksSize[facet,1] << 3`
  - `bool[] StaticMapPassability.Build(ClassicUO.Assets.UOFileManager fm, int facet)` → length `Width*Height`, indexed `x*Height + y`; `true` = optimistically walkable.

**Design notes (concrete):**
- **Passability (optimistic).** Tile `(x,y)` is passable if its land is walkable **or** it carries any non-impassable static surface:
  - Land: read the block's `MapBlock` (seek `IndexMap.MapFile` to `MapAddress`, `Read<MapBlock>()`), cell `[(my<<3)+mx]` → land `graphic` → `fm.TileData.LandData[graphic].Flags`. Passable-land = `!IsImpassable`. Also treat the classic no-draw graphics `0x0002` and `0x01DB` as passable void (matches the land filter in `Pathfinder.CreateItemList`).
  - Statics: if `IndexMap.StaticFile != null && StaticCount > 0`, seek to `StaticAddress`, read `StaticCount` × `StaticsBlock`; for each, `graphic` → `fm.TileData.StaticData[graphic]`; if `IsSurface && !IsImpassable`, mark tile `(blockX*8 + s.X, blockY*8 + s.Y)` passable.
  - Ignore Z entirely (2D over-approximation) — only ever *over*-connects, which is safe (Global Constraints).
- **Iteration.** Loop blocks `bx in [0,MapBlocksSize[facet,0])`, `by in [0,MapBlocksSize[facet,1])`; block index = `bx * MapBlocksSize[facet,1] + by` (matches `Map.GetBlock`); decode its 64 land cells + statics into the shared `bool[]`. One seek per block, not per tile.
- Use `unsafe`/`Read<T>` the same way `Map.GetTileZ` (`src/ClassicUO.Client/Game/Map/Map.cs:124`) reads a `MapBlock`.

- [ ] **Step 1: Write the failing integration test**

```csharp
// tests/ClassicUO.UnitTests/Game/Pathfinder/StaticMapPassabilityIntegrationTests.cs
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Game;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    /// <summary>
    /// Asset-backed. Resolves a real UO data dir from CUO_UO_TEST_DIR (default
    /// E:/Games/Ultima Online Classic) and returns early when absent so CI
    /// without assets stays green.
    /// </summary>
    public class StaticMapPassabilityIntegrationTests
    {
        private static string ResolveDir()
        {
            string dir = Environment.GetEnvironmentVariable("CUO_UO_TEST_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                dir = @"E:/Games/Ultima Online Classic";
            }
            return Directory.Exists(dir) ? dir : null;
        }

        private static UOFileManager LoadFileManager(string dir)
        {
            if (!ClientVersionHelper.TryParseFromFile(Path.Combine(dir, "client.exe"), out string vtext)
                || !ClientVersionHelper.IsClientVersionValid(vtext, out ClientVersion version))
            {
                version = ClientVersion.CV_70796;
            }

            var fm = new UOFileManager(version, dir);
            fm.Load(false, "enu");
            return fm;
        }

        [Fact]
        public void Felucca_region_map_separates_water_split_landmasses()
        {
            string dir = ResolveDir();
            if (dir == null)
            {
                return; // no data dir: skip
            }

            UOFileManager fm = LoadFileManager(dir);

            int w = StaticMapPassability.Width(fm, 0);
            int h = StaticMapPassability.Height(fm, 0);
            Assert.Equal(7168, w);
            Assert.Equal(4096, h);

            bool[] passable = StaticMapPassability.Build(fm, 0);
            int[] ids = RegionMapBuilder.Build(w, h, (x, y) => passable[x * h + y]);
            var map = new RegionMap(0, w, h, ids);

            // Sanity: both passable and impassable tiles exist.
            bool anyPassable = false, anyBlocked = false;
            for (int i = 0; i < ids.Length && !(anyPassable && anyBlocked); i += 997)
            {
                if (ids[i] != 0) anyPassable = true; else anyBlocked = true;
            }
            Assert.True(anyPassable && anyBlocked);

            // Britain mainland vs. Moonglow (Verity Isle) — ocean-separated in
            // Felucca; scan a small window around each anchor for a passable tile.
            Assert.True(TryFindPassableNear(map, 1416, 1690, out int bx, out int by), "no passable tile near Britain");
            Assert.True(TryFindPassableNear(map, 4467, 1283, out int mx, out int my), "no passable tile near Moonglow");
            Assert.False(map.SameRegion(bx, by, mx, my), "Britain and Moonglow must be different regions");
        }

        private static bool TryFindPassableNear(RegionMap map, int cx, int cy, out int fx, out int fy)
        {
            for (int r = 0; r <= 40; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (map.RegionOf(x, y) != 0)
                        {
                            fx = x; fy = y; return true;
                        }
                    }
                }
            }
            fx = fy = 0; return false;
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~StaticMapPassabilityIntegrationTests"`
Expected: FAIL to compile — `StaticMapPassability` does not exist. (If the data dir is absent it would instead pass trivially once compiling; that is acceptable — the point is it must fail now.)

- [ ] **Step 3: Implement `StaticMapPassability`** per the design notes (block-by-block land + statics decode into `bool[]`, index `x*Height+y`).

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~StaticMapPassabilityIntegrationTests"`
Expected: PASS with the real data dir (dims 7168×4096; Britain and Moonglow in different regions). If anchors ever miss, widen the scan radius or adjust the anchors to known walkable spots — do NOT weaken the different-region assertion.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/StaticMapPassability.cs tests/ClassicUO.UnitTests/Game/Pathfinder/StaticMapPassabilityIntegrationTests.cs
git commit -m "feat(pathfinder): static land+statics passability extraction (asset-backed test)"
```

---

### Task A6: Background build/load provider + World wiring

Load the disk cache or build the region map on a background thread at facet load, and expose it as `World.RegionMap`. The heavy correctness (passability + labelling + cache) is already covered by A1–A5; this task is the threading + wiring glue, verified by build + a runtime smoke test.

**Files:**
- Create: `src/ClassicUO.Client/Game/RegionMapProvider.cs`
- Modify: `src/ClassicUO.Client/Game/World.cs` (own a `RegionMapProvider`, expose `RegionMap`, kick off build on map change)

**Interfaces:**
- Consumes: `StaticMapPassability` (A5), `RegionMapBuilder.Build`, `RegionMap`, `RegionMapCache`, `ClassicUO.Assets.UOFileManager`.
- Produces:
  - `internal sealed class RegionMapProvider`
  - `RegionMapProvider(ClassicUO.Assets.UOFileManager fm, string cacheDir)`
  - `RegionMap Current { get; }` — the active facet's map, or `null` while building / unavailable.
  - `void EnsureFor(int facet)` — idempotent; returns immediately if `Current?.Facet == facet` or a build for `facet` is already in flight. Otherwise tries the disk cache (`region_<facet>.bin`), else starts a background build (`Task.Run`), then writes the cache.
  - On `World`: `internal RegionMap RegionMap => _regionMapProvider?.Current;`

**Design notes (concrete):**
- **Threading (safe).** Build reads only immutable map **files** via the loader (never the live streaming `Map`/`Chunk`), so it is safe off the game thread. Publish the finished `RegionMap` to `Current` via a single volatile reference assignment — game-thread readers see either the old value (`null`) or the complete map, never a half-built one. Guard duplicate builds with a `_buildingFacet` int + `lock`.
- **Cache location.** `cacheDir/region_<facet>.bin`. On `EnsureFor`: try `RegionMapCache.TryRead(fileStream, facet, out map)`; on success publish immediately (fast path, no build). Otherwise build (`StaticMapPassability.Build` → `RegionMapBuilder.Build` → `new RegionMap`), publish, then `RegionMapCache.Write`.
- **Invalidation.** Include the map file length in the header via a `Version = 2` bump (`long mapFileLength` after `height`; `TryRead` also takes `expectedMapFileLength` and rejects on mismatch), OR document that users clear `regioncache/` after a client/mul update. While UltimaLive is active, do NOT build (leave `Current == null`) so the gate never rejects on a stale cache.
- **Wiring.** Instantiate the provider once (pass `Client.Game.UO.FileManager` and a cache dir under the profile/data path). Call `EnsureFor(map.Index)` from `World` wherever `World.Map` is created/changed. Never block the caller. Remove the temporary `RegionMap => null` stub from Task A4.

- [ ] **Step 1: Implement `RegionMapProvider`** (cache-first, background build, atomic publish, duplicate-build guard) per the design notes.

- [ ] **Step 2: Wire `World`** — add the provider field, the `RegionMap` property, and the `EnsureFor(index)` call on map (re)creation. Remove the A4 stub.

- [ ] **Step 3: Build the client**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 4: Runtime smoke test (client + `E:/Games/Ultima Online Classic`)**

1. Delete any existing `regioncache/`. Log into Felucca; a temporary `Log.Trace` on publish confirms the off-thread build finishes with no frame hitch; `region_0.bin` is written.
2. Restart + log in → cache loads (fast path), no rebuild.
3. From a mainland tile, `WalkTo` a tile on an ocean-separated island → returns `false` immediately (no A* run, no freeze).
4. `WalkTo` a far same-landmass tile → still routes (gate does not reject); with Part B it starts walking without a stall.
5. `WalkTo` an adjacent water/blocked tile → not wrongly rejected (region-0 endpoint → gate passes through to A*).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/RegionMapProvider.cs src/ClassicUO.Client/Game/World.cs
git commit -m "feat(pathfinder): background region-map provider with disk cache, wired to World"
```

---

## Self-Review notes

- **Spec coverage:** Part A = region map (A1 flood fill, A2 query, A3 cache, A4 gate+integration, A5 asset passability + integration test, A6 provider/wiring). Part B = time-slicing (B1 resumable core, B2 WalkTo/ProcessAutoWalk wiring + budgets). Both requested subsystems covered; independence documented. Asset reading (A5) is now integration-tested against real data rather than manual-only; A6 (threading/wiring) stays build + runtime smoke.
- **Type consistency:** `AStarSearch.Status`/`Begin`/`Step`/`GetPath`/`ExpandedNodes` used identically in B1 and B2. `RegionMap` ctor `(facet,width,height,ids)` and `Ids`/`Width`/`Height`/`RegionOf`/`SameRegion` consistent across A2–A6. `RegionGate.IsUnreachable` signature matches its WalkTo call site. `RegionMapCache.Write/TryRead(...,expectedFacet,...)` consistent A3↔A6 (A6 may bump to `Version=2` with a `mapFileLength` field — update both `Write` and `TryRead` together if so). `StaticMapPassability.Width/Height/Build(fm,facet)` consistent A5↔A6. Index convention `x*height+y` identical in builder, RegionMap, cache, passability, and provider.
- **Optimism invariant** enforced at two layers: builder marks unknown as region 0; `RegionGate` refuses to reject when either endpoint is region 0 or the map is null.
- **Known cost (flag for reviewer):** the per-tile `int[]` region map is ~117 MB for the largest facet (7168×4096). Acceptable for a first cut (active facet only, RLE on disk). If memory-constrained, a follow-up can compact ids to `ushort` or drop to per-8×8-block granularity (coarser but still separates water-split continents) — out of scope here.
```
