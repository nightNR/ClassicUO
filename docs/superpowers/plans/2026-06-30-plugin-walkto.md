# Plugin v2 `WalkTo` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Plugin v2 two action primitives (`WalkTo`, `StopWalk`) plus one event (`WalkProgress`) so a plugin can reliably walk/run a character to a tile and re-route around dynamic obstacles, with the policy living in the plugin and only thin generic primitives in the client.

**Architecture:** Three new surface elements thread through the existing 4-layer FFI boundary already used by `CastSpell`/`RequestMove`/`UpdatePlayerPosition`. Actions (plugin → cuo) ride the cuo-populated `ClientBindings` table (like `CastSpellFn`/`RequestMoveFn`). The event (cuo → plugin) rides the apphost-populated `HostBindings` table (like `UpdatePlayerPosFn`) and is fanned out to every loaded plugin context. The client's only new logic is a `Pathfinder.EndAutoWalk(WalkState reason)` teardown that emits a `WalkState` from the transition points the pathfinder already passes through, plus the missing `_run` setter.

**Tech Stack:** C#, .NET 10 (BootstrapHost + PluginApi + tests), .NET (cuo client), legacy .NET Framework 4.7.2 apphost (`ClassicUO.Bootstrap`), xUnit + FluentAssertions, NativeAOT cdecl function-pointer interop.

## Global Constraints

- **Thin client, fat plugin.** The client exposes generic primitives only. No retry/give-up policy, no plugin knowledge in the client. (spec "Guiding constraint")
- **Append-only struct layout.** `HostBindings` and `ClientBindings` are shared by cuo (`src/ClassicUO.Client/PluginHost.cs`), the v2 host (`src/ClassicUO.BootstrapHost/HostBridge.cs`), and the legacy net472 apphost (`src/ClassicUO.Bootstrap/src/Program.cs`). New function-pointer fields MUST be appended at the end of each struct, never reordered. (spec "Open questions / risks")
- **cuo binds every `HostBindings` field unconditionally** in `UnmanagedAssistantHost`'s constructor (`Marshal.GetDelegateForFunctionPointer` with no null check). Therefore every apphost — v2 AND legacy — MUST populate `WalkProgressFn` with a non-null pointer, or cuo throws at startup.
- **Out of scope:** teleporters, doors, stairs, multi-step travel, any `IWorld` query surface, client-side retry policy. "Bypass obstacles" = dynamic mobiles/items only, handled by the plugin re-issuing `WalkTo` on `Blocked`.
- **Build/test commands (Windows):** `dotnet build`; `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~<Name>"`; `dotnet test tests/ClassicUO.BootstrapHost.Tests`.
- Every commit message ends with the two trailer lines shown in each Commit step.

## Testability note (read before Task 2)

The real `Pathfinder.WalkTo` / `ProcessAutoWalk` require a live `World` with a `Player` **and loaded map (mul/uop) data** for `FindPath` / `CalculateNewZ`, which CI does not have. So the spec's "client unit tests" cannot drive an end-to-end walk. This plan unit-tests the **new emit logic at its seams** — `EndAutoWalk(reason)` reason→`WalkState` mapping, `ResetAutoWalk()` no-emit, and `StopAutoWalk()` → `Stopped` + teardown — which is exactly the new code. The actual per-call-site wiring (`Arrived` on exhaust, `Blocked` on step fail) and `run` propagation are verified by the manual `HelloPlugin` walk demo in Task 5. This is a deliberate, documented deviation from the spec's implied "drive a full walk in a unit test."

## File Structure

| Layer | File | Responsibility |
|-------|------|----------------|
| Contract | `src/ClassicUO.PluginApi/IGameActions.cs` | `WalkState` enum + `WalkTo`/`StopWalk`/`WalkProgress` on `IGameActions` |
| Host (net10) | `src/ClassicUO.BootstrapHost/HostBridge.cs` | append `WalkProgressFn` (HostBindings) + `WalkToFn`/`StopWalkFn` (ClientBindings); `OnWalkProgress` fan-out; test seams |
| Host (net10) | `src/ClassicUO.BootstrapHost/PluginContextImpl.cs` | `GameActionsImpl.WalkTo`/`StopWalk` + `WalkProgress` event; context `RaiseWalkProgress` |
| cuo | `src/ClassicUO.Client/Game/Pathfinder.cs` | client `WalkState`; static `WalkProgress` event; `EndAutoWalk`/`ResetAutoWalk`; `run` param; emit at transitions |
| cuo | `src/ClassicUO.Client/PluginHost.cs` | append struct fields; bind `_walkProgress`; expose `_walkTo`/`_stopWalk` |
| cuo | `src/ClassicUO.Client/Network/Plugin.cs` | static `WalkTo`/`StopWalk`/`OnWalkProgress` bridge methods |
| cuo | `src/ClassicUO.Client/GameController.cs` | one-time `Pathfinder.WalkProgress += Plugin.OnWalkProgress` subscription |
| cuo callers | `PacketHandlers.cs`, `Game/Scenes/GameScene.cs`, `Game/Scenes/GameSceneInputHandler.cs` | pass new `run` arg to `WalkTo` |
| legacy | `src/ClassicUO.Bootstrap/src/Program.cs` | append `WalkProgressFn` to legacy HostBindings + no-op handler (layout/bind parity) |
| sample | `samples/HelloPlugin/HelloPlugin.cs` | subscribe `WalkProgress`; log; manual walk demo |
| tests | `tests/ClassicUO.BootstrapHost.Tests/*` | fan-out + binding-invocation tests |
| tests | `tests/ClassicUO.UnitTests/Game/Pathfinder/WalkProgressTests.cs` | Pathfinder emit-seam tests |

---

### Task 1: PluginApi contract + BootstrapHost event fan-out and actions

This task is entirely net10 (PluginApi + BootstrapHost + HelloPlugin + host tests) and is independent of the cuo client assembly. It delivers the full plugin-facing surface and proves the event fan-out and the action-binding invocation work.

**Files:**
- Modify: `src/ClassicUO.PluginApi/IGameActions.cs`
- Modify: `src/ClassicUO.BootstrapHost/HostBridge.cs`
- Modify: `src/ClassicUO.BootstrapHost/PluginContextImpl.cs`
- Modify: `samples/HelloPlugin/HelloPlugin.cs`
- Modify: `tests/ClassicUO.BootstrapHost.Tests/SmokeTests.cs`
- Create: `tests/ClassicUO.BootstrapHost.Tests/ActionBindingTests.cs`

**Interfaces:**
- Consumes: existing `GameActionsImpl(HostBridge)`, `HostBridge.IsGameThread`, `HostBridge.PostToGameThread(Action)`, `HostBridge.ClientBindings`, `HostBridge.RaiseEachPlugin(Action<PluginContextImpl>)`, `PluginContextImpl.RaisePlayerPositionChanged` pattern.
- Produces:
  - `public enum ClassicUO.PluginApi.WalkState { Walking, Arrived, Blocked, Stopped }`
  - `bool IGameActions.WalkTo(int x, int y, int z, int distance, bool run)`
  - `void IGameActions.StopWalk()`
  - `event Action<WalkState>? IGameActions.WalkProgress`
  - `internal struct ClientBindings` gains `nint WalkToFn` (bool(int,int,int,int,byte run)) and `nint StopWalkFn` (void())
  - `internal struct HostBindings` gains `nint WalkProgressFn` (void(int state))
  - `internal void HostBridge.TestRaiseWalkProgress(int state)`
  - `internal void HostBridge.InstallClientBindingsForTest(ClientBindings)`
  - `internal void PluginContextImpl.RaiseWalkProgress(WalkState)` and `internal void GameActionsImpl.RaiseWalkProgress(WalkState)`

- [ ] **Step 1: Add the contract to PluginApi**

In `src/ClassicUO.PluginApi/IGameActions.cs`, add the three members inside the `IGameActions` interface (after `TryGetPlayerPosition`) and the enum below the interface:

```csharp
    /// <summary>
    /// Reads the player's tile coordinates. Returns <c>false</c> if the
    /// player object is not initialized (pre-login or mid-disconnect).
    /// </summary>
    bool TryGetPlayerPosition(out int x, out int y, out int z);

    /// <summary>
    /// Pathfinds to tile (x, y, z) and starts auto-walking toward it, stopping
    /// within <paramref name="distance"/> tiles of the goal. Pass
    /// <paramref name="run"/> = true to run instead of walk. Returns
    /// <c>false</c> if no path can be found right now. Must be called on the
    /// game thread; throws otherwise. Use <see cref="IPluginContext.Game"/>.
    /// <c>Post</c> to marshal.
    /// </summary>
    bool WalkTo(int x, int y, int z, int distance, bool run);

    /// <summary>
    /// Cancels any active auto-walk. Safe to call at any time and from any
    /// thread (auto-marshals to the game thread).
    /// </summary>
    void StopWalk();

    /// <summary>
    /// Raised on auto-walk state transitions. Fired on the game thread.
    /// </summary>
    event Action<WalkState>? WalkProgress;
}

/// <summary>State of an auto-walk requested via <see cref="IGameActions.WalkTo"/>.</summary>
public enum WalkState
{
    /// <summary>A path was found and the player started moving.</summary>
    Walking,
    /// <summary>The player reached within <c>distance</c> tiles of the goal.</summary>
    Arrived,
    /// <summary>A step failed (a dynamic mobile/item blocks the path). The
    /// plugin may re-issue WalkTo to re-route.</summary>
    Blocked,
    /// <summary>The walk was cancelled, no path existed, or the player object
    /// went away.</summary>
    Stopped,
}
```

Note: `IGameActions.cs` currently has no `using System;`. Add `using System;` at the top (under the SPDX comment, above `namespace`) so `Action<>` resolves.

- [ ] **Step 2: Add HelloPlugin subscription so the fan-out test has an observer**

In `samples/HelloPlugin/HelloPlugin.cs`, inside `OnInitialize`, add a `WalkProgress` subscription next to the other `context.*` subscriptions (after the `context.Closing += ...` line):

```csharp
        context.Tick                  += () => Log("Tick");
        context.Closing               += () => Log("Closing");

        context.Actions.WalkProgress  += state => Log($"Walk:{state}");
```

- [ ] **Step 3: Write the failing fan-out test**

In `tests/ClassicUO.BootstrapHost.Tests/SmokeTests.cs`, add a `[Collection("BootstrapHost")]` attribute to the existing `SmokeTests` class declaration, define the collection marker, and add the new test method. Change the class line:

```csharp
[Collection("BootstrapHost")]
public sealed class SmokeTests : IDisposable
```

At the bottom of the file (after the closing brace of `SmokeTests`), add:

```csharp
[CollectionDefinition("BootstrapHost", DisableParallelization = true)]
public sealed class BootstrapHostCollection { }
```

And inside `SmokeTests`, add:

```csharp
    [Fact]
    public void WalkProgress_event_reaches_the_plugin()
    {
        var bridge = new HostBridge();
        bridge.LoadPluginsForTest(_pluginsRoot);

        bridge.TestRaiseWalkProgress((int)ClassicUO.PluginApi.WalkState.Blocked);

        ReadLog().Should().Contain("Walk:Blocked");
    }
```

- [ ] **Step 4: Run the fan-out test — expect FAIL (compile error)**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~WalkProgress_event_reaches_the_plugin"`
Expected: build FAILS — `HostBridge` has no `TestRaiseWalkProgress`, and `RaiseEachPlugin(p => p.RaiseWalkProgress(...))` does not exist yet.

- [ ] **Step 5: Implement the HostBindings/ClientBindings struct fields and fan-out**

In `src/ClassicUO.BootstrapHost/HostBridge.cs`:

(a) Add the PluginApi using at the top (after the existing `using System.Runtime.InteropServices;`):

```csharp
using ClassicUO.PluginApi;
```

(b) Append `WalkProgressFn` to the end of the `HostBindings` struct (after `PacketOutFn`):

```csharp
    public nint PacketInFn;
    public nint PacketOutFn;
    public nint WalkProgressFn;
}
```

(c) Append `WalkToFn`/`StopWalkFn` to the end of the `ClientBindings` struct (after `ReflectionCmdFn`):

```csharp
    public nint ReflectionCmdFn;       // legacy reflection commands; unused by v2
    public nint WalkToFn;              // bool(int x, int y, int z, int distance, byte run)
    public nint StopWalkFn;            // void()
}
```

(d) In `BuildHostBindings()`, add the new pointer to the initializer (after `PacketOutFn`):

```csharp
        PacketOutFn       = (nint)(delegate* unmanaged[Cdecl]<nint, int*, byte>)        &OnPacketOut,
        WalkProgressFn    = (nint)(delegate* unmanaged[Cdecl]<int, void>)               &OnWalkProgress,
    };
```

(e) Add the unmanaged callback next to `OnUpdatePlayerPos`:

```csharp
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWalkProgress(int state)
        => _instance?.RaiseEachPlugin(p => p.RaiseWalkProgress((WalkState)state));
```

(f) Add the test seam next to `TestRaisePlayerPositionChanged`:

```csharp
    internal void TestRaiseWalkProgress(int state) { foreach (var p in _loader.Plugins) p.RaiseWalkProgress((WalkState)state); }
```

(g) Add the client-bindings test seam (used by Task 1 binding test) next to `LoadPluginsForTest`:

```csharp
    /// <summary>Test-only: inject a ClientBindings table without going through cuo's Initialize.</summary>
    internal void InstallClientBindingsForTest(ClientBindings bindings) => _clientBindings = bindings;
```

- [ ] **Step 6: Implement GameActionsImpl + PluginContextImpl members**

In `src/ClassicUO.BootstrapHost/PluginContextImpl.cs`:

(a) In `PluginContextImpl`, add the forwarder next to `RaisePlayerPositionChanged`:

```csharp
    public void RaisePlayerPositionChanged(int x, int y, int z) => PlayerPositionChanged?.Invoke(x, y, z);
    public void RaiseWalkProgress(WalkState state) => _actions.RaiseWalkProgress(state);
```

(b) In `GameActionsImpl`, add the two action methods plus the event (after `TryGetPlayerPosition`):

```csharp
    public unsafe bool WalkTo(int x, int y, int z, int distance, bool run)
    {
        var fn = _bridge.ClientBindings.WalkToFn;
        if (fn == 0) return false;
        if (!_bridge.IsGameThread)
            throw new InvalidOperationException("WalkTo must be called from the game thread; use IDispatcher.Post.");
        return ((delegate* unmanaged[Cdecl]<int, int, int, int, byte, byte>)fn)(x, y, z, distance, run ? (byte)1 : (byte)0) != 0;
    }

    public unsafe void StopWalk()
    {
        var fn = _bridge.ClientBindings.StopWalkFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<void>)fn)();
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<void>)fn)());
    }

    public event Action<WalkState>? WalkProgress;
    internal void RaiseWalkProgress(WalkState state) => WalkProgress?.Invoke(state);
```

- [ ] **Step 7: Run the fan-out test — expect PASS**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~WalkProgress_event_reaches_the_plugin"`
Expected: PASS (1 passed). The HelloPlugin fixture is rebuilt and copied by the test project's `CopyHelloPluginFixture` target.

- [ ] **Step 8: Write the failing action-binding test**

Create `tests/ClassicUO.BootstrapHost.Tests/ActionBindingTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace ClassicUO.BootstrapHost.Tests;

/// <summary>
/// Verifies that GameActionsImpl.WalkTo / StopWalk invoke the cuo-side
/// ClientBindings function pointers with the right arguments. We inject a
/// ClientBindings table whose WalkToFn / StopWalkFn point at managed cdecl
/// recorders, then call the plugin-facing IGameActions surface.
/// </summary>
[Collection("BootstrapHost")]
public sealed unsafe class ActionBindingTests : IDisposable
{
    private static int s_x, s_y, s_z, s_dist;
    private static bool s_run, s_stopCalled;

    private readonly string _tempRoot;
    private readonly string _pluginsRoot;

    public ActionBindingTests()
    {
        s_x = s_y = s_z = s_dist = 0;
        s_run = s_stopCalled = false;

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
    private static byte WalkToRecorder(int x, int y, int z, int distance, byte run)
    {
        s_x = x; s_y = y; s_z = z; s_dist = distance; s_run = run != 0;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void StopWalkRecorder() => s_stopCalled = true;

    [Fact]
    public void WalkTo_invokes_the_client_binding_with_its_arguments()
    {
        var bridge = new HostBridge();
        bridge.LoadPluginsForTest(_pluginsRoot); // sets game thread to this thread

        var cb = new ClientBindings
        {
            WalkToFn   = (nint)(delegate* unmanaged[Cdecl]<int, int, int, int, byte, byte>)&WalkToRecorder,
            StopWalkFn = (nint)(delegate* unmanaged[Cdecl]<void>)&StopWalkRecorder,
        };
        bridge.InstallClientBindingsForTest(cb);

        var actions = bridge.Plugins[0].Actions;

        actions.WalkTo(10, 20, 5, 1, run: true).Should().BeTrue();
        s_x.Should().Be(10);
        s_y.Should().Be(20);
        s_z.Should().Be(5);
        s_dist.Should().Be(1);
        s_run.Should().BeTrue();
    }

    [Fact]
    public void StopWalk_invokes_the_client_binding()
    {
        var bridge = new HostBridge();
        bridge.LoadPluginsForTest(_pluginsRoot);

        var cb = new ClientBindings
        {
            StopWalkFn = (nint)(delegate* unmanaged[Cdecl]<void>)&StopWalkRecorder,
        };
        bridge.InstallClientBindingsForTest(cb);

        bridge.Plugins[0].Actions.StopWalk();

        s_stopCalled.Should().BeTrue();
    }
}
```

- [ ] **Step 9: Run the binding test — expect PASS**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~ActionBindingTests"`
Expected: PASS (2 passed). (`LoadPluginsForTest` sets the game thread to the test thread, so `WalkTo`'s game-thread guard is satisfied and `StopWalk` invokes inline rather than queueing.)

- [ ] **Step 10: Run the full BootstrapHost suite — expect PASS**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests`
Expected: PASS — all existing smoke tests plus the 3 new tests green.

- [ ] **Step 11: Commit**

```bash
git add src/ClassicUO.PluginApi/IGameActions.cs src/ClassicUO.BootstrapHost/HostBridge.cs src/ClassicUO.BootstrapHost/PluginContextImpl.cs samples/HelloPlugin/HelloPlugin.cs tests/ClassicUO.BootstrapHost.Tests/SmokeTests.cs tests/ClassicUO.BootstrapHost.Tests/ActionBindingTests.cs
git commit -m "feat(plugin-v2): add WalkTo/StopWalk/WalkProgress to host contract

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW"
```

---

### Task 2: Client Pathfinder emit refactor + run parameter

Adds the client-side `WalkState`, a static `WalkProgress` event, the `EndAutoWalk`/`ResetAutoWalk` split, the `run` setter, and routes each transition to the right reason. Updates all `WalkTo` callers for the new signature. Unit-tests the emit seams.

**Files:**
- Modify: `src/ClassicUO.Client/Game/Pathfinder.cs:15-49` (class top: enum + static event), `:930-996` (`WalkTo`), `:1005-1037` (`ProcessAutoWalk`/`StopAutoWalk`)
- Modify: `src/ClassicUO.Client/Network/PacketHandlers.cs:2086`
- Modify: `src/ClassicUO.Client/Game/Scenes/GameScene.cs:789`
- Modify: `src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs:875,889`
- Modify: `src/ClassicUO.Client/PluginHost.cs:263`
- Create: `tests/ClassicUO.UnitTests/Game/Pathfinder/WalkProgressTests.cs`

**Interfaces:**
- Consumes: existing `Pathfinder` fields `_run`, `_pathSize`, property `AutoWalking`; `World()` parameterless ctor; `new Pathfinder(World)`.
- Produces:
  - `internal enum ClassicUO.Game.WalkState { Walking, Arrived, Blocked, Stopped }` (ordinals match the PluginApi enum)
  - `internal static event Action<WalkState>? Pathfinder.WalkProgress`
  - `public bool Pathfinder.WalkTo(int x, int y, int z, int distance, bool run)` (signature change)
  - `public void Pathfinder.StopAutoWalk()` — now emits `Stopped`
  - `internal void Pathfinder.EndAutoWalk(WalkState reason)`
  - `internal void Pathfinder.ResetAutoWalk()`

- [ ] **Step 1: Write the failing Pathfinder emit-seam tests**

Create `tests/ClassicUO.UnitTests/Game/Pathfinder/WalkProgressTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game;
using Xunit;

namespace ClassicUO.UnitTests.Game.Pathfinder
{
    /// <summary>
    /// Exercises the new auto-walk emit seams without map data:
    /// EndAutoWalk reason mapping, ResetAutoWalk's no-emit teardown, and
    /// StopAutoWalk emitting Stopped. Pathfinder.WalkProgress is a static
    /// event; each test subscribes a local handler and unsubscribes in a
    /// finally so methods (run serially within this class) stay isolated.
    /// </summary>
    public class WalkProgressTests
    {
        private static ClassicUO.Game.Pathfinder NewPathfinder() => new ClassicUO.Game.Pathfinder(new World());

        private static List<WalkState> Capture(System.Action<ClassicUO.Game.Pathfinder> act)
        {
            var states = new List<WalkState>();
            void Handler(WalkState s) => states.Add(s);
            ClassicUO.Game.Pathfinder.WalkProgress += Handler;
            try
            {
                var pf = NewPathfinder();
                pf.AutoWalking = true;
                act(pf);
            }
            finally
            {
                ClassicUO.Game.Pathfinder.WalkProgress -= Handler;
            }
            return states;
        }

        [Fact]
        public void EndAutoWalk_Arrived_emits_Arrived_and_clears_AutoWalking()
        {
            ClassicUO.Game.Pathfinder captured = null;
            var states = new List<WalkState>();
            void Handler(WalkState s) => states.Add(s);
            ClassicUO.Game.Pathfinder.WalkProgress += Handler;
            try
            {
                captured = NewPathfinder();
                captured.AutoWalking = true;
                captured.EndAutoWalk(WalkState.Arrived);
            }
            finally
            {
                ClassicUO.Game.Pathfinder.WalkProgress -= Handler;
            }

            Assert.Equal(new[] { WalkState.Arrived }, states);
            Assert.False(captured.AutoWalking);
        }

        [Fact]
        public void EndAutoWalk_Blocked_emits_Blocked()
        {
            var states = Capture(pf => pf.EndAutoWalk(WalkState.Blocked));
            Assert.Equal(new[] { WalkState.Blocked }, states);
        }

        [Fact]
        public void StopAutoWalk_emits_Stopped_and_clears_AutoWalking()
        {
            ClassicUO.Game.Pathfinder captured = null;
            var states = new List<WalkState>();
            void Handler(WalkState s) => states.Add(s);
            ClassicUO.Game.Pathfinder.WalkProgress += Handler;
            try
            {
                captured = NewPathfinder();
                captured.AutoWalking = true;
                captured.StopAutoWalk();
            }
            finally
            {
                ClassicUO.Game.Pathfinder.WalkProgress -= Handler;
            }

            Assert.Equal(new[] { WalkState.Stopped }, states);
            Assert.False(captured.AutoWalking);
        }

        [Fact]
        public void ResetAutoWalk_does_not_emit_but_clears_AutoWalking()
        {
            ClassicUO.Game.Pathfinder captured = null;
            var states = new List<WalkState>();
            void Handler(WalkState s) => states.Add(s);
            ClassicUO.Game.Pathfinder.WalkProgress += Handler;
            try
            {
                captured = NewPathfinder();
                captured.AutoWalking = true;
                captured.ResetAutoWalk();
            }
            finally
            {
                ClassicUO.Game.Pathfinder.WalkProgress -= Handler;
            }

            Assert.Empty(states);
            Assert.False(captured.AutoWalking);
        }
    }
}
```

- [ ] **Step 2: Run the tests — expect FAIL (compile error)**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~WalkProgressTests"`
Expected: build FAILS — `WalkState`, `Pathfinder.WalkProgress`, `EndAutoWalk`, `ResetAutoWalk` do not exist yet.

- [ ] **Step 3: Add the client WalkState enum + static event**

In `src/ClassicUO.Client/Game/Pathfinder.cs`, add the enum just inside the `namespace ClassicUO.Game` block, above `internal sealed class Pathfinder` (line ~15):

```csharp
namespace ClassicUO.Game
{
    /// <summary>State of an auto-walk. Mirrors ClassicUO.PluginApi.WalkState
    /// (same ordinals); the int value crosses the plugin FFI boundary.</summary>
    internal enum WalkState
    {
        Walking,
        Arrived,
        Blocked,
        Stopped,
    }

    internal sealed class Pathfinder
    {
```

Then add the static event near the `AutoWalking` property (after line 49, `public bool PathindingCanBeCancelled { get; set; }`):

```csharp
        public bool PathindingCanBeCancelled { get; set; }

        /// <summary>Raised on auto-walk state transitions (game thread). The
        /// plugin bridge subscribes; see GameController / Network.Plugin.</summary>
        internal static event Action<WalkState> WalkProgress;
```

(`using System;` is already present at the top of the file.)

- [ ] **Step 4: Change WalkTo to take `run`, set `_run`, emit Walking, use the no-emit reset**

In `WalkTo` (lines 930-996), change the signature and the prelude. Replace the signature line:

```csharp
        public bool WalkTo(int x, int y, int z, int distance, bool run)
```

Replace lines 981-993 (from `PathindingCanBeCancelled = true;` through the `else` block):

```csharp
            PathindingCanBeCancelled = true;
            ResetAutoWalk();
            _run = run;
            AutoWalking = true;

            if (FindPath(PATHFINDER_MAX_NODES))
            {
                _pointIndex = 1;
                RaiseWalkProgress(WalkState.Walking);
                ProcessAutoWalk();
            }
            else
            {
                AutoWalking = false;
            }
```

Rationale: the old `StopAutoWalk()` at the top is replaced by the no-emit `ResetAutoWalk()` so the prior-walk clear does not spuriously fire `Stopped` before `Walking`. `_run = run` is set after the reset (which zeroes `_run`). `FindPath` may still raise `_run = true` for long paths (>14 goal-dist) — existing auto-run behavior is preserved.

- [ ] **Step 5: Route ProcessAutoWalk transitions and rework StopAutoWalk**

In `ProcessAutoWalk` (lines 1005-1030), replace the two `StopAutoWalk();` calls:

```csharp
                    if (!_world.Player.Walk((Direction) p.Direction, _run))
                    {
                        EndAutoWalk(WalkState.Blocked);
                    }
                }
                else
                {
                    EndAutoWalk(WalkState.Arrived);
                }
```

Replace the whole `StopAutoWalk` method (lines 1032-1037) with the new method set:

```csharp
        public void StopAutoWalk()
        {
            EndAutoWalk(WalkState.Stopped);
        }

        internal void ResetAutoWalk()
        {
            AutoWalking = false;
            _run = false;
            _pathSize = 0;
        }

        internal void EndAutoWalk(WalkState reason)
        {
            ResetAutoWalk();
            RaiseWalkProgress(reason);
        }

        private static void RaiseWalkProgress(WalkState reason)
        {
            WalkProgress?.Invoke(reason);
        }
```

Behavior map (matches the spec table): WalkTo prior-walk clear → `ResetAutoWalk` (no emit); `FindPath` success → `Walking`; `FindPath` fail → no emit (bool return); path exhausted → `Arrived`; step fail → `Blocked`; `StopAutoWalk` / external cancel → `Stopped`.

- [ ] **Step 6: Run the Pathfinder tests — expect PASS**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~WalkProgressTests"`
Expected: PASS (4 passed).

- [ ] **Step 7: Update all WalkTo callers for the new signature**

The signature change breaks 5 call sites. Edit each, preserving today's behavior by passing `run: false` (today `WalkTo` never set `_run` before walking, so callers walked, with `FindPath` auto-running only long paths):

`src/ClassicUO.Client/Network/PacketHandlers.cs:2086`:
```csharp
            world.Player.Pathfinder.WalkTo(x, y, z, 0, false);
```

`src/ClassicUO.Client/Game/Scenes/GameScene.cs:789`:
```csharp
                        _world.Player.Pathfinder.WalkTo(follow.X, follow.Y, follow.Z, 1, false);
```

`src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs:875`:
```csharp
                        if (_world.Player.Pathfinder.WalkTo(obj.X, obj.Y, obj.Z, 0, false))
```

`src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs:889`:
```csharp
                    else if (obj is Land && _world.Player.Pathfinder.WalkTo(obj.X, obj.Y, obj.Z, 0, false))
```

`src/ClassicUO.Client/PluginHost.cs:263` (legacy reflection command path):
```csharp
                    bool started = Client.Game.UO?.World?.Player?.Pathfinder?.WalkTo(args.Item2, args.Item3, args.Item4, args.Item5, false) ?? false;
```

- [ ] **Step 8: Build the client — expect PASS**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: build succeeds (all `WalkTo` callers updated; `WalkProgress` static event unreferenced-but-present is fine).

- [ ] **Step 9: Commit**

```bash
git add src/ClassicUO.Client/Game/Pathfinder.cs src/ClassicUO.Client/Network/PacketHandlers.cs src/ClassicUO.Client/Game/Scenes/GameScene.cs src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs src/ClassicUO.Client/PluginHost.cs tests/ClassicUO.UnitTests/Game/Pathfinder/WalkProgressTests.cs
git commit -m "feat(pathfinder): emit WalkState transitions and honor run flag

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW"
```

---

### Task 3: cuo boundary wiring (PluginHost + Plugin + GameController)

Wires the client `Pathfinder` to the FFI boundary: cuo exposes `WalkToFn`/`StopWalkFn` in `ClientBindings` (like `RequestMoveFn`), binds the incoming `WalkProgressFn` from `HostBindings` (like `UpdatePlayerPosFn`), and forwards the static `Pathfinder.WalkProgress` event to the host once. No automated test — verified by build; the live path is exercised in Task 5.

**Files:**
- Modify: `src/ClassicUO.Client/PluginHost.cs:14-46` (structs), `:48-128` (delegate fields + ctor bind), `:175-287` (ClientBindings delegates + Initialize), `:342-367` (IPluginHost + impl)
- Modify: `src/ClassicUO.Client/Network/Plugin.cs:478-497` (new statics)
- Modify: `src/ClassicUO.Client/GameController.cs:43-64` (subscription)

**Interfaces:**
- Consumes: `Pathfinder.WalkTo(int,int,int,int,bool)`, `Pathfinder.StopAutoWalk()`, `Pathfinder.WalkProgress` (Task 2); `WalkState` (Task 2).
- Produces:
  - `internal static bool Plugin.WalkTo(int x, int y, int z, int distance, bool run)`
  - `internal static void Plugin.StopWalk()`
  - `internal static void Plugin.OnWalkProgress(ClassicUO.Game.WalkState state)`
  - `void IPluginHost.WalkProgress(int state)` + `UnmanagedAssistantHost.WalkProgress(int)` impl
  - cuo `HostBindings.WalkProgressFn`, `ClientBindings.WalkToFn`, `ClientBindings.StopWalkFn`

- [ ] **Step 1: Append the cuo struct fields**

In `src/ClassicUO.Client/PluginHost.cs`, append `WalkProgressFn` to the end of the `HostBindings` struct (after `PacketOutFn`, line 31):

```csharp
        public IntPtr PacketInFn;
        public IntPtr PacketOutFn;
        public IntPtr /*delegate*<int, void>*/ WalkProgressFn;
    }
```

Append `WalkToFn`/`StopWalkFn` to the end of the `ClientBindings` struct (after `ReflectionCmdFn`, line 45):

```csharp
        public IntPtr ReflectionCmdFn;
        public IntPtr /*delegate*<int, int, int, int, bool, bool>*/ WalkToFn;
        public IntPtr /*delegate*<void>*/ StopWalkFn;
    }
```

- [ ] **Step 2: Bind the incoming WalkProgressFn (HostBindings → host event into cuo)**

In `UnmanagedAssistantHost`, add the delegate type + field next to `_updatePlayerPos` (after line 93):

```csharp
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dOnWalkProgress(int state);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dOnWalkProgress _walkProgress;
```

In the constructor (after line 121, the `_updatePlayerPos = ...` line), bind it:

```csharp
            _walkProgress = Marshal.GetDelegateForFunctionPointer<dOnWalkProgress>(setup->WalkProgressFn);
```

- [ ] **Step 3: Expose the outgoing WalkToFn/StopWalkFn (cuo → plugin actions)**

In `UnmanagedAssistantHost`, add the two delegate fields next to `_requestMove` (after line 200):

```csharp
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate bool dWalkTo(int x, int y, int z, int distance, bool run);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dWalkTo _walkTo = Plugin.WalkTo;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dStopWalk();
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dStopWalk _stopWalk = Plugin.StopWalk;
```

In `Initialize()`, set the two pointers (after line 284, the `cuoHost.ReflectionCmdFn = ...` line):

```csharp
            cuoHost.WalkToFn = Marshal.GetFunctionPointerForDelegate(_walkTo);
            cuoHost.StopWalkFn = Marshal.GetFunctionPointerForDelegate(_stopWalk);
```

- [ ] **Step 4: Add WalkProgress to IPluginHost and implement it**

In `UnmanagedAssistantHost`, add the method next to `UpdatePlayerPosition` (after line 345):

```csharp
        public void WalkProgress(int state)
        {
            _walkProgress?.Invoke(state);
        }
```

In the `IPluginHost` interface (after line 364, `public void UpdatePlayerPosition(int x, int y, int z);`):

```csharp
        public void WalkProgress(int state);
```

- [ ] **Step 5: Add the Plugin bridge statics**

In `src/ClassicUO.Client/Network/Plugin.cs`, add three static methods next to `RequestMove`/`GetPlayerPosition` (after line 497):

```csharp
        internal static bool WalkTo(int x, int y, int z, int distance, bool run)
        {
            return Client.Game.UO?.World?.Player?.Pathfinder?.WalkTo(x, y, z, distance, run) ?? false;
        }

        internal static void StopWalk()
        {
            Client.Game.UO?.World?.Player?.Pathfinder?.StopAutoWalk();
        }

        internal static void OnWalkProgress(ClassicUO.Game.WalkState state)
        {
            Client.Game.PluginHost?.WalkProgress((int)state);
        }
```

(`ClassicUO.Game` is already imported via `using ClassicUO.Game;` at the top of `Plugin.cs`; `ClassicUO.Game.WalkState` is used fully-qualified above for clarity.)

- [ ] **Step 6: Subscribe the forward once in GameController**

In `src/ClassicUO.Client/GameController.cs`, in the constructor, add the subscription immediately after `PluginHost = pluginHost;` (line 63):

```csharp
            PluginHost = pluginHost;
            ClassicUO.Game.Pathfinder.WalkProgress += ClassicUO.Network.Plugin.OnWalkProgress;
```

This runs once per client process (GameController is constructed once) and is never reached by unit tests, so `Pathfinder.WalkProgress` stays clean in the test harness. `Plugin.OnWalkProgress` reads `Client.Game.PluginHost` lazily at fire time, by which point `Client.Game` is set.

- [ ] **Step 7: Build the client — expect PASS**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: build succeeds. The cuo `HostBindings`/`ClientBindings` now match the v2 BootstrapHost structs field-for-field.

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/PluginHost.cs src/ClassicUO.Client/Network/Plugin.cs src/ClassicUO.Client/GameController.cs
git commit -m "feat(plugin-v2): wire WalkTo/StopWalk/WalkProgress across the cuo boundary

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW"
```

---

### Task 4: Legacy net472 apphost layout + bind parity

cuo binds `setup->WalkProgressFn` unconditionally, and the legacy `ClassicUO.Bootstrap` apphost allocates `HostBindings` using its own `sizeof`. Without appending the field, the legacy buffer is one pointer too small (cuo reads out of bounds) and the bound pointer is garbage/null (cuo throws). This task appends `WalkProgressFn` to the legacy struct and populates it with a no-op handler. The legacy `ClientBindings` needs no change: cuo allocates and sizes that table, the appended `WalkToFn`/`StopWalkFn` sit after the fields the legacy host reads, and existing field offsets are unchanged.

**Files:**
- Modify: `src/ClassicUO.Bootstrap/src/Program.cs:124-147` (delegate type + field), `:152-166` (ctor init), `:219-234` (BuildHostBindings set), `:309-313` (handler), `:529-547` (HostBindings struct)

**Interfaces:**
- Consumes: existing `FuncPointer<T>`, the `hostSetup.*Fn = _<x>Del.Pointer` pattern.
- Produces: legacy `HostBindings.WalkProgressFn` populated with a non-null no-op cdecl pointer.

- [ ] **Step 1: Append WalkProgressFn to the legacy HostBindings struct**

In `src/ClassicUO.Bootstrap/src/Program.cs`, append the field to the end of the `HostBindings` struct (after `PacketOutFn`, line 546):

```csharp
    public IntPtr PacketInFn;
    public IntPtr PacketOutFn;
    public IntPtr /*delegate*<int, void>*/ WalkProgressFn;
}
```

- [ ] **Step 2: Declare the delegate type, field, and no-op handler**

Add the delegate type next to the other host-callback delegate declarations (near line 124, `delegate bool dOnUpdatePlayerPosition(int x, int y, int z);`):

```csharp
    delegate void dOnWalkProgress(int state);
```

Add the backing field next to `_updatePlayerPosDel` (after line 144):

```csharp
    private readonly FuncPointer<dOnWalkProgress> _walkProgressDel;
```

Add the no-op handler next to `UpdatePlayerPosition` (after the method ending at line 313):

```csharp
    void WalkProgress(int state)
    {
        // The legacy razor host has no v2 WalkProgress consumers. This no-op
        // keeps the HostBindings function-pointer table fully populated so
        // cuo can bind WalkProgressFn unconditionally (matching UpdatePlayerPos).
    }
```

- [ ] **Step 3: Initialize and publish the pointer**

In the constructor, initialize the FuncPointer next to `_updatePlayerPosDel = ...` (after line 162):

```csharp
        _walkProgressDel = new FuncPointer<dOnWalkProgress>(WalkProgress);
```

In the host-bindings build block, set the struct field next to `hostSetup.UpdatePlayerPosFn = ...` (after line 228):

```csharp
            hostSetup.WalkProgressFn = _walkProgressDel.Pointer;
```

- [ ] **Step 4: Build the legacy apphost — expect PASS**

Run: `dotnet build src/ClassicUO.Bootstrap/src/ClassicUO.Bootstrap.csproj`
Expected: build succeeds. (If the project does not build standalone on this machine due to its net472 target, instead run `dotnet build` at the repo root and confirm no new errors in `ClassicUO.Bootstrap`.)

- [ ] **Step 5: Full solution build + both test suites — expect PASS**

Run:
```bash
dotnet build
dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~WalkProgressTests"
dotnet test tests/ClassicUO.BootstrapHost.Tests
```
Expected: solution builds; 4 Pathfinder tests pass; all BootstrapHost tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Bootstrap/src/Program.cs
git commit -m "fix(bootstrap-legacy): append WalkProgressFn to keep HostBindings layout in sync

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW"
```

---

### Task 5: Manual verification via HelloPlugin walk demo

Automated tests cover the emit seams (Task 2) and the FFI fan-out / binding invocation (Task 1). The end-to-end path — a real walk, a real `Blocked` re-route, an eventual `Arrived`, and `run` vs walk — can only be confirmed against a live cuo + UO data. This task adds a small, opt-in demo to `HelloPlugin` and documents the manual check. Note: the spec references `tools/stage-bootstraphost.ps1`, which does not exist in this repo; the v2 BootstrapHost is run by building it and placing the plugin under its `Data/Plugins/` folder (the test project already auto-stages the fixture for unit runs).

**Files:**
- Modify: `samples/HelloPlugin/HelloPlugin.cs`

**Interfaces:**
- Consumes: `IPluginContext.Connected`, `IPluginContext.Game` (`IDispatcher.Post`), `IGameActions.WalkTo`, `IGameActions.StopWalk`, `IGameActions.WalkProgress`, `IGameActions.TryGetPlayerPosition`.
- Produces: an opt-in demo gated by `CUO_PLUGIN_WALK_DEMO` that requests a walk on connect and re-routes on `Blocked`.

- [ ] **Step 1: Add the gated walk demo to HelloPlugin**

In `samples/HelloPlugin/HelloPlugin.cs`, add the demo fields and wiring. Add fields to the class:

```csharp
    private string? _logPath;
    private readonly object _logLock = new();

    private IPluginContext? _ctx;
    private int _retries;
    private (int x, int y, int z, int dist, bool run) _goal;
    private const int MaxRetries = 5;
```

In `OnInitialize`, capture the context and, when the demo env var is set, subscribe the orchestration (add after the existing `context.Actions.WalkProgress += ...` line from Task 1):

```csharp
        _ctx = context;

        if (Environment.GetEnvironmentVariable("CUO_PLUGIN_WALK_DEMO") is { Length: > 0 } target
            && TryParseGoal(target, out _goal))
        {
            context.Connected += () => context.Game.Post(() =>
            {
                _retries = 0;
                Log($"WalkDemo:start {_goal.x},{_goal.y},{_goal.z} dist={_goal.dist} run={_goal.run}");
                _ctx!.Actions.WalkTo(_goal.x, _goal.y, _goal.z, _goal.dist, _goal.run);
            });

            context.Actions.WalkProgress += state =>
            {
                if (state == ClassicUO.PluginApi.WalkState.Blocked && _retries++ < MaxRetries)
                {
                    Log($"WalkDemo:reroute attempt={_retries}");
                    _ctx!.Game.Post(() =>
                        _ctx!.Actions.WalkTo(_goal.x, _goal.y, _goal.z, _goal.dist, _goal.run));
                }
            };
        }
```

Add the parse helper to the class (after `OnShutdown`):

```csharp
    // "x,y,z,dist,run" e.g. "1450,1670,0,1,true"
    private static bool TryParseGoal(string s, out (int x, int y, int z, int dist, bool run) goal)
    {
        goal = default;
        var p = s.Split(',');
        if (p.Length != 5) return false;
        if (!int.TryParse(p[0], out var x) || !int.TryParse(p[1], out var y) ||
            !int.TryParse(p[2], out var z) || !int.TryParse(p[3], out var d) ||
            !bool.TryParse(p[4], out var run))
            return false;
        goal = (x, y, z, d, run);
        return true;
    }
```

- [ ] **Step 2: Build HelloPlugin + run the host suite (regression) — expect PASS**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests`
Expected: PASS — the demo is gated by `CUO_PLUGIN_WALK_DEMO` (unset in tests), so existing smoke/binding/fan-out tests are unaffected.

- [ ] **Step 3: Manual end-to-end check (documented; not a CI gate)**

Procedure (operator, with a live UO server + data):
1. Build the v2 host and client: `dotnet build`.
2. Stage `HelloPlugin.dll` under the BootstrapHost's `Data/Plugins/HelloPlugin/`.
3. Set environment: `CUO_PLUGIN_TEST_LOG=<path>\walk.log`, `CUO_PLUGIN_WALK_DEMO=<x>,<y>,<z>,1,true` where the tile sits a few steps away behind a mobile.
4. Launch via the BootstrapHost apphost and log in.
5. Confirm in `walk.log`: a `WalkDemo:start ...` line, a `Walk:Walking` line, at least one `Walk:Blocked` + `WalkDemo:reroute attempt=N` when a mobile crosses the path, and a terminal `Walk:Arrived`. Confirm the character **runs** (not walks) for `run=true`, and walks when the env var ends in `false`.
6. Confirm cancelling (e.g. click elsewhere → `StopAutoWalk`) produces `Walk:Stopped`.

- [ ] **Step 4: Commit**

```bash
git add samples/HelloPlugin/HelloPlugin.cs
git commit -m "test(hello-plugin): add gated WalkTo demo for manual obstacle-bypass verification

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW"
```

---

## Self-Review

**1. Spec coverage:**

| Spec requirement | Task / Step |
|---|---|
| `IGameActions.WalkTo(x,y,z,distance,run):bool` | T1 S1, impl T1 S6 |
| `IGameActions.StopWalk()` | T1 S1, impl T1 S6 |
| `event WalkProgress` | T1 S1, impl T1 S6 |
| `WalkState` enum (PluginApi) | T1 S1 |
| Client `EndAutoWalk(reason)` + no-emit reset | T2 S5 |
| Emit Walking / Arrived / Blocked / Stopped at the 4 transition points | T2 S4 (Walking, reset), T2 S5 (Arrived/Blocked/Stopped) |
| Prior-walk clear must NOT emit | T2 S4 (`ResetAutoWalk`) + T2 S1 test |
| `_run = run` setter | T2 S4 |
| Boundary wiring — PluginApi | T1 S1 |
| Boundary wiring — BootstrapHost HostBridge (fan-out + structs) | T1 S5 |
| Boundary wiring — BootstrapHost PluginContextImpl (actions + event) | T1 S6 |
| Boundary wiring — cuo PluginHost (fn-ptr fields, bind, emit) | T3 S1-S4 |
| Boundary wiring — cuo Pathfinder | T2 |
| Boundary wiring — cuo Network/Plugin bridge + emit subscription | T3 S5-S6 |
| Legacy struct layout parity | T4 |
| Client unit tests (emit seams) | T2 S1 |
| Host tests (fan-out + binding invocation) | T1 S3, S8 |
| Manual HelloPlugin re-path/Arrived demo | T5 |

**2. Placeholder scan:** No TBD/TODO/"add error handling"/"similar to Task N" left in code steps; every code step shows complete code. The legacy no-op `WalkProgress` body is intentionally empty with an explanatory comment, not a placeholder.

**3. Type consistency:** `WalkState` appears as two parallel enums by design — `ClassicUO.PluginApi.WalkState` (public, T1) and `ClassicUO.Game.WalkState` (internal, T2) — with identical ordinals (`Walking=0, Arrived=1, Blocked=2, Stopped=3`); the boundary passes the `int` value and casts on each side (`(WalkState)state` in `OnWalkProgress`, `(int)state` in `Plugin.OnWalkProgress`), mirroring how `UpdatePlayerPosition` passes raw ints. Method names are consistent across tasks: `EndAutoWalk`/`ResetAutoWalk`/`RaiseWalkProgress` (Pathfinder), `RaiseWalkProgress` (PluginContextImpl + GameActionsImpl), `TestRaiseWalkProgress`/`InstallClientBindingsForTest` (HostBridge), `WalkTo`/`StopWalk`/`OnWalkProgress` (Plugin). Struct field order is append-only and identical across the three `HostBindings`/`ClientBindings` copies.

## Risks / spec deviations discovered while reading the real code

- **Action pointers live in `ClientBindings`, not `HostBindings`.** The spec's architecture diagram labels the action arrows `HostBindings.WalkToFn`/`StopWalkFn`, but `CastSpellFn`/`RequestMoveFn` (the cited precedent) live in the cuo-populated `ClientBindings` table. This plan puts `WalkToFn`/`StopWalkFn` in `ClientBindings` and only the event `WalkProgressFn` in `HostBindings` — matching the real precedent. Both structs are defined in `HostBridge.cs`, so the spec's "files touched" entry is still accurate.
- **Pathfinder transitions are not unit-testable end-to-end.** `WalkTo`/`ProcessAutoWalk` need a live `Player` and loaded map data (`FindPath`/`CalculateNewZ`), unavailable in CI. The spec's client unit tests are therefore implemented at the emit seams (`EndAutoWalk`/`ResetAutoWalk`/`StopAutoWalk`); `run` propagation and the exact Arrived/Blocked call sites move to the manual demo (T5). Documented in "Testability note."
- **`FindPath` already force-sets `_run = true` for long paths** (`Pathfinder.cs:880-883`, goal-dist > 14). So a plugin passing `run:false` to a far tile will still run. This is pre-existing auto-run behavior, preserved deliberately (the spec calls the `run` change "the only behavior change"). Flagged so it is not mistaken for a bug.
- **Legacy parity is REQUIRED, not optional.** Because cuo binds `WalkProgressFn` unconditionally and the legacy net472 apphost both allocates `HostBindings` by its own `sizeof` and is bound by cuo, the legacy `Program.cs` MUST append the field and populate it (Task 4) or the legacy launch path reads out of bounds / throws. The spec's "if required" is, in practice, required. Legacy `ClientBindings` needs no change (cuo owns that allocation; appended fields are past the legacy read set).
- **`tools/stage-bootstraphost.ps1` does not exist.** The spec references it for manual staging; this repo stages the test fixture via the test project's `CopyHelloPluginFixture` MSBuild target instead. Manual run staging is described procedurally in T5 S3.
- **Static `WalkProgress` event + xUnit parallelism.** The Pathfinder event is static (process-wide). Unit tests subscribe/unsubscribe per test in `finally`; only one test class touches it, and xUnit runs methods within a class serially, so isolation holds. The BootstrapHost tests share a `[Collection("BootstrapHost", DisableParallelization=true)]` to avoid the process-wide `CUO_PLUGIN_TEST_LOG` env var racing across classes.
