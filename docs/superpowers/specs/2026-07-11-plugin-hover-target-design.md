# Plugin-driven hover-instant targeting — design

**Date:** 2026-07-11
**Status:** Approved (brainstorm), pending implementation plan
**Branch:** `chore/update-fna-26.07` (implementation should land on its own branch)

## Summary

Let a v2 plugin trigger a client-side targeting session that resolves
**instantly against whatever object is under the mouse cursor**, instead of
requiring the player to click. A plugin already fakes a server "please pick a
target" request by injecting a synthetic `0x6C` (Target Cursor) packet via the
existing `IPacketPipeline.SendToClient`. This design adds a new, wire-level
subtype of that same packet that the client recognizes as "resolve on hover,
not on click" — no new `ClassicUO.PluginApi` surface at all.

## Background — what already exists

- **`0x6C` Target Cursor packet** (`PacketHandlers.cs:359`, `TargetCursor`)
  reads `CursorTarget` type (byte @1), `cursorId` (uint32BE @2-5), `TargetType`
  (byte @6) and calls `TargetManager.SetTargeting(...)`. The packet's fixed
  wire length is 19 bytes (`PacketsTable.cs:120`); real UO servers only ever
  populate the first 7 bytes, the rest is zero-padding the handler never reads.
- **`CursorTarget` enum** (`TargetManager.cs:16`) already has values (`Grab`,
  `SetGrabBag`, `HueCommandTarget`, `IgnorePlayerTarget`, `CallbackTarget`)
  that a real server **never** sends over the wire — they're only ever set
  directly by client-side C# code. `TargetCursor` has no whitelist: whatever
  byte a packet carries is cast straight into the enum. This is the existing
  precedent this design piggybacks on.
- **Confirm-on-click today**: `GameSceneInputHandler.cs:530-575` — on left
  click, while `IsTargeting`, switches on `TargetingState`
  (`Grab`/`SetGrabBag`/`Position`/`Object`/`MultiPlacement`/`CallbackTarget`)
  and dispatches the currently-hovered `SelectedObject.Object`
  (`Entity` → `Target(serial)`, `Land` → `Target(0, x, y, z, wet)`,
  other `GameObject` → `Target(graphic, x, y, z)`).
- **`TargetManager.Target(uint serial)`** (`TargetManager.cs:240`) sends the
  real outgoing Target Response packet (`TargetPacket`) and, for the
  `Object`/`Position`/`HueCommandTarget`/`SetTargetClientSide` case, calls
  `ClearTargetingWithoutTargetCancelPacket()` — `IsTargeting` flips `false`,
  ending the session. This packet is observable by any plugin today via the
  existing `IPacketPipeline.Outgoing` event — no new plugin-facing callback
  is needed to learn the result.
- **`HighlightObjectTypes`** (`PluginApi/IHighlight.cs`) is an existing
  `[Flags]` enum (`Mobile | Item | Corpse | Land | Static | Multi | All`)
  already referenced from `ClassicUO.Client` (`PluginHighlights.cs`). Reused
  here for the hover accept-mask instead of inventing a parallel enum.
- **No plugin-facing helper needed**: the plugin already builds raw `0x6C`
  bytes itself today to fake the initial target-cursor request, so a
  convenience wrapper method buys nothing here (confirmed during brainstorm —
  decided against adding anything to `IGameActions`).

## Decisions (from brainstorm)

| Question | Decision |
|---|---|
| New plugin API surface? | None. Pure packet-protocol + client-internal change. Plugin keeps using `Packets.SendToClient`/`Packets.Outgoing`, both of which already exist. |
| Confirm trigger | Instant — first frame the hovered object matches the accept-mask, resolve immediately. No dwell delay. |
| Mismatch behavior | Also instant: first frame the hovered object does **not** match the mask (including nothing under the cursor at all — e.g. mouse over a gump) → cancel the session immediately. In effect the very first evaluation after the session opens decides everything; the plugin is expected to have positioned the mouse before injecting the packet. |
| Scope of "object" | World-space objects only (`SelectedObject.Object`: mobile/item/corpse/land/static/multi). Status-bar-gump click-to-target (`HealthBarGump`, etc.) is a separate UI-control path, not `SelectedObject`-driven, and is out of scope. |
| Response packet | The client sends the exact same real outgoing Target Response packet it would send for a manual click, through the existing `Target(...)` codepath. No parallel packet format. |
| Manual click fallback | Still works — the new `CursorTarget` value is added to the same switch case as `Object`/`Position`/etc. in both the click handler and `Target(uint serial)`, so a player (or a plugin relying on click instead of hover) isn't broken. |

## Design

### 1. New `CursorTarget` value (`Game/Managers/TargetManager.cs:16`)

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
    PluginHoverTarget // new — appended, never sent by a real server
}
```

Appended at the end (never inserted), same rule as the `ClientBindings`
ABI-append convention elsewhere in this codebase — nothing currently persists
or logs this enum's numeric value across versions, but keeping the same
append-only habit avoids surprises if that ever changes.

### 2. Packet parsing (`PacketHandlers.cs:359`, `TargetCursor`)

```csharp
private static void TargetCursor(World world, ref StackDataReader p)
{
    var cursorTarget = (CursorTarget)p.ReadUInt8();
    uint cursorId = p.ReadUInt32BE();
    var targetType = (TargetType)p.ReadUInt8();

    HighlightObjectTypes acceptedTypes = HighlightObjectTypes.All;
    if (cursorTarget == CursorTarget.PluginHoverTarget)
    {
        acceptedTypes = (HighlightObjectTypes)p.ReadUInt8(); // byte @7, real servers never populate this
    }

    world.TargetManager.SetTargeting(cursorTarget, cursorId, targetType, acceptedTypes);
    // ... existing PartyHealTarget logic unchanged
}
```

`SetTargeting` gains an optional `HighlightObjectTypes acceptedTypes = All`
parameter, stored on `TargetManager` as `_pluginHoverAcceptedTypes`, reset in
`Reset()`/`CancelTarget()` like the other per-session fields.

### 3. Shared object-dispatch helper (`TargetManager`)

Extract the switch currently inline in `GameSceneInputHandler.cs:548-570`
into a new method on `TargetManager`:

```csharp
internal bool TryResolveObject(BaseGameObject obj)
{
    if (obj is TextObject ov) obj = ov.Owner;

    switch (obj)
    {
        case Entity ent: Target(ent.Serial); return true;
        case Land land: Target(0, land.X, land.Y, land.Z, land.TileData.IsWet); return true;
        case GameObject o: Target(o.Graphic, o.X, o.Y, o.Z); return true;
        default: return false;
    }
}
```

`GameSceneInputHandler`'s click handler calls this instead of its inline
switch (behavior-preserving refactor). The new hover-check (§4) calls the
same method, so entity/land/static dispatch logic exists in exactly one
place.

### 4. Per-frame hover-check (`GameScene.Update`)

`GameScene.Update` already reads `_world.TargetManager.IsTargeting` in
several places (lines 809, 825, 1037) — add one more cheap check alongside
them:

```csharp
if (_world.TargetManager.IsTargeting &&
    _world.TargetManager.TargetingState == CursorTarget.PluginHoverTarget)
{
    _world.TargetManager.CheckPluginHoverTarget(SelectedObject.Object);
}
```

New `TargetManager.CheckPluginHoverTarget(BaseGameObject hovered)`:

```csharp
internal void CheckPluginHoverTarget(BaseGameObject hovered)
{
    if (!IsTargeting || TargetingState != CursorTarget.PluginHoverTarget)
    {
        return;
    }

    if (hovered != null && MatchesAcceptedTypes(hovered, _pluginHoverAcceptedTypes))
    {
        TryResolveObject(hovered); // sends real Target Response, ends session
    }
    else
    {
        CancelTarget(); // hovered is null, or wrong category — abort immediately
    }
}
```

`MatchesAcceptedTypes` classifies `hovered` by concrete type (`Mobile` →
`HighlightObjectTypes.Mobile`, `Corpse`-flagged `Item` → `Corpse`, plain
`Item` → `Item`, `Land` → `Land`, other `GameObject`/static → `Static`,
`Multi` → `Multi`) and checks the flag is set in the mask — simple type
switch, no new classification helper needed elsewhere in the codebase.

Because both the confirm and the cancel branch end the session
(`IsTargeting` becomes `false`), this check is naturally idempotent — it
only ever does something on the first frame after the session opens where
`SelectedObject.Object` has a value (which, in practice, is the very next
frame after the packet is injected, since `SelectedObject` is already
computed every frame regardless of targeting state).

### 5. Testing

Unit tests (`tests/ClassicUO.UnitTests`), pure logic:

- `TargetCursor` handler: real-server byte values (0-3) behave unchanged
  (no regression); `PluginHoverTarget` byte value is parsed and reads the
  extra accept-mask byte.
- `TargetManager.CheckPluginHoverTarget`: matching hovered object → resolves
  and sends the same packet shape as a manual click, `IsTargeting` becomes
  `false`; non-matching object → `IsTargeting` becomes `false` via cancel,
  no Target Response sent; `null` hovered → same cancel path.
- Manual-click path (`TryResolveObject` via `GameSceneInputHandler`) still
  resolves entity/land/static exactly as before the refactor (regression
  guard on the extraction in §3).
- Non-`PluginHoverTarget` sessions are untouched by `CheckPluginHoverTarget`
  (early-return guard).

**Manual in-game verification**: inject a `PluginHoverTarget` packet with the
mouse already over a mobile → confirm the client sends a Target Response for
that mobile's serial (observe via a test plugin's `Packets.Outgoing` hook);
inject with the mouse over a disallowed category or empty gump space →
confirm a Cancel is sent and no further hover ever resolves that same session
(it's already over).

### Files touched

| File | Change |
|---|---|
| `Game/Managers/TargetManager.cs` | new `PluginHoverTarget` enum value; `_pluginHoverAcceptedTypes` field; `SetTargeting` gains accept-mask param; new `TryResolveObject`, `CheckPluginHoverTarget`, `MatchesAcceptedTypes` |
| `Network/PacketHandlers.cs` | `TargetCursor` reads the extra accept-mask byte when subtype matches |
| `Game/Scenes/GameSceneInputHandler.cs` | click handler calls `TryResolveObject` instead of its inline switch (refactor, behavior-preserving) |
| `Game/Scenes/GameScene.cs` | one new `CheckPluginHoverTarget` call in `Update`, alongside existing `IsTargeting` checks |
| `tests/ClassicUO.UnitTests/...` | new cases per §5 |

## Out of scope

- No `ClassicUO.PluginApi` changes — no new interface, no new method, no new
  enum in the plugin-facing assembly. Plugins keep using the packet pipeline
  they already have.
- No dwell/delay-based hover confirmation — instant only, per decision above.
- No status-bar-gump (`HealthBarGump`, etc.) target-click integration — that
  path doesn't go through `SelectedObject` and isn't touched here.
- No new outgoing packet format — the Target Response sent on hover-confirm
  is byte-for-byte the same as a manual click would produce.
- No timeout/expiry for an open hover session — since mismatch already
  cancels on the very next frame, there is no scenario where a session
  lingers waiting.
