# Anchored Screen Timers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a plugin pin a screen timer to an in-game anchor (mobile/item serial, absolute map tile, or self); the timer hides while the anchor is off-screen but keeps counting, reappears when the anchor returns, and auto-removes after a grace period once the anchor is lost.

**Architecture:** Extend the existing plugin-driven screen-timer chain (`TimerConfig` → `AddTimerFn` ABI → `PluginTimersManager.AddTimer` → `ScreenTimers.AddOrUpdate` → `ScreenTimerEntry` → `ScreenTimersGump`) with anchor fields. Anchor→screen resolution and off-screen culling live in the gump render path (copying the `NameOverheadGump`/`HealthLinesManager` recipe). Lost-anchor grace/removal lives in `PluginTimersManager.Update` (runs from `World.Update`, gump-independent), so timers keep counting regardless of visibility.

**Tech Stack:** C# (`net10.0`, `LangVersion=Preview`, `AllowUnsafeBlocks=true`), FNA-XNA, xUnit. Client library `ClassicUO.Client`; host `ClassicUO.BootstrapHost`; public contract `ClassicUO.PluginApi`.

## Global Constraints

- Every new source file carries the BSD-2-Clause license header: `// SPDX-License-Identifier: BSD-2-Clause` (enforced by `ClassicUO.licenseheader`).
- Match surrounding style: no allocations in the per-frame render/update hot paths; reuse scratch lists/dictionaries.
- `ClassicUO.Client` internals are visible to `ClassicUO.UnitTests` via `InternalsVisibleTo` — tests reference `internal` types directly.
- Plugin v2 contract is still evolving; a breaking `AddTimerFn` ABI change is acceptable because host + client rebuild together from this repo.
- Anchored timers (`AnchorKind != None`) ignore `GroupId`/stacking; position comes from the anchor. `None` keeps existing fixed/group behavior byte-for-byte.
- **Off-screen ≠ lost.** Off-screen = anchor resolves but is outside the camera → skip draw, no timeout. Lost = `World.Get`/`World.Player` is null → start grace countdown.
- Timers are not persisted (the overlay uses `GumpType.None`); do not add save/restore.

---

### Task 1: Anchor data model + replace-by-anchor in `ScreenTimers`

Adds the anchor concept to the entry struct and the store, with optional
parameters so every existing caller compiles unchanged.

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/ScreenTimers.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/ScreenTimersAnchorTests.cs` (create)

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `enum AnchorKind { None = 0, Serial = 1, Absolute = 2, Self = 3 }` (in `ClassicUO.Game.Managers`).
  - New public fields on `ScreenTimerEntry`: `AnchorKind AnchorKind; uint AnchorSerial; ushort AnchorX; ushort AnchorY; sbyte AnchorZ; short AnchorOffsetX; short AnchorOffsetY; int AnchorGraceMs; long MissingSinceTicks;`
  - Extended signature:
    `ScreenTimers.AddOrUpdate(int id, TimerShape shape, int durationMs, ushort hue, int groupId, int x, int y, int width, int height, string label, bool showTime, long now, AnchorKind anchorKind = AnchorKind.None, uint anchorSerial = 0, ushort anchorX = 0, ushort anchorY = 0, sbyte anchorZ = 0, short anchorOffsetX = 0, short anchorOffsetY = 0, int anchorGraceMs = 0)`
  - `const int ScreenTimers.DefaultGraceMs = 5000;`
  - `void ScreenTimers.SetMissingSince(int id, long ticks)` — used by the manager (Task 2) to persist grace state back into the stored struct.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Managers/ScreenTimersAnchorTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class ScreenTimersAnchorTests
    {
        public ScreenTimersAnchorTests() => ScreenTimers.Reset();

        [Fact]
        public void AddOrUpdate_StoresSerialAnchorFields()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, "poison", true,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000,
                anchorOffsetX: 3, anchorOffsetY: -4, anchorGraceMs: 2000);

            var e = ScreenTimers.Entries[1];
            Assert.Equal(AnchorKind.Serial, e.AnchorKind);
            Assert.Equal((uint)0x4000, e.AnchorSerial);
            Assert.Equal((short)3, e.AnchorOffsetX);
            Assert.Equal((short)-4, e.AnchorOffsetY);
            Assert.Equal(2000, e.AnchorGraceMs);
            Assert.Equal(0, e.MissingSinceTicks);
        }

        [Fact]
        public void AddOrUpdate_SecondSerialTimer_ReplacesFirstOnSameSerial()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000);

            Assert.False(ScreenTimers.Entries.ContainsKey(1)); // purged
            Assert.True(ScreenTimers.Entries.ContainsKey(2));
        }

        [Fact]
        public void AddOrUpdate_SameSerialDifferentEntries_DoNotPurgeDifferentSerial()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x5000);

            Assert.True(ScreenTimers.Entries.ContainsKey(1));
            Assert.True(ScreenTimers.Entries.ContainsKey(2));
        }

        [Fact]
        public void SetMissingSince_UpdatesStoredEntry()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000);
            ScreenTimers.SetMissingSince(1, 1234);
            Assert.Equal(1234, ScreenTimers.Entries[1].MissingSinceTicks);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ScreenTimersAnchorTests"`
Expected: FAIL — compile error (no `AnchorKind`, no anchor parameters, no `SetMissingSince`).

- [ ] **Step 3: Add the enum and struct fields**

In `src/ClassicUO.Client/Game/Managers/ScreenTimers.cs`, after the existing
`enum StackDirection` (line 9) add:

```csharp
    internal enum AnchorKind { None = 0, Serial = 1, Absolute = 2, Self = 3 }
```

Append these fields inside `struct ScreenTimerEntry` (after `Order`):

```csharp
        public AnchorKind AnchorKind;
        public uint AnchorSerial;   // Serial kind
        public ushort AnchorX;      // Absolute kind
        public ushort AnchorY;
        public sbyte AnchorZ;
        public short AnchorOffsetX; // pixel nudge from resolved anchor point
        public short AnchorOffsetY;
        public int AnchorGraceMs;   // Serial/Self lost-grace; 0 => DefaultGraceMs
        public long MissingSinceTicks; // runtime state; 0 = currently resolvable
```

- [ ] **Step 4: Extend `AddOrUpdate` with anchor params + replace-by-anchor**

Add near the other constants (line 55):

```csharp
        public const int DefaultGraceMs = 5000;
```

Replace the `AddOrUpdate` signature and body (lines 74-103) with:

```csharp
        public static void AddOrUpdate(int id, TimerShape shape, int durationMs, ushort hue, int groupId,
                                       int x, int y, int width, int height, string label, bool showTime, long now,
                                       AnchorKind anchorKind = AnchorKind.None, uint anchorSerial = 0,
                                       ushort anchorX = 0, ushort anchorY = 0, sbyte anchorZ = 0,
                                       short anchorOffsetX = 0, short anchorOffsetY = 0, int anchorGraceMs = 0)
        {
            // One anchor = one timer: adding a Serial-anchored timer purges any
            // OTHER entry pinned to the same serial (a same-id update restarts in place).
            if (anchorKind == AnchorKind.Serial)
            {
                _purgeScratch.Clear();
                foreach (var kv in _timers)
                    if (kv.Key != id && kv.Value.AnchorKind == AnchorKind.Serial && kv.Value.AnchorSerial == anchorSerial)
                        _purgeScratch.Add(kv.Key);
                foreach (int pid in _purgeScratch)
                    _timers.Remove(pid);
            }

            int order;
            if (_timers.TryGetValue(id, out var existing))
                order = existing.Order;          // update in place, keep stack slot
            else
            {
                _nextOrderByGroup.TryGetValue(groupId, out order);
                _nextOrderByGroup[groupId] = order + 1;
            }

            _timers[id] = new ScreenTimerEntry
            {
                Id = id,
                Shape = shape,
                StartTicks = now,
                DurationMs = durationMs,
                Hue = hue,
                GroupId = groupId,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Label = label ?? string.Empty,
                ShowTime = showTime,
                Order = order,
                AnchorKind = anchorKind,
                AnchorSerial = anchorSerial,
                AnchorX = anchorX,
                AnchorY = anchorY,
                AnchorZ = anchorZ,
                AnchorOffsetX = anchorOffsetX,
                AnchorOffsetY = anchorOffsetY,
                AnchorGraceMs = anchorGraceMs <= 0 ? DefaultGraceMs : anchorGraceMs,
                MissingSinceTicks = 0,
            };
        }

        public static void SetMissingSince(int id, long ticks)
        {
            if (_timers.TryGetValue(id, out var e))
            {
                e.MissingSinceTicks = ticks;
                _timers[id] = e;
            }
        }
```

Add the scratch list next to the other static fields (after `_nextOrderByGroup`, line 61):

```csharp
        private static readonly List<int> _purgeScratch = new List<int>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ScreenTimersAnchorTests"`
Expected: PASS (4 tests). Also run the existing suite to confirm no regressions:
Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginTimersManagerTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/ScreenTimers.cs tests/ClassicUO.UnitTests/Game/Managers/ScreenTimersAnchorTests.cs
git commit -m "feat(timers): anchor fields + replace-by-anchor in ScreenTimers"
```

---

### Task 2: Lost-anchor grace pass in `PluginTimersManager`

Adds the authoritative per-frame grace/removal for anchored timers, plus a new
removal reason. Runs from `World.Update` so it works regardless of the gump.

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginTimersManager.cs`
- Modify: `src/ClassicUO.Client/Game/World.cs:284`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/PluginTimersManagerTests.cs`

**Interfaces:**
- Consumes: `ScreenTimers.AddOrUpdate(...)` anchor params, `ScreenTimers.SetMissingSince`, `ScreenTimers.Entries` (Task 1).
- Produces:
  - `const int PluginTimersManager.ReasonAnchorLost = 3;`
  - `internal static Func<uint, bool> SerialResolver;` (test seam; `null` → real `World` lookup).
  - `void PluginTimersManager.Update(World world, long now)` (new overload); existing `Update(long now)` delegates with `world: null`.

- [ ] **Step 1: Write the failing test**

Append to `tests/ClassicUO.UnitTests/Game/Managers/PluginTimersManagerTests.cs` (inside the class):

```csharp
        [Fact]
        public void Update_SerialAnchorMissingBeyondGrace_RemovesWithAnchorLost()
        {
            var events = new List<(int id, int reason)>();
            PluginTimersManager.TimerEventSink = (id, reason) => events.Add((id, reason));
            PluginTimersManager.SerialResolver = _ => false; // always missing

            ScreenTimers.AddOrUpdate(7, TimerShape.Bar, 60000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000, anchorGraceMs: 1000);

            PluginTimersManager.Update(now: 500);   // within grace: still alive
            Assert.True(ScreenTimers.Entries.ContainsKey(7));
            Assert.Empty(events);

            PluginTimersManager.Update(now: 1500);  // grace elapsed: removed
            Assert.False(ScreenTimers.Entries.ContainsKey(7));
            Assert.Equal(new[] { (7, PluginTimersManager.ReasonAnchorLost) }, events);
        }

        [Fact]
        public void Update_SerialAnchorReappearsWithinGrace_ResetsAndKeeps()
        {
            var events = new List<(int id, int reason)>();
            PluginTimersManager.TimerEventSink = (id, reason) => events.Add((id, reason));

            ScreenTimers.AddOrUpdate(7, TimerShape.Bar, 60000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Serial, anchorSerial: 0x4000, anchorGraceMs: 1000);

            PluginTimersManager.SerialResolver = _ => false;
            PluginTimersManager.Update(now: 500);   // marks missing
            Assert.Equal(500, ScreenTimers.Entries[7].MissingSinceTicks);

            PluginTimersManager.SerialResolver = _ => true; // back
            PluginTimersManager.Update(now: 700);
            Assert.Equal(0, ScreenTimers.Entries[7].MissingSinceTicks);
            Assert.True(ScreenTimers.Entries.ContainsKey(7));
            Assert.Empty(events);
        }

        [Fact]
        public void Update_AbsoluteAnchor_NeverLost()
        {
            var events = new List<(int id, int reason)>();
            PluginTimersManager.TimerEventSink = (id, reason) => events.Add((id, reason));
            PluginTimersManager.SerialResolver = _ => false;

            ScreenTimers.AddOrUpdate(8, TimerShape.Bar, 60000, 0, 0, 0, 0, 0, 0, null, false,
                now: 0, anchorKind: AnchorKind.Absolute, anchorX: 100, anchorY: 100, anchorGraceMs: 1000);

            PluginTimersManager.Update(now: 5000);
            Assert.True(ScreenTimers.Entries.ContainsKey(8));
            Assert.Empty(events);
        }
```

Also update the reset in the test constructor so the seam never leaks between
tests — replace `public PluginTimersManagerTests() => PluginTimersManager.Reset();`
with:

```csharp
        public PluginTimersManagerTests()
        {
            PluginTimersManager.Reset();
            PluginTimersManager.SerialResolver = null;
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginTimersManagerTests"`
Expected: FAIL — compile error (`ReasonAnchorLost`, `SerialResolver` undefined).

- [ ] **Step 3: Add the reason, seam, and grace pass**

In `src/ClassicUO.Client/Game/Managers/PluginTimersManager.cs`:

Add the reason constant next to the others (after line 20):

```csharp
        public const int ReasonAnchorLost = 3;
```

Add the resolver seam next to the other seams (after `TimerEventSink`, line 24):

```csharp
        // Test seam; when null, serial resolution uses the live World.
        internal static Func<uint, bool> SerialResolver;
```

Add a scratch list next to `_expiredScratch` (line 29):

```csharp
        private static readonly List<int> _lostScratch = new List<int>();
```

Replace the existing `Update(long now)` (lines 31-52) with an overload pair:

```csharp
        public static void Update(long now) => Update(null, now);

        public static void Update(World world, long now)
        {
            _expiredScratch.Clear();
            PluginBuffs.CollectExpired(now, _expiredScratch);
            if (_expiredScratch.Count > 0)
            {
                foreach (int id in _expiredScratch)
                {
                    PluginBuffs.Remove(id);
                    RaiseBuffEvent(id, ReasonExpired);
                }
                GumpRefresh?.Invoke();
            }

            _expiredScratch.Clear();
            ScreenTimers.CollectExpired(now, _expiredScratch);
            foreach (int id in _expiredScratch)
            {
                ScreenTimers.Remove(id);
                RaiseTimerEvent(id, ReasonExpired);
            }

            UpdateAnchors(world, now);
        }

        // Lost-anchor grace for Serial/Self timers. Off-screen is NOT handled here
        // (that is a render-only concern); this only fires when the anchor entity
        // no longer exists in the world. Absolute anchors are never lost.
        private static void UpdateAnchors(World world, long now)
        {
            _lostScratch.Clear();

            foreach (var kv in ScreenTimers.Entries)
            {
                var e = kv.Value;
                if (e.AnchorKind != AnchorKind.Serial && e.AnchorKind != AnchorKind.Self)
                    continue;

                bool resolvable = e.AnchorKind == AnchorKind.Self
                    ? ResolvePlayer(world)
                    : ResolveSerial(world, e.AnchorSerial);

                if (resolvable)
                {
                    if (e.MissingSinceTicks != 0)
                        ScreenTimers.SetMissingSince(e.Id, 0);
                    continue;
                }

                if (e.MissingSinceTicks == 0)
                {
                    ScreenTimers.SetMissingSince(e.Id, now);
                }
                else if (now - e.MissingSinceTicks >= e.AnchorGraceMs)
                {
                    _lostScratch.Add(e.Id);
                }
            }

            foreach (int id in _lostScratch)
            {
                ScreenTimers.Remove(id);
                RaiseTimerEvent(id, ReasonAnchorLost);
            }
        }

        private static bool ResolveSerial(World world, uint serial)
            => SerialResolver != null ? SerialResolver(serial) : world?.Get(serial) != null;

        private static bool ResolvePlayer(World world)
            => SerialResolver != null ? SerialResolver(0) : world?.Player != null;
```

Update `Reset()` (line 71-78) to also clear the new seam — add `SerialResolver = null;` inside it.

Add `using ClassicUO.Game;` if `World` is not already resolvable in this file
(it is in namespace `ClassicUO.Game.Managers`, so `World` is visible without a
new using — verify by build).

- [ ] **Step 4: Pass the live `World` from `World.Update`**

In `src/ClassicUO.Client/Game/World.cs:284`, change:

```csharp
            Managers.PluginTimersManager.Update(Time.Ticks);
```

to:

```csharp
            Managers.PluginTimersManager.Update(this, Time.Ticks);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginTimersManagerTests"`
Expected: PASS (all, including the 3 new anchor tests and the unchanged expiry tests).

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginTimersManager.cs src/ClassicUO.Client/Game/World.cs tests/ClassicUO.UnitTests/Game/Managers/PluginTimersManagerTests.cs
git commit -m "feat(timers): lost-anchor grace pass + ReasonAnchorLost"
```

---

### Task 3: Pure absolute-tile → world-pixel helper

Isolate the one piece of anchor math that is unit-testable (absolute tile to
isometric world pixel) into a pure static helper the render path calls.

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/ScreenTimers.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/ScreenTimersAnchorTests.cs`

**Interfaces:**
- Produces: `static (int wx, int wy) ScreenTimers.TileToWorldPixel(int x, int y, int z)` returning `((x - y) * 22, (x + y) * 22 - (z << 2))` — the same isometric formula `GameObject.UpdateRealScreenPosition` uses (`GameObject.cs:150-154`).

- [ ] **Step 1: Write the failing test**

Append to `ScreenTimersAnchorTests.cs`:

```csharp
        [Theory]
        [InlineData(0, 0, 0, 0, 0)]
        [InlineData(1, 0, 0, 22, 22)]
        [InlineData(0, 1, 0, -22, 22)]
        [InlineData(10, 10, 5, 0, 420)] // (10+10)*22 - (5<<2) = 440 - 20 = 420
        public void TileToWorldPixel_MatchesIsoFormula(int x, int y, int z, int wx, int wy)
        {
            var (gx, gy) = ScreenTimers.TileToWorldPixel(x, y, z);
            Assert.Equal(wx, gx);
            Assert.Equal(wy, gy);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TileToWorldPixel"`
Expected: FAIL — `TileToWorldPixel` not defined.

- [ ] **Step 3: Add the helper**

In `ScreenTimers.cs`, add inside the class:

```csharp
        /// <summary>
        /// Absolute tile (x,y,z) → isometric world pixel, matching
        /// GameObject.UpdateRealScreenPosition. The camera/draw-offset transform
        /// to screen space is applied by the render path.
        /// </summary>
        public static (int wx, int wy) TileToWorldPixel(int x, int y, int z)
            => ((x - y) * 22, (x + y) * 22 - (z << 2));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TileToWorldPixel"`
Expected: PASS (4 cases).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/ScreenTimers.cs tests/ClassicUO.UnitTests/Game/Managers/ScreenTimersAnchorTests.cs
git commit -m "feat(timers): pure tile->world-pixel anchor helper"
```

---

### Task 4: Anchor resolution + off-screen cull in `ScreenTimersGump`

Render path: for anchored entries, resolve a screen position each frame, cull
against camera bounds, and draw with the existing shape renderer. Non-anchored
entries keep their current fixed/group layout. This task is verified by build +
in-game observation (render math is not unit-testable without a live scene).

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/ScreenTimersGump.cs`

**Interfaces:**
- Consumes: `ScreenTimerEntry` anchor fields (Task 1), `ScreenTimers.TileToWorldPixel` (Task 3), the gump's `World` property (base `Gump`), `Client.Game.Scene.Camera`.
- Produces: no new public surface; changes `AddToRenderLists` behavior only.

- [ ] **Step 1: Add the anchor branch to `AddToRenderLists`**

In `ScreenTimersGump.cs`, replace the body of the `foreach (var kv in ScreenTimers.Entries)` loop (lines 66-82) with:

```csharp
            foreach (var kv in ScreenTimers.Entries)
            {
                var e = kv.Value;

                int px, py;

                if (e.AnchorKind != AnchorKind.None)
                {
                    if (!TryResolveAnchorScreen(in e, out px, out py))
                    {
                        continue; // anchor missing or off-screen: hide, keep counting
                    }
                }
                else
                {
                    StackDirection dir = StackDirection.Down;
                    TimerGroup group = default;
                    if (e.GroupId != 0 && ScreenTimers.TryGetGroup(e.GroupId, out group))
                    {
                        dir = group.Direction;
                    }

                    int extent = ScreenTimers.DefaultExtent(e.Shape, dir, e.Width, e.Height);
                    (px, py) = ScreenTimers.ComputePosition(e, group, extent);
                }

                float remaining = ScreenTimers.RemainingFraction(e, now);
                DrawEntry(renderLists, in e, px, py, remaining, depth, now);
            }
```

- [ ] **Step 2: Add the `TryResolveAnchorScreen` helper**

Add this method to `ScreenTimersGump` (near `DrawEntry`). It follows the
`NameOverheadGump.AddToRenderLists` recipe (`NameOverheadGump.cs:561-648`) and
derives the current draw offset from the player for the Absolute case:

```csharp
        // Resolves an anchored timer to a top-left screen pixel, or returns false
        // when the anchor entity is gone or the placement is outside the camera.
        // World-space pixel math mirrors NameOverheadGump / GameObject; the camera
        // transform + bounds cull mirror HealthLinesManager.
        private bool TryResolveAnchorScreen(in ScreenTimerEntry e, out int outX, out int outY)
        {
            outX = 0;
            outY = 0;

            int wx, wy;

            switch (e.AnchorKind)
            {
                case AnchorKind.Serial when SerialHelper.IsMobile(e.AnchorSerial):
                case AnchorKind.Self:
                {
                    Mobile m = e.AnchorKind == AnchorKind.Self
                        ? World.Player
                        : World.Mobiles.Get(e.AnchorSerial);
                    if (m == null)
                        return false;

                    Client.Game.UO.Animations.GetAnimationDimensions(
                        m.AnimIndex, m.GetGraphicForAnimation(), 0, 0, m.IsMounted, 0,
                        out int _, out int centerY, out int _, out int height);

                    wx = (int)(m.RealScreenPosition.X + m.Offset.X + 22);
                    wy = (int)(m.RealScreenPosition.Y + (m.Offset.Y - m.Offset.Z)
                             - (height + centerY + 8 + 22)
                             + (m.IsGargoyle && m.IsFlying ? -22 : !m.IsMounted ? 22 : 0));
                    break;
                }

                case AnchorKind.Serial: // item
                {
                    Item item = World.Items.Get(e.AnchorSerial);
                    if (item == null)
                        return false;

                    var bounds = Client.Game.UO.Arts.GetRealArtBounds(item.Graphic);
                    wx = item.RealScreenPosition.X + (int)item.Offset.X + 22;
                    wy = item.RealScreenPosition.Y + (int)(item.Offset.Y - item.Offset.Z)
                         - (bounds.Height >> 1);
                    break;
                }

                case AnchorKind.Absolute:
                {
                    // Derive the current draw offset from the player, whose world
                    // pixel and RealScreenPosition are both known this frame:
                    //   RealScreenPos = worldPixel - drawOffset - 22   (GameObject.cs:150-154)
                    Mobile player = World.Player;
                    if (player == null)
                        return false;

                    var (pwx, pwy) = ScreenTimers.TileToWorldPixel(player.X, player.Y, player.Z);
                    float offX = pwx - player.RealScreenPosition.X - 22;
                    float offY = pwy - player.RealScreenPosition.Y - 22;

                    var (twx, twy) = ScreenTimers.TileToWorldPixel(e.AnchorX, e.AnchorY, e.AnchorZ);
                    wx = (int)(twx - offX - 22);
                    wy = (int)(twy - offY - 22);
                    break;
                }

                default:
                    return false;
            }

            (int w, int h) = ScreenTimers.DefaultSize(e.Shape);
            if (e.Width > 0) w = e.Width;
            if (e.Height > 0) h = e.Height;

            var camera = Client.Game.Scene.Camera;
            Point p = camera.WorldToScreen(new Point(wx, wy));

            // Center horizontally on the anchor, sit above it, then apply nudge.
            int sx = p.X - (w >> 1) + e.AnchorOffsetX + camera.Bounds.X;
            int sy = p.Y + e.AnchorOffsetY + camera.Bounds.Y;

            if (sx < camera.Bounds.X || sx + w > camera.Bounds.Right)
                return false;
            if (sy < camera.Bounds.Y || sy + h > camera.Bounds.Bottom)
                return false;

            outX = sx;
            outY = sy;
            return true;
        }
```

Add the needed usings at the top of the file if missing:
`using ClassicUO.Game;` (for `SerialHelper`), and confirm `ClassicUO.Game.GameObjects` (already present, line 6) covers `Mobile`/`Item`.

- [ ] **Step 3: Build the client**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: build succeeds, no errors.

- [ ] **Step 4: Run the full client test suite (no regressions)**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/ScreenTimersGump.cs
git commit -m "feat(timers): resolve + cull anchored timers in overlay"
```

---

### Task 5: Extend client ABI command (`AddTimer` + `PluginHost` delegate)

Widen the `AddTimer` binding so the plugin host can pass anchor scalars, keeping
the C# delegate and the manager method in lockstep. (Host-side wiring is Task 6.)

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginTimersManager.cs:113-118`
- Modify: `src/ClassicUO.Client/PluginHost.cs:315-317` (delegate + field), and the `AddTimerFn` comment at `:58`

**Interfaces:**
- Consumes: `ScreenTimers.AddOrUpdate(...)` anchor params (Task 1).
- Produces: new ABI for `AddTimerFn`:
  `void(int id, int shape, int durationMs, ushort hue, int groupId, int x, int y, int width, int height, IntPtr labelUtf8, byte showTime, int anchorKind, uint anchorSerial, ushort ax, ushort ay, sbyte az, short offX, short offY, int graceMs)`

- [ ] **Step 1: Widen `PluginTimersManager.AddTimer`**

Replace `AddTimer` (lines 113-118) with:

```csharp
        public static void AddTimer(int id, int shape, int durationMs, ushort hue, int groupId,
                                    int x, int y, int width, int height, IntPtr labelUtf8, byte showTime,
                                    int anchorKind, uint anchorSerial, ushort ax, ushort ay, sbyte az,
                                    short offX, short offY, int graceMs)
        {
            ScreenTimers.AddOrUpdate(id, (TimerShape)shape, durationMs, hue, groupId,
                x, y, width, height, PtrToString(labelUtf8), showTime != 0, Time.Ticks,
                (AnchorKind)anchorKind, anchorSerial, ax, ay, az, offX, offY, graceMs);
        }
```

- [ ] **Step 2: Widen the `PluginHost` delegate + field comment**

In `src/ClassicUO.Client/PluginHost.cs`, replace the `dAddTimer` delegate
declaration (line 315) with:

```csharp
        delegate void dAddTimer(int id, int shape, int durationMs, ushort hue, int groupId, int x, int y, int width, int height, IntPtr labelUtf8, byte showTime, int anchorKind, uint anchorSerial, ushort ax, ushort ay, sbyte az, short offX, short offY, int graceMs);
```

(The field assignment at line 317, `_addTimer = ...PluginTimersManager.AddTimer`,
now binds to the widened method — no change needed there.)

Update the descriptive comment at line 58 to match:

```csharp
        public IntPtr /*delegate*<int, int, int, ushort, int, int, int, int, int, IntPtr, byte, int, uint, ushort, ushort, sbyte, short, short, int, void>*/ AddTimerFn;
```

- [ ] **Step 3: Build the client**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: build succeeds (delegate arity matches the widened method).

- [ ] **Step 4: Run tests (existing manager tests still pass)**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginTimersManagerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginTimersManager.cs src/ClassicUO.Client/PluginHost.cs
git commit -m "feat(timers): widen AddTimer ABI with anchor scalars"
```

---

### Task 6: Host + PluginApi surface (`TimerConfig`, `AnchorKind`, `AnchorLost`)

Complete the public contract and the host marshaling so plugins can set anchors
and receive the `AnchorLost` removal reason.

**Files:**
- Modify: `src/ClassicUO.PluginApi/IScreenTimers.cs`
- Modify: `src/ClassicUO.BootstrapHost/HostBridge.cs:366` (ABI comment) and the `ClientBindings` decl area
- Modify: `src/ClassicUO.BootstrapHost/PluginContextImpl.cs:320-356`
- Modify: `src/ClassicUO.PluginApi/README.md`

**Interfaces:**
- Consumes: the widened `AddTimerFn` ABI (Task 5).
- Produces:
  - `enum AnchorKind { None = 0, Serial = 1, Absolute = 2, Self = 3 }` in `ClassicUO.PluginApi`.
  - `TimerRemoveReason.AnchorLost` (value `3`).
  - New `TimerConfig` init properties: `AnchorKind Anchor; uint AnchorSerial; ushort AnchorX; ushort AnchorY; sbyte AnchorZ; short AnchorOffsetX; short AnchorOffsetY; int AnchorGraceMs;`

- [ ] **Step 1: Extend the PluginApi contract**

In `src/ClassicUO.PluginApi/IScreenTimers.cs`:

Add after `enum StackDirection` (line 11):

```csharp
/// <summary>What an anchored timer follows. None = fixed screen position / group.</summary>
public enum AnchorKind { None = 0, Serial = 1, Absolute = 2, Self = 3 }
```

Add `AnchorLost` to `TimerRemoveReason` (line 17):

```csharp
public enum TimerRemoveReason { Expired, RemovedByPlugin, RemovedByUser, AnchorLost }
```

Append to `TimerConfig` (after `ShowTime`, line 32):

```csharp
    public AnchorKind Anchor { get; init; }         // None => fixed X/Y or group
    public uint AnchorSerial { get; init; }         // Serial kind: mobile/item
    public ushort AnchorX { get; init; }            // Absolute kind: tile
    public ushort AnchorY { get; init; }
    public sbyte AnchorZ { get; init; }
    public short AnchorOffsetX { get; init; }        // pixel nudge from anchor
    public short AnchorOffsetY { get; init; }
    public int AnchorGraceMs { get; init; }          // Serial/Self lost-grace; 0 => host default (5000)
```

- [ ] **Step 2: Widen the host ABI declaration**

In `src/ClassicUO.BootstrapHost/HostBridge.cs`, update the `AddTimerFn` ABI
comment (line 366) to:

```csharp
    public nint AddTimerFn;            // void(int id, int shape, int durationMs, ushort hue, int groupId, int x, int y, int width, int height, nint labelUtf8, byte showTime, int anchorKind, uint anchorSerial, ushort ax, ushort ay, sbyte az, short offX, short offY, int graceMs)
```

(`AddTimerFn` is an opaque `nint`; only the call-site signature in
`PluginContextImpl` needs to change — next step.)

- [ ] **Step 3: Marshal the anchor fields in `ScreenTimersImpl.AddOrUpdate`**

In `src/ClassicUO.BootstrapHost/PluginContextImpl.cs`, replace `AddOrUpdate`
(lines 337-356) with:

```csharp
    public unsafe void AddOrUpdate(TimerConfig timer)
    {
        var fn = _bridge.ClientBindings.AddTimerFn;
        if (fn == 0 || timer is null) return;

        int id = timer.Id, shape = (int)timer.Shape, dur = timer.DurationMs, gid = timer.GroupId;
        ushort hue = timer.Hue;
        int x = timer.X, y = timer.Y, w = timer.Width, h = timer.Height;
        byte showTime = timer.ShowTime ? (byte)1 : (byte)0;
        int anchorKind = (int)timer.Anchor;
        uint anchorSerial = timer.AnchorSerial;
        ushort ax = timer.AnchorX, ay = timer.AnchorY;
        sbyte az = timer.AnchorZ;
        short offX = timer.AnchorOffsetX, offY = timer.AnchorOffsetY;
        int graceMs = timer.AnchorGraceMs;
        nint labelPtr = string.IsNullOrEmpty(timer.Label) ? nint.Zero : Marshal.StringToHGlobalAnsi(timer.Label);

        void Call()
        {
            try
            {
                ((delegate* unmanaged[Cdecl]<int, int, int, ushort, int, int, int, int, int, nint, byte, int, uint, ushort, ushort, sbyte, short, short, int, void>)fn)(
                    id, shape, dur, hue, gid, x, y, w, h, labelPtr, showTime,
                    anchorKind, anchorSerial, ax, ay, az, offX, offY, graceMs);
            }
            finally { if (labelPtr != nint.Zero) Marshal.FreeHGlobal(labelPtr); }
        }

        if (_bridge.IsGameThread) Call();
        else _bridge.PostToGameThread(Call);
    }
```

The `RaiseEvent` mapping (lines 320-326) already casts `reason` to
`TimerRemoveReason`, so `ReasonAnchorLost` (3) maps to `TimerRemoveReason.AnchorLost`
automatically — no change needed there.

- [ ] **Step 4: Build the host**

Run: `dotnet build src/ClassicUO.BootstrapHost/ClassicUO.BootstrapHost.csproj -c Debug`
Expected: build succeeds.

- [ ] **Step 5: Document the anchor usage**

In `src/ClassicUO.PluginApi/README.md`, find the screen-timers section and add a
short subsection. Insert this Markdown (adjust the surrounding heading level to
match the file):

```markdown
#### Anchoring a timer to the world

Set `TimerConfig.Anchor` to pin a timer to an in-game target instead of a fixed
screen position:

- `AnchorKind.Serial` + `AnchorSerial` — follow a mobile or item.
- `AnchorKind.Absolute` + `AnchorX/Y/Z` — pin to a map tile.
- `AnchorKind.Self` — follow the player.

While the anchor is off-screen the timer is hidden but keeps counting, and
reappears when the anchor scrolls back into view. If a `Serial`/`Self` anchor is
destroyed, the timer keeps running for `AnchorGraceMs` (default 5000 ms); if the
anchor does not return in time the timer is removed and `Removed` fires with
`TimerRemoveReason.AnchorLost`. Only one Serial-anchored timer may target a given
serial — adding another replaces it. Anchored timers ignore `GroupId`.

Example:

    ctx.ScreenTimers.AddOrUpdate(new TimerConfig
    {
        Id = 1,
        Shape = TimerShape.Bar,
        DurationMs = 15000,
        Label = "Poison",
        ShowTime = true,
        Anchor = AnchorKind.Serial,
        AnchorSerial = mobileSerial,
        AnchorOffsetY = -4,
    });
```

- [ ] **Step 6: Build the whole solution + run all tests**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds across client, host, and PluginApi.
Run: `dotnet test tests/ClassicUO.UnitTests` and `dotnet test tests/ClassicUO.BootstrapHost.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ClassicUO.PluginApi/IScreenTimers.cs src/ClassicUO.PluginApi/README.md src/ClassicUO.BootstrapHost/HostBridge.cs src/ClassicUO.BootstrapHost/PluginContextImpl.cs
git commit -m "feat(timers): plugin API anchor config + AnchorLost reason"
```

---

### Task 7: In-game verification

Manual end-to-end check that the render + grace behavior works against a live
world (the parts that cannot be unit-tested).

**Files:** none (verification only).

- [ ] **Step 1: Build the run artifacts**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: success.

- [ ] **Step 2: Exercise with the sample plugin**

Using `samples/HelloPlugin` (or a scratch v2 plugin), add a Serial-anchored
timer on a nearby mobile via `ctx.ScreenTimers.AddOrUpdate(...)` with
`Anchor = AnchorKind.Serial` and the mobile's serial. Launch the client via
`ClassicUO.BootstrapHost` against a legally-obtained UO data dir.

- [ ] **Step 3: Observe the four behaviors**

Confirm each:
1. Timer bar/countdown renders above the mobile and follows it as it moves.
2. Scroll the mobile off-screen (walk away) → timer disappears; walk back before
   expiry → timer reappears with the correct reduced remaining time (it kept
   counting).
3. Kill/remove the mobile → after `AnchorGraceMs` the timer vanishes and the
   plugin's `Removed` handler fires with `TimerRemoveReason.AnchorLost`.
4. An `AnchorKind.Absolute` timer stays pinned to its tile as the camera scrolls,
   and hides/reappears at the screen edge.

- [ ] **Step 4: Record the result**

Note pass/fail per behavior in the PR description. If any fail, debug with
superpowers:systematic-debugging before merging.

---

## Notes for the implementer

- The `[Collection("PluginManagerState")]` attribute serializes the timer tests
  so the shared static `ScreenTimers`/`PluginTimersManager` state does not race.
  Keep new test classes in that collection.
- `World.Get(uint)` returns a mobile-or-item (or null if destroyed) —
  `PluginTimersManager` uses it only to test existence; the gump uses the typed
  `World.Mobiles.Get` / `World.Items.Get` because it needs animation/art bounds.
- Do not add persistence: the overlay is `GumpType.None` on purpose.
