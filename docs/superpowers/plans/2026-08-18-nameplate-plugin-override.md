# Plugin Nameplate Override Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a v2 plugin force a specific entity's nameplate (Object Handles
name+HP box) visible-with-opacity or hidden, overriding the player's own
filters/toggle/manual-close.

**Architecture:** A per-serial override dictionary lives on
`NameOverHeadManager` (visible/hidden + opacity). `GameSceneDrawingSorting`
and `Entity.Update()` consult it before their existing filter/opacity logic.
The plugin surface is a new `INameplates` service on `IPluginContext`, wired
to cuo through three new `GenericInvokeFn` opcodes (the documented
escape-valve pattern already used for `Attack`/`ListOpenStatusBars`) —
no new `ClientBindings` fields, so the append-only ABI-layout guard tests
don't apply here.

**Tech Stack:** C# / .NET 10, xUnit + FluentAssertions (BootstrapHost.Tests),
xUnit (ClassicUO.UnitTests), unsafe/cdecl function pointers for the plugin ABI.

## Global Constraints

- SPDX header `// SPDX-License-Identifier: BSD-2-Clause` on every new file (matches every file read in this repo).
- `GenericOpcodes` values are append-only — never renumber `GetSupportedOpcodes`(0)/`ListOpenStatusBars`(1)/`Attack`(2).
- No per-plugin ownership partitioning for the new API — matches the existing precedent (`ClearCharacters`/`ClearAreas`/timers/buffs `ClearAll` all clear *globally*, not per-plugin, despite doc comments saying "this plugin owns"). `ClearAllOverrides` clears every override regardless of which plugin set it.
- Mutating `INameplates` methods auto-marshal to the game thread (matches `IHighlight`/`IGameActions.Attack`), never throw off-thread.

---

### Task 1: ABI opcodes and payload struct

**Files:**
- Modify: `src/ClassicUO.PluginAbi/GenericDispatch.cs`

**Interfaces:**
- Produces: `GenericOpcodes.SetNameplateOverride` (`3`), `GenericOpcodes.ClearNameplateOverride` (`4`), `GenericOpcodes.ClearAllNameplateOverrides` (`5`); `struct NameplateOverridePayload { uint Serial; byte Visible; byte OpacityPercent; }`. Task 6 and Task 7 both depend on these.

This is a shared, dependency-free assembly (referenced by both the client and
`BootstrapHost`), so there's no test project attached to it directly — it's
exercised by Task 6/7's tests. No failing-test step here; this task is pure
ABI surface addition, verified by build.

- [ ] **Step 1: Add the three opcodes**

Edit `src/ClassicUO.PluginAbi/GenericDispatch.cs`, inside `GenericOpcodes`, after the existing `Attack` member (currently the last member, `= 2`):

```csharp
    /// <summary>
    /// Forces serial's nameplate (Object Handles name+HP box) visible at a
    /// given opacity, or hidden, overriding the player's own name-overhead
    /// filters/toggle and any manual close. Replaces any existing override
    /// for the same serial. Args: <see cref="NameplateOverridePayload"/>.
    /// Result: none.
    /// </summary>
    public const int SetNameplateOverride = 3;

    /// <summary>
    /// Removes the nameplate override for one serial, reverting to normal
    /// (player-controlled) behavior. Args: <c>uint</c> serial. Result: none.
    /// </summary>
    public const int ClearNameplateOverride = 4;

    /// <summary>
    /// Removes every nameplate override, regardless of which plugin set it
    /// (matches <c>ClearCharacters</c>/<c>ClearAreas</c>: no per-plugin
    /// partitioning). Args: none. Result: none.
    /// </summary>
    public const int ClearAllNameplateOverrides = 5;
```

- [ ] **Step 2: Add the payload struct**

Still in `src/ClassicUO.PluginAbi/GenericDispatch.cs`, after the `StatusBarChangedPayload` struct at the end of the file:

```csharp
/// <summary>
/// Wire payload for <see cref="GenericOpcodes.SetNameplateOverride"/>. Fixed
/// layout, blitted directly — no serialization step on either side.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NameplateOverridePayload
{
    public uint Serial;
    /// <summary>0 = force-hidden, 1 = force-visible.</summary>
    public byte Visible;
    /// <summary>0-100. Meaningful only when <see cref="Visible"/> is 1.</summary>
    public byte OpacityPercent;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ClassicUO.PluginAbi/ClassicUO.PluginAbi.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.PluginAbi/GenericDispatch.cs
git commit -m "Add nameplate override opcodes and payload to plugin ABI"
```

---

### Task 2: Plugin-facing INameplates interface

**Files:**
- Create: `src/ClassicUO.PluginApi/INameplates.cs`
- Modify: `src/ClassicUO.PluginApi/IPluginContext.cs:44-46`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: `ClassicUO.PluginApi.INameplates` with `SetOverride(uint serial, bool visible, byte opacityPercent = 100)`, `ClearOverride(uint serial)`, `ClearAllOverrides()`; `IPluginContext.Nameplates` property of that type. Task 7 implements this interface; samples/consumers call through it.

Pure contract addition (an interface + a property on another interface) —
nothing to unit-test at this layer; verified by build, consumed and tested
via Task 7's ABI round-trip tests.

- [ ] **Step 1: Create the interface**

Create `src/ClassicUO.PluginApi/INameplates.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.PluginApi;

/// <summary>
/// Plugin-driven override of a single entity's nameplate (the Object
/// Handles name+HP box). An override for a serial replaces any previous
/// override for that serial and persists until cleared; it wins over the
/// player's own name-overhead filters, the global name-overhead toggle,
/// and a manual close.
/// </summary>
public interface INameplates
{
    /// <summary>
    /// Forces serial's nameplate visible or hidden. When
    /// <paramref name="visible"/> is true, <paramref name="opacityPercent"/>
    /// (0-100, clamped) sets the box's opacity; ignored when false.
    /// Auto-marshals to the game thread.
    /// </summary>
    void SetOverride(uint serial, bool visible, byte opacityPercent = 100);

    /// <summary>
    /// Removes the override for serial, reverting to normal (player-driven)
    /// behavior. Auto-marshals to the game thread.
    /// </summary>
    void ClearOverride(uint serial);

    /// <summary>
    /// Removes every nameplate override, regardless of which plugin set it.
    /// Auto-marshals to the game thread.
    /// </summary>
    void ClearAllOverrides();
}
```

- [ ] **Step 2: Expose it on IPluginContext**

In `src/ClassicUO.PluginApi/IPluginContext.cs`, add after the `Highlight` property (line 45, `IHighlight Highlight { get; }`):

```csharp

    /// <summary>Plugin-driven per-serial nameplate visibility/opacity override.</summary>
    INameplates Nameplates { get; }
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ClassicUO.PluginApi/ClassicUO.PluginApi.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.PluginApi/INameplates.cs src/ClassicUO.PluginApi/IPluginContext.cs
git commit -m "Add INameplates to the v2 plugin API contract"
```

---

### Task 3: NameOverHeadManager override storage

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/NameOverHeadManager.cs:1-9` (usings), `:42-47` (insert point)
- Test: `tests/ClassicUO.UnitTests/Game/Managers/NameOverHeadManagerOverrideTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: on `NameOverHeadManager`: `void SetOverride(uint serial, bool visible, byte opacityPercent)`, `void ClearOverride(uint serial)`, `void ClearAllOverrides()`, `bool TryGetOverride(uint serial, out bool visible, out byte opacityPercent)`. Task 4, 5, and 7's dispatch handler all call these.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClassicUO.UnitTests/Game/Managers/NameOverHeadManagerOverrideTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class NameOverHeadManagerOverrideTests
    {
        private static NameOverHeadManager CreateSut() => new NameOverHeadManager(new World());

        [Fact]
        public void TryGetOverride_ReturnsFalse_WhenNoneSet()
        {
            var sut = CreateSut();

            var found = sut.TryGetOverride(0x40000001, out bool visible, out byte opacity);

            Assert.False(found);
            Assert.False(visible);
            Assert.Equal(100, opacity);
        }

        [Fact]
        public void SetOverride_VisibleWithOpacity_IsReadBackByTryGetOverride()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: true, opacityPercent: 40);
            var found = sut.TryGetOverride(0x40000001, out bool visible, out byte opacity);

            Assert.True(found);
            Assert.True(visible);
            Assert.Equal(40, opacity);
        }

        [Fact]
        public void SetOverride_Hidden_IsReadBackByTryGetOverride()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: false, opacityPercent: 100);
            var found = sut.TryGetOverride(0x40000001, out bool visible, out _);

            Assert.True(found);
            Assert.False(visible);
        }

        [Fact]
        public void SetOverride_ClampsOpacityAbove100()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: true, opacityPercent: 255);
            sut.TryGetOverride(0x40000001, out _, out byte opacity);

            Assert.Equal(100, opacity);
        }

        [Fact]
        public void SetOverride_ReplacesPreviousOverrideForSameSerial()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: true, opacityPercent: 40);
            sut.SetOverride(0x40000001, visible: false, opacityPercent: 100);
            sut.TryGetOverride(0x40000001, out bool visible, out _);

            Assert.False(visible);
        }

        [Fact]
        public void ClearOverride_RemovesOnlyThatSerial()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: true, opacityPercent: 40);
            sut.SetOverride(0x40000002, visible: true, opacityPercent: 60);
            sut.ClearOverride(0x40000001);

            Assert.False(sut.TryGetOverride(0x40000001, out _, out _));
            Assert.True(sut.TryGetOverride(0x40000002, out _, out _));
        }

        [Fact]
        public void ClearAllOverrides_RemovesEveryOverride()
        {
            var sut = CreateSut();

            sut.SetOverride(0x40000001, visible: true, opacityPercent: 40);
            sut.SetOverride(0x40000002, visible: false, opacityPercent: 100);
            sut.ClearAllOverrides();

            Assert.False(sut.TryGetOverride(0x40000001, out _, out _));
            Assert.False(sut.TryGetOverride(0x40000002, out _, out _));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~NameOverHeadManagerOverrideTests"`
Expected: FAIL to build — `NameOverHeadManager` has no `SetOverride`/`ClearOverride`/`ClearAllOverrides`/`TryGetOverride` members.

- [ ] **Step 3: Implement the override storage**

In `src/ClassicUO.Client/Game/Managers/NameOverHeadManager.cs`, add the using at the top (after `using System;` on line 3):

```csharp
using System.Collections.Generic;
```

Then insert after the `IsToggled` property (line 46, right before `public bool IsAllowed(Entity entity)`):

```csharp

        private readonly Dictionary<uint, (bool Visible, byte OpacityPercent)> _overrides = new();

        /// <summary>
        /// Forces serial's nameplate visible (at <paramref name="opacityPercent"/>,
        /// clamped 0-100) or hidden, replacing any existing override for the
        /// same serial.
        /// </summary>
        public void SetOverride(uint serial, bool visible, byte opacityPercent) =>
            _overrides[serial] = (visible, opacityPercent > 100 ? (byte)100 : opacityPercent);

        /// <summary>Removes the override for serial, if any.</summary>
        public void ClearOverride(uint serial) => _overrides.Remove(serial);

        /// <summary>Removes every override, regardless of which plugin set it.</summary>
        public void ClearAllOverrides() => _overrides.Clear();

        /// <summary>
        /// Looks up the override for serial. Returns <c>false</c> (with
        /// <paramref name="visible"/> = false, <paramref name="opacityPercent"/>
        /// = 100) when no override is set.
        /// </summary>
        public bool TryGetOverride(uint serial, out bool visible, out byte opacityPercent)
        {
            if (_overrides.TryGetValue(serial, out var o))
            {
                visible = o.Visible;
                opacityPercent = o.OpacityPercent;
                return true;
            }

            visible = false;
            opacityPercent = 100;
            return false;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~NameOverHeadManagerOverrideTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Run the existing IsAllowed tests to confirm no regression**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~NameOverHeadManagerIsAllowedTests"`
Expected: PASS (5 tests, unchanged).

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/NameOverHeadManager.cs tests/ClassicUO.UnitTests/Game/Managers/NameOverHeadManagerOverrideTests.cs
git commit -m "Add per-serial nameplate override storage to NameOverHeadManager"
```

---

### Task 4: Wire overrides into the draw-time visibility decision

**Files:**
- Modify: `src/ClassicUO.Client/Game/Scenes/GameSceneDrawingSorting.cs:258-282`

**Interfaces:**
- Consumes: `NameOverHeadManager.TryGetOverride` (Task 3).
- Produces: nothing new for later tasks — this is the visibility half of the feature.

`UpdateObjectHandles` reads live per-frame `Entity`/`GameScene` state, so
there's no practical unit test for it (same reasoning the spec gives for why
`IGameActions.Attack` only has ABI-level tests, not render-effect tests).
This task is verified by build + full existing suite, then manual smoke
test.

- [ ] **Step 1: Replace UpdateObjectHandles**

In `src/ClassicUO.Client/Game/Scenes/GameSceneDrawingSorting.cs`, replace the method at lines 258-282:

```csharp
        private void UpdateObjectHandles(Entity obj, bool useObjectHandles)
        {
            if (useObjectHandles && _world.NameOverHeadManager.IsAllowed(obj))
            {
                if (obj.ObjectHandlesStatus != ObjectHandlesStatus.CLOSED)
                {
                    if (obj.ObjectHandlesStatus == ObjectHandlesStatus.NONE)
                    {
                        obj.ObjectHandlesStatus = ObjectHandlesStatus.OPEN;
                    }

                    obj.UpdateTextCoordsV();
                }
            }
            else if (obj.Serial == _healthLinesManager.ForcedOverheadSerial)
            {
                // HealthLinesManager owns this mobile's overhead this frame (new-target-system
                // nameplate mode) — don't fight it just because the global name-overhead toggle is off.
            }
            else if (obj.ObjectHandlesStatus != ObjectHandlesStatus.NONE)
            {
                obj.ObjectHandlesStatus = ObjectHandlesStatus.NONE;
                obj.UpdateTextCoordsV();
            }
        }
```

with:

```csharp
        private void UpdateObjectHandles(Entity obj, bool useObjectHandles)
        {
            bool hasOverride = _world.NameOverHeadManager.TryGetOverride(obj.Serial, out bool forceVisible, out _);

            // A plugin-forced hide wins over everything else, including the
            // player's own filters and a manually-reopened handle.
            if (hasOverride && !forceVisible)
            {
                if (obj.ObjectHandlesStatus != ObjectHandlesStatus.CLOSED)
                {
                    obj.ObjectHandlesStatus = ObjectHandlesStatus.CLOSED;
                    obj.UpdateTextCoordsV();
                }

                return;
            }

            bool allowed = (useObjectHandles && _world.NameOverHeadManager.IsAllowed(obj)) || (hasOverride && forceVisible);

            if (allowed)
            {
                if (obj.ObjectHandlesStatus == ObjectHandlesStatus.NONE ||
                    // A plugin-forced show reopens a handle even if the player closed it manually.
                    (hasOverride && obj.ObjectHandlesStatus == ObjectHandlesStatus.CLOSED))
                {
                    obj.ObjectHandlesStatus = ObjectHandlesStatus.OPEN;
                }

                if (obj.ObjectHandlesStatus != ObjectHandlesStatus.CLOSED)
                {
                    obj.UpdateTextCoordsV();
                }
            }
            else if (obj.Serial == _healthLinesManager.ForcedOverheadSerial)
            {
                // HealthLinesManager owns this mobile's overhead this frame (new-target-system
                // nameplate mode) — don't fight it just because the global name-overhead toggle is off.
            }
            else if (obj.ObjectHandlesStatus != ObjectHandlesStatus.NONE)
            {
                obj.ObjectHandlesStatus = ObjectHandlesStatus.NONE;
                obj.UpdateTextCoordsV();
            }
        }
```

- [ ] **Step 2: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the full unit test suite to confirm no regression**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: All tests PASS (no test currently exercises `UpdateObjectHandles`, so this is a regression check on everything else).

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/Scenes/GameSceneDrawingSorting.cs
git commit -m "Let nameplate overrides force object-handle visibility"
```

---

### Task 5: Apply override opacity to the nameplate gump

**Files:**
- Modify: `src/ClassicUO.Client/Game/GameObjects/Entity.cs:131-143`

**Interfaces:**
- Consumes: `NameOverHeadManager.TryGetOverride` (Task 3).
- Produces: nothing new for later tasks — this is the opacity half of the feature.

Same reasoning as Task 4: `Entity.Update()` is live per-frame render-path
code with no practical unit test. Verified by build + full suite + manual
smoke test.

- [ ] **Step 1: Apply opacity when the gump is created**

In `src/ClassicUO.Client/Game/GameObjects/Entity.cs`, replace lines 131-143:

```csharp
            if (ObjectHandlesStatus == ObjectHandlesStatus.OPEN)
            {
                ObjectHandlesStatus = ObjectHandlesStatus.DISPLAYING;

                // TODO: Some servers may not want to receive this (causing original client to not send it),
                //but all servers tested (latest POL, old POL, ServUO, Outlands) do.
                if ( /*Client.Game.UO.Version > ClientVersion.CV_200 &&*/ SerialHelper.IsMobile(Serial))
                {
                    Socket.Send_NameRequest(Serial);
                }

                UIManager.Add(new NameOverheadGump(World, this));
            }
```

with:

```csharp
            if (ObjectHandlesStatus == ObjectHandlesStatus.OPEN)
            {
                ObjectHandlesStatus = ObjectHandlesStatus.DISPLAYING;

                // TODO: Some servers may not want to receive this (causing original client to not send it),
                //but all servers tested (latest POL, old POL, ServUO, Outlands) do.
                if ( /*Client.Game.UO.Version > ClientVersion.CV_200 &&*/ SerialHelper.IsMobile(Serial))
                {
                    Socket.Send_NameRequest(Serial);
                }

                var nameplateGump = new NameOverheadGump(World, this);

                if (World.NameOverHeadManager.TryGetOverride(Serial, out bool overrideVisible, out byte opacityPercent) && overrideVisible)
                {
                    nameplateGump.Alpha = opacityPercent / 100f;
                }

                UIManager.Add(nameplateGump);
            }
```

- [ ] **Step 2: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the full unit test suite to confirm no regression**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/GameObjects/Entity.cs
git commit -m "Apply nameplate override opacity to the Object Handles gump"
```

---

### Task 6: Client-side opcode dispatch

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginGenericDispatch.cs`

**Interfaces:**
- Consumes: `GenericOpcodes.SetNameplateOverride/ClearNameplateOverride/ClearAllNameplateOverrides` and `NameplateOverridePayload` (Task 1); `NameOverHeadManager.SetOverride/ClearOverride/ClearAllOverrides` (Task 3).
- Produces: nothing new for later tasks — this is the receiving end cuo-side of the ABI Task 7 calls into.

`PluginGenericDispatch.Invoke` has no existing dedicated unit test in this
repo (`InvokeAttack` isn't tested here either — it's covered indirectly by
`ActionBindingTests` stubbing `GenericInvokeFn` itself, not this switch).
Task 7 provides the end-to-end coverage for the new opcodes; this task is
verified by build + full suite.

- [ ] **Step 1: Add the three opcode cases and their handlers**

In `src/ClassicUO.Client/Game/Managers/PluginGenericDispatch.cs`, add a using at the top (after `using ClassicUO.PluginAbi;` on line 4):

```csharp
using ClassicUO.Game;
```

Add cases to the switch in `Invoke` (after the `GenericOpcodes.Attack` case, before `default`):

```csharp
                case GenericOpcodes.SetNameplateOverride:
                    return InvokeSetNameplateOverride(argsPtr, argsLen);

                case GenericOpcodes.ClearNameplateOverride:
                    return InvokeClearNameplateOverride(argsPtr, argsLen);

                case GenericOpcodes.ClearAllNameplateOverrides:
                    return InvokeClearAllNameplateOverrides();
```

Add the three handler methods after `InvokeAttack`:

```csharp
        private static int InvokeSetNameplateOverride(IntPtr argsPtr, int argsLen)
        {
            if (argsLen < sizeof(NameplateOverridePayload))
                return -2; // args buffer too small

            var payload = *(NameplateOverridePayload*)argsPtr;

            World world = Client.Game?.UO?.World;
            if (world == null)
                return -1;

            world.NameOverHeadManager.SetOverride(payload.Serial, payload.Visible != 0, payload.OpacityPercent);
            return 0;
        }

        private static int InvokeClearNameplateOverride(IntPtr argsPtr, int argsLen)
        {
            if (argsLen < sizeof(uint))
                return -2; // args buffer too small

            uint serial = *(uint*)argsPtr;

            World world = Client.Game?.UO?.World;
            if (world == null)
                return -1;

            world.NameOverHeadManager.ClearOverride(serial);
            return 0;
        }

        private static int InvokeClearAllNameplateOverrides()
        {
            World world = Client.Game?.UO?.World;
            if (world == null)
                return -1;

            world.NameOverHeadManager.ClearAllOverrides();
            return 0;
        }
```

- [ ] **Step 2: List the new opcodes as supported**

In `WriteSupportedOpcodes`, replace the array:

```csharp
            ReadOnlySpan<int> opcodes =
            [
                GenericOpcodes.GetSupportedOpcodes,
                GenericOpcodes.ListOpenStatusBars,
                GenericOpcodes.Attack,
            ];
```

with:

```csharp
            ReadOnlySpan<int> opcodes =
            [
                GenericOpcodes.GetSupportedOpcodes,
                GenericOpcodes.ListOpenStatusBars,
                GenericOpcodes.Attack,
                GenericOpcodes.SetNameplateOverride,
                GenericOpcodes.ClearNameplateOverride,
                GenericOpcodes.ClearAllNameplateOverrides,
            ];
```

- [ ] **Step 3: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the full unit test suite to confirm no regression**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginGenericDispatch.cs
git commit -m "Dispatch nameplate override opcodes to NameOverHeadManager"
```

---

### Task 7: Host-side INameplates implementation + ABI round-trip tests

**Files:**
- Modify: `src/ClassicUO.BootstrapHost/PluginContextImpl.cs`
- Test: Create `tests/ClassicUO.BootstrapHost.Tests/NameplateBindingTests.cs`

**Interfaces:**
- Consumes: `INameplates` (Task 2), `GenericOpcodes.SetNameplateOverride/ClearNameplateOverride/ClearAllNameplateOverrides` + `NameplateOverridePayload` (Task 1).
- Produces: `PluginContextImpl.Nameplates` — the property a plugin actually calls through `IPluginContext.Nameplates`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClassicUO.BootstrapHost.Tests/NameplateBindingTests.cs`, mirroring `ActionBindingTests.cs`'s structure:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ClassicUO.PluginAbi;
using FluentAssertions;
using Xunit;

namespace ClassicUO.BootstrapHost.Tests;

/// <summary>
/// Verifies that NameplatesImpl.SetOverride/ClearOverride/ClearAllOverrides
/// invoke the cuo-side GenericInvokeFn with the right opcode and payload. We
/// inject a ClientBindings table whose GenericInvokeFn points at a managed
/// cdecl recorder, then call the plugin-facing INameplates surface.
/// </summary>
[Collection("BootstrapHost")]
public sealed unsafe class NameplateBindingTests : IDisposable
{
    private static int s_opcode, s_argsLen;
    private static NameplateOverridePayload s_payload;
    private static uint s_clearedSerial;

    private readonly string _tempRoot;
    private readonly string _pluginsRoot;

    public NameplateBindingTests()
    {
        s_opcode = s_argsLen = 0;
        s_payload = default;
        s_clearedSerial = 0;

        _tempRoot = Path.Combine(Path.GetTempPath(), "cuo-bootstraphost-tests", Guid.NewGuid().ToString("N"));
        _pluginsRoot = Path.Combine(_tempRoot, "Plugins");
        var pluginDir = Path.Combine(_pluginsRoot, "HelloPlugin");
        Directory.CreateDirectory(pluginDir);

        var fixtureDll = Path.Combine(AppContext.BaseDirectory, "Fixtures", "HelloPlugin", "HelloPlugin.dll");
        File.Exists(fixtureDll).Should().BeTrue($"HelloPlugin fixture should be staged at {fixtureDll}");
        File.Copy(fixtureDll, Path.Combine(pluginDir, "HelloPlugin.dll"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int GenericInvokeRecorder(int opcode, nint argsPtr, int argsLen, nint outBuf, int outCap)
    {
        s_opcode = opcode;
        s_argsLen = argsLen;

        if (opcode == GenericOpcodes.SetNameplateOverride && argsLen >= sizeof(NameplateOverridePayload))
            s_payload = *(NameplateOverridePayload*)argsPtr;
        else if (opcode == GenericOpcodes.ClearNameplateOverride && argsLen >= sizeof(uint))
            s_clearedSerial = *(uint*)argsPtr;

        return 0;
    }

    private static HostBridge CreateBridgeWithRecorder(string pluginsRoot)
    {
        var bridge = new HostBridge();
        bridge.LoadPluginsForTest(pluginsRoot);

        var cb = new ClientBindings
        {
            GenericInvokeFn = (nint)(delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)&GenericInvokeRecorder,
        };
        bridge.InstallClientBindingsForTest(cb);

        return bridge;
    }

    [Fact]
    public void SetOverride_invokes_generic_dispatch_with_the_payload()
    {
        var bridge = CreateBridgeWithRecorder(_pluginsRoot);

        bridge.Plugins[0].Nameplates.SetOverride(0xDEADBEEF, visible: true, opacityPercent: 40);

        s_opcode.Should().Be(GenericOpcodes.SetNameplateOverride);
        s_argsLen.Should().Be(sizeof(NameplateOverridePayload));
        s_payload.Serial.Should().Be(0xDEADBEEFu);
        s_payload.Visible.Should().Be((byte)1);
        s_payload.OpacityPercent.Should().Be((byte)40);
    }

    [Fact]
    public void SetOverride_clamps_opacity_above_100()
    {
        var bridge = CreateBridgeWithRecorder(_pluginsRoot);

        bridge.Plugins[0].Nameplates.SetOverride(0xDEADBEEF, visible: true, opacityPercent: 255);

        s_payload.OpacityPercent.Should().Be((byte)100);
    }

    [Fact]
    public void ClearOverride_invokes_generic_dispatch_with_the_serial()
    {
        var bridge = CreateBridgeWithRecorder(_pluginsRoot);

        bridge.Plugins[0].Nameplates.ClearOverride(0xDEADBEEF);

        s_opcode.Should().Be(GenericOpcodes.ClearNameplateOverride);
        s_argsLen.Should().Be(sizeof(uint));
        s_clearedSerial.Should().Be(0xDEADBEEFu);
    }

    [Fact]
    public void ClearAllOverrides_invokes_generic_dispatch_with_no_args()
    {
        var bridge = CreateBridgeWithRecorder(_pluginsRoot);

        bridge.Plugins[0].Nameplates.ClearAllOverrides();

        s_opcode.Should().Be(GenericOpcodes.ClearAllNameplateOverrides);
        s_argsLen.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~NameplateBindingTests"`
Expected: FAIL to build — `IPluginContext`/`PluginContextImpl` has no `Nameplates` member yet.

- [ ] **Step 3: Implement NameplatesImpl and wire it into PluginContextImpl**

In `src/ClassicUO.BootstrapHost/PluginContextImpl.cs`, add a field (after `private readonly HighlightImpl _highlight;` on line 19):

```csharp
    private readonly NameplatesImpl _nameplates;
```

Initialize it in the constructor (after `_highlight  = new HighlightImpl(bridge);` on line 36):

```csharp
        _nameplates = new NameplatesImpl(bridge);
```

Expose it (after `public IHighlight Highlight => _highlight;` on line 53):

```csharp
    public INameplates Nameplates => _nameplates;
```

Add the implementation class after `HighlightImpl` at the end of the file:

```csharp
internal sealed class NameplatesImpl : INameplates
{
    private readonly HostBridge _bridge;
    public NameplatesImpl(HostBridge bridge) { _bridge = bridge; }

    public unsafe void SetOverride(uint serial, bool visible, byte opacityPercent = 100)
    {
        var fn = _bridge.ClientBindings.GenericInvokeFn;
        if (fn == 0) return;

        var payload = new NameplateOverridePayload
        {
            Serial = serial,
            Visible = visible ? (byte)1 : (byte)0,
            OpacityPercent = opacityPercent > 100 ? (byte)100 : opacityPercent,
        };

        if (_bridge.IsGameThread)
            InvokeSetOverride(fn, payload);
        else
            _bridge.PostToGameThread(() => InvokeSetOverride(fn, payload));
    }

    private static unsafe void InvokeSetOverride(nint fn, NameplateOverridePayload payload)
    {
        ((delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)fn)(
            GenericOpcodes.SetNameplateOverride, (nint)(&payload), sizeof(NameplateOverridePayload), 0, 0);
    }

    public unsafe void ClearOverride(uint serial)
    {
        var fn = _bridge.ClientBindings.GenericInvokeFn;
        if (fn == 0) return;

        if (_bridge.IsGameThread)
            InvokeClearOverride(fn, serial);
        else
            _bridge.PostToGameThread(() => InvokeClearOverride(fn, serial));
    }

    private static unsafe void InvokeClearOverride(nint fn, uint serial)
    {
        ((delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)fn)(
            GenericOpcodes.ClearNameplateOverride, (nint)(&serial), sizeof(uint), 0, 0);
    }

    public unsafe void ClearAllOverrides()
    {
        var fn = _bridge.ClientBindings.GenericInvokeFn;
        if (fn == 0) return;

        if (_bridge.IsGameThread)
            InvokeClearAllOverrides(fn);
        else
            _bridge.PostToGameThread(() => InvokeClearAllOverrides(fn));
    }

    private static unsafe void InvokeClearAllOverrides(nint fn)
    {
        ((delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)fn)(
            GenericOpcodes.ClearAllNameplateOverrides, 0, 0, 0, 0);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~NameplateBindingTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Run the full BootstrapHost.Tests suite to confirm no regression**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests`
Expected: All tests PASS.

- [ ] **Step 6: Run the full solution build once more**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/ClassicUO.BootstrapHost/PluginContextImpl.cs tests/ClassicUO.BootstrapHost.Tests/NameplateBindingTests.cs
git commit -m "Implement INameplates over the nameplate-override ABI opcodes"
```

---

## Manual verification (after Task 7)

The render-path pieces (Task 4, Task 5) have no automated coverage, so do
one manual pass using `samples/HelloPlugin` (or a scratch plugin) once all
seven tasks are merged:

1. Build and run the client against a live server, log in.
2. From a plugin, call `context.Nameplates.SetOverride(someMobileSerial, true, 40)` for a mobile the player's own filters would normally hide (e.g. an `Enemy`-flagged mobile with `NameOverheadTypeAllowed.Enemy` unset). Confirm its nameplate appears at ~40% opacity.
3. Call `SetOverride(sameSerial, false)`. Confirm the nameplate disappears even with ctrl+shift held (global hover-show).
4. Call `ClearOverride(sameSerial)`. Confirm normal filter behavior resumes.
5. Call `SetOverride` for two different serials, then `ClearAllOverrides()`. Confirm both revert.
