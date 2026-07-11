# Plugin Hover-Instant Targeting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a v2 plugin trigger a client-side targeting session that resolves instantly against whatever object is under the mouse cursor, by adding a new client-recognized subtype of the existing `0x6C` Target Cursor packet — no new `ClassicUO.PluginApi` surface.

**Architecture:** A new `CursorTarget.PluginHoverTarget` enum value (a byte value a real UO server never sends) carries an extra accept-mask byte in the packet's existing unused padding. The `TargetCursor` packet handler parses it and stores it on `TargetManager`. A new per-frame hook in `GameScene.Update` checks the already-computed `SelectedObject.Object` against the mask: match → resolve exactly like a manual click (real outgoing Target Response, observable by the plugin via the existing `Packets.Outgoing` hook); no match (including nothing under the cursor) → cancel immediately. Manual click still works as a fallback for this state.

**Tech Stack:** C# / .NET 10, xUnit (`tests/ClassicUO.UnitTests`), existing `ClassicUO.Client` internals (`TargetManager`, `PacketHandlers`, `GameSceneInputHandler`, `GameScene`), `ClassicUO.PluginApi.HighlightObjectTypes` (reused, not modified).

## Global Constraints

- No new `ClassicUO.PluginApi` surface — this is a pure client-internal + wire-protocol change. Do not touch `src/ClassicUO.PluginApi/*` or `PluginApi/README.md`.
- `PluginHoverTarget` must be **appended** to the `CursorTarget` enum, never inserted between existing values.
- Hover confirm/cancel is **instant** — no dwell delay, no timeout/expiry.
- Mismatch (including `SelectedObject.Object == null`, e.g. mouse over a gump) → cancel **immediately**, symmetric with the confirm path.
- The Target Response packet sent on hover-confirm must be byte-identical to what a manual click would send — reuse the existing `Target(...)` codepath, no parallel packet format.
- Manual click must keep working unchanged for every existing `CursorTarget` state, including the new one (fallback path).
- Design reference: `docs/superpowers/specs/2026-07-11-plugin-hover-target-design.md`.

---

### Task 1: `PluginHoverTarget` state on `TargetManager`

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/TargetManager.cs:1-12` (add `using ClassicUO.PluginApi;`)
- Modify: `src/ClassicUO.Client/Game/Managers/TargetManager.cs:16-28` (`CursorTarget` enum)
- Modify: `src/ClassicUO.Client/Game/Managers/TargetManager.cs:113` (new field/property, next to `LastAttack, SelectedTarget, NewTargetSystemSerial`)
- Modify: `src/ClassicUO.Client/Game/Managers/TargetManager.cs:138-148` (`Reset()`)
- Modify: `src/ClassicUO.Client/Game/Managers/TargetManager.cs:157-183` (`SetTargeting(CursorTarget, uint, TargetType)`)
- Test: `tests/ClassicUO.UnitTests/Game/Managers/TargetManagerPluginHoverStateTests.cs` (new)

**Interfaces:**
- Produces: `public HighlightObjectTypes TargetManager.PluginHoverAcceptedTypes { get; }` (default `HighlightObjectTypes.All`); `SetTargeting(CursorTarget targeting, uint cursorID, TargetType cursorType, HighlightObjectTypes acceptedTypes = HighlightObjectTypes.All)`; new enum value `CursorTarget.PluginHoverTarget`.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Managers/TargetManagerPluginHoverStateTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.Managers;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class TargetManagerPluginHoverStateTests
    {
        [Fact]
        public void PluginHoverAcceptedTypes_DefaultsToAll()
        {
            var world = new World();
            var sut = new TargetManager(world);

            Assert.Equal(HighlightObjectTypes.All, sut.PluginHoverAcceptedTypes);
        }

        [Fact]
        public void SetTargeting_StoresAcceptedTypes_ForPluginHoverTarget()
        {
            var world = new World();
            var sut = new TargetManager(world);

            sut.SetTargeting(
                CursorTarget.PluginHoverTarget,
                cursorID: 1,
                TargetType.Neutral,
                HighlightObjectTypes.Mobile | HighlightObjectTypes.Item
            );

            Assert.Equal(CursorTarget.PluginHoverTarget, sut.TargetingState);
            Assert.Equal(HighlightObjectTypes.Mobile | HighlightObjectTypes.Item, sut.PluginHoverAcceptedTypes);
        }

        [Fact]
        public void Reset_RestoresAcceptedTypes_ToAll()
        {
            var world = new World();
            var sut = new TargetManager(world);
            sut.SetTargeting(CursorTarget.PluginHoverTarget, 1, TargetType.Neutral, HighlightObjectTypes.Land);

            sut.Reset();

            Assert.Equal(HighlightObjectTypes.All, sut.PluginHoverAcceptedTypes);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TargetManagerPluginHoverStateTests"`
Expected: FAIL (compile error) — `PluginHoverAcceptedTypes` does not exist on `TargetManager`, and `SetTargeting` has no 4-argument overload.

- [ ] **Step 3: Implement**

In `src/ClassicUO.Client/Game/Managers/TargetManager.cs`, add to the using block at the top:

```csharp
using ClassicUO.PluginApi;
```

Change the `CursorTarget` enum:

```csharp
internal enum CursorTarget
{
    Invalid = -1,
    Object = 0,
    Position = 1,
    MultiPlacement = 2,
    SetTargetClientSide = 3,
    Grab,
    SetGrabBag,
    HueCommandTarget,
    IgnorePlayerTarget,
    CallbackTarget,
    PluginHoverTarget
}
```

Add the property next to `LastAttack, SelectedTarget, NewTargetSystemSerial;`:

```csharp
public uint LastAttack, SelectedTarget, NewTargetSystemSerial;

public HighlightObjectTypes PluginHoverAcceptedTypes { get; private set; } = HighlightObjectTypes.All;
```

Update `Reset()`:

```csharp
public void Reset()
{
    ClearTargetingWithoutTargetCancelPacket();

    _targetCallback = null;

    TargetingState = 0;
    _targetCursorId = 0;
    MultiTargetInfo = null;
    TargetingType = 0;
    PluginHoverAcceptedTypes = HighlightObjectTypes.All;
}
```

Update `SetTargeting`:

```csharp
public void SetTargeting(CursorTarget targeting, uint cursorID, TargetType cursorType, HighlightObjectTypes acceptedTypes = HighlightObjectTypes.All)
{
    if (targeting == CursorTarget.Invalid)
    {
        return;
    }

    bool lastTargetting = IsTargeting;
    IsTargeting = cursorType < TargetType.Cancel;
    TargetingState = targeting;
    TargetingType = cursorType;
    PluginHoverAcceptedTypes = acceptedTypes;

    if (IsTargeting)
    {
        //UIManager.RemoveTargetLineGump(LastTarget);
    }
    else if (lastTargetting)
    {
        CancelTarget();
    }

    // https://github.com/andreakarasho/ClassicUO/issues/1373
    // when receiving a cancellation target from the server we need
    // to send the last active cursorID, so update cursor data later

    _targetCursorId = cursorID;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TargetManagerPluginHoverStateTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add tests/ClassicUO.UnitTests/Game/Managers/TargetManagerPluginHoverStateTests.cs src/ClassicUO.Client/Game/Managers/TargetManager.cs
git commit -m "feat(targeting): add PluginHoverTarget cursor state"
```

---

### Task 2: Packet-level accept-mask parsing

**Files:**
- Modify: `src/ClassicUO.Client/Network/PacketHandlers.cs:1-9` (add `using ClassicUO.PluginApi;` if not already present)
- Modify: `src/ClassicUO.Client/Network/PacketHandlers.cs:359-373` (`TargetCursor` handler)
- Test: `tests/ClassicUO.UnitTests/Network/TargetCursorPacketParsingTests.cs` (new)

**Interfaces:**
- Consumes: `CursorTarget.PluginHoverTarget` (Task 1), `TargetManager.SetTargeting(..., HighlightObjectTypes)` (Task 1).
- Produces: `internal static HighlightObjectTypes PacketHandlers.ReadPluginHoverAcceptedTypes(CursorTarget cursorTarget, ref StackDataReader p)`.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Network/TargetCursorPacketParsingTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Game.Managers;
using ClassicUO.IO;
using ClassicUO.Network;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Network
{
    public class TargetCursorPacketParsingTests
    {
        [Fact]
        public void ReadPluginHoverAcceptedTypes_ReturnsAll_ForRealServerCursorTarget()
        {
            Span<byte> data = new byte[] { 0x00 };
            var reader = new StackDataReader(data);

            HighlightObjectTypes result = PacketHandlers.ReadPluginHoverAcceptedTypes(CursorTarget.Object, ref reader);

            Assert.Equal(HighlightObjectTypes.All, result);
            Assert.Equal(1, reader.Remaining);

            reader.Release();
        }

        [Fact]
        public void ReadPluginHoverAcceptedTypes_ReadsMaskByte_ForPluginHoverTarget()
        {
            Span<byte> data = new byte[] { (byte)(HighlightObjectTypes.Mobile | HighlightObjectTypes.Item) };
            var reader = new StackDataReader(data);

            HighlightObjectTypes result = PacketHandlers.ReadPluginHoverAcceptedTypes(CursorTarget.PluginHoverTarget, ref reader);

            Assert.Equal(HighlightObjectTypes.Mobile | HighlightObjectTypes.Item, result);
            Assert.Equal(0, reader.Remaining);

            reader.Release();
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TargetCursorPacketParsingTests"`
Expected: FAIL (compile error) — `PacketHandlers.ReadPluginHoverAcceptedTypes` does not exist.

- [ ] **Step 3: Implement**

In `src/ClassicUO.Client/Network/PacketHandlers.cs`, add (if missing) to the using block:

```csharp
using ClassicUO.PluginApi;
```

Replace the `TargetCursor` method (currently at line 359):

```csharp
private static void TargetCursor(World world, ref StackDataReader p)
{
    var cursorTarget = (CursorTarget)p.ReadUInt8();
    uint cursorId = p.ReadUInt32BE();
    var targetType = (TargetType)p.ReadUInt8();
    HighlightObjectTypes acceptedTypes = ReadPluginHoverAcceptedTypes(cursorTarget, ref p);

    world.TargetManager.SetTargeting(cursorTarget, cursorId, targetType, acceptedTypes);

    if (world.Party.PartyHealTimer < Time.Ticks && world.Party.PartyHealTarget != 0)
    {
        world.TargetManager.Target(world.Party.PartyHealTarget);
        world.Party.PartyHealTimer = 0;
        world.Party.PartyHealTarget = 0;
    }
}

internal static HighlightObjectTypes ReadPluginHoverAcceptedTypes(CursorTarget cursorTarget, ref StackDataReader p)
{
    return cursorTarget == CursorTarget.PluginHoverTarget
        ? (HighlightObjectTypes)p.ReadUInt8()
        : HighlightObjectTypes.All;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TargetCursorPacketParsingTests"`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add tests/ClassicUO.UnitTests/Network/TargetCursorPacketParsingTests.cs src/ClassicUO.Client/Network/PacketHandlers.cs
git commit -m "feat(targeting): parse plugin hover accept-mask from Target Cursor packet"
```

---

### Task 3: Object-category classification (`MatchesAcceptedTypes`)

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/TargetManager.cs:405-407` (insert new method after `Target(uint serial)`, before `Target(ushort graphic, ...)`)
- Test: `tests/ClassicUO.UnitTests/Game/Managers/TargetManagerMatchesAcceptedTypesTests.cs` (new)

**Interfaces:**
- Consumes: `ClassicUO.Game.GameObjects.{Mobile, Item, Land, Static, Multi, GameObject, BaseGameObject}` (existing).
- Produces: `internal static bool TargetManager.MatchesAcceptedTypes(BaseGameObject obj, HighlightObjectTypes accepted)`.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Managers/TargetManagerMatchesAcceptedTypesTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class TargetManagerMatchesAcceptedTypesTests
    {
        [Fact]
        public void Mobile_MatchesMobileFlag_NotItemFlag()
        {
            var world = new World();
            var mobile = new Mobile(world);

            Assert.True(TargetManager.MatchesAcceptedTypes(mobile, HighlightObjectTypes.Mobile));
            Assert.False(TargetManager.MatchesAcceptedTypes(mobile, HighlightObjectTypes.Item));
        }

        [Fact]
        public void CorpseItem_MatchesCorpseFlag_NotItemFlag()
        {
            var world = new World();
            var corpse = new Item(world) { Graphic = 0x2006 };

            Assert.True(TargetManager.MatchesAcceptedTypes(corpse, HighlightObjectTypes.Corpse));
            Assert.False(TargetManager.MatchesAcceptedTypes(corpse, HighlightObjectTypes.Item));
        }

        [Fact]
        public void PlainItem_MatchesItemFlag_NotCorpseFlag()
        {
            var world = new World();
            var item = new Item(world) { Graphic = 0x0EED };

            Assert.True(TargetManager.MatchesAcceptedTypes(item, HighlightObjectTypes.Item));
            Assert.False(TargetManager.MatchesAcceptedTypes(item, HighlightObjectTypes.Corpse));
        }

        [Fact]
        public void Land_MatchesLandFlag_NotStaticFlag()
        {
            var world = new World();
            var land = Land.Create(world, 3);

            Assert.True(TargetManager.MatchesAcceptedTypes(land, HighlightObjectTypes.Land));
            Assert.False(TargetManager.MatchesAcceptedTypes(land, HighlightObjectTypes.Static));
        }

        [Fact]
        public void Static_MatchesStaticFlag()
        {
            var world = new World();
            var stat = new Static(world);

            Assert.True(TargetManager.MatchesAcceptedTypes(stat, HighlightObjectTypes.Static));
        }

        [Fact]
        public void Multi_MatchesMultiFlag()
        {
            var world = new World();
            var multi = new Multi(world);

            Assert.True(TargetManager.MatchesAcceptedTypes(multi, HighlightObjectTypes.Multi));
        }

        [Fact]
        public void ReturnsFalse_WhenMaskExcludesCategory()
        {
            var world = new World();
            var mobile = new Mobile(world);

            Assert.False(TargetManager.MatchesAcceptedTypes(mobile, HighlightObjectTypes.Land | HighlightObjectTypes.Static));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TargetManagerMatchesAcceptedTypesTests"`
Expected: FAIL (compile error) — `TargetManager.MatchesAcceptedTypes` does not exist.

- [ ] **Step 3: Implement**

In `src/ClassicUO.Client/Game/Managers/TargetManager.cs`, insert this new method right after the closing brace of `Target(uint serial)` (line 405), before `public void Target(ushort graphic, ...)`:

```csharp
internal static bool MatchesAcceptedTypes(BaseGameObject obj, HighlightObjectTypes accepted)
{
    HighlightObjectTypes category = obj switch
    {
        Mobile => HighlightObjectTypes.Mobile,
        Item item when item.IsCorpse => HighlightObjectTypes.Corpse,
        Item => HighlightObjectTypes.Item,
        Land => HighlightObjectTypes.Land,
        Multi => HighlightObjectTypes.Multi,
        Static => HighlightObjectTypes.Static,
        GameObject => HighlightObjectTypes.Static,
        _ => HighlightObjectTypes.None
    };

    return category != HighlightObjectTypes.None && (accepted & category) != 0;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TargetManagerMatchesAcceptedTypesTests"`
Expected: PASS (6 tests)

- [ ] **Step 5: Commit**

```bash
git add tests/ClassicUO.UnitTests/Game/Managers/TargetManagerMatchesAcceptedTypesTests.cs src/ClassicUO.Client/Game/Managers/TargetManager.cs
git commit -m "feat(targeting): classify hovered objects against plugin accept-mask"
```

---

### Task 4: Extract shared object-dispatch (`TryResolveObject`)

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/TargetManager.cs` (insert new method, same location as Task 3 — after `MatchesAcceptedTypes`)
- Modify: `src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs:530-575` (click handler)
- Test: `tests/ClassicUO.UnitTests/Game/Managers/TargetManagerTryResolveObjectTests.cs` (new)

**Interfaces:**
- Consumes: `TargetManager.Target(uint serial)`, `TargetManager.Target(ushort graphic, ushort x, ushort y, short z, bool wet)` (existing, unchanged signatures), `TextObject.Owner` (existing).
- Produces: `internal bool TargetManager.TryResolveObject(BaseGameObject obj)`.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Managers/TargetManagerTryResolveObjectTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class TargetManagerTryResolveObjectTests
    {
        [Fact]
        public void ReturnsFalse_ForNullObject()
        {
            var world = new World();
            var sut = new TargetManager(world);

            Assert.False(sut.TryResolveObject(null));
        }

        [Fact]
        public void ReturnsTrue_ForEntityObject()
        {
            var world = new World();
            var sut = new TargetManager(world);
            var item = new Item(world);

            Assert.True(sut.TryResolveObject(item));
        }

        [Fact]
        public void ReturnsTrue_ForLandObject()
        {
            var world = new World();
            var sut = new TargetManager(world);
            var land = Land.Create(world, 3);

            Assert.True(sut.TryResolveObject(land));
        }

        [Fact]
        public void ReturnsTrue_ForStaticObject()
        {
            var world = new World();
            var sut = new TargetManager(world);
            var stat = new Static(world);

            Assert.True(sut.TryResolveObject(stat));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TargetManagerTryResolveObjectTests"`
Expected: FAIL (compile error) — `TargetManager.TryResolveObject` does not exist.

- [ ] **Step 3: Implement**

In `src/ClassicUO.Client/Game/Managers/TargetManager.cs`, add next to `MatchesAcceptedTypes`:

```csharp
internal bool TryResolveObject(BaseGameObject obj)
{
    if (obj is TextObject textObject)
    {
        obj = textObject.Owner;
    }

    switch (obj)
    {
        case Entity ent:
            Target(ent.Serial);
            return true;

        case Land land:
            Target(0, land.X, land.Y, land.Z, land.TileData.IsWet);
            return true;

        case GameObject o:
            Target(o.Graphic, o.X, o.Y, o.Z);
            return true;

        default:
            return false;
    }
}
```

In `src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs`, replace the block currently at lines 530-575:

```csharp
else if (_world.TargetManager.IsTargeting)
{
    switch (_world.TargetManager.TargetingState)
    {
        case CursorTarget.Grab:
        case CursorTarget.SetGrabBag:
        case CursorTarget.Position:
        case CursorTarget.Object:
        case CursorTarget.MultiPlacement when _world.CustomHouseManager == null:
        case CursorTarget.CallbackTarget:
        case CursorTarget.PluginHoverTarget:
        {
            _world.TargetManager.TryResolveObject(lastObj);

            Mouse.LastLeftButtonClickTime = 0;

            break;
        }

        case CursorTarget.SetTargetClientSide:
        // ... unchanged, keep everything below this line exactly as-is
```

Everything from `case CursorTarget.SetTargetClientSide:` onward (its own inline switch on `obj`/`InspectorGump`) is untouched — only the first `case` block's body is replaced, and `CursorTarget.PluginHoverTarget` is added to that first case list so manual click still works for the new state as a fallback.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TargetManagerTryResolveObjectTests"`
Expected: PASS (4 tests)

Then confirm the whole solution still builds (this step touches a scene file with no direct unit test):

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds, no errors.

- [ ] **Step 5: Commit**

```bash
git add tests/ClassicUO.UnitTests/Game/Managers/TargetManagerTryResolveObjectTests.cs src/ClassicUO.Client/Game/Managers/TargetManager.cs src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs
git commit -m "refactor(targeting): extract click-confirm dispatch into TryResolveObject"
```

---

### Task 5: Hover confirm/cancel decision (`CheckPluginHoverTarget`)

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/TargetManager.cs` (insert new method after `TryResolveObject`)
- Test: `tests/ClassicUO.UnitTests/Game/Managers/TargetManagerCheckPluginHoverTargetTests.cs` (new)

**Interfaces:**
- Consumes: `TargetManager.MatchesAcceptedTypes` (Task 3), `TargetManager.TryResolveObject` (Task 4), `TargetManager.CancelTarget()` (existing), `TargetManager.PluginHoverAcceptedTypes` (Task 1).
- Produces: `internal void TargetManager.CheckPluginHoverTarget(BaseGameObject hovered)`.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Managers/TargetManagerCheckPluginHoverTargetTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class TargetManagerCheckPluginHoverTargetTests
    {
        [Fact]
        public void NoOp_WhenNotTargeting()
        {
            var world = new World();
            var sut = new TargetManager(world);

            sut.CheckPluginHoverTarget(new Item(world));

            Assert.False(sut.IsTargeting);
        }

        [Fact]
        public void NoOp_WhenTargetingButNotPluginHoverState()
        {
            var world = new World();
            var sut = new TargetManager(world);
            sut.SetTargeting(CursorTarget.Object, 1, TargetType.Neutral);

            sut.CheckPluginHoverTarget(new Item(world));

            Assert.True(sut.IsTargeting);
            Assert.Equal(CursorTarget.Object, sut.TargetingState);
        }

        [Fact]
        public void Cancels_WhenHoveredObjectIsNull()
        {
            var world = new World();
            var sut = new TargetManager(world);
            sut.SetTargeting(CursorTarget.PluginHoverTarget, 1, TargetType.Neutral, HighlightObjectTypes.All);

            sut.CheckPluginHoverTarget(null);

            Assert.False(sut.IsTargeting);
        }

        [Fact]
        public void Cancels_WhenHoveredObjectDoesNotMatchMask()
        {
            var world = new World();
            var sut = new TargetManager(world);
            sut.SetTargeting(CursorTarget.PluginHoverTarget, 1, TargetType.Neutral, HighlightObjectTypes.Mobile);
            var item = new Item(world);

            sut.CheckPluginHoverTarget(item);

            Assert.False(sut.IsTargeting);
        }

        [Fact]
        public void DoesNotCancel_WhenHoveredObjectMatchesMask()
        {
            // Full packet-send confirmation requires world.InGame (Player + Map),
            // which needs a real profile/session and is out of scope for a unit
            // test (see the design spec's "manual in-game verification" note).
            // This asserts the dispatcher took the confirm branch (not cancel):
            // Target(...) itself no-ops without InGame, but does NOT cancel.
            var world = new World();
            var sut = new TargetManager(world);
            sut.SetTargeting(CursorTarget.PluginHoverTarget, 1, TargetType.Neutral, HighlightObjectTypes.Item);
            var item = new Item(world);

            sut.CheckPluginHoverTarget(item);

            Assert.True(sut.IsTargeting);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TargetManagerCheckPluginHoverTargetTests"`
Expected: FAIL (compile error) — `TargetManager.CheckPluginHoverTarget` does not exist.

- [ ] **Step 3: Implement**

In `src/ClassicUO.Client/Game/Managers/TargetManager.cs`, add next to `TryResolveObject`:

```csharp
internal void CheckPluginHoverTarget(BaseGameObject hovered)
{
    if (!IsTargeting || TargetingState != CursorTarget.PluginHoverTarget)
    {
        return;
    }

    if (hovered != null && MatchesAcceptedTypes(hovered, PluginHoverAcceptedTypes))
    {
        TryResolveObject(hovered);
    }
    else
    {
        CancelTarget();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TargetManagerCheckPluginHoverTargetTests"`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add tests/ClassicUO.UnitTests/Game/Managers/TargetManagerCheckPluginHoverTargetTests.cs src/ClassicUO.Client/Game/Managers/TargetManager.cs
git commit -m "feat(targeting): resolve or cancel plugin hover session based on cursor object"
```

---

### Task 6: Wire the per-frame hover-check into `GameScene.Update`

**Files:**
- Modify: `src/ClassicUO.Client/Game/Scenes/GameScene.cs:819-822`

**Interfaces:**
- Consumes: `TargetManager.CheckPluginHoverTarget(BaseGameObject)` (Task 5), `SelectedObject.Object` (existing static, already read/written a few lines above the insertion point).

No unit test is possible for this step — `GameScene` requires a full graphics/game-loop context that isn't available in `tests/ClassicUO.UnitTests`. Correctness is covered by: (a) the `CheckPluginHoverTarget` unit tests from Task 5, which already prove the decision logic in isolation, and (b) the manual in-game verification below.

- [ ] **Step 1: Implement**

In `src/ClassicUO.Client/Game/Scenes/GameScene.cs`, change:

```csharp
if (!UIManager.IsMouseOverWorld)
{
    SelectedObject.Object = null;
}

if (
    _world.TargetManager.IsTargeting
    && _world.TargetManager.TargetingState == CursorTarget.MultiPlacement
    && _world.CustomHouseManager == null
    && _world.TargetManager.MultiTargetInfo != null
)
```

to:

```csharp
if (!UIManager.IsMouseOverWorld)
{
    SelectedObject.Object = null;
}

_world.TargetManager.CheckPluginHoverTarget(SelectedObject.Object);

if (
    _world.TargetManager.IsTargeting
    && _world.TargetManager.TargetingState == CursorTarget.MultiPlacement
    && _world.CustomHouseManager == null
    && _world.TargetManager.MultiTargetInfo != null
)
```

The insertion is placed after the `IsMouseOverWorld` null-out so a mouse-over-gump frame is already reflected as `SelectedObject.Object == null` before the hover-check runs (matches the design's "null counts as mismatch" decision without any extra guard).

- [ ] **Step 2: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds, no errors.

- [ ] **Step 3: Run the full unit test suite (regression check)**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: All tests pass, including every test added in Tasks 1-5.

- [ ] **Step 4: Manual in-game verification**

Requires a real UO account/session (see `docs/superpowers/specs/2026-07-11-plugin-hover-target-design.md`, §5 Testing):

1. Build and run the client, log into a shard.
2. Using a v2 test plugin (or a scripted `IPacketPipeline.SendToClient` call), inject a `0x6C` packet: byte 1 = `PluginHoverTarget`'s numeric value (10 — `Object=0` through `CallbackTarget=9`, so `PluginHoverTarget=10`), bytes 2-5 = any cursor id (e.g. `0x00000001`), byte 6 = `TargetType.Neutral` (0), byte 7 = `HighlightObjectTypes.Mobile` (`0x01`), remaining bytes zero-padded to 19 total.
3. With the mouse already over a mobile when the packet is injected: confirm (via the test plugin's `Packets.Outgoing` hook) that a real Target Response packet is sent for that mobile's serial, and that the targeting cursor clears immediately.
4. Repeat with the mouse over an item (category excluded by the `Mobile`-only mask) or over a gump (nothing under the cursor): confirm a Cancel is sent and no Target Response for anything follows.
5. Confirm a normal manual-click target request (e.g. cast a spell) is completely unaffected — the player still has to click as before.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Scenes/GameScene.cs
git commit -m "feat(targeting): drive plugin hover-target resolution from the per-frame scene update"
```

## Self-Review Notes

- **Spec coverage:** §1 (enum) → Task 1. §2 (packet parsing) → Task 2. §3 (shared dispatch) → Task 4. §4 (per-frame hover-check + classification) → Tasks 3, 5, 6. §5 (testing) → automated tests in Tasks 1-5 plus the manual verification script in Task 6. No PluginApi changes anywhere, matching "Out of scope" in the design.
- **Placeholder scan:** no TBD/TODO; every step has runnable code or an exact command with expected output.
- **Type consistency:** `HighlightObjectTypes` used consistently from `ClassicUO.PluginApi` throughout (no parallel enum introduced); `SetTargeting`'s 4th parameter name (`acceptedTypes`) and `TargetManager.PluginHoverAcceptedTypes` property name are consistent across Tasks 1, 2, and 5; `TryResolveObject`/`CheckPluginHoverTarget`/`MatchesAcceptedTypes` signatures introduced in Tasks 3-5 match exactly how Task 6 and the Task 4 `GameSceneInputHandler` edit call them.
- **Known testability limit (documented, not a gap):** the "hovered object matches mask" branch cannot assert an actual sent packet or `IsTargeting == false` in a unit test, because real entity resolution requires `world.InGame` (`Player != null && Map != null`), and `World.CreatePlayer` pulls in `ProfileManager.Load`/account/session state that's out of scope to fake here. This is a pre-existing limitation of `TargetManager`'s design, not something introduced by this feature. Task 5's test documents this in a code comment; Task 6's manual verification step is the actual end-to-end proof.
