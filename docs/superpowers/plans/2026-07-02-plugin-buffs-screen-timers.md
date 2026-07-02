# Plugin Buffs & Screen Timers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose two v2-plugin features — plugin-controlled buff icons in the existing `BuffGump`, and a plugin-driven on-screen timer overlay — across the `ClassicUO.PluginApi → ClassicUO.BootstrapHost → cuo` boundary.

**Architecture:** Config classes live in `ClassicUO.PluginApi` (managed, plugin↔host only). The BootstrapHost `*Impl` flattens each config into scalar arguments and calls cuo command function pointers appended to the shared `ClientBindings` table. cuo stores state in three new static managers (`PluginBuffs`, `ScreenTimers`, `PluginTimersManager`), ticked once per frame from `World.Update`. Expiry/removal events flow back cuo → host via new `HostBindings` callbacks (pattern: `WalkProgressFn`), fanned to plugins by `RaiseEachPlugin`.

**Tech Stack:** C# / .NET 10, NativeAOT (cuo), function-pointer FFI (`delegate* unmanaged[Cdecl]`), xUnit + `InternalsVisibleTo`, FNA render layer.

## Global Constraints

- License header on every new source file, exact text: `// SPDX-License-Identifier: BSD-2-Clause`
- Only scalars cross the cuo boundary: primitives, plus strings marshaled as a `nint` UTF‑8/ANSI pointer freed by the caller after the call (pattern: `ClientImpl.SetWindowTitle`, `ClientImpl.GetCliloc`).
- cuo is host-agnostic and never references `ClassicUO.PluginApi`; cuo defines its **own** enums whose integer values must match the PluginApi enums.
- New function-pointer fields are **appended to the end** of `HostBindings` and `ClientBindings` in BOTH copies (`src/ClassicUO.Client/PluginHost.cs` and `src/ClassicUO.BootstrapHost/HostBridge.cs`). Never insert in the middle — the two copies are matched positionally and the legacy net472 host must stay ABI-compatible.
- cuo null-checks every event function pointer before invoking (the legacy v1 host leaves them unset).
- All plugin → cuo command impls auto-marshal to the game thread (pattern: `StatusBarsImpl`: call directly when `_bridge.IsGameThread`, else `_bridge.PostToGameThread`).
- Reason integer mapping (shared, both sides): `0 = Expired`, `1 = RemovedByPlugin`, `2 = RemovedByUser`. Matches enum declaration order.
- `DurationMs == 0` means infinite / no expiry for buffs (consistent with server `Timer == 0xFFFF_FFFF`). For timers `DurationMs` is required (> 0).
- No persistence; the plugin re-creates its buffs/timers after relog.
- Match surrounding style: spans/pooled/`unsafe` in hot paths, no free allocation in render/tick loops.

---

## File Structure

**`ClassicUO.PluginApi` (new contract files):**
- `IPluginBuffs.cs` — `IPluginBuffs`, `BuffConfig`, `BuffDisplayKind`, `BuffRemoveReason`
- `IScreenTimers.cs` — `IScreenTimers`, `TimerConfig`, `TimerGroupConfig`, `TimerShape`, `StackDirection`, `PlacementMode`, `TimerRemoveReason`
- `IPluginContext.cs` (modify) — add `Buffs`, `ScreenTimers` properties

**`ClassicUO.Client` (cuo):**
- `Game/Data/BuffIcon.cs` (modify) — add `Kind` + `BuffDisplayKind` enum
- `Game/Managers/PluginBuffs.cs` (new) — plugin buff storage + expiry core
- `Game/Managers/ScreenTimers.cs` (new) — timer + group storage, layout math, remaining%
- `Game/Managers/PluginTimersManager.cs` (new) — central `Update(now)` + event dispatch seam + command targets
- `Game/UI/Gumps/BuffGump.cs` (modify) — merge plugin buffs, generalize entry, tint by `Kind`
- `Game/UI/Gumps/ScreenTimersGump.cs` (new) — top-layer overlay render
- `PluginHost.cs` (modify) — new `ClientBindings`/`HostBindings` fields, bind command targets, wrap event delegates
- `Network/Plugin.cs` (modify) — nothing required (events go through `IPluginHost`); see Task 8
- `Game/World.cs` (modify) — call `PluginTimersManager.Update(Time.Ticks)` each tick

**`ClassicUO.BootstrapHost`:**
- `HostBridge.cs` (modify) — struct fields, `BuildHostBindings`, `OnBuffEvent`/`OnTimerEvent` callbacks
- `PluginContextImpl.cs` (modify) — `BuffsImpl`, `ScreenTimersImpl`, wire props, `RaiseBuffEvent`/`RaiseTimerEvent`

**Tests:**
- `tests/ClassicUO.UnitTests/Game/Managers/PluginBuffsTests.cs`
- `tests/ClassicUO.UnitTests/Game/Managers/ScreenTimersTests.cs`
- `tests/ClassicUO.UnitTests/Game/Managers/PluginTimersManagerTests.cs`
- `tests/ClassicUO.UnitTests/Game/Data/BuffIconKindTests.cs`

---

## Task 1: PluginApi — buffs contract

**Files:**
- Create: `src/ClassicUO.PluginApi/IPluginBuffs.cs`

**Interfaces:**
- Produces: `IPluginBuffs` (`AddOrUpdate(BuffConfig)`, `Remove(int)`, `ClearAll()`, `event Action<int> Expired`, `event Action<int, BuffRemoveReason> Removed`); `BuffConfig`; enums `BuffDisplayKind { None, Buff, Debuff }`, `BuffRemoveReason { Expired, RemovedByPlugin, RemovedByUser }`.

- [ ] **Step 1: Write the contract file**

Create `src/ClassicUO.PluginApi/IPluginBuffs.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace ClassicUO.PluginApi;

/// <summary>Kind of a plugin buff, controlling its tint in the BuffGump.</summary>
public enum BuffDisplayKind { None, Buff, Debuff }

/// <summary>Why a plugin buff was removed.</summary>
public enum BuffRemoveReason { Expired, RemovedByPlugin, RemovedByUser }

/// <summary>Immutable description of a plugin buff. Flattened to scalars by the host.</summary>
public sealed class BuffConfig
{
    public int Id { get; init; }              // required, plugin-chosen key
    public ushort Graphic { get; init; }      // required, gump graphic id
    public int DurationMs { get; init; }      // 0 = infinite
    public BuffDisplayKind Kind { get; init; }
    public string? Text { get; init; }
}

/// <summary>
/// Adds/updates/removes plugin buff icons that render alongside server buffs in
/// the client's BuffGump. All methods auto-marshal to the game thread. The
/// plugin owns lifecycle; buffs are not persisted across relogs.
/// </summary>
public interface IPluginBuffs
{
    /// <summary>Adds a buff, or updates in place when <see cref="BuffConfig.Id"/> already exists.</summary>
    void AddOrUpdate(BuffConfig config);

    /// <summary>Removes the buff with <paramref name="id"/> if present.</summary>
    void Remove(int id);

    /// <summary>Removes every plugin buff owned by this plugin.</summary>
    void ClearAll();

    /// <summary>Raised with the buff id when a buff expires.</summary>
    event Action<int> Expired;

    /// <summary>Raised with the buff id and reason on any removal (including expiry).</summary>
    event Action<int, BuffRemoveReason> Removed;
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.PluginApi/ClassicUO.PluginApi.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.PluginApi/IPluginBuffs.cs
git commit -m "feat(pluginapi): buffs contract (IPluginBuffs, BuffConfig)"
```

---

## Task 2: PluginApi — screen timers contract

**Files:**
- Create: `src/ClassicUO.PluginApi/IScreenTimers.cs`

**Interfaces:**
- Produces: `IScreenTimers` (`DefineGroup(TimerGroupConfig)`, `AddOrUpdate(TimerConfig)`, `Remove(int)`, `RemoveGroup(int)`, `ClearAll()`, `event Action<int> Expired`, `event Action<int, TimerRemoveReason> Removed`); `TimerConfig`; `TimerGroupConfig`; enums `TimerShape { Circle, Bar, Numeric }`, `StackDirection { Down, Up, Right, Left }`, `PlacementMode { Lone, Stacking }`, `TimerRemoveReason { Expired, RemovedByPlugin, RemovedByUser }`.

- [ ] **Step 1: Write the contract file**

Create `src/ClassicUO.PluginApi/IScreenTimers.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace ClassicUO.PluginApi;

/// <summary>Visual shape of a screen timer.</summary>
public enum TimerShape { Circle, Bar, Numeric }

/// <summary>Direction stacking timers grow from their group anchor.</summary>
public enum StackDirection { Down, Up, Right, Left }

/// <summary>Placement intent. Reserved; a <c>GroupId == 0</c> already implies Lone.</summary>
public enum PlacementMode { Lone, Stacking }

/// <summary>Why a screen timer was removed.</summary>
public enum TimerRemoveReason { Expired, RemovedByPlugin, RemovedByUser }

/// <summary>Immutable description of a screen timer. Flattened to scalars by the host.</summary>
public sealed class TimerConfig
{
    public int Id { get; init; }              // required, plugin-chosen key
    public TimerShape Shape { get; init; }    // required
    public int DurationMs { get; init; }      // required, > 0
    public ushort Hue { get; init; }
    public int GroupId { get; init; }         // 0 = lone
    public int X { get; init; }               // used only when GroupId == 0
    public int Y { get; init; }
    public int Width { get; init; }           // 0 = default per shape
    public int Height { get; init; }          // 0 = default per shape
    public string? Label { get; init; }
    public bool ShowTime { get; init; }
}

/// <summary>Layout definition for a stacking timer group.</summary>
public sealed class TimerGroupConfig
{
    public int GroupId { get; init; }         // required, non-zero
    public int X { get; init; }               // group anchor
    public int Y { get; init; }
    public StackDirection Direction { get; init; }
    public int Gap { get; init; }             // pixels between members
}

/// <summary>
/// Plugin-driven on-screen timer overlay. Timers are fixed-position and
/// non-interactive. All methods auto-marshal to the game thread. Setting an
/// existing id restarts that timer with the new duration.
/// </summary>
public interface IScreenTimers
{
    /// <summary>Defines or updates a stacking group's anchor, direction, and gap.</summary>
    void DefineGroup(TimerGroupConfig group);

    /// <summary>Adds a timer, or updates+restarts the running timer when the id exists.</summary>
    void AddOrUpdate(TimerConfig timer);

    /// <summary>Removes the timer with <paramref name="id"/> if present.</summary>
    void Remove(int id);

    /// <summary>Removes every timer belonging to <paramref name="groupId"/> and the group.</summary>
    void RemoveGroup(int groupId);

    /// <summary>Removes every timer and group owned by this plugin.</summary>
    void ClearAll();

    /// <summary>Raised with the timer id when a timer expires.</summary>
    event Action<int> Expired;

    /// <summary>Raised with the timer id and reason on any removal (including expiry).</summary>
    event Action<int, TimerRemoveReason> Removed;
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.PluginApi/ClassicUO.PluginApi.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.PluginApi/IScreenTimers.cs
git commit -m "feat(pluginapi): screen timers contract (IScreenTimers, TimerConfig)"
```

---

## Task 3: PluginApi — expose services on IPluginContext

**Files:**
- Modify: `src/ClassicUO.PluginApi/IPluginContext.cs`

**Interfaces:**
- Consumes: `IPluginBuffs` (Task 1), `IScreenTimers` (Task 2).
- Produces: `IPluginContext.Buffs` (type `IPluginBuffs`), `IPluginContext.ScreenTimers` (type `IScreenTimers`).

- [ ] **Step 1: Add the two properties**

In `src/ClassicUO.PluginApi/IPluginContext.cs`, after the existing `StatusBars` property (line 36), add:

```csharp
    /// <summary>Add/update/remove plugin buff icons in the client's BuffGump.</summary>
    IPluginBuffs Buffs { get; }

    /// <summary>Plugin-driven on-screen timer overlay.</summary>
    IScreenTimers ScreenTimers { get; }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.PluginApi/ClassicUO.PluginApi.csproj -c Debug`
Expected: Build succeeded, 0 errors. (The BootstrapHost impl is added in Task 14; PluginApi alone still compiles because it's just an interface.)

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.PluginApi/IPluginContext.cs
git commit -m "feat(pluginapi): expose Buffs and ScreenTimers on IPluginContext"
```

---

## Task 4: cuo — BuffIcon.Kind

**Files:**
- Modify: `src/ClassicUO.Client/Game/Data/BuffIcon.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Data/BuffIconKindTests.cs`

**Interfaces:**
- Produces: cuo enum `ClassicUO.Game.Data.BuffDisplayKind { None, Buff, Debuff }` (int values match PluginApi); `BuffIcon.Kind` field defaulting to `None` on the server constructor.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Data/BuffIconKindTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Data;
using Xunit;

namespace ClassicUO.UnitTests.Game.Data
{
    public class BuffIconKindTests
    {
        [Fact]
        public void ServerConstructor_DefaultsKindToNone()
        {
            var icon = new BuffIcon(BuffIconType.NightSight, 0x0000, 0, "text");
            Assert.Equal(BuffDisplayKind.None, icon.Kind);
        }

        [Fact]
        public void Kind_EnumValues_MatchPluginApiOrder()
        {
            Assert.Equal(0, (int)BuffDisplayKind.None);
            Assert.Equal(1, (int)BuffDisplayKind.Buff);
            Assert.Equal(2, (int)BuffDisplayKind.Debuff);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~BuffIconKindTests"`
Expected: FAIL — `BuffDisplayKind` / `Kind` do not exist (compile error).

- [ ] **Step 3: Add the enum and field**

In `src/ClassicUO.Client/Game/Data/BuffIcon.cs`, inside `namespace ClassicUO.Game.Data`, add the enum above the class and a settable `Kind` field with a `None` default. Replace the class body's field region so it reads:

```csharp
    internal enum BuffDisplayKind { None, Buff, Debuff }

    internal class BuffIcon : IEquatable<BuffIcon>
    {
        public BuffIcon(BuffIconType type, ushort graphic, long timer, string text)
        {
            Type = type;
            Graphic = graphic;
            Timer = (timer <= 0 ? 0xFFFF_FFFF : Time.Ticks + timer * 1000);
            Text = text;
        }

        public bool Equals(BuffIcon other)
        {
            return other != null && Type == other.Type;
        }

        public readonly ushort Graphic;

        public readonly string Text;

        public readonly long Timer;

        public readonly BuffIconType Type;

        public BuffDisplayKind Kind = BuffDisplayKind.None;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~BuffIconKindTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Data/BuffIcon.cs tests/ClassicUO.UnitTests/Game/Data/BuffIconKindTests.cs
git commit -m "feat(buffs): add BuffDisplayKind to BuffIcon, default None"
```

---

## Task 5: cuo — PluginBuffs storage manager

**Files:**
- Create: `src/ClassicUO.Client/Game/Managers/PluginBuffs.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/PluginBuffsTests.cs`

**Interfaces:**
- Consumes: `ClassicUO.Game.Data.BuffDisplayKind` (Task 4).
- Produces:
  - `readonly struct PluginBuffEntry` with fields `int Id`, `ushort Graphic`, `long ExpiryTicks`, `BuffDisplayKind Kind`, `string Text` (`ExpiryTicks == long.MaxValue` means infinite).
  - `static class PluginBuffs`:
    - `void AddOrUpdate(int id, ushort graphic, int durationMs, BuffDisplayKind kind, string text, long now)`
    - `bool Remove(int id)`
    - `IReadOnlyDictionary<int, PluginBuffEntry> Entries { get; }`
    - `void CollectExpired(long now, List<int> into)`
    - `void Clear()`
    - `void Reset()` (test-only alias of internal clear)

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Managers/PluginBuffsTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class PluginBuffsTests
    {
        public PluginBuffsTests() => PluginBuffs.Reset();

        [Fact]
        public void AddOrUpdate_StoresEntry()
        {
            PluginBuffs.AddOrUpdate(7, 0x1234, 5000, BuffDisplayKind.Buff, "hi", now: 1000);
            Assert.True(PluginBuffs.Entries.TryGetValue(7, out var e));
            Assert.Equal((ushort)0x1234, e.Graphic);
            Assert.Equal(BuffDisplayKind.Buff, e.Kind);
            Assert.Equal(6000, e.ExpiryTicks);
        }

        [Fact]
        public void AddOrUpdate_SameId_OverwritesInPlace()
        {
            PluginBuffs.AddOrUpdate(7, 0x1111, 5000, BuffDisplayKind.Buff, "a", now: 0);
            PluginBuffs.AddOrUpdate(7, 0x2222, 1000, BuffDisplayKind.Debuff, "b", now: 100);
            Assert.Single(PluginBuffs.Entries);
            var e = PluginBuffs.Entries[7];
            Assert.Equal((ushort)0x2222, e.Graphic);
            Assert.Equal(BuffDisplayKind.Debuff, e.Kind);
            Assert.Equal(1100, e.ExpiryTicks);
        }

        [Fact]
        public void AddOrUpdate_ZeroDuration_IsInfinite()
        {
            PluginBuffs.AddOrUpdate(7, 0x1234, 0, BuffDisplayKind.None, "", now: 1000);
            Assert.Equal(long.MaxValue, PluginBuffs.Entries[7].ExpiryTicks);
        }

        [Fact]
        public void Remove_DropsEntry_ReturnsTrue()
        {
            PluginBuffs.AddOrUpdate(7, 0x1234, 5000, BuffDisplayKind.Buff, "hi", now: 0);
            Assert.True(PluginBuffs.Remove(7));
            Assert.False(PluginBuffs.Remove(7));
            Assert.Empty(PluginBuffs.Entries);
        }

        [Fact]
        public void CollectExpired_ReturnsOnlyElapsedFiniteBuffs()
        {
            PluginBuffs.AddOrUpdate(1, 0, 1000, BuffDisplayKind.Buff, "", now: 0);   // expires 1000
            PluginBuffs.AddOrUpdate(2, 0, 0, BuffDisplayKind.Buff, "", now: 0);      // infinite
            PluginBuffs.AddOrUpdate(3, 0, 5000, BuffDisplayKind.Buff, "", now: 0);   // expires 5000

            var due = new List<int>();
            PluginBuffs.CollectExpired(now: 1000, due);
            Assert.Equal(new[] { 1 }, due);
        }

        [Fact]
        public void Clear_EmptiesAll()
        {
            PluginBuffs.AddOrUpdate(1, 0, 1000, BuffDisplayKind.Buff, "", now: 0);
            PluginBuffs.Clear();
            Assert.Empty(PluginBuffs.Entries);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginBuffsTests"`
Expected: FAIL — `PluginBuffs` does not exist (compile error).

- [ ] **Step 3: Write the manager**

Create `src/ClassicUO.Client/Game/Managers/PluginBuffs.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Data;

namespace ClassicUO.Game.Managers
{
    /// <summary>Immutable snapshot of a plugin-owned buff icon.</summary>
    internal readonly struct PluginBuffEntry
    {
        public readonly int Id;
        public readonly ushort Graphic;
        public readonly long ExpiryTicks;   // long.MaxValue == infinite
        public readonly BuffDisplayKind Kind;
        public readonly string Text;

        public PluginBuffEntry(int id, ushort graphic, long expiryTicks, BuffDisplayKind kind, string text)
        {
            Id = id;
            Graphic = graphic;
            ExpiryTicks = expiryTicks;
            Kind = kind;
            Text = text ?? string.Empty;
        }

        public bool IsInfinite => ExpiryTicks == long.MaxValue;
    }

    /// <summary>
    /// Plugin-driven buff icons, keyed by the plugin's int id, rendered
    /// alongside server buffs in <see cref="UI.Gumps.BuffGump"/>. Storage only;
    /// expiry detection and event dispatch live in <see cref="PluginTimersManager"/>.
    /// </summary>
    internal static class PluginBuffs
    {
        private static readonly Dictionary<int, PluginBuffEntry> _buffs = new Dictionary<int, PluginBuffEntry>();

        public static IReadOnlyDictionary<int, PluginBuffEntry> Entries => _buffs;

        public static void AddOrUpdate(int id, ushort graphic, int durationMs, BuffDisplayKind kind, string text, long now)
        {
            long expiry = durationMs <= 0 ? long.MaxValue : now + durationMs;
            _buffs[id] = new PluginBuffEntry(id, graphic, expiry, kind, text);
        }

        public static bool Remove(int id) => _buffs.Remove(id);

        public static void CollectExpired(long now, List<int> into)
        {
            foreach (var kv in _buffs)
            {
                var e = kv.Value;
                if (!e.IsInfinite && now >= e.ExpiryTicks)
                    into.Add(e.Id);
            }
        }

        public static void Clear() => _buffs.Clear();

        /// <summary>Test-only: drop all entries so tests start clean.</summary>
        public static void Reset() => _buffs.Clear();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginBuffsTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginBuffs.cs tests/ClassicUO.UnitTests/Game/Managers/PluginBuffsTests.cs
git commit -m "feat(buffs): PluginBuffs storage manager with expiry query"
```

---

## Task 6: cuo — ScreenTimers storage + layout math

**Files:**
- Create: `src/ClassicUO.Client/Game/Managers/ScreenTimers.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/ScreenTimersTests.cs`

**Interfaces:**
- Produces:
  - cuo enums `TimerShape { Circle, Bar, Numeric }`, `StackDirection { Down, Up, Right, Left }` (int values match PluginApi).
  - `struct ScreenTimerEntry` — mutable fields `int Id`, `TimerShape Shape`, `long StartTicks`, `int DurationMs`, `ushort Hue`, `int GroupId`, `int X`, `int Y`, `int Width`, `int Height`, `string Label`, `bool ShowTime`, and `int Order` (insertion order within group).
  - `readonly struct TimerGroup` — `int GroupId`, `int X`, `int Y`, `StackDirection Direction`, `int Gap`.
  - `static class ScreenTimers`:
    - `void DefineGroup(int groupId, int x, int y, StackDirection dir, int gap)`
    - `void AddOrUpdate(int id, TimerShape shape, int durationMs, ushort hue, int groupId, int x, int y, int width, int height, string label, bool showTime, long now)`
    - `bool Remove(int id)`
    - `void RemoveGroup(int groupId, List<int> removedIdsInto)`
    - `void Clear()` / `void Reset()`
    - `IReadOnlyDictionary<int, ScreenTimerEntry> Entries { get; }`
    - `bool TryGetGroup(int groupId, out TimerGroup group)`
    - `void CollectExpired(long now, List<int> into)`
    - `static float RemainingFraction(in ScreenTimerEntry e, long now)` → clamped `[0,1]`
    - `static (int x, int y) ComputePosition(in ScreenTimerEntry e, in TimerGroup group, int extent)` — pure layout: lone uses entry X/Y; grouped offsets `Order * (extent + group.Gap)` along `Direction` from the group anchor.
    - `static int DefaultExtent(TimerShape shape, in StackDirection dir, int width, int height)` — per-shape default size used as the stacking extent when width/height are 0.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Managers/ScreenTimersTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class ScreenTimersTests
    {
        public ScreenTimersTests() => ScreenTimers.Reset();

        [Fact]
        public void AddOrUpdate_StoresEntry_WithStartAndOrder()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 33, groupId: 0,
                x: 10, y: 20, width: 0, height: 0, label: "L", showTime: true, now: 1000);

            Assert.True(ScreenTimers.Entries.TryGetValue(1, out var e));
            Assert.Equal(TimerShape.Bar, e.Shape);
            Assert.Equal(1000, e.StartTicks);
            Assert.Equal(0, e.Order);
        }

        [Fact]
        public void AddOrUpdate_SameId_RestartsTimer_KeepsOrder()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 5000, 0, 5, 0, 0, 0, 0, null, false, now: 0);
            int order0 = ScreenTimers.Entries[1].Order;
            ScreenTimers.AddOrUpdate(1, TimerShape.Circle, 2000, 0, 5, 0, 0, 0, 0, null, false, now: 500);

            var e = ScreenTimers.Entries[1];
            Assert.Equal(TimerShape.Circle, e.Shape);
            Assert.Equal(500, e.StartTicks);      // restarted
            Assert.Equal(2000, e.DurationMs);
            Assert.Equal(order0, e.Order);        // order preserved
        }

        [Fact]
        public void AddOrUpdate_AssignsIncreasingOrderWithinGroup()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, groupId: 9, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 1000, 0, groupId: 9, 0, 0, 0, 0, null, false, now: 0);
            Assert.Equal(0, ScreenTimers.Entries[1].Order);
            Assert.Equal(1, ScreenTimers.Entries[2].Order);
        }

        [Fact]
        public void RemainingFraction_IsClampedZeroToOne()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, 0, 0, 0, 0, 0, null, false, now: 0);
            var e = ScreenTimers.Entries[1];
            Assert.Equal(1f, ScreenTimers.RemainingFraction(e, 0));
            Assert.Equal(0.5f, ScreenTimers.RemainingFraction(e, 500), 3);
            Assert.Equal(0f, ScreenTimers.RemainingFraction(e, 1000));
            Assert.Equal(0f, ScreenTimers.RemainingFraction(e, 5000)); // past expiry clamps
        }

        [Fact]
        public void CollectExpired_ReturnsElapsedTimers()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, 0, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 5000, 0, 0, 0, 0, 0, 0, null, false, now: 0);
            var due = new List<int>();
            ScreenTimers.CollectExpired(1000, due);
            Assert.Equal(new[] { 1 }, due);
        }

        [Fact]
        public void ComputePosition_Lone_UsesEntryXY()
        {
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, groupId: 0, x: 40, y: 60, 0, 0, null, false, now: 0);
            var e = ScreenTimers.Entries[1];
            var group = default(TimerGroup);
            var (x, y) = ScreenTimers.ComputePosition(e, group, extent: 20);
            Assert.Equal((40, 60), (x, y));
        }

        [Fact]
        public void ComputePosition_GroupDown_OffsetsByOrderTimesExtentPlusGap()
        {
            ScreenTimers.DefineGroup(9, x: 100, y: 200, StackDirection.Down, gap: 5);
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0); // order 0
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0); // order 1

            Assert.True(ScreenTimers.TryGetGroup(9, out var g));
            var p0 = ScreenTimers.ComputePosition(ScreenTimers.Entries[1], g, extent: 20);
            var p1 = ScreenTimers.ComputePosition(ScreenTimers.Entries[2], g, extent: 20);
            Assert.Equal((100, 200), p0);
            Assert.Equal((100, 200 + 1 * (20 + 5)), p1);
        }

        [Fact]
        public void ComputePosition_GroupUpAndLeft_UseNegativeOffsets()
        {
            ScreenTimers.DefineGroup(9, 100, 200, StackDirection.Up, gap: 5);
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.TryGetGroup(9, out var gUp);
            Assert.Equal((100, 200 - 1 * (20 + 5)), ScreenTimers.ComputePosition(ScreenTimers.Entries[2], gUp, 20));

            ScreenTimers.DefineGroup(8, 300, 400, StackDirection.Left, gap: 2);
            ScreenTimers.AddOrUpdate(3, TimerShape.Bar, 1000, 0, 8, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(4, TimerShape.Bar, 1000, 0, 8, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.TryGetGroup(8, out var gLeft);
            Assert.Equal((300 - 1 * (30 + 2), 400), ScreenTimers.ComputePosition(ScreenTimers.Entries[4], gLeft, 30));
        }

        [Fact]
        public void RemoveGroup_RemovesMembersAndGroup()
        {
            ScreenTimers.DefineGroup(9, 0, 0, StackDirection.Down, 0);
            ScreenTimers.AddOrUpdate(1, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(2, TimerShape.Bar, 1000, 0, 9, 0, 0, 0, 0, null, false, now: 0);
            ScreenTimers.AddOrUpdate(3, TimerShape.Bar, 1000, 0, 0, 0, 0, 0, 0, null, false, now: 0); // lone

            var removed = new List<int>();
            ScreenTimers.RemoveGroup(9, removed);

            Assert.Equal(new[] { 1, 2 }, removed);
            Assert.False(ScreenTimers.TryGetGroup(9, out _));
            Assert.True(ScreenTimers.Entries.ContainsKey(3));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ScreenTimersTests"`
Expected: FAIL — `ScreenTimers` does not exist (compile error).

- [ ] **Step 3: Write the manager**

Create `src/ClassicUO.Client/Game/Managers/ScreenTimers.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace ClassicUO.Game.Managers
{
    internal enum TimerShape { Circle, Bar, Numeric }
    internal enum StackDirection { Down, Up, Right, Left }

    internal struct ScreenTimerEntry
    {
        public int Id;
        public TimerShape Shape;
        public long StartTicks;
        public int DurationMs;
        public ushort Hue;
        public int GroupId;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public string Label;
        public bool ShowTime;
        public int Order;   // insertion order within its group
    }

    internal readonly struct TimerGroup
    {
        public readonly int GroupId;
        public readonly int X;
        public readonly int Y;
        public readonly StackDirection Direction;
        public readonly int Gap;

        public TimerGroup(int groupId, int x, int y, StackDirection direction, int gap)
        {
            GroupId = groupId;
            X = x;
            Y = y;
            Direction = direction;
            Gap = gap;
        }
    }

    /// <summary>
    /// Plugin-driven screen timers keyed by int id, plus their stacking groups.
    /// Storage + pure layout math only; expiry detection and event dispatch live
    /// in <see cref="PluginTimersManager"/>. Layout is a pure function of the
    /// entry, its group, and the member extent, so it is unit-testable without
    /// rendering.
    /// </summary>
    internal static class ScreenTimers
    {
        private const int DefaultBarW = 120, DefaultBarH = 14;
        private const int DefaultCircle = 32;
        private const int DefaultNumericW = 40, DefaultNumericH = 20;

        private static readonly Dictionary<int, ScreenTimerEntry> _timers = new Dictionary<int, ScreenTimerEntry>();
        private static readonly Dictionary<int, TimerGroup> _groups = new Dictionary<int, TimerGroup>();
        private static int _nextOrder;

        public static IReadOnlyDictionary<int, ScreenTimerEntry> Entries => _timers;

        public static void DefineGroup(int groupId, int x, int y, StackDirection dir, int gap)
        {
            if (groupId == 0)
                return;
            _groups[groupId] = new TimerGroup(groupId, x, y, dir, gap);
        }

        public static bool TryGetGroup(int groupId, out TimerGroup group) => _groups.TryGetValue(groupId, out group);

        public static void AddOrUpdate(int id, TimerShape shape, int durationMs, ushort hue, int groupId,
                                       int x, int y, int width, int height, string label, bool showTime, long now)
        {
            int order;
            if (_timers.TryGetValue(id, out var existing))
                order = existing.Order;          // update in place, keep stack slot
            else
                order = _nextOrder++;            // new -> append to end of stack order

            _timers[id] = new ScreenTimerEntry
            {
                Id = id,
                Shape = shape,
                StartTicks = now,                // set/restart
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
            };
        }

        public static bool Remove(int id) => _timers.Remove(id);

        public static void RemoveGroup(int groupId, List<int> removedIdsInto)
        {
            foreach (var kv in _timers)
                if (kv.Value.GroupId == groupId)
                    removedIdsInto.Add(kv.Key);

            foreach (var id in removedIdsInto)
                _timers.Remove(id);

            _groups.Remove(groupId);
        }

        public static void CollectExpired(long now, List<int> into)
        {
            foreach (var kv in _timers)
            {
                var e = kv.Value;
                if (e.DurationMs > 0 && now >= e.StartTicks + e.DurationMs)
                    into.Add(e.Id);
            }
        }

        public static void Clear()
        {
            _timers.Clear();
            _groups.Clear();
            _nextOrder = 0;
        }

        /// <summary>Test-only: reset all state.</summary>
        public static void Reset() => Clear();

        public static float RemainingFraction(in ScreenTimerEntry e, long now)
        {
            if (e.DurationMs <= 0)
                return 0f;
            float f = 1f - (now - e.StartTicks) / (float)e.DurationMs;
            if (f < 0f) return 0f;
            if (f > 1f) return 1f;
            return f;
        }

        public static int DefaultExtent(TimerShape shape, in StackDirection dir, int width, int height)
        {
            bool vertical = dir == StackDirection.Down || dir == StackDirection.Up;
            (int w, int h) = DefaultSize(shape);
            if (width > 0) w = width;
            if (height > 0) h = height;
            return vertical ? h : w;
        }

        public static (int w, int h) DefaultSize(TimerShape shape) => shape switch
        {
            TimerShape.Bar => (DefaultBarW, DefaultBarH),
            TimerShape.Circle => (DefaultCircle, DefaultCircle),
            _ => (DefaultNumericW, DefaultNumericH),
        };

        public static (int x, int y) ComputePosition(in ScreenTimerEntry e, in TimerGroup group, int extent)
        {
            if (e.GroupId == 0 || group.GroupId == 0)
                return (e.X, e.Y);

            int step = e.Order * (extent + group.Gap);
            return group.Direction switch
            {
                StackDirection.Down  => (group.X, group.Y + step),
                StackDirection.Up    => (group.X, group.Y - step),
                StackDirection.Right => (group.X + step, group.Y),
                StackDirection.Left  => (group.X - step, group.Y),
                _ => (group.X, group.Y),
            };
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ScreenTimersTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/ScreenTimers.cs tests/ClassicUO.UnitTests/Game/Managers/ScreenTimersTests.cs
git commit -m "feat(timers): ScreenTimers storage manager and pure layout math"
```

---

## Task 7: cuo — PluginTimersManager central tick + event dispatch

**Files:**
- Create: `src/ClassicUO.Client/Game/Managers/PluginTimersManager.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/PluginTimersManagerTests.cs`

**Interfaces:**
- Consumes: `PluginBuffs` (Task 5), `ScreenTimers` (Task 6).
- Produces `static class PluginTimersManager`:
  - reason constants `const int ReasonExpired = 0, ReasonRemovedByPlugin = 1, ReasonRemovedByUser = 2`
  - test seams `internal static Action<int,int> BuffEventSink;` and `internal static Action<int,int> TimerEventSink;` (each `(id, reasonInt)`)
  - `internal static Action GumpRefresh;` — invoked after buff-set changes so an open BuffGump rebuilds (defaults null; wired in Task 11)
  - `void Update(long now)` — collects expired buffs+timers, removes them, dispatches `Expired`, refreshes gump
  - `void RaiseBuffEvent(int id, int reason)` / `void RaiseTimerEvent(int id, int reason)` — route to sink, else to `Client.Game.PluginHost`
  - `void Reset()` (test-only): clears seams + both managers

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Managers/PluginTimersManagerTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class PluginTimersManagerTests
    {
        public PluginTimersManagerTests() => PluginTimersManager.Reset();

        [Fact]
        public void Update_ExpiresBuff_RemovesAndDispatchesExpired()
        {
            var events = new List<(int id, int reason)>();
            PluginTimersManager.BuffEventSink = (id, reason) => events.Add((id, reason));

            PluginBuffs.AddOrUpdate(5, 0x10, 1000, BuffDisplayKind.Buff, "", now: 0);
            PluginTimersManager.Update(now: 1000);

            Assert.False(PluginBuffs.Entries.ContainsKey(5));
            Assert.Equal(new[] { (5, PluginTimersManager.ReasonExpired) }, events);
        }

        [Fact]
        public void Update_ExpiresTimer_RemovesAndDispatchesExpired()
        {
            var events = new List<(int id, int reason)>();
            PluginTimersManager.TimerEventSink = (id, reason) => events.Add((id, reason));

            ScreenTimers.AddOrUpdate(3, TimerShape.Bar, 1000, 0, 0, 0, 0, 0, 0, null, false, now: 0);
            PluginTimersManager.Update(now: 1000);

            Assert.False(ScreenTimers.Entries.ContainsKey(3));
            Assert.Equal(new[] { (3, PluginTimersManager.ReasonExpired) }, events);
        }

        [Fact]
        public void Update_LeavesLiveEntriesUntouched()
        {
            var buffEvents = new List<(int, int)>();
            PluginTimersManager.BuffEventSink = (id, reason) => buffEvents.Add((id, reason));
            PluginBuffs.AddOrUpdate(5, 0x10, 5000, BuffDisplayKind.Buff, "", now: 0);

            PluginTimersManager.Update(now: 1000);

            Assert.True(PluginBuffs.Entries.ContainsKey(5));
            Assert.Empty(buffEvents);
        }

        [Fact]
        public void RaiseBuffEvent_UsesSinkWhenSet()
        {
            var events = new List<(int, int)>();
            PluginTimersManager.BuffEventSink = (id, reason) => events.Add((id, reason));
            PluginTimersManager.RaiseBuffEvent(9, PluginTimersManager.ReasonRemovedByPlugin);
            Assert.Equal(new[] { (9, PluginTimersManager.ReasonRemovedByPlugin) }, events);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginTimersManagerTests"`
Expected: FAIL — `PluginTimersManager` does not exist (compile error).

- [ ] **Step 3: Write the manager**

Create `src/ClassicUO.Client/Game/Managers/PluginTimersManager.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Single per-frame driver for both plugin buffs and screen timers. Called
    /// from <see cref="World.Update"/> regardless of whether any gump is open.
    /// Detects expiry, removes entries, and dispatches events back to the plugin
    /// host. Event routing goes through test seams so the pure logic is testable
    /// without a native host.
    /// </summary>
    internal static class PluginTimersManager
    {
        public const int ReasonExpired = 0;
        public const int ReasonRemovedByPlugin = 1;
        public const int ReasonRemovedByUser = 2;

        // Test seams; when null, events route to the real plugin host.
        internal static Action<int, int> BuffEventSink;
        internal static Action<int, int> TimerEventSink;

        // Wired by BuffGump so an open gump rebuilds after a set change.
        internal static Action GumpRefresh;

        private static readonly List<int> _expiredScratch = new List<int>();

        public static void Update(long now)
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
        }

        public static void RaiseBuffEvent(int id, int reason)
        {
            if (BuffEventSink != null)
                BuffEventSink(id, reason);
            else
                Client.Game?.PluginHost?.BuffEvent(id, reason);
        }

        public static void RaiseTimerEvent(int id, int reason)
        {
            if (TimerEventSink != null)
                TimerEventSink(id, reason);
            else
                Client.Game?.PluginHost?.TimerEvent(id, reason);
        }

        /// <summary>Test-only: clear seams and both stores.</summary>
        public static void Reset()
        {
            BuffEventSink = null;
            TimerEventSink = null;
            GumpRefresh = null;
            PluginBuffs.Reset();
            ScreenTimers.Reset();
        }
    }
}
```

> Note: `Client.Game.PluginHost.BuffEvent/TimerEvent` do not exist yet — they are added in Task 8. Because the tests set the sinks, the `else` branch is not exercised until then, but the code must still compile. Task 8 adds those `IPluginHost` methods; if you run Task 7's build before Task 8, temporarily nothing references the methods at compile time only if they exist. **Therefore complete Task 8's interface additions in the same working session before building the full client.** For the unit test run in Step 4, `dotnet test tests/ClassicUO.UnitTests` compiles the client project, so Task 8's `IPluginHost` methods MUST already exist. Reorder if needed: do Task 8 Step 1–3 (interface + stub) before Task 7 Step 4, or add the two `IPluginHost` method signatures now as part of this task.

- [ ] **Step 3b: Add the two IPluginHost method signatures (prereq for compile)**

In `src/ClassicUO.Client/PluginHost.cs`, in the `interface IPluginHost` (near line 395), add:

```csharp
        public void BuffEvent(int id, int reason);
        public void TimerEvent(int id, int reason);
```

And add stub implementations to `UnmanagedAssistantHost` (full delegate wiring comes in Task 8) so the class still satisfies the interface — add near the other public methods (e.g. after `WalkProgress`):

```csharp
        public void BuffEvent(int id, int reason) { }
        public void TimerEvent(int id, int reason) { }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginTimersManagerTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginTimersManager.cs src/ClassicUO.Client/PluginHost.cs tests/ClassicUO.UnitTests/Game/Managers/PluginTimersManagerTests.cs
git commit -m "feat(timers): PluginTimersManager central tick and event dispatch"
```

---

## Task 8: cuo — event channel (cuo → host callbacks)

**Files:**
- Modify: `src/ClassicUO.Client/PluginHost.cs`

**Interfaces:**
- Consumes: `HostBindings` struct, `UnmanagedAssistantHost`, `IPluginHost` (existing).
- Produces: `HostBindings.BuffEventFn`, `HostBindings.TimerEventFn` (appended last); wrapped delegates `_buffEvent`, `_timerEvent`; real bodies for `UnmanagedAssistantHost.BuffEvent`/`TimerEvent` invoking those delegates null-checked.

- [ ] **Step 1: Append the two event fields to the cuo HostBindings struct**

In `src/ClassicUO.Client/PluginHost.cs`, in `struct HostBindings` (ends at `WalkProgressFn` line 32), append AFTER `WalkProgressFn`:

```csharp
        public IntPtr /*delegate*<int, int, void>*/ BuffEventFn;
        public IntPtr /*delegate*<int, int, void>*/ TimerEventFn;
```

- [ ] **Step 2: Declare the delegate type and fields**

In `UnmanagedAssistantHost`, near the other event delegate declarations (after `_walkProgress`, around line 104), add:

```csharp
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dOnPluginTimerEvent(int id, int reason);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dOnPluginTimerEvent _buffEvent, _timerEvent;
```

- [ ] **Step 3: Wrap the pointers in the constructor**

In the `UnmanagedAssistantHost(HostBindings* setup)` constructor (after the `_cmdList` assignment, line 139), add:

```csharp
            if (setup->BuffEventFn != IntPtr.Zero)
                _buffEvent = Marshal.GetDelegateForFunctionPointer<dOnPluginTimerEvent>(setup->BuffEventFn);
            if (setup->TimerEventFn != IntPtr.Zero)
                _timerEvent = Marshal.GetDelegateForFunctionPointer<dOnPluginTimerEvent>(setup->TimerEventFn);
```

- [ ] **Step 4: Replace the Task-7 stubs with real bodies**

Replace the two stub methods added in Task 7:

```csharp
        public void BuffEvent(int id, int reason) { }
        public void TimerEvent(int id, int reason) { }
```

with:

```csharp
        public void BuffEvent(int id, int reason)
        {
            _buffEvent?.Invoke(id, reason);
        }

        public void TimerEvent(int id, int reason)
        {
            _timerEvent?.Invoke(id, reason);
        }
```

- [ ] **Step 5: Build the client**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/PluginHost.cs
git commit -m "feat(plugins): cuo->host BuffEvent/TimerEvent callback channel"
```

---

## Task 9: cuo — command targets (host → cuo)

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginTimersManager.cs`
- Modify: `src/ClassicUO.Client/PluginHost.cs`

**Interfaces:**
- Consumes: `PluginBuffs`, `ScreenTimers`, `PluginTimersManager` (Tasks 5–7); `ClientBindings` struct + `Initialize()` (existing).
- Produces static command targets on `PluginTimersManager` (bound into `ClientBindings`):
  - `void AddBuff(int id, ushort graphic, int durationMs, int kind, IntPtr textUtf8)`
  - `void RemoveBuff(int id)`
  - `void ClearBuffs()`
  - `void DefineTimerGroup(int groupId, int x, int y, int direction, int gap)`
  - `void AddTimer(int id, int shape, int durationMs, ushort hue, int groupId, int x, int y, int width, int height, IntPtr labelUtf8, byte showTime)`
  - `void RemoveTimer(int id)`
  - `void RemoveTimerGroup(int groupId)`
  - `void ClearTimers()`
  - `ClientBindings` fields (appended): `AddBuffFn, RemoveBuffFn, ClearBuffsFn, DefineTimerGroupFn, AddTimerFn, RemoveTimerFn, RemoveTimerGroupFn, ClearTimersFn`.

- [ ] **Step 1: Add static command targets to PluginTimersManager**

In `src/ClassicUO.Client/Game/Managers/PluginTimersManager.cs`, add `using System.Runtime.InteropServices;` at the top, then add these methods inside the class (they read `Time.Ticks`, marshal strings, and route user/plugin removals through the event channel):

```csharp
        private static string PtrToString(IntPtr p) => p == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringAnsi(p) ?? string.Empty);

        // ── Buff commands (bound into ClientBindings) ──
        public static void AddBuff(int id, ushort graphic, int durationMs, int kind, IntPtr textUtf8)
        {
            PluginBuffs.AddOrUpdate(id, graphic, durationMs, (Data.BuffDisplayKind)kind, PtrToString(textUtf8), Time.Ticks);
            GumpRefresh?.Invoke();
        }

        public static void RemoveBuff(int id)
        {
            if (PluginBuffs.Remove(id))
            {
                RaiseBuffEvent(id, ReasonRemovedByPlugin);
                GumpRefresh?.Invoke();
            }
        }

        public static void ClearBuffs()
        {
            var ids = new List<int>(PluginBuffs.Entries.Keys);
            PluginBuffs.Clear();
            foreach (int id in ids)
                RaiseBuffEvent(id, ReasonRemovedByPlugin);
            GumpRefresh?.Invoke();
        }

        // ── Timer commands (bound into ClientBindings) ──
        public static void DefineTimerGroup(int groupId, int x, int y, int direction, int gap)
        {
            ScreenTimers.DefineGroup(groupId, x, y, (StackDirection)direction, gap);
        }

        public static void AddTimer(int id, int shape, int durationMs, ushort hue, int groupId,
                                    int x, int y, int width, int height, IntPtr labelUtf8, byte showTime)
        {
            ScreenTimers.AddOrUpdate(id, (TimerShape)shape, durationMs, hue, groupId,
                x, y, width, height, PtrToString(labelUtf8), showTime != 0, Time.Ticks);
        }

        public static void RemoveTimer(int id)
        {
            if (ScreenTimers.Remove(id))
                RaiseTimerEvent(id, ReasonRemovedByPlugin);
        }

        public static void RemoveTimerGroup(int groupId)
        {
            var removed = new List<int>();
            ScreenTimers.RemoveGroup(groupId, removed);
            foreach (int id in removed)
                RaiseTimerEvent(id, ReasonRemovedByPlugin);
        }

        public static void ClearTimers()
        {
            var ids = new List<int>(ScreenTimers.Entries.Keys);
            ScreenTimers.Clear();
            foreach (int id in ids)
                RaiseTimerEvent(id, ReasonRemovedByPlugin);
        }
```

> `Data.BuffDisplayKind` is `ClassicUO.Game.Data.BuffDisplayKind`; `PluginTimersManager` is in `ClassicUO.Game.Managers`, so `Data.BuffDisplayKind` resolves via the parent `ClassicUO.Game` namespace. If it does not resolve, add `using ClassicUO.Game.Data;` and use `BuffDisplayKind` directly.

- [ ] **Step 2: Append fields to the cuo ClientBindings struct**

In `src/ClassicUO.Client/PluginHost.cs`, in `struct ClientBindings` (ends at `SetOverlayFn` line 51), append AFTER `SetOverlayFn`:

```csharp
        public IntPtr /*delegate*<int, ushort, int, int, IntPtr, void>*/ AddBuffFn;
        public IntPtr /*delegate*<int, void>*/ RemoveBuffFn;
        public IntPtr /*delegate*<void>*/ ClearBuffsFn;
        public IntPtr /*delegate*<int, int, int, int, int, void>*/ DefineTimerGroupFn;
        public IntPtr /*delegate*<int, int, int, ushort, int, int, int, int, int, IntPtr, byte, void>*/ AddTimerFn;
        public IntPtr /*delegate*<int, void>*/ RemoveTimerFn;
        public IntPtr /*delegate*<int, void>*/ RemoveTimerGroupFn;
        public IntPtr /*delegate*<void>*/ ClearTimersFn;
```

- [ ] **Step 3: Declare binding delegates + static targets**

In `UnmanagedAssistantHost`, near the `_setOverlay` field (after line 254), add:

```csharp
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dAddBuff(int id, ushort graphic, int durationMs, int kind, IntPtr textUtf8);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dAddBuff _addBuff = Game.Managers.PluginTimersManager.AddBuff;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dRemoveById(int id);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dRemoveById _removeBuff = Game.Managers.PluginTimersManager.RemoveBuff;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dNoArg();
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dNoArg _clearBuffs = Game.Managers.PluginTimersManager.ClearBuffs;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dDefineTimerGroup(int groupId, int x, int y, int direction, int gap);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dDefineTimerGroup _defineTimerGroup = Game.Managers.PluginTimersManager.DefineTimerGroup;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dAddTimer(int id, int shape, int durationMs, ushort hue, int groupId, int x, int y, int width, int height, IntPtr labelUtf8, byte showTime);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dAddTimer _addTimer = Game.Managers.PluginTimersManager.AddTimer;

        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dRemoveById _removeTimer = Game.Managers.PluginTimersManager.RemoveTimer;

        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dRemoveById _removeTimerGroup = Game.Managers.PluginTimersManager.RemoveTimerGroup;

        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dNoArg _clearTimers = Game.Managers.PluginTimersManager.ClearTimers;
```

- [ ] **Step 4: Set the pointers in Initialize()**

In `Initialize()` (after the `cuoHost.SetOverlayFn = ...` line 326), add:

```csharp
            cuoHost.AddBuffFn = Marshal.GetFunctionPointerForDelegate(_addBuff);
            cuoHost.RemoveBuffFn = Marshal.GetFunctionPointerForDelegate(_removeBuff);
            cuoHost.ClearBuffsFn = Marshal.GetFunctionPointerForDelegate(_clearBuffs);
            cuoHost.DefineTimerGroupFn = Marshal.GetFunctionPointerForDelegate(_defineTimerGroup);
            cuoHost.AddTimerFn = Marshal.GetFunctionPointerForDelegate(_addTimer);
            cuoHost.RemoveTimerFn = Marshal.GetFunctionPointerForDelegate(_removeTimer);
            cuoHost.RemoveTimerGroupFn = Marshal.GetFunctionPointerForDelegate(_removeTimerGroup);
            cuoHost.ClearTimersFn = Marshal.GetFunctionPointerForDelegate(_clearTimers);
```

- [ ] **Step 5: Build the client**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginTimersManager.cs src/ClassicUO.Client/PluginHost.cs
git commit -m "feat(plugins): host->cuo buff/timer command bindings"
```

---

## Task 10: cuo — drive the tick from World.Update

**Files:**
- Modify: `src/ClassicUO.Client/Game/World.cs`

**Interfaces:**
- Consumes: `PluginTimersManager.Update(long)` (Task 7).

- [ ] **Step 1: Call the central tick each frame**

In `src/ClassicUO.Client/Game/World.cs`, at the top of `public void Update()` (line 279), before the `if (Player != null)` block, add:

```csharp
            Managers.PluginTimersManager.Update(Time.Ticks);
```

> If `Managers` does not resolve, add `using ClassicUO.Game.Managers;` to the file's usings and call `PluginTimersManager.Update(Time.Ticks);`. Expiry must run independently of `Player`, so it goes above the `Player != null` guard.

- [ ] **Step 2: Build the client**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Client/Game/World.cs
git commit -m "feat(timers): tick PluginTimersManager from World.Update"
```

---

## Task 11: cuo — render plugin buffs in BuffGump (merge + tint)

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/BuffGump.cs`

**Interfaces:**
- Consumes: `PluginBuffs.Entries` / `PluginBuffEntry` (Task 5); `BuffDisplayKind` (Task 4); `PluginTimersManager.GumpRefresh` (Task 7).
- Produces: `BuffGump` renders server + plugin buffs; `BuffControlEntry` accepts a neutral input carrying graphic/expiry/text/kind; `Debuff` → red tint, `Buff` → green tint, `None` → unchanged.

- [ ] **Step 1: Add a neutral input struct**

In `src/ClassicUO.Client/Game/UI/Gumps/BuffGump.cs`, inside `namespace ClassicUO.Game.UI.Gumps` (above `internal class BuffGump`), add:

```csharp
    /// <summary>Rendering input for one buff icon, from either a server BuffIcon or a plugin buff.</summary>
    internal readonly struct BuffEntryInput
    {
        public readonly ushort Graphic;
        public readonly long Timer;          // 0xFFFF_FFFF == infinite
        public readonly string Text;
        public readonly Data.BuffDisplayKind Kind;
        public readonly string TooltipId;    // "ID: <type>" or "ID: <pluginId>"

        public BuffEntryInput(ushort graphic, long timer, string text, Data.BuffDisplayKind kind, string tooltipId)
        {
            Graphic = graphic;
            Timer = timer;
            Text = text ?? string.Empty;
            Kind = kind;
            TooltipId = tooltipId ?? string.Empty;
        }
    }
```

- [ ] **Step 2: Merge both sources in BuildGump**

Replace the server-only loop (lines 101–107):

```csharp
            if (World.Player != null)
            {
                foreach (var k in World.Player.BuffIcons)
                {
                    _box.Add(new BuffControlEntry(World.Player.BuffIcons[k.Key]));
                }
            }
```

with a merged loop:

```csharp
            if (World.Player != null)
            {
                foreach (var k in World.Player.BuffIcons)
                {
                    BuffIcon icon = World.Player.BuffIcons[k.Key];
                    _box.Add(new BuffControlEntry(new BuffEntryInput(
                        icon.Graphic, icon.Timer, icon.Text, icon.Kind, $"ID: {icon.Type}")));
                }
            }

            foreach (var kv in Managers.PluginBuffs.Entries)
            {
                var e = kv.Value;
                long timer = e.IsInfinite ? 0xFFFF_FFFF : e.ExpiryTicks;
                _box.Add(new BuffControlEntry(new BuffEntryInput(
                    e.Graphic, timer, e.Text, e.Kind, $"ID: {e.Id}")));
            }
```

- [ ] **Step 3: Rewrite BuffControlEntry to take BuffEntryInput and tint by Kind**

Replace the `BuffControlEntry` constructor signature and its `Icon` usages. Change the constructor (line 291) from `public BuffControlEntry(BuffIcon icon) : base(0, 0, icon.Graphic, 0)` to accept the struct, store the fields locally, and drop the `BuffIcon Icon { get; }` property. Concretely, replace the field/ctor/property region:

```csharp
        private class BuffControlEntry : GumpPic
        {
            private byte _alpha;
            private bool _decreaseAlpha;
            private readonly RenderedText _gText;
            private float _updateTooltipTime;
            private readonly long _timer;
            private readonly string _text;
            private readonly string _tooltipId;
            private readonly Data.BuffDisplayKind _kind;

            public BuffControlEntry(BuffEntryInput input) : base(0, 0, input.Graphic, 0)
            {
                if (IsDisposed)
                {
                    return;
                }

                _timer = input.Timer;
                _text = input.Text;
                _tooltipId = input.TooltipId;
                _kind = input.Kind;
                _alpha = 0xFF;
                _decreaseAlpha = true;

                _gText = RenderedText.Create(
                    "",
                    0xFFFF,
                    2,
                    true,
                    FontStyle.Fixed | FontStyle.BlackBorder,
                    TEXT_ALIGN_TYPE.TS_CENTER,
                    Width
                );

                AcceptMouseInput = true;
                WantUpdateSize = false;
                CanMove = true;

                SetTooltip(_text + "\n" + _tooltipId);
            }
```

In `Update()`, replace every `Icon.Timer` with `_timer`, every `Icon.Text + $"\nID: {Icon.Type}"` with `_text + "\n" + _tooltipId`, and the `Icon != null` guard with `true` (there is no Icon reference now). The delta/alpha pulse logic is unchanged.

- [ ] **Step 4: Tint by Kind in AddToRenderLists**

In `AddToRenderLists`, replace the hue vector line:

```csharp
                Vector3 hueVector = ShaderHueTranslator.GetHueVector(0, false, _alpha / 255f, true);
```

with a kind-driven hue:

```csharp
                ushort hue = _kind switch
                {
                    Data.BuffDisplayKind.Debuff => 0x0021,   // red
                    Data.BuffDisplayKind.Buff => 0x0044,     // green
                    _ => (ushort)0,
                };
                Vector3 hueVector = ShaderHueTranslator.GetHueVector(hue, false, _alpha / 255f, true);
```

- [ ] **Step 5: Wire the gump-refresh seam**

So expiry/set changes rebuild an open gump, register `RequestUpdateContents` while the gump exists. In the `BuffGump(World world, int x, int y)` constructor body (after `BuildGump();` at line 42), add:

```csharp
            Managers.PluginTimersManager.GumpRefresh = RequestUpdateContents;
```

and override `Dispose` to clear it. Add this method inside `BuffGump`:

```csharp
        public override void Dispose()
        {
            if (Managers.PluginTimersManager.GumpRefresh == (System.Action)RequestUpdateContents)
                Managers.PluginTimersManager.GumpRefresh = null;
            base.Dispose();
        }
```

> `RequestUpdateContents` is an existing `Control` method (used at line 272). If `Gump` already overrides `Dispose`, add the two clear-seam lines to the existing override instead of creating a new one.

- [ ] **Step 6: Build the client**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Run the full unit suite (nothing regressed)**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS (all tests, including Tasks 4–7).

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/BuffGump.cs
git commit -m "feat(buffs): render plugin buffs in BuffGump with kind tint"
```

---

## Task 12: cuo — ScreenTimersGump overlay

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Gumps/ScreenTimersGump.cs`
- Modify: `src/ClassicUO.Client/Game/Scenes/GameScene.cs` (add the overlay once, on scene load)

**Interfaces:**
- Consumes: `ScreenTimers` (Task 6), `RenderLists`, `UIManager`.
- Produces: a single always-present, non-interactive top-layer gump that draws each timer each frame.

- [ ] **Step 1: Write the overlay gump**

Create `src/ClassicUO.Client/Game/UI/Gumps/ScreenTimersGump.cs`. It follows the `BuffControlEntry.AddToRenderLists` pattern (query `Client.Game.UO.Gumps`/text via `RenderedText`, push draw closures into `RenderLists`). Bars/circles/numeric are drawn from `RemainingFraction`; grouped entries use `ScreenTimers.ComputePosition`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Game.Managers;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Fixed-position, non-interactive overlay that renders every plugin screen
    /// timer each frame. One instance is added to UIManager at the top layer.
    /// Layout/position math lives in <see cref="ScreenTimers"/>; this class only
    /// turns entries into draw calls.
    /// </summary>
    internal sealed class ScreenTimersGump : Gump
    {
        public ScreenTimersGump(World world) : base(world, 0, 0)
        {
            CanMove = false;
            CanCloseWithRightClick = false;
            AcceptMouseInput = false;
            WantUpdateSize = false;
            X = 0;
            Y = 0;
            Width = 0;
            Height = 0;
        }

        public override GumpType GumpType => GumpType.Buff; // reuse; no dedicated type needed

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            long now = Time.Ticks;
            float depth = layerDepthRef;

            foreach (var kv in ScreenTimers.Entries)
            {
                var e = kv.Value;

                int extent = ScreenTimers.DefaultExtent(e.Shape, e.GroupId != 0 && ScreenTimers.TryGetGroup(e.GroupId, out var gForExtent)
                    ? gForExtent.Direction : StackDirection.Down, e.Width, e.Height);

                TimerGroup group = default;
                if (e.GroupId != 0)
                    ScreenTimers.TryGetGroup(e.GroupId, out group);

                var (px, py) = ScreenTimers.ComputePosition(e, group, extent);
                float remaining = ScreenTimers.RemainingFraction(e, now);
                DrawEntry(renderLists, e, px, py, remaining, depth);
            }

            return true;
        }

        private static void DrawEntry(RenderLists renderLists, in ScreenTimerEntry e, int px, int py, float remaining, float depth)
        {
            (int w, int h) = ScreenTimers.DefaultSize(e.Shape);
            if (e.Width > 0) w = e.Width;
            if (e.Height > 0) h = e.Height;

            Vector3 hueVector = ShaderHueTranslator.GetHueVector(e.Hue, false, 1f, true);

            switch (e.Shape)
            {
                case TimerShape.Bar:
                    int fill = (int)(w * remaining);
                    renderLists.AddGumpNoAtlas(batcher =>
                    {
                        batcher.DrawRectangle(SolidColorTextureCache.GetTexture(Color.Gray), px, py, w, h, hueVector, depth);
                        batcher.Draw(SolidColorTextureCache.GetTexture(Color.White),
                            new Rectangle(px, py, fill, h), hueVector);
                        return true;
                    });
                    break;

                case TimerShape.Circle:
                    // Approximate radial depletion with a shrinking filled square;
                    // a true arc can replace this later without touching layout.
                    int side = (int)(w * remaining);
                    int cx = px + (w - side) / 2;
                    int cy = py + (h - side) / 2;
                    renderLists.AddGumpNoAtlas(batcher =>
                    {
                        batcher.Draw(SolidColorTextureCache.GetTexture(Color.White),
                            new Rectangle(cx, cy, side, side), hueVector);
                        return true;
                    });
                    break;

                case TimerShape.Numeric:
                default:
                    break; // numeric text handled by label/time block below
            }

            if (e.ShowTime || e.Shape == TimerShape.Numeric || !string.IsNullOrEmpty(e.Label))
            {
                int secs = (int)Math.Ceiling(Math.Max(0, e.StartTicks + e.DurationMs - Time.Ticks) / 1000f);
                string text = !string.IsNullOrEmpty(e.Label) ? e.Label : string.Empty;
                if (e.ShowTime || e.Shape == TimerShape.Numeric)
                    text = string.IsNullOrEmpty(text) ? $"{secs}s" : $"{text} {secs}s";

                var rt = RenderedText.Create(text, e.Hue, 0xFF, true, FontStyle.BlackBorder, TEXT_ALIGN_TYPE.TS_LEFT);
                renderLists.AddGumpNoAtlas(batcher =>
                {
                    rt.Draw(batcher, px, py + h + 1, depth);
                    return true;
                });
            }
        }
    }
}
```

> The exact batcher primitives (`DrawRectangle`, `SolidColorTextureCache`, `RenderedText.Create` overloads) must match the signatures used elsewhere in `Game/UI`. If a helper name differs, grep `Game/UI/Gumps` for the closest existing rectangle/line draw (e.g. in `HealthBarGumpCustom`) and mirror it — the layout inputs (`px`, `py`, `w`, `h`, `remaining`, `hueVector`) are already computed above and do not change.

- [ ] **Step 2: Add the overlay once when the game scene loads**

In `src/ClassicUO.Client/Game/Scenes/GameScene.cs`, in the scene `Load()` method (grep for `public override void Load(`), after the base UI is set up, add a guarded singleton add:

```csharp
            if (UIManager.GetGump<ScreenTimersGump>() == null)
                UIManager.Add(new ScreenTimersGump(World));
```

- [ ] **Step 3: Build the client**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeded, 0 errors. Fix any batcher-signature mismatches per the Step 1 note.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/ScreenTimersGump.cs src/ClassicUO.Client/Game/Scenes/GameScene.cs
git commit -m "feat(timers): ScreenTimersGump top-layer overlay"
```

---

## Task 13: BootstrapHost — struct fields + event callbacks

**Files:**
- Modify: `src/ClassicUO.BootstrapHost/HostBridge.cs`

**Interfaces:**
- Consumes: `PluginContextImpl.RaiseBuffEvent` / `RaiseTimerEvent` (Task 14 — add signatures now or reorder).
- Produces: `HostBindings.BuffEventFn`/`TimerEventFn` (appended, set in `BuildHostBindings`); `ClientBindings` command fields (appended, matching cuo order); `OnBuffEvent`/`OnTimerEvent` `[UnmanagedCallersOnly]` callbacks fanning out via `RaiseEachPlugin`.

- [ ] **Step 1: Append fields to the host HostBindings struct**

In `src/ClassicUO.BootstrapHost/HostBridge.cs`, in `struct HostBindings` (ends at `WalkProgressFn` line 330), append:

```csharp
    public nint BuffEventFn;   // void(int id, int reason)
    public nint TimerEventFn;  // void(int id, int reason)
```

- [ ] **Step 2: Append matching command fields to the host ClientBindings struct**

In `struct ClientBindings` (ends at `SetOverlayFn` line 349), append in the SAME order as cuo (Task 9 Step 2):

```csharp
    public nint AddBuffFn;             // void(int id, ushort graphic, int durationMs, int kind, nint textUtf8)
    public nint RemoveBuffFn;          // void(int id)
    public nint ClearBuffsFn;          // void()
    public nint DefineTimerGroupFn;    // void(int groupId, int x, int y, int direction, int gap)
    public nint AddTimerFn;            // void(int id, int shape, int durationMs, ushort hue, int groupId, int x, int y, int width, int height, nint labelUtf8, byte showTime)
    public nint RemoveTimerFn;         // void(int id)
    public nint RemoveTimerGroupFn;    // void(int groupId)
    public nint ClearTimersFn;         // void()
```

- [ ] **Step 3: Set the event pointers in BuildHostBindings**

In `BuildHostBindings()` (ends at `WalkProgressFn` line 160), append inside the initializer:

```csharp
        BuffEventFn       = (nint)(delegate* unmanaged[Cdecl]<int, int, void>)         &OnBuffEvent,
        TimerEventFn      = (nint)(delegate* unmanaged[Cdecl]<int, int, void>)         &OnTimerEvent,
```

- [ ] **Step 4: Add the two callbacks**

After `OnWalkProgress` (line 215), add:

```csharp
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnBuffEvent(int id, int reason)
        => _instance?.RaiseEachPlugin(p => p.RaiseBuffEvent(id, reason));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnTimerEvent(int id, int reason)
        => _instance?.RaiseEachPlugin(p => p.RaiseTimerEvent(id, reason));
```

> `RaiseBuffEvent`/`RaiseTimerEvent` on `PluginContextImpl` are added in Task 14. Do Task 14 Steps 1–2 first, or add stub methods to `PluginContextImpl` now so this compiles.

- [ ] **Step 5: Build the host**

Run: `dotnet build src/ClassicUO.BootstrapHost/ClassicUO.BootstrapHost.csproj -c Debug`
Expected: Build succeeded, 0 errors (once `PluginContextImpl.RaiseBuffEvent/RaiseTimerEvent` exist).

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.BootstrapHost/HostBridge.cs
git commit -m "feat(host): buff/timer struct fields and event callbacks"
```

---

## Task 14: BootstrapHost — BuffsImpl + ScreenTimersImpl

**Files:**
- Modify: `src/ClassicUO.BootstrapHost/PluginContextImpl.cs`
- Test: `tests/ClassicUO.BootstrapHost.Tests` (extend existing test that exercises ClientBindings, if present; otherwise add a focused impl test)

**Interfaces:**
- Consumes: `IPluginBuffs`, `IScreenTimers`, `BuffConfig`, `TimerConfig`, `TimerGroupConfig` (Tasks 1–2); `ClientBindings` fields (Task 13); `HostBridge` (`IsGameThread`, `PostToGameThread`, `ClientBindings`).
- Produces: `BuffsImpl : IPluginBuffs`, `ScreenTimersImpl : IScreenTimers`; `PluginContextImpl.Buffs`/`ScreenTimers` properties; `RaiseBuffEvent(int,int)`/`RaiseTimerEvent(int,int)` that map the reason int to `Expired` + `Removed`.

- [ ] **Step 1: Wire the two impls into PluginContextImpl**

In `src/ClassicUO.BootstrapHost/PluginContextImpl.cs`, add fields + properties (mirroring `_statusBars`):

```csharp
    private readonly BuffsImpl _buffs;
    private readonly ScreenTimersImpl _screenTimers;
```

In the constructor (after `_statusBars = new StatusBarsImpl(bridge);`):

```csharp
        _buffs = new BuffsImpl(bridge);
        _screenTimers = new ScreenTimersImpl(bridge);
```

Add properties (after `public IStatusBars StatusBars => _statusBars;`):

```csharp
    public IPluginBuffs Buffs => _buffs;
    public IScreenTimers ScreenTimers => _screenTimers;
```

- [ ] **Step 2: Add the event-fan methods**

After `RaiseWalkProgress` (line 77), add:

```csharp
    public void RaiseBuffEvent(int id, int reason) => _buffs.RaiseEvent(id, reason);
    public void RaiseTimerEvent(int id, int reason) => _screenTimers.RaiseEvent(id, reason);
```

- [ ] **Step 3: Implement BuffsImpl**

Add to `src/ClassicUO.BootstrapHost/PluginContextImpl.cs` (after `StatusBarsImpl`). It flattens `BuffConfig`, marshals `Text` as a freed ANSI pointer, and auto-marshals to the game thread:

```csharp
internal sealed class BuffsImpl : IPluginBuffs
{
    private readonly HostBridge _bridge;
    public BuffsImpl(HostBridge bridge) { _bridge = bridge; }

    public event Action<int>? Expired;
    public event Action<int, BuffRemoveReason>? Removed;

    internal void RaiseEvent(int id, int reason)
    {
        var r = (BuffRemoveReason)reason;
        if (r == BuffRemoveReason.Expired)
            Expired?.Invoke(id);
        Removed?.Invoke(id, r);
    }

    public unsafe void AddOrUpdate(BuffConfig config)
    {
        var fn = _bridge.ClientBindings.AddBuffFn;
        if (fn == 0 || config is null) return;

        int id = config.Id;
        ushort graphic = config.Graphic;
        int dur = config.DurationMs;
        int kind = (int)config.Kind;
        nint textPtr = string.IsNullOrEmpty(config.Text) ? nint.Zero : Marshal.StringToHGlobalAnsi(config.Text);

        void Call()
        {
            try { ((delegate* unmanaged[Cdecl]<int, ushort, int, int, nint, void>)fn)(id, graphic, dur, kind, textPtr); }
            finally { if (textPtr != nint.Zero) Marshal.FreeHGlobal(textPtr); }
        }

        if (_bridge.IsGameThread) Call();
        else _bridge.PostToGameThread(Call);
    }

    public unsafe void Remove(int id)
    {
        var fn = _bridge.ClientBindings.RemoveBuffFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<int, void>)fn)(id);
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<int, void>)fn)(id));
    }

    public unsafe void ClearAll()
    {
        var fn = _bridge.ClientBindings.ClearBuffsFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<void>)fn)();
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<void>)fn)());
    }
}
```

- [ ] **Step 4: Implement ScreenTimersImpl**

Add after `BuffsImpl`:

```csharp
internal sealed class ScreenTimersImpl : IScreenTimers
{
    private readonly HostBridge _bridge;
    public ScreenTimersImpl(HostBridge bridge) { _bridge = bridge; }

    public event Action<int>? Expired;
    public event Action<int, TimerRemoveReason>? Removed;

    internal void RaiseEvent(int id, int reason)
    {
        var r = (TimerRemoveReason)reason;
        if (r == TimerRemoveReason.Expired)
            Expired?.Invoke(id);
        Removed?.Invoke(id, r);
    }

    public unsafe void DefineGroup(TimerGroupConfig group)
    {
        var fn = _bridge.ClientBindings.DefineTimerGroupFn;
        if (fn == 0 || group is null) return;
        int gid = group.GroupId, x = group.X, y = group.Y, dir = (int)group.Direction, gap = group.Gap;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<int, int, int, int, int, void>)fn)(gid, x, y, dir, gap);
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<int, int, int, int, int, void>)fn)(gid, x, y, dir, gap));
    }

    public unsafe void AddOrUpdate(TimerConfig timer)
    {
        var fn = _bridge.ClientBindings.AddTimerFn;
        if (fn == 0 || timer is null) return;

        int id = timer.Id, shape = (int)timer.Shape, dur = timer.DurationMs, gid = timer.GroupId;
        ushort hue = timer.Hue;
        int x = timer.X, y = timer.Y, w = timer.Width, h = timer.Height;
        byte showTime = timer.ShowTime ? (byte)1 : (byte)0;
        nint labelPtr = string.IsNullOrEmpty(timer.Label) ? nint.Zero : Marshal.StringToHGlobalAnsi(timer.Label);

        void Call()
        {
            try { ((delegate* unmanaged[Cdecl]<int, int, int, ushort, int, int, int, int, int, nint, byte, void>)fn)(id, shape, dur, hue, gid, x, y, w, h, labelPtr, showTime); }
            finally { if (labelPtr != nint.Zero) Marshal.FreeHGlobal(labelPtr); }
        }

        if (_bridge.IsGameThread) Call();
        else _bridge.PostToGameThread(Call);
    }

    public unsafe void Remove(int id) => CallById(_bridge.ClientBindings.RemoveTimerFn, id);
    public unsafe void RemoveGroup(int groupId) => CallById(_bridge.ClientBindings.RemoveTimerGroupFn, groupId);

    public unsafe void ClearAll()
    {
        var fn = _bridge.ClientBindings.ClearTimersFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<void>)fn)();
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<void>)fn)());
    }

    private unsafe void CallById(nint fn, int id)
    {
        if (fn == 0) return;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<int, void>)fn)(id);
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<int, void>)fn)(id));
    }
}
```

- [ ] **Step 5: Write a host impl test (event mapping)**

Add `tests/ClassicUO.BootstrapHost.Tests/BuffTimerEventMappingTests.cs`. This verifies reason-int → `Expired`/`Removed` mapping without needing cuo (construct the impl with a bridge that has no bindings; only test `RaiseEvent`):

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.BootstrapHost;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.BootstrapHost.Tests
{
    public class BuffTimerEventMappingTests
    {
        [Fact]
        public void BuffsImpl_ExpiredReason_FiresExpiredAndRemoved()
        {
            var impl = new BuffsImpl(new HostBridge());
            int? expired = null;
            (int id, BuffRemoveReason reason)? removed = null;
            impl.Expired += id => expired = id;
            impl.Removed += (id, r) => removed = (id, r);

            impl.RaiseEvent(7, (int)BuffRemoveReason.Expired);

            Assert.Equal(7, expired);
            Assert.Equal((7, BuffRemoveReason.Expired), removed);
        }

        [Fact]
        public void BuffsImpl_RemovedByPlugin_FiresOnlyRemoved()
        {
            var impl = new BuffsImpl(new HostBridge());
            bool expiredFired = false;
            (int id, BuffRemoveReason reason)? removed = null;
            impl.Expired += _ => expiredFired = true;
            impl.Removed += (id, r) => removed = (id, r);

            impl.RaiseEvent(7, (int)BuffRemoveReason.RemovedByPlugin);

            Assert.False(expiredFired);
            Assert.Equal((7, BuffRemoveReason.RemovedByPlugin), removed);
        }

        [Fact]
        public void ScreenTimersImpl_ExpiredReason_FiresExpiredAndRemoved()
        {
            var impl = new ScreenTimersImpl(new HostBridge());
            int? expired = null;
            (int id, TimerRemoveReason reason)? removed = null;
            impl.Expired += id => expired = id;
            impl.Removed += (id, r) => removed = (id, r);

            impl.RaiseEvent(3, (int)TimerRemoveReason.Expired);

            Assert.Equal(3, expired);
            Assert.Equal((3, TimerRemoveReason.Expired), removed);
        }
    }
}
```

> `BuffsImpl`/`ScreenTimersImpl` are `internal`; the test project already sees BootstrapHost internals if an `InternalsVisibleTo` is present (check `ClassicUO.BootstrapHost.csproj`). If not, add `[assembly: InternalsVisibleTo("ClassicUO.BootstrapHost.Tests")]` to the host project (mirror how `ClassicUO.Client` exposes internals). `RaiseEvent` is `internal` and never touches `_bridge.ClientBindings`, so a bare `new HostBridge()` is safe.

- [ ] **Step 6: Run the host tests**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~BuffTimerEventMappingTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Build the whole solution**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.BootstrapHost/PluginContextImpl.cs tests/ClassicUO.BootstrapHost.Tests/BuffTimerEventMappingTests.cs
git commit -m "feat(host): BuffsImpl and ScreenTimersImpl with event mapping"
```

---

## Task 15: Final verification

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Full unit test run**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS — includes `BuffIconKindTests`, `PluginBuffsTests`, `ScreenTimersTests`, `PluginTimersManagerTests`.

- [ ] **Step 3: Host test run**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests`
Expected: PASS — includes `BuffTimerEventMappingTests`, plus existing plugin smoke tests still green.

- [ ] **Step 4: Confirm ABI-append discipline**

Manually diff the two struct copies and confirm the new fields are in identical order at the end of both `HostBindings` and both `ClientBindings`:
Run: `git diff --stat` then inspect `src/ClassicUO.Client/PluginHost.cs` and `src/ClassicUO.BootstrapHost/HostBridge.cs`.
Expected: `BuffEventFn, TimerEventFn` appended to both `HostBindings`; `AddBuffFn … ClearTimersFn` appended (same order) to both `ClientBindings`.

---

## Self-Review

**Spec coverage:**
- Feature A data model (`BuffIcon.Kind`, `PluginBuffs`) → Tasks 4, 5. ✓
- Feature A render/merge/tint → Task 11. ✓
- Feature A lifecycle/expiry + reasons → Tasks 7, 9. ✓
- Feature A plugin API → Tasks 1, 3, 14. ✓
- Feature B data model (`ScreenTimers`, groups) → Task 6. ✓
- Feature B AddOrUpdate restart semantics + order → Task 6. ✓
- Feature B render overlay → Task 12. ✓
- Feature B stacking layout as pure function → Task 6 (`ComputePosition`), tested. ✓
- Feature B lifecycle/expiry + reasons → Tasks 7, 9. ✓
- Feature B plugin API → Tasks 2, 3, 14. ✓
- Cross-boundary scalar/string marshaling → Tasks 9, 14. ✓
- ABI append discipline + null-checked events → Tasks 8, 13, 15. ✓
- Central `PluginTimersManager.Update` from `World.Update` → Tasks 7, 10. ✓
- Threading auto-marshal → Task 14 impls. ✓
- Testing matrix (buffs, timers, layout math, restart, remaining%, expiry dispatch, BuffIcon.Kind default) → Tasks 4–7, 14. ✓
- Out of scope (persistence, dragging, packets, v1 host) → not implemented. ✓

**Type consistency:** `BuffDisplayKind` exists twice by design (PluginApi + `Game.Data`), int values asserted equal in Task 4. Reason ints (0/1/2) consistent across `PluginTimersManager`, PluginApi enums, and host mapping. `AddTimer`/`AddTimerFn` scalar signature identical in cuo target (Task 9), cuo struct comment (Task 9), host struct comment (Task 13), and host impl call site (Task 14). `ComputePosition`/`RemainingFraction`/`DefaultExtent`/`DefaultSize` names used identically in Task 6 and Task 12.

**Placeholder scan:** Render primitive names in Task 12 flagged with an explicit "grep the closest existing draw and mirror" instruction rather than left vague — this is the one spot where exact batcher signatures must be confirmed against the live renderer API, called out clearly.
