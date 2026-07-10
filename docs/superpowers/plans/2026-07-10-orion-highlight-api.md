# OrionUO-style plugin highlighting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose OrionUO-style `AddHighlightArea`/`AddHighlightCharacter` highlighting through the plugin v2 API (`IHighlight` on `IPluginContext`), rendered by injecting hue overrides into the client's existing per-object/per-mesh hue computation — no new draw calls, no spatial index.

**Architecture:** Two independent static stores in `Game/Managers/PluginHighlights.cs` (`PluginHighlightCharacters`: two-tier `Dictionary<uint, ushort>`; `PluginHighlightAreas`: `Dictionary<string, AreaEntry>` with linear-scan matching). A `PluginHighlights` facade is bound into the same `ClientBindings` function-pointer ABI used by `IStatusBars`/`IScreenTimers` (`HostBridge.cs` ↔ `PluginHost.cs`). Rendering hooks into five existing hue-computation sites: `MobileView.Draw`, `ItemView.Draw` (ground items + corpses), `LandView.Draw`, `StaticView.Draw`, `MultiView.Draw` (non-meshed path), and `GameSceneDrawingSorting.ApplyMeshHue` (meshed land/static/multi path).

**Tech Stack:** C# / .NET 10, xUnit, FluentAssertions (BootstrapHost.Tests only), unsafe function pointers for the native ABI.

## Global Constraints

- Hue type is `ushort` (hue-table index), never RGB — matches `IStatusBars`/`IScreenTimers` convention.
- `ClientBindings` struct field order is a **shared memory layout** between `src/ClassicUO.BootstrapHost/HostBridge.cs` and `src/ClassicUO.Client/PluginHost.cs` — new fields MUST be appended at the exact same relative position in both, or the BootstrapHost side reads garbage/zero function pointers. New fields go immediately after `ClearTimersFn` in both structs (see Task 5/6).
- All plugin-facing mutation methods (`AddArea`, `RemoveArea`, `ClearAreas`, `AddCharacter`, `RemoveCharacter`, `ClearCharacters`) auto-marshal to the game thread via `HostBridge.PostToGameThread`, matching `IStatusBars`. `GetAreaTimer` is a direct synchronous read (no marshaling), matching `IGameActions.TryGetPlayerPosition`.
- No spatial index for areas — linear scan over the active-area dictionary per queried tile/object (see spec `docs/superpowers/specs/2026-07-10-orion-highlight-api-design.md`, corrected during planning).
- Design source of truth: `docs/superpowers/specs/2026-07-10-orion-highlight-api-design.md` (as corrected).

---

## Task 1: PluginApi surface — `IHighlight`

**Files:**
- Create: `src/ClassicUO.PluginApi/IHighlight.cs`
- Modify: `src/ClassicUO.PluginApi/IPluginContext.cs`

**Interfaces:**
- Produces: `HighlightSnap` enum, `HighlightObjectTypes` flags enum, `IHighlight` interface — consumed by every later task.

This task is pure interface/enum definition with no logic, so there is no red/green test cycle — verification is a successful build.

- [ ] **Step 1: Create `IHighlight.cs`**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace ClassicUO.PluginApi;

/// <summary>What an area highlight's center follows.</summary>
public enum HighlightSnap { Mouse, Position, Serial }

/// <summary>Object categories an area highlight can tint. Combine with |.</summary>
[Flags]
public enum HighlightObjectTypes
{
    None = 0,
    Mobile = 1 << 0,
    Item = 1 << 1,
    Corpse = 1 << 2,
    Land = 1 << 3,
    Static = 1 << 4,
    Multi = 1 << 5,
    All = Mobile | Item | Corpse | Land | Static | Multi
}

/// <summary>
/// Plugin-driven object/area highlighting (OrionUO parity). Character
/// highlighting persists until explicitly removed (no timer, matching
/// AddHighlightCharacter). Area highlighting supports a duration and follows
/// the mouse, a fixed position, or a serial. All mutating methods auto-marshal
/// to the game thread; <see cref="GetAreaTimer"/> is a direct synchronous read.
/// </summary>
public interface IHighlight
{
    /// <summary>
    /// Adds or replaces the area highlight <paramref name="id"/>. When two
    /// areas overlap the same tile, the most recently added one wins.
    /// </summary>
    /// <param name="id">Area id; re-adding the same id replaces it.</param>
    /// <param name="durationMs">Lifetime in ms; -1 (default) never expires.</param>
    /// <param name="snap">What the area's center follows.</param>
    /// <param name="anchorSerial">Used when <paramref name="snap"/> is Serial.</param>
    /// <param name="hue">Highlight hue-table index.</param>
    /// <param name="rangeX">Half-width of the area along X.</param>
    /// <param name="rangeY">Half-height of the area along Y.</param>
    /// <param name="objectTypes">Which object categories this area tints.</param>
    /// <param name="x">Used when <paramref name="snap"/> is Position.</param>
    /// <param name="y">Used when <paramref name="snap"/> is Position.</param>
    void AddArea(
        string id,
        int durationMs = -1,
        HighlightSnap snap = HighlightSnap.Mouse,
        uint anchorSerial = 0,
        ushort hue = 0x0386,
        int rangeX = 3,
        int rangeY = 3,
        HighlightObjectTypes objectTypes = HighlightObjectTypes.All,
        int x = 0,
        int y = 0
    );

    /// <summary>Removes the area highlight with <paramref name="id"/> if present.</summary>
    void RemoveArea(string id);

    /// <summary>Removes every area highlight owned by this plugin.</summary>
    void ClearAreas();

    /// <summary>
    /// Remaining lifetime in ms for area <paramref name="id"/>. Returns 0 if the
    /// id doesn't exist or has expired; <see cref="int.MaxValue"/> if it never expires.
    /// </summary>
    int GetAreaTimer(string id);

    /// <summary>
    /// Tints mobile <paramref name="serial"/> with <paramref name="hue"/>.
    /// <paramref name="priorityHighlight"/> true always wins over the client's
    /// own status coloring (poison/paralyze/invul/attacked/notoriety); false
    /// loses to an active status color but wins over the default hue.
    /// </summary>
    void AddCharacter(uint serial, ushort hue, bool priorityHighlight = false);

    /// <summary>Removes the character highlight for <paramref name="serial"/> in the given tier.</summary>
    void RemoveCharacter(uint serial, bool priorityHighlight = false);

    /// <summary>Removes every character highlight this plugin owns in the given tier.</summary>
    void ClearCharacters(bool priorityHighlight = false);
}
```

- [ ] **Step 2: Add `Highlight` to `IPluginContext`**

In `src/ClassicUO.PluginApi/IPluginContext.cs`, add after the `ScreenTimers` property (after line 42):

```csharp
    /// <summary>Plugin-driven object/area highlighting.</summary>
    IHighlight Highlight { get; }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.PluginApi/ClassicUO.PluginApi.csproj -c Debug`
Expected: Build succeeded (0 errors). `IPluginContext` will show an "interface not implemented" error in `ClassicUO.BootstrapHost` until Task 5 — that's expected at this point; build only `ClassicUO.PluginApi` in isolation for this step.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.PluginApi/IHighlight.cs src/ClassicUO.PluginApi/IPluginContext.cs
git commit -m "feat(plugin): add IHighlight plugin API surface"
```

---

## Task 2: Character highlight store — `PluginHighlightCharacters`

**Files:**
- Create: `src/ClassicUO.Client/Game/Managers/PluginHighlights.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/PluginHighlightCharactersTests.cs`

**Interfaces:**
- Produces: `internal static class PluginHighlightCharacters` with `Set(uint serial, ushort hue, bool priorityHighlight)`, `Remove(uint serial, bool priorityHighlight)`, `ClearAll(bool priorityHighlight)`, `TryResolve(uint serial, bool statusOverrideActive, out ushort hue)`, `Reset()`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClassicUO.UnitTests/Game/Managers/PluginHighlightCharactersTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class PluginHighlightCharactersTests
    {
        public PluginHighlightCharactersTests() => PluginHighlightCharacters.Reset();

        [Fact]
        public void TryResolve_ReturnsFalse_WhenNothingSet()
        {
            bool found = PluginHighlightCharacters.TryResolve(0x1234, statusOverrideActive: false, out ushort hue);
            Assert.False(found);
            Assert.Equal((ushort)0, hue);
        }

        [Fact]
        public void TryResolve_NormalTier_WinsWhenNoStatusOverride()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0021, priorityHighlight: false);

            bool found = PluginHighlightCharacters.TryResolve(0x1234, statusOverrideActive: false, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0021, hue);
        }

        [Fact]
        public void TryResolve_NormalTier_LosesToActiveStatusOverride()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0021, priorityHighlight: false);

            bool found = PluginHighlightCharacters.TryResolve(0x1234, statusOverrideActive: true, out ushort hue);

            Assert.False(found);
        }

        [Fact]
        public void TryResolve_PriorityTier_WinsEvenWithActiveStatusOverride()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0055, priorityHighlight: true);

            bool found = PluginHighlightCharacters.TryResolve(0x1234, statusOverrideActive: true, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0055, hue);
        }

        [Fact]
        public void TryResolve_PriorityTier_TakesPrecedenceOverNormalTier()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0021, priorityHighlight: false);
            PluginHighlightCharacters.Set(0x1234, 0x0055, priorityHighlight: true);

            bool found = PluginHighlightCharacters.TryResolve(0x1234, statusOverrideActive: false, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0055, hue);
        }

        [Fact]
        public void Remove_RemovesOnlyMatchingTier()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0021, priorityHighlight: false);
            PluginHighlightCharacters.Set(0x1234, 0x0055, priorityHighlight: true);

            PluginHighlightCharacters.Remove(0x1234, priorityHighlight: false);

            Assert.True(PluginHighlightCharacters.TryResolve(0x1234, false, out ushort hue));
            Assert.Equal((ushort)0x0055, hue); // priority tier survives
        }

        [Fact]
        public void ClearAll_ClearsOnlyMatchingTier()
        {
            PluginHighlightCharacters.Set(0x1234, 0x0021, priorityHighlight: false);
            PluginHighlightCharacters.Set(0x5678, 0x0055, priorityHighlight: true);

            PluginHighlightCharacters.ClearAll(priorityHighlight: false);

            Assert.False(PluginHighlightCharacters.TryResolve(0x1234, false, out _));
            Assert.True(PluginHighlightCharacters.TryResolve(0x5678, false, out ushort hue));
            Assert.Equal((ushort)0x0055, hue);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginHighlightCharactersTests"`
Expected: FAIL — `PluginHighlightCharacters` does not exist.

- [ ] **Step 3: Create `Game/Managers/PluginHighlights.cs` with `PluginHighlightCharacters`**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Plugin-driven per-serial highlight hues, two priority tiers. Policy
    /// (which serial gets which hue, and whether it should override the
    /// client's own status coloring) lives entirely in the plugin; the client
    /// only stores and resolves. Mirrors <see cref="PluginStatusOverlays"/>.
    /// </summary>
    internal static class PluginHighlightCharacters
    {
        private static readonly Dictionary<uint, ushort> _priority = new Dictionary<uint, ushort>();
        private static readonly Dictionary<uint, ushort> _normal = new Dictionary<uint, ushort>();

        public static void Set(uint serial, ushort hue, bool priorityHighlight)
        {
            if (priorityHighlight)
            {
                _priority[serial] = hue;
            }
            else
            {
                _normal[serial] = hue;
            }
        }

        public static void Remove(uint serial, bool priorityHighlight)
        {
            if (priorityHighlight)
            {
                _priority.Remove(serial);
            }
            else
            {
                _normal.Remove(serial);
            }
        }

        public static void ClearAll(bool priorityHighlight)
        {
            if (priorityHighlight)
            {
                _priority.Clear();
            }
            else
            {
                _normal.Clear();
            }
        }

        /// <summary>
        /// Resolves the highlight hue for <paramref name="serial"/>. The
        /// priority tier always wins. The normal tier only applies when
        /// <paramref name="statusOverrideActive"/> is false (the client's own
        /// status/notoriety coloring did not already set an override hue).
        /// </summary>
        public static bool TryResolve(uint serial, bool statusOverrideActive, out ushort hue)
        {
            if (_priority.TryGetValue(serial, out hue))
            {
                return true;
            }

            if (statusOverrideActive)
            {
                hue = 0;
                return false;
            }

            if (_normal.TryGetValue(serial, out hue))
            {
                return true;
            }

            hue = 0;
            return false;
        }

        /// <summary>Test-only: drops both tiers so tests start clean.</summary>
        public static void Reset()
        {
            _priority.Clear();
            _normal.Clear();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginHighlightCharactersTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginHighlights.cs tests/ClassicUO.UnitTests/Game/Managers/PluginHighlightCharactersTests.cs
git commit -m "feat(plugin): add PluginHighlightCharacters store"
```

---

## Task 3: Area highlight store — `PluginHighlightAreas`

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginHighlights.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/PluginHighlightAreasTests.cs`

**Interfaces:**
- Consumes: `HighlightSnap`, `HighlightObjectTypes` (Task 1).
- Produces: `internal static class PluginHighlightAreas` with `Add(World world, string id, int durationMs, HighlightSnap snap, uint anchorSerial, ushort hue, int rangeX, int rangeY, HighlightObjectTypes objectTypes, int x, int y, long now)`, `Remove(string id)`, `ClearAll()`, `GetTimer(string id, long now)`, `TryResolve(int x, int y, sbyte z, HighlightObjectTypes type, out ushort hue)`, `Update(World world, long now)`, `Reset()`, plus test seams `SerialResolver` and `MouseWorldResolver`.

This store needs to resolve a `Mouse`/`Serial`-snap area's world center without depending on a live game world in tests — it uses the same injectable-delegate test seam pattern as `PluginTimersManager.SerialResolver`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClassicUO.UnitTests/Game/Managers/PluginHighlightAreasTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class PluginHighlightAreasTests
    {
        public PluginHighlightAreasTests()
        {
            PluginHighlightAreas.Reset();
        }

        [Fact]
        public void GetTimer_ReturnsZero_WhenIdDoesNotExist()
        {
            Assert.Equal(0, PluginHighlightAreas.GetTimer("missing", now: 0));
        }

        [Fact]
        public void GetTimer_ReturnsMaxValue_WhenAreaNeverExpires()
        {
            // Add(world, id, durationMs, snap, anchorSerial, hue, rangeX, rangeY, objectTypes, x, y, now)
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x21, 3, 3, HighlightObjectTypes.All, 100, 100, 0);

            Assert.Equal(int.MaxValue, PluginHighlightAreas.GetTimer("a", 999999));
        }

        [Fact]
        public void GetTimer_ReturnsRemainingMs_ForTimedArea()
        {
            PluginHighlightAreas.Add(null, "a", 5000, HighlightSnap.Position, 0, (ushort)0x21, 3, 3, HighlightObjectTypes.All, 100, 100, 1000);

            Assert.Equal(3000, PluginHighlightAreas.GetTimer("a", 3000));
        }

        [Fact]
        public void Update_RemovesExpiredArea()
        {
            PluginHighlightAreas.Add(null, "a", 1000, HighlightSnap.Position, 0, (ushort)0x21, 3, 3, HighlightObjectTypes.All, 100, 100, 0);

            PluginHighlightAreas.Update(null, 1500);

            Assert.Equal(0, PluginHighlightAreas.GetTimer("a", 1500));
        }

        [Fact]
        public void TryResolve_MatchesPositionSnap_WithinRange()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0099, 3, 3, HighlightObjectTypes.Land, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(102, 101, 0, HighlightObjectTypes.Land, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0099, hue);
        }

        [Fact]
        public void TryResolve_ReturnsFalse_OutsideRange()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0099, 3, 3, HighlightObjectTypes.Land, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(200, 200, 0, HighlightObjectTypes.Land, out _);

            Assert.False(found);
        }

        [Fact]
        public void TryResolve_ReturnsFalse_WhenObjectTypeNotFlagged()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0099, 3, 3, HighlightObjectTypes.Mobile, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.Land, out _);

            Assert.False(found);
        }

        [Fact]
        public void TryResolve_LastAddedWins_OnOverlap()
        {
            PluginHighlightAreas.Add(null, "first", -1, HighlightSnap.Position, 0, (ushort)0x0011, 5, 5, HighlightObjectTypes.Land, 100, 100, 0);
            PluginHighlightAreas.Add(null, "second", -1, HighlightSnap.Position, 0, (ushort)0x0022, 5, 5, HighlightObjectTypes.Land, 100, 100, 0);

            bool found = PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.Land, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0022, hue);
        }

        [Fact]
        public void Add_SerialSnap_ResolvesCenterFromSerialResolver()
        {
            PluginHighlightAreas.SerialResolver = serial => serial == 0xAAAA ? (true, 50, 60, (sbyte)0) : (false, 0, 0, (sbyte)0);

            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Serial, 0xAAAA, (ushort)0x0033, 2, 2, HighlightObjectTypes.Mobile, 0, 0, 0);

            bool found = PluginHighlightAreas.TryResolve(51, 60, 0, HighlightObjectTypes.Mobile, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0033, hue);
        }

        [Fact]
        public void Update_SerialSnap_AutoRemovesWhenAnchorLost()
        {
            PluginHighlightAreas.SerialResolver = _ => (true, 50, 60, (sbyte)0);
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Serial, 0xAAAA, (ushort)0x0033, 2, 2, HighlightObjectTypes.Mobile, 0, 0, 0);

            PluginHighlightAreas.SerialResolver = _ => (false, 0, 0, (sbyte)0);
            PluginHighlightAreas.Update(null, 1);

            Assert.Equal(0, PluginHighlightAreas.GetTimer("a", 1));
        }

        [Fact]
        public void Add_MouseSnap_ResolvesCenterFromMouseWorldResolver()
        {
            PluginHighlightAreas.MouseWorldResolver = () => (77, 88, (sbyte)0);

            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Mouse, 0, (ushort)0x0044, 1, 1, HighlightObjectTypes.Static, 0, 0, 0);

            bool found = PluginHighlightAreas.TryResolve(77, 88, 0, HighlightObjectTypes.Static, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0044, hue);
        }

        [Fact]
        public void Remove_DropsArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0011, 5, 5, HighlightObjectTypes.All, 100, 100, 0);

            PluginHighlightAreas.Remove("a");

            Assert.False(PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.All, out _));
        }

        [Fact]
        public void ClearAll_DropsEveryArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0011, 5, 5, HighlightObjectTypes.All, 100, 100, 0);
            PluginHighlightAreas.Add(null, "b", -1, HighlightSnap.Position, 0, (ushort)0x0022, 5, 5, HighlightObjectTypes.All, 200, 200, 0);

            PluginHighlightAreas.ClearAll();

            Assert.False(PluginHighlightAreas.TryResolve(100, 100, 0, HighlightObjectTypes.All, out _));
            Assert.False(PluginHighlightAreas.TryResolve(200, 200, 0, HighlightObjectTypes.All, out _));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginHighlightAreasTests"`
Expected: FAIL — `PluginHighlightAreas` does not exist.

- [ ] **Step 3: Add `PluginHighlightAreas` to `Game/Managers/PluginHighlights.cs`**

Append to the file created in Task 2 (same `ClassicUO.Game.Managers` namespace block), and add `using ClassicUO.PluginApi;`, `using ClassicUO.Game.GameObjects;`, and `using System;` to the top of the file (matching how every other file in `Game/Managers/` that touches `GameObject` imports it explicitly):

```csharp
    internal sealed class AreaEntry
    {
        public string Id;
        public HighlightSnap Snap;
        public uint AnchorSerial;
        public ushort Hue;
        public int RangeX;
        public int RangeY;
        public HighlightObjectTypes ObjectTypes;
        public long ExpireAtTicks; // -1 = never
        public int CenterX;
        public int CenterY;
        public sbyte CenterZ;
        public bool AnchorResolved;
        public int InsertionSeq;
    }

    /// <summary>
    /// Plugin-driven world-space area highlights, keyed by plugin-chosen id.
    /// No spatial index: membership is a linear scan over the active-area
    /// dictionary, which is cheap because both the on-screen tile count
    /// (isometric view, ~24x24) and the realistic active-area count (tens)
    /// stay small. Last-added-wins on overlap, tracked via a monotonic
    /// insertion sequence rather than dictionary iteration order.
    /// </summary>
    internal static class PluginHighlightAreas
    {
        private static readonly Dictionary<string, AreaEntry> _areas = new Dictionary<string, AreaEntry>();
        private static readonly List<string> _expiredScratch = new List<string>();
        private static int _nextSeq;

        // Test seams; when null, resolution uses the live World / SelectedObject.
        internal static Func<uint, (bool found, int x, int y, sbyte z)> SerialResolver;
        internal static Func<(int x, int y, sbyte z)> MouseWorldResolver;

        public static void Add(
            World world,
            string id,
            int durationMs,
            HighlightSnap snap,
            uint anchorSerial,
            ushort hue,
            int rangeX,
            int rangeY,
            HighlightObjectTypes objectTypes,
            int x,
            int y,
            long now
        )
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            var entry = new AreaEntry
            {
                Id = id,
                Snap = snap,
                AnchorSerial = anchorSerial,
                Hue = hue,
                RangeX = rangeX,
                RangeY = rangeY,
                ObjectTypes = objectTypes,
                ExpireAtTicks = durationMs < 0 ? -1 : now + durationMs,
                CenterX = x,
                CenterY = y,
                CenterZ = 0,
                InsertionSeq = _nextSeq++
            };

            _areas[id] = entry;
            ResolveCenter(world, entry);
        }

        public static void Remove(string id) => _areas.Remove(id);

        public static void ClearAll() => _areas.Clear();

        public static int GetTimer(string id, long now)
        {
            if (!_areas.TryGetValue(id, out AreaEntry e))
            {
                return 0;
            }

            if (e.ExpireAtTicks < 0)
            {
                return int.MaxValue;
            }

            long remaining = e.ExpireAtTicks - now;
            return remaining > 0 ? (int)remaining : 0;
        }

        public static bool TryResolve(int x, int y, sbyte z, HighlightObjectTypes type, out ushort hue)
        {
            AreaEntry best = null;

            foreach (KeyValuePair<string, AreaEntry> kv in _areas)
            {
                AreaEntry e = kv.Value;

                if ((e.ObjectTypes & type) == 0 || !e.AnchorResolved)
                {
                    continue;
                }

                if (Math.Abs(x - e.CenterX) > e.RangeX || Math.Abs(y - e.CenterY) > e.RangeY)
                {
                    continue;
                }

                if (best == null || e.InsertionSeq > best.InsertionSeq)
                {
                    best = e;
                }
            }

            if (best == null)
            {
                hue = 0;
                return false;
            }

            hue = best.Hue;
            return true;
        }

        /// <summary>Per-frame maintenance: expire timed areas, re-resolve Mouse/Serial centers.</summary>
        public static void Update(World world, long now)
        {
            _expiredScratch.Clear();

            foreach (KeyValuePair<string, AreaEntry> kv in _areas)
            {
                AreaEntry e = kv.Value;

                if (e.ExpireAtTicks >= 0 && now >= e.ExpireAtTicks)
                {
                    _expiredScratch.Add(kv.Key);
                    continue;
                }

                if (e.Snap == HighlightSnap.Mouse || e.Snap == HighlightSnap.Serial)
                {
                    ResolveCenter(world, e);

                    if (e.Snap == HighlightSnap.Serial && !e.AnchorResolved)
                    {
                        _expiredScratch.Add(kv.Key);
                    }
                }
            }

            foreach (string id in _expiredScratch)
            {
                _areas.Remove(id);
            }
        }

        private static void ResolveCenter(World world, AreaEntry e)
        {
            switch (e.Snap)
            {
                case HighlightSnap.Position:
                    e.AnchorResolved = true;
                    break;

                case HighlightSnap.Mouse:
                    (int mx, int my, sbyte mz) = MouseWorldResolver != null ? MouseWorldResolver() : DefaultMouseWorld(world);
                    e.CenterX = mx;
                    e.CenterY = my;
                    e.CenterZ = mz;
                    e.AnchorResolved = true;
                    break;

                case HighlightSnap.Serial:
                    (bool found, int sx, int sy, sbyte sz) = SerialResolver != null
                        ? SerialResolver(e.AnchorSerial)
                        : DefaultSerialWorld(world, e.AnchorSerial);
                    e.AnchorResolved = found;
                    if (found)
                    {
                        e.CenterX = sx;
                        e.CenterY = sy;
                        e.CenterZ = sz;
                    }
                    break;
            }
        }

        private static (int x, int y, sbyte z) DefaultMouseWorld(World world)
        {
            if (SelectedObject.Object is GameObject g)
            {
                return (g.X, g.Y, g.Z);
            }

            if (world?.Player != null)
            {
                return (world.Player.X, world.Player.Y, world.Player.Z);
            }

            return (0, 0, 0);
        }

        private static (bool found, int x, int y, sbyte z) DefaultSerialWorld(World world, uint serial)
        {
            Entity entity = world?.Get(serial);

            if (entity == null)
            {
                return (false, 0, 0, 0);
            }

            return (true, entity.X, entity.Y, entity.Z);
        }

        /// <summary>Test-only: drops every area and test seam so tests start clean.</summary>
        public static void Reset()
        {
            _areas.Clear();
            SerialResolver = null;
            MouseWorldResolver = null;
            _nextSeq = 0;
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginHighlightAreasTests"`
Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginHighlights.cs tests/ClassicUO.UnitTests/Game/Managers/PluginHighlightAreasTests.cs
git commit -m "feat(plugin): add PluginHighlightAreas store"
```

---

## Task 4: `PluginHighlights` ABI facade + resolution helpers + `World.Update` wiring

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginHighlights.cs`
- Modify: `src/ClassicUO.Client/Game/World.cs:287`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/PluginHighlightsResolutionTests.cs`

**Interfaces:**
- Consumes: `PluginHighlightCharacters.TryResolve`, `PluginHighlightAreas.TryResolve`/`Add`/`Remove`/`ClearAll`/`GetTimer`/`Update` (Tasks 2-3).
- Produces: `internal static class PluginHighlights` with ABI-shaped methods `AddArea(IntPtr, int, int, uint, int, int, ushort, int, int, int)`, `RemoveArea(IntPtr)`, `ClearAreas()`, `GetAreaTimer(IntPtr) : int`, `AddCharacter(uint, ushort, byte)`, `RemoveCharacter(uint, byte)`, `ClearCharacters(byte)`, `Update(World, long)`, plus the render-facing composition helper `TryResolveMobileHue(uint serial, bool statusOverrideActive, int x, int y, sbyte z, out ushort hue)` used by Task 7.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Managers/PluginHighlightsResolutionTests.cs` — this is the table-driven test for the spec's priority-resolution order (character priority tier > status override > character normal tier > area):

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class PluginHighlightsResolutionTests
    {
        public PluginHighlightsResolutionTests()
        {
            PluginHighlightCharacters.Reset();
            PluginHighlightAreas.Reset();
        }

        [Fact]
        public void TryResolveMobileHue_FallsBackToArea_WhenNoCharacterHighlight()
        {
            // Add(world, id, durationMs, snap, anchorSerial, hue, rangeX, rangeY, objectTypes, x, y, now)
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0077, 5, 5, HighlightObjectTypes.Mobile, 100, 100, 0);

            bool found = PluginHighlights.TryResolveMobileHue(0x1234, statusOverrideActive: false, 100, 100, 0, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0077, hue);
        }

        [Fact]
        public void TryResolveMobileHue_NormalCharacterTier_BeatsArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0077, 5, 5, HighlightObjectTypes.Mobile, 100, 100, 0);
            PluginHighlightCharacters.Set(0x1234, 0x0022, priorityHighlight: false);

            bool found = PluginHighlights.TryResolveMobileHue(0x1234, statusOverrideActive: false, 100, 100, 0, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0022, hue);
        }

        [Fact]
        public void TryResolveMobileHue_StatusOverrideActive_SkipsNormalTierAndArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0077, 5, 5, HighlightObjectTypes.Mobile, 100, 100, 0);
            PluginHighlightCharacters.Set(0x1234, 0x0022, priorityHighlight: false);

            bool found = PluginHighlights.TryResolveMobileHue(0x1234, statusOverrideActive: true, 100, 100, 0, out _);

            Assert.False(found);
        }

        [Fact]
        public void TryResolveMobileHue_PriorityTier_BeatsStatusOverrideAndArea()
        {
            PluginHighlightAreas.Add(null, "a", -1, HighlightSnap.Position, 0, (ushort)0x0077, 5, 5, HighlightObjectTypes.Mobile, 100, 100, 0);
            PluginHighlightCharacters.Set(0x1234, 0x0099, priorityHighlight: true);

            bool found = PluginHighlights.TryResolveMobileHue(0x1234, statusOverrideActive: true, 100, 100, 0, out ushort hue);

            Assert.True(found);
            Assert.Equal((ushort)0x0099, hue);
        }

        [Fact]
        public void TryResolveMobileHue_ReturnsFalse_WhenNothingMatches()
        {
            bool found = PluginHighlights.TryResolveMobileHue(0x1234, statusOverrideActive: false, 100, 100, 0, out ushort hue);

            Assert.False(found);
            Assert.Equal((ushort)0, hue);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginHighlightsResolutionTests"`
Expected: FAIL — `PluginHighlights` does not exist.

- [ ] **Step 3: Add `PluginHighlights` facade to `Game/Managers/PluginHighlights.cs`**

Append to the file (add `using System.Runtime.InteropServices;` to the top):

```csharp
    /// <summary>
    /// Static bind targets for the plugin-&gt;cuo highlight primitives (bound
    /// into ClientBindings in PluginHost.cs) plus the render-facing resolution
    /// helper. All run on the game thread (the host marshals before calling).
    /// Mirrors PluginStatusBars/PluginTimersManager as static binding targets.
    /// </summary>
    internal static class PluginHighlights
    {
        private static string PtrToString(System.IntPtr p) =>
            p == System.IntPtr.Zero ? string.Empty : (Marshal.PtrToStringAnsi(p) ?? string.Empty);

        public static void AddArea(
            System.IntPtr idUtf8,
            int durationMs,
            int snapKind,
            uint anchorSerial,
            int x,
            int y,
            ushort hue,
            int rangeX,
            int rangeY,
            int objectTypes
        )
        {
            string id = PtrToString(idUtf8);

            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            World world = Client.Game?.UO?.World;
            PluginHighlightAreas.Add(
                world, id, durationMs, (HighlightSnap)snapKind, anchorSerial,
                hue, rangeX, rangeY, (HighlightObjectTypes)objectTypes, x, y, Time.Ticks
            );
        }

        public static void RemoveArea(System.IntPtr idUtf8) => PluginHighlightAreas.Remove(PtrToString(idUtf8));

        public static void ClearAreas() => PluginHighlightAreas.ClearAll();

        public static int GetAreaTimer(System.IntPtr idUtf8) => PluginHighlightAreas.GetTimer(PtrToString(idUtf8), Time.Ticks);

        public static void AddCharacter(uint serial, ushort hue, byte priorityHighlight) =>
            PluginHighlightCharacters.Set(serial, hue, priorityHighlight != 0);

        public static void RemoveCharacter(uint serial, byte priorityHighlight) =>
            PluginHighlightCharacters.Remove(serial, priorityHighlight != 0);

        public static void ClearCharacters(byte priorityHighlight) =>
            PluginHighlightCharacters.ClearAll(priorityHighlight != 0);

        /// <summary>Per-frame driver, called alongside PluginTimersManager.Update.</summary>
        public static void Update(World world, long now) => PluginHighlightAreas.Update(world, now);

        /// <summary>
        /// Composes character-highlight and area-highlight resolution for a
        /// mobile, in spec priority order: priority character tier, then the
        /// caller's own status override, then normal character tier, then area.
        /// </summary>
        public static bool TryResolveMobileHue(uint serial, bool statusOverrideActive, int x, int y, sbyte z, out ushort hue)
        {
            if (PluginHighlightCharacters.TryResolve(serial, statusOverrideActive, out hue))
            {
                return true;
            }

            // Area highlight sits at the same priority level as the normal character
            // tier (spec rule 4): it must not leak through when a status override is active.
            if (statusOverrideActive)
            {
                hue = 0;
                return false;
            }

            return PluginHighlightAreas.TryResolve(x, y, z, HighlightObjectTypes.Mobile, out hue);
        }
    }
```

- [ ] **Step 4: Wire `PluginHighlights.Update` into `World.Update`**

In `src/ClassicUO.Client/Game/World.cs`, at line 287, change:

```csharp
            Managers.PluginTimersManager.Update(this, Time.Ticks);
```

to:

```csharp
            Managers.PluginTimersManager.Update(this, Time.Ticks);
            Managers.PluginHighlights.Update(this, Time.Ticks);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginHighlightsResolutionTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Run the full unit test suite to check for regressions**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS, all tests including the new ones.

- [ ] **Step 7: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginHighlights.cs src/ClassicUO.Client/Game/World.cs tests/ClassicUO.UnitTests/Game/Managers/PluginHighlightsResolutionTests.cs
git commit -m "feat(plugin): add PluginHighlights ABI facade and mobile resolution helper"
```

---

## Task 5: BootstrapHost ABI wiring — `HostBridge`, `HighlightImpl`

**Files:**
- Modify: `src/ClassicUO.BootstrapHost/HostBridge.cs`
- Modify: `src/ClassicUO.BootstrapHost/PluginContextImpl.cs`
- Test: `tests/ClassicUO.BootstrapHost.Tests/HighlightTests.cs`

**Interfaces:**
- Consumes: `IHighlight` (Task 1).
- Produces: 7 new `nint` fields on `ClientBindings` (`AddAreaFn`, `RemoveAreaFn`, `ClearAreasFn`, `GetAreaTimerFn`, `AddCharacterFn`, `RemoveCharacterFn`, `ClearCharactersFn`); `internal sealed class HighlightImpl : IHighlight`; `PluginContextImpl.Highlight` property.

**Critical ABI constraint:** the 7 new fields must be inserted at the exact same relative position in this struct and in the native-side struct in `src/ClassicUO.Client/PluginHost.cs` (Task 6) — immediately after `ClearTimersFn`. `HostBridge`'s `ClientBindings` is intentionally a byte-for-byte *prefix* of the native struct (it stops before `SetPluginPartyMemberFn`/`CheckLosFn`/`ClientVersion`, which BootstrapHost doesn't yet support) — as long as both sides keep the same field order up through the fields each side declares, `Unsafe.AsRef<ClientBindings>` reads correctly.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.BootstrapHost.Tests/HighlightTests.cs` (mirrors `StatusBarsTests.cs`):

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace ClassicUO.BootstrapHost.Tests;

/// <summary>
/// Verifies each IHighlight method dispatches into the matching ClientBindings
/// function pointer with the right argument marshaling. A fake binding records
/// the call; no cuo.dll is involved.
/// </summary>
public sealed unsafe class HighlightTests
{
    private static string _lastId;
    private static int _durationMs, _snapKind, _x, _y, _rangeX, _rangeY, _objectTypes;
    private static uint _anchorSerial, _serial;
    private static ushort _hue;
    private static byte _priority;
    private static int _addAreaCalls, _removeAreaCalls, _clearAreasCalls, _getTimerCalls;
    private static int _addCharCalls, _removeCharCalls, _clearCharCalls;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureAddArea(nint idPtr, int durationMs, int snapKind, uint anchorSerial, int x, int y, ushort hue, int rangeX, int rangeY, int objectTypes)
    {
        _lastId = Marshal.PtrToStringAnsi(idPtr);
        _durationMs = durationMs; _snapKind = snapKind; _anchorSerial = anchorSerial;
        _x = x; _y = y; _hue = hue; _rangeX = rangeX; _rangeY = rangeY; _objectTypes = objectTypes;
        _addAreaCalls++;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureRemoveArea(nint idPtr) { _lastId = Marshal.PtrToStringAnsi(idPtr); _removeAreaCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureClearAreas() { _clearAreasCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CaptureGetAreaTimer(nint idPtr) { _lastId = Marshal.PtrToStringAnsi(idPtr); _getTimerCalls++; return 4242; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureAddCharacter(uint serial, ushort hue, byte priority)
    { _serial = serial; _hue = hue; _priority = priority; _addCharCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureRemoveCharacter(uint serial, byte priority) { _serial = serial; _priority = priority; _removeCharCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureClearCharacters(byte priority) { _priority = priority; _clearCharCalls++; }

    private static HighlightImpl NewImplWithBindings(ClientBindings bindings)
    {
        var bridge = new HostBridge();
        bridge.SetClientBindingsForTest(bindings);
        return new HighlightImpl(bridge);
    }

    [Fact]
    public void AddArea_InvokesBinding_WithArgs()
    {
        _addAreaCalls = 0;
        var bindings = new ClientBindings
        {
            AddAreaFn = (nint)(delegate* unmanaged[Cdecl]<nint, int, int, uint, int, int, ushort, int, int, int, void>)&CaptureAddArea
        };
        var impl = NewImplWithBindings(bindings);

        impl.AddArea("zone1", durationMs: 5000, snap: ClassicUO.PluginApi.HighlightSnap.Position,
            anchorSerial: 0, hue: 0x0021, rangeX: 4, rangeY: 4,
            objectTypes: ClassicUO.PluginApi.HighlightObjectTypes.Land, x: 10, y: 20);

        _addAreaCalls.Should().Be(1);
        _lastId.Should().Be("zone1");
        _durationMs.Should().Be(5000);
        _snapKind.Should().Be((int)ClassicUO.PluginApi.HighlightSnap.Position);
        _hue.Should().Be((ushort)0x0021);
        _rangeX.Should().Be(4);
        _rangeY.Should().Be(4);
        _objectTypes.Should().Be((int)ClassicUO.PluginApi.HighlightObjectTypes.Land);
        _x.Should().Be(10);
        _y.Should().Be(20);
    }

    [Fact]
    public void RemoveArea_InvokesBinding_WithId()
    {
        _removeAreaCalls = 0;
        var bindings = new ClientBindings { RemoveAreaFn = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&CaptureRemoveArea };
        var impl = NewImplWithBindings(bindings);

        impl.RemoveArea("zone1");

        _removeAreaCalls.Should().Be(1);
        _lastId.Should().Be("zone1");
    }

    [Fact]
    public void ClearAreas_InvokesBinding()
    {
        _clearAreasCalls = 0;
        var bindings = new ClientBindings { ClearAreasFn = (nint)(delegate* unmanaged[Cdecl]<void>)&CaptureClearAreas };
        var impl = NewImplWithBindings(bindings);

        impl.ClearAreas();

        _clearAreasCalls.Should().Be(1);
    }

    [Fact]
    public void GetAreaTimer_ReturnsBindingResult()
    {
        _getTimerCalls = 0;
        var bindings = new ClientBindings { GetAreaTimerFn = (nint)(delegate* unmanaged[Cdecl]<nint, int>)&CaptureGetAreaTimer };
        var impl = NewImplWithBindings(bindings);

        int result = impl.GetAreaTimer("zone1");

        _getTimerCalls.Should().Be(1);
        result.Should().Be(4242);
    }

    [Fact]
    public void AddCharacter_InvokesBinding_WithPriorityByte()
    {
        _addCharCalls = 0;
        var bindings = new ClientBindings { AddCharacterFn = (nint)(delegate* unmanaged[Cdecl]<uint, ushort, byte, void>)&CaptureAddCharacter };
        var impl = NewImplWithBindings(bindings);

        impl.AddCharacter(0x1234, 0x0055, priorityHighlight: true);

        _addCharCalls.Should().Be(1);
        _serial.Should().Be(0x1234u);
        _hue.Should().Be((ushort)0x0055);
        _priority.Should().Be((byte)1);
    }

    [Fact]
    public void RemoveCharacter_InvokesBinding()
    {
        _removeCharCalls = 0;
        var bindings = new ClientBindings { RemoveCharacterFn = (nint)(delegate* unmanaged[Cdecl]<uint, byte, void>)&CaptureRemoveCharacter };
        var impl = NewImplWithBindings(bindings);

        impl.RemoveCharacter(0x1234, priorityHighlight: false);

        _removeCharCalls.Should().Be(1);
        _serial.Should().Be(0x1234u);
        _priority.Should().Be((byte)0);
    }

    [Fact]
    public void ClearCharacters_InvokesBinding()
    {
        _clearCharCalls = 0;
        var bindings = new ClientBindings { ClearCharactersFn = (nint)(delegate* unmanaged[Cdecl]<byte, void>)&CaptureClearCharacters };
        var impl = NewImplWithBindings(bindings);

        impl.ClearCharacters(priorityHighlight: true);

        _clearCharCalls.Should().Be(1);
        _priority.Should().Be((byte)1);
    }

    [Fact]
    public void Methods_AreNoOps_WhenBindingMissing()
    {
        var impl = NewImplWithBindings(new ClientBindings());
        // Zeroed function pointers: must not throw. GetAreaTimer returns 0.
        impl.AddArea("x");
        impl.RemoveArea("x");
        impl.ClearAreas();
        impl.GetAreaTimer("x").Should().Be(0);
        impl.AddCharacter(1, 1);
        impl.RemoveCharacter(1);
        impl.ClearCharacters();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~HighlightTests"`
Expected: FAIL — `HighlightImpl` and the new `ClientBindings` fields don't exist.

- [ ] **Step 3: Add the 7 fields to `ClientBindings` in `HostBridge.cs`**

In `src/ClassicUO.BootstrapHost/HostBridge.cs`, change the end of the `ClientBindings` struct from:

```csharp
    public nint AddTimerFn;            // void(int id, int shape, int durationMs, ushort hue, int groupId, int x, int y, int width, int height, nint labelUtf8, byte showTime, int anchorKind, uint anchorSerial, ushort ax, ushort ay, sbyte az, short offX, short offY, int graceMs)
    public nint RemoveTimerFn;         // void(int id)
    public nint RemoveTimerGroupFn;    // void(int groupId)
    public nint ClearTimersFn;         // void()
}
```

to:

```csharp
    public nint AddTimerFn;            // void(int id, int shape, int durationMs, ushort hue, int groupId, int x, int y, int width, int height, nint labelUtf8, byte showTime, int anchorKind, uint anchorSerial, ushort ax, ushort ay, sbyte az, short offX, short offY, int graceMs)
    public nint RemoveTimerFn;         // void(int id)
    public nint RemoveTimerGroupFn;    // void(int groupId)
    public nint ClearTimersFn;         // void()
    public nint AddAreaFn;             // void(nint idUtf8, int durationMs, int snapKind, uint anchorSerial, int x, int y, ushort hue, int rangeX, int rangeY, int objectTypes)
    public nint RemoveAreaFn;          // void(nint idUtf8)
    public nint ClearAreasFn;          // void()
    public nint GetAreaTimerFn;        // int(nint idUtf8)
    public nint AddCharacterFn;        // void(uint serial, ushort hue, byte priorityHighlight)
    public nint RemoveCharacterFn;     // void(uint serial, byte priorityHighlight)
    public nint ClearCharactersFn;     // void(byte priorityHighlight)
}
```

- [ ] **Step 4: Add `HighlightImpl`, wire it into `PluginContextImpl`**

In `src/ClassicUO.BootstrapHost/PluginContextImpl.cs`, add the field, constructor line, and property (mirroring `_statusBars`/`StatusBars`):

```csharp
    private readonly StatusBarsImpl _statusBars;
    private readonly HighlightImpl _highlight;
```

```csharp
        _statusBars = new StatusBarsImpl(bridge);
        _highlight = new HighlightImpl(bridge);
```

```csharp
    public IStatusBars StatusBars => _statusBars;
    public IHighlight Highlight => _highlight;
```

Then append the new `HighlightImpl` class at the end of the file, after `DispatcherImpl`:

```csharp
internal sealed class HighlightImpl : IHighlight
{
    private readonly HostBridge _bridge;
    public HighlightImpl(HostBridge bridge) { _bridge = bridge; }

    public unsafe void AddArea(
        string id,
        int durationMs = -1,
        HighlightSnap snap = HighlightSnap.Mouse,
        uint anchorSerial = 0,
        ushort hue = 0x0386,
        int rangeX = 3,
        int rangeY = 3,
        HighlightObjectTypes objectTypes = HighlightObjectTypes.All,
        int x = 0,
        int y = 0)
    {
        var fn = _bridge.ClientBindings.AddAreaFn;
        if (fn == 0 || string.IsNullOrEmpty(id)) return;

        nint idPtr = Marshal.StringToHGlobalAnsi(id);
        int snapKind = (int)snap;
        int types = (int)objectTypes;

        void Call()
        {
            try
            {
                ((delegate* unmanaged[Cdecl]<nint, int, int, uint, int, int, ushort, int, int, int, void>)fn)(
                    idPtr, durationMs, snapKind, anchorSerial, x, y, hue, rangeX, rangeY, types);
            }
            finally { Marshal.FreeHGlobal(idPtr); }
        }

        if (_bridge.IsGameThread) Call();
        else _bridge.PostToGameThread(Call);
    }

    public unsafe void RemoveArea(string id)
    {
        var fn = _bridge.ClientBindings.RemoveAreaFn;
        if (fn == 0 || string.IsNullOrEmpty(id)) return;

        nint idPtr = Marshal.StringToHGlobalAnsi(id);

        void Call()
        {
            try { ((delegate* unmanaged[Cdecl]<nint, void>)fn)(idPtr); }
            finally { Marshal.FreeHGlobal(idPtr); }
        }

        if (_bridge.IsGameThread) Call();
        else _bridge.PostToGameThread(Call);
    }

    public unsafe void ClearAreas()
    {
        var fn = _bridge.ClientBindings.ClearAreasFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<void>)fn)();
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<void>)fn)());
    }

    // Direct synchronous read (no thread marshal), matching IGameActions.TryGetPlayerPosition:
    // it only reads live state, and off-thread callers need the value immediately.
    public unsafe int GetAreaTimer(string id)
    {
        var fn = _bridge.ClientBindings.GetAreaTimerFn;
        if (fn == 0 || string.IsNullOrEmpty(id)) return 0;

        nint idPtr = Marshal.StringToHGlobalAnsi(id);
        try { return ((delegate* unmanaged[Cdecl]<nint, int>)fn)(idPtr); }
        finally { Marshal.FreeHGlobal(idPtr); }
    }

    public unsafe void AddCharacter(uint serial, ushort hue, bool priorityHighlight = false)
    {
        var fn = _bridge.ClientBindings.AddCharacterFn;
        if (fn == 0) return;
        byte p = priorityHighlight ? (byte)1 : (byte)0;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<uint, ushort, byte, void>)fn)(serial, hue, p);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, ushort, byte, void>)fn)(serial, hue, p));
    }

    public unsafe void RemoveCharacter(uint serial, bool priorityHighlight = false)
    {
        var fn = _bridge.ClientBindings.RemoveCharacterFn;
        if (fn == 0) return;
        byte p = priorityHighlight ? (byte)1 : (byte)0;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<uint, byte, void>)fn)(serial, p);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, byte, void>)fn)(serial, p));
    }

    public unsafe void ClearCharacters(bool priorityHighlight = false)
    {
        var fn = _bridge.ClientBindings.ClearCharactersFn;
        if (fn == 0) return;
        byte p = priorityHighlight ? (byte)1 : (byte)0;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<byte, void>)fn)(p);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<byte, void>)fn)(p));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~HighlightTests"`
Expected: PASS (8 tests).

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.BootstrapHost/HostBridge.cs src/ClassicUO.BootstrapHost/PluginContextImpl.cs tests/ClassicUO.BootstrapHost.Tests/HighlightTests.cs
git commit -m "feat(plugin): wire IHighlight through BootstrapHost ABI"
```

---

## Task 6: Native-side wiring — `src/ClassicUO.Client/PluginHost.cs`

**Files:**
- Modify: `src/ClassicUO.Client/PluginHost.cs`

**Interfaces:**
- Consumes: `PluginHighlights.AddArea/RemoveArea/ClearAreas/GetAreaTimer/AddCharacter/RemoveCharacter/ClearCharacters` (Task 4).
- Produces: 7 new fields on the native `ClientBindings` struct, populated in `Initialize()`.

No dedicated unit test exists for this file's wiring (matching the existing convention — `StatusBars`/`ScreenTimers` native wiring here is likewise untested directly; it's exercised end-to-end only by a running client + plugin). Verification is build success plus careful field-order review.

- [ ] **Step 1: Add the 7 fields to the native `ClientBindings` struct**

In `src/ClassicUO.Client/PluginHost.cs`, insert immediately after `ClearTimersFn` and before `SetPluginPartyMemberFn` (this exact position matters — see Task 5's ABI constraint note):

```csharp
        public IntPtr /*delegate*<int, void>*/ ClearTimersFn;
        public IntPtr /*delegate*<IntPtr, int, int, uint, int, int, ushort, int, int, int, void>*/ AddAreaFn;
        public IntPtr /*delegate*<IntPtr, void>*/ RemoveAreaFn;
        public IntPtr /*delegate*<void>*/ ClearAreasFn;
        public IntPtr /*delegate*<IntPtr, int>*/ GetAreaTimerFn;
        public IntPtr /*delegate*<uint, ushort, byte, void>*/ AddCharacterFn;
        public IntPtr /*delegate*<uint, byte, void>*/ RemoveCharacterFn;
        public IntPtr /*delegate*<byte, void>*/ ClearCharactersFn;
        public IntPtr /*delegate*<uint, ushort, int, int, int, int, void>*/ SetPluginPartyMemberFn;
```

(i.e. replace the single existing `public IntPtr ClearTimersFn;` line with the block above, which keeps `SetPluginPartyMemberFn` and everything after it unchanged.)

- [ ] **Step 2: Declare delegate types + binding fields**

In the `UnmanagedAssistantHost` class, immediately after the existing timer-related delegate block (after `_clearTimers`, before the `dSetPluginPartyMember` block, i.e. right after line ~326 `private readonly dNoArg _clearTimers = ...ClearTimers;`):

```csharp
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dAddArea(IntPtr idUtf8, int durationMs, int snapKind, uint anchorSerial, int x, int y, ushort hue, int rangeX, int rangeY, int objectTypes);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dAddArea _addArea = Game.Managers.PluginHighlights.AddArea;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dRemoveArea(IntPtr idUtf8);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dRemoveArea _removeArea = Game.Managers.PluginHighlights.RemoveArea;

        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dNoArg _clearAreas = Game.Managers.PluginHighlights.ClearAreas;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int dGetAreaTimer(IntPtr idUtf8);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dGetAreaTimer _getAreaTimer = Game.Managers.PluginHighlights.GetAreaTimer;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dAddCharacter(uint serial, ushort hue, byte priorityHighlight);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dAddCharacter _addCharacter = Game.Managers.PluginHighlights.AddCharacter;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dRemoveCharacter(uint serial, byte priorityHighlight);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dRemoveCharacter _removeCharacter = Game.Managers.PluginHighlights.RemoveCharacter;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dClearCharacters(byte priorityHighlight);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dClearCharacters _clearCharacters = Game.Managers.PluginHighlights.ClearCharacters;
```

(`dNoArg` is the existing zero-arg delegate type already declared above for `_clearBuffs`/`_clearTimers` — reused here for `_clearAreas`.)

- [ ] **Step 3: Populate the fields in `Initialize()`**

In `Initialize()`, immediately after the existing `cuoHost.ClearTimersFn = Marshal.GetFunctionPointerForDelegate(_clearTimers);` line:

```csharp
            cuoHost.ClearTimersFn = Marshal.GetFunctionPointerForDelegate(_clearTimers);
            cuoHost.AddAreaFn = Marshal.GetFunctionPointerForDelegate(_addArea);
            cuoHost.RemoveAreaFn = Marshal.GetFunctionPointerForDelegate(_removeArea);
            cuoHost.ClearAreasFn = Marshal.GetFunctionPointerForDelegate(_clearAreas);
            cuoHost.GetAreaTimerFn = Marshal.GetFunctionPointerForDelegate(_getAreaTimer);
            cuoHost.AddCharacterFn = Marshal.GetFunctionPointerForDelegate(_addCharacter);
            cuoHost.RemoveCharacterFn = Marshal.GetFunctionPointerForDelegate(_removeCharacter);
            cuoHost.ClearCharactersFn = Marshal.GetFunctionPointerForDelegate(_clearCharacters);
```

- [ ] **Step 4: Build the client to verify it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded (0 errors).

- [ ] **Step 5: Build BootstrapHost + run its full test suite to catch any struct-layout drift**

Run: `dotnet build ClassicUO.sln -c Debug` then `dotnet test tests/ClassicUO.BootstrapHost.Tests`
Expected: Build succeeded; all tests (including `HighlightTests` and the pre-existing `StatusBarsTests`/`SmokeTests`) PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/PluginHost.cs
git commit -m "feat(plugin): bind IHighlight into the native cuo ClientBindings ABI"
```

---

## Task 7: Mobile rendering — `MobileView.Draw`

**Files:**
- Modify: `src/ClassicUO.Client/Game/GameObjects/Views/MobileView.cs`

**Interfaces:**
- Consumes: `PluginHighlights.TryResolveMobileHue(uint, bool, int, int, sbyte, out ushort)` (Task 4).

No new automated test — `MobileView.Draw` requires a live graphics batcher and is exercised the same way the rest of the file already is (manual in-game verification), consistent with `docs/superpowers/specs/2026-07-10-anchored-screen-timers-design.md`'s precedent for render-path code. The resolution *logic* itself is already fully covered by Task 4's tests.

- [ ] **Step 1: Add the missing `using`**

`MobileView.cs` doesn't currently import `ClassicUO.Game.Managers` (it reaches `Notoriety`/`ProfileManager`/`SelectedObject` via `ClassicUO.Game.Data`/`ClassicUO.Configuration`/the enclosing `ClassicUO.Game` namespace instead). Add, alongside the existing usings at the top of the file:

```csharp
using ClassicUO.Game.Managers;
```

- [ ] **Step 2: Insert the highlight resolution call**

In `src/ClassicUO.Client/Game/GameObjects/Views/MobileView.cs`, immediately after the existing hue chain ends (after line 151, `overridedHue = Notoriety.GetHue(NotorietyFlag);` closing the `isAttack || isUnderMouse` block) and before line 153 (`ProcessSteps(out byte dir);`), insert:

```csharp
            if (PluginHighlights.TryResolveMobileHue(Serial, overridedHue != 0, X, Y, Z, out ushort pluginHue))
            {
                overridedHue = pluginHue;
            }

```

`overridedHue != 0` is the "did the existing chain already set a status/selection override" signal `TryResolveMobileHue` needs — it's a `ushort` local already computed by every branch above, and every one of those branches leaves it non-zero exactly when an override applies (0 is the sentinel for "no override" throughout this method, e.g. the reset at line 106).

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded (0 errors).

- [ ] **Step 4: Manual in-game verification**

Start the client against a test/dev shard with a v2 test plugin (see `samples/HelloPlugin` for a template) that calls `context.Highlight.AddCharacter(someMobileSerial, 0x0044)`. Confirm:
- The mobile's body recolors to hue `0x0044` when idle.
- If that mobile becomes poisoned (status override active) and the highlight was added with `priorityHighlight: false`, the poison hue takes over instead.
- Re-adding with `priorityHighlight: true` keeps `0x0044` even while poisoned.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/GameObjects/Views/MobileView.cs
git commit -m "feat(plugin): apply character/area highlight hue to mobiles"
```

---

## Task 8: Item and corpse rendering — `ItemView.Draw`

**Files:**
- Modify: `src/ClassicUO.Client/Game/GameObjects/Views/ItemView.cs`

**Interfaces:**
- Consumes: `PluginHighlightAreas.TryResolve(int, int, sbyte, HighlightObjectTypes, out ushort)` (Task 3).

`ItemView.cs` already has `using ClassicUO.Game.Managers;`, so no new using is needed for this task.

- [ ] **Step 1: Insert area-highlight resolution for ground items**

In `ItemView.cs`, immediately after the existing hue chain ends (after line 133, the `else if (IsHidden) { hue = 0x038E; }` closing brace) and before line 135 (`hueVec = ShaderHueTranslator.GetHueVector(hue, partial, alpha);`), insert:

```csharp
            if (PluginHighlightAreas.TryResolve(X, Y, Z, ClassicUO.PluginApi.HighlightObjectTypes.Item, out ushort pluginItemHue))
            {
                hue = pluginItemHue;
            }

```

- [ ] **Step 2: Insert area-highlight resolution for corpses**

Corpses take a separate early-return path (`DrawCorpse`, called at line 54). The corpse's base body hue is the `color` argument (`Hue`, the item's own field) passed into the first `DrawLayer` call inside `DrawCorpse`. In `ItemView.cs`, change the `DrawCorpse` method's first `DrawLayer` call (lines 200-215):

```csharp
            DrawLayer(
                batcher,
                posX,
                posY,
                this,
                Layer.Invalid,
                animIndex,
                ishuman,
                Hue,
                IsFlipped,
                hueVec.Z,
                group,
                direction,
                hueVec,
                depth
            );
```

to compute the corpse's own hue first, so an area highlight tints the corpse body while equipped-item layers on the corpse (the loop right after this call) keep their own colors, unchanged:

```csharp
            ushort corpseHue = PluginHighlightAreas.TryResolve(X, Y, Z, ClassicUO.PluginApi.HighlightObjectTypes.Corpse, out ushort pluginCorpseHue)
                ? pluginCorpseHue
                : Hue;

            DrawLayer(
                batcher,
                posX,
                posY,
                this,
                Layer.Invalid,
                animIndex,
                ishuman,
                corpseHue,
                IsFlipped,
                hueVec.Z,
                group,
                direction,
                hueVec,
                depth
            );
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded (0 errors).

- [ ] **Step 4: Manual in-game verification**

With a test plugin calling `context.Highlight.AddArea("zone", hue: 0x0055, objectTypes: HighlightObjectTypes.Item)` positioned over a tile with a loose ground item, confirm the item recolors to `0x0055`. Repeat with `HighlightObjectTypes.Corpse` over a corpse and confirm the body recolors while any equipped gear on the corpse keeps its own color.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/GameObjects/Views/ItemView.cs
git commit -m "feat(plugin): apply area highlight hue to ground items and corpses"
```

---

## Task 9: Non-meshed land/static/multi rendering

**Files:**
- Modify: `src/ClassicUO.Client/Game/GameObjects/Views/LandView.cs`
- Modify: `src/ClassicUO.Client/Game/GameObjects/Views/StaticView.cs`
- Modify: `src/ClassicUO.Client/Game/GameObjects/Views/MultiView.cs`

**Interfaces:**
- Consumes: `PluginHighlightAreas.TryResolve` (Task 3).

These three files cover objects that render via the per-object `Draw()` path rather than the GPU mesh (animated/light-emitting statics, excluded tiles) — the meshed path is Task 10.

- [ ] **Step 1: `LandView.cs`**

`LandView.cs` doesn't currently import `ClassicUO.Game.Managers` — add it alongside the existing usings:

```csharp
using ClassicUO.Game.Managers;
```

Immediately after the existing 3-branch hue chain ends (after line 39, the `else if (World.Player.IsDead...)` closing brace) and before line 41 (`Vector3 hueVec;`), insert:

```csharp
            if (PluginHighlightAreas.TryResolve(X, Y, Z, ClassicUO.PluginApi.HighlightObjectTypes.Land, out ushort pluginLandHue))
            {
                hue = pluginLandHue;
            }

```

- [ ] **Step 2: `StaticView.cs`**

`StaticView.cs` doesn't currently import `ClassicUO.Game.Managers` — add it alongside the existing usings:

```csharp
using ClassicUO.Game.Managers;
```

Immediately after the existing 3-branch hue chain ends (after line 61, the `else if (World.Player.IsDead...)` closing brace) and before line 63 (`bool isTree = ...`), insert:

```csharp
            if (PluginHighlightAreas.TryResolve(X, Y, Z, ClassicUO.PluginApi.HighlightObjectTypes.Static, out ushort pluginStaticHue))
            {
                hue = pluginStaticHue;
                partial = false;
            }

```

- [ ] **Step 3: `MultiView.cs`**

`MultiView.cs` already has `using ClassicUO.Game.Managers;`, so no new using is needed. Immediately after the existing 3-branch hue chain ends (after line 81, the `else if (World.Player.IsDead...)` closing brace) and before line 83 (`bool cot = ...`), insert:

```csharp
            if (PluginHighlightAreas.TryResolve(X, Y, Z, ClassicUO.PluginApi.HighlightObjectTypes.Multi, out ushort pluginMultiHue))
            {
                hue = pluginMultiHue;
                partial = false;
            }

```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded (0 errors).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/GameObjects/Views/LandView.cs src/ClassicUO.Client/Game/GameObjects/Views/StaticView.cs src/ClassicUO.Client/Game/GameObjects/Views/MultiView.cs
git commit -m "feat(plugin): apply area highlight hue to non-meshed land/static/multi"
```

---

## Task 10: Meshed land/static/multi rendering — `ApplyMeshHue`

**Files:**
- Modify: `src/ClassicUO.Client/Game/Scenes/GameSceneDrawingSorting.cs`

**Interfaces:**
- Consumes: `PluginHighlightAreas.TryResolve` (Task 3).

This is the common case — most map terrain/furniture is baked into the per-chunk GPU mesh and never calls `Draw()`. Its hue is recomputed every visible frame in `ApplyMeshHue` (`GameSceneDrawingSorting.cs:644-686`).

- [ ] **Step 1: Insert area-highlight resolution into `ApplyMeshHue`**

`GameSceneDrawingSorting.cs` already has `using ClassicUO.Game.Managers;`, so no new using is needed. In `src/ClassicUO.Client/Game/Scenes/GameSceneDrawingSorting.cs`, in `ApplyMeshHue` (around line 644-686), immediately after the existing `else if (obj is Multi m) { partial = m.ItemData.IsPartialHue; }` branch closes (after line 665) and before `float hueX, hueY;` (line 667), insert:

```csharp
            ClassicUO.PluginApi.HighlightObjectTypes meshType = obj is Land
                ? ClassicUO.PluginApi.HighlightObjectTypes.Land
                : obj is Multi
                    ? ClassicUO.PluginApi.HighlightObjectTypes.Multi
                    : ClassicUO.PluginApi.HighlightObjectTypes.Static;

            if (PluginHighlightAreas.TryResolve(obj.X, obj.Y, obj.Z, meshType, out ushort pluginMeshHue))
            {
                hue = pluginMeshHue;
            }

```

`hue` here is the pre-existing `int hue = obj.Hue;` local (line 647) that the rest of the method already uses to compute `hueX`/`hueY`; a `ushort` result assigns into it implicitly.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded (0 errors).

- [ ] **Step 3: Manual in-game verification**

With a test plugin calling `context.Highlight.AddArea("zone", hue: 0x0066, snap: HighlightSnap.Position, x: <player X>, y: <player Y>, objectTypes: HighlightObjectTypes.Land | HighlightObjectTypes.Static)`, walk near the player's spawn position and confirm the ground tiles and nearby wall/furniture statics within range recolor to `0x0066`, and that untouched tiles just outside the range keep their normal color. Confirm no visible flicker/z-fighting versus the mesh's normal draw.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/Scenes/GameSceneDrawingSorting.cs
git commit -m "feat(plugin): apply area highlight hue to meshed land/static/multi"
```

---

## Task 11: Documentation

**Files:**
- Modify: `src/ClassicUO.PluginApi/README.md`

- [ ] **Step 1: Document `IHighlight` in the plugin API reference**

Add a new section to `src/ClassicUO.PluginApi/README.md` (find the existing sections for `IStatusBars`/`IScreenTimers` and add a matching one for `IHighlight`, following the same structure: short description, method list with parameter meanings, a minimal usage snippet). Example snippet to include:

```csharp
// Tint a specific mobile, losing to poison/paralyze/attacked coloring:
context.Highlight.AddCharacter(mobileSerial, hue: 0x0044);

// Tint a specific mobile, always winning:
context.Highlight.AddCharacter(mobileSerial, hue: 0x0044, priorityHighlight: true);

// Paint a 5x5 zone around a fixed world position for 10 seconds, land + statics only:
context.Highlight.AddArea(
    "danger-zone",
    durationMs: 10000,
    snap: HighlightSnap.Position,
    hue: 0x0021,
    rangeX: 5,
    rangeY: 5,
    objectTypes: HighlightObjectTypes.Land | HighlightObjectTypes.Static,
    x: 1234,
    y: 5678
);

context.Highlight.RemoveArea("danger-zone");
```

- [ ] **Step 2: Commit**

```bash
git add src/ClassicUO.PluginApi/README.md
git commit -m "docs(plugin): document IHighlight"
```

---

## Final verification (after all tasks)

- [ ] Run: `dotnet build ClassicUO.sln -c Debug` — expect 0 errors.
- [ ] Run: `dotnet test tests/ClassicUO.UnitTests` — expect all PASS.
- [ ] Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests` — expect all PASS.
- [ ] Manual smoke test per Tasks 7/8/10's in-game verification steps, in one session, with one test plugin exercising all seven `IHighlight` methods.
