# Plugin API: per-serial nameplate override with opacity

## Problem

Plugins have no way to force a specific entity's nameplate (the Object Handles
name+HP box, `NameOverheadGump`) to show or hide independent of the player's
own filters (`NameOverHeadHandlerGump` type checkboxes, the global
name-overhead toggle, or a manual close). There's also no way to render that
box at partial opacity.

## Scope

"Nameplate" here means the Object Handles box exactly: `NameOverheadGump`,
driven by `Entity.ObjectHandlesStatus` and gated by
`NameOverHeadManager.IsAllowed`. This is a different system from
`HealthLinesManager`'s colored HP line, which is out of scope.

A serial can have at most one active override: force-visible (with an
opacity) or force-hidden. Setting a new override for a serial replaces any
existing one. Force-hidden wins over the player's own settings and over a
force-visible request for a *different* plugin (last write wins — the API
gives one override slot per serial, not per-plugin layering).

## Plugin API surface (`ClassicUO.PluginApi`)

New interface, alongside `IHighlight`:

```csharp
namespace ClassicUO.PluginApi;

/// <summary>
/// Plugin-driven override of the per-entity nameplate (Object Handles
/// name+HP box). An override for a serial replaces any previous one and
/// persists until cleared or the plugin disconnects.
/// </summary>
public interface INameplates
{
    /// <summary>
    /// Forces serial's nameplate visible or hidden, regardless of the
    /// player's own name-overhead filters/toggle or a manual close.
    /// <paramref name="opacityPercent"/> (0-100, clamped) sets the box's
    /// alpha when <paramref name="visible"/> is true; ignored when false.
    /// </summary>
    void SetOverride(uint serial, bool visible, byte opacityPercent = 100);

    /// <summary>Removes the override for serial, reverting to normal behavior.</summary>
    void ClearOverride(uint serial);

    /// <summary>Removes every override this plugin owns.</summary>
    void ClearAllOverrides();
}
```

Added to `IPluginContext`:

```csharp
/// <summary>Plugin-driven per-serial nameplate visibility/opacity override.</summary>
INameplates Nameplates { get; }
```

All methods auto-marshal to the game thread (matches `IHighlight` convention).

## Host implementation

### `NameOverHeadManager` (`Game/Managers/NameOverHeadManager.cs`)

Add:

```csharp
private readonly Dictionary<uint, (bool Visible, byte OpacityPercent)> _overrides = new();

public void SetOverride(uint serial, bool visible, byte opacityPercent) =>
    _overrides[serial] = (visible, Math.Min(opacityPercent, (byte)100));

public void ClearOverride(uint serial) => _overrides.Remove(serial);

public void ClearAllOverrides() => _overrides.Clear();

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

`IsAllowed` is unchanged — override handling happens at the call site
(`GameSceneDrawingSorting.UpdateObjectHandles`) so `IsAllowed`'s existing
unit tests and semantics (type filtering) stay pure.

### `GameSceneDrawingSorting.UpdateObjectHandles`

Current logic (`src/ClassicUO.Client/Game/Scenes/GameSceneDrawingSorting.cs:258`):

```csharp
private void UpdateObjectHandles(Entity obj, bool useObjectHandles)
{
    if (useObjectHandles && _world.NameOverHeadManager.IsAllowed(obj))
    { ... }
    else if (obj.Serial == _healthLinesManager.ForcedOverheadSerial) { }
    else if (obj.ObjectHandlesStatus != ObjectHandlesStatus.NONE) { ... }
}
```

New logic, override checked first:

```csharp
private void UpdateObjectHandles(Entity obj, bool useObjectHandles)
{
    bool hasOverride = _world.NameOverHeadManager.TryGetOverride(obj.Serial, out bool forceVisible, out _);

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
        // unchanged: HealthLinesManager owns this frame's overhead.
    }
    else if (obj.ObjectHandlesStatus != ObjectHandlesStatus.NONE)
    {
        obj.ObjectHandlesStatus = ObjectHandlesStatus.NONE;
        obj.UpdateTextCoordsV();
    }
}
```

Force-visible reopens a box the player manually closed (CLOSED); the
player's manual close only "sticks" again once the plugin clears its
override (next tick's `hasOverride` is false, box behaves normally — the
player would need to close it again if still allowed).

### Opacity — `Entity.Update()` (`Game/GameObjects/Entity.cs:131-143`)

```csharp
if (ObjectHandlesStatus == ObjectHandlesStatus.OPEN)
{
    ObjectHandlesStatus = ObjectHandlesStatus.DISPLAYING;

    if (SerialHelper.IsMobile(Serial))
    {
        Socket.Send_NameRequest(Serial);
    }

    var gump = new NameOverheadGump(World, this);

    if (World.NameOverHeadManager.TryGetOverride(Serial, out bool visible, out byte opacityPercent) && visible)
    {
        gump.Alpha = opacityPercent / 100f;
    }

    UIManager.Add(gump);
}
```

## ABI (`ClassicUO.PluginAbi/GenericDispatch.cs`)

Same opcode escape-valve pattern as `IHighlight`/`Attack` (append-only,
existing opcodes never renumbered):

```csharp
/// <summary>
/// Forces serial's nameplate visible (with opacity) or hidden. Args:
/// <see cref="NameplateOverridePayload"/>. Result: none.
/// </summary>
public const int SetNameplateOverride = 3;

/// <summary>Clears the nameplate override for one serial. Args: uint serial. Result: none.</summary>
public const int ClearNameplateOverride = 4;

/// <summary>Clears every nameplate override owned by the calling plugin. Args: none. Result: none.</summary>
public const int ClearAllNameplateOverrides = 5;
```

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct NameplateOverridePayload
{
    public uint Serial;
    public byte Visible;        // 0/1
    public byte OpacityPercent; // 0-100, meaningful only when Visible == 1
}
```

## Host ABI wiring (`ClassicUO.BootstrapHost/PluginContextImpl.cs`)

`NameplatesImpl : INameplates`, same shape as `HighlightImpl`: packs a
`NameplateOverridePayload` (or bare `uint` for clear-one) and calls
`GenericInvokeFn` with the new opcodes. Dispatch on the client side
(wherever `GenericOpcodes.Attack` is handled, e.g.
`Game/Managers/PluginGenericDispatch.cs`) reads the payload and calls
`World.NameOverHeadManager.SetOverride/ClearOverride/ClearAllOverrides`
via `Game.Post` (game-thread marshal, matching existing opcode handlers).

Per-plugin ownership for `ClearAllOverrides`: each plugin's dispatch context
tracks which serials it has set overrides for (small `HashSet<uint>` in the
plugin's dispatch state, same pattern likely already used for
highlight/timer ownership) so one plugin's `ClearAllOverrides` cannot wipe
another plugin's override.

## Testing

- `NameOverHeadManagerOverrideTests.cs` (new, alongside existing
  `NameOverHeadManagerIsAllowedTests.cs`): `SetOverride`/`TryGetOverride`/
  `ClearOverride`/`ClearAllOverrides` round-trip, opacity clamping (>100 →
  100).
- No render-path unit test for `UpdateObjectHandles`/`Entity.Update` (both
  depend on live scene/UIManager state) — covered by manual verification
  per `IGameActions.Attack`'s existing precedent (`ActionBindingTests.cs`
  tests the ABI plumbing, not the render effect).
- `ActionBindingTests.cs`-style ABI round-trip test for the three new
  opcodes.

## Out of scope

- Per-plugin layered overrides (two plugins wanting different states for
  the same serial) — last write wins, single slot per serial.
- Persisting overrides across reconnect/relog — cleared implicitly since
  `NameOverHeadManager`/`World` are recreated per session.
- Applying opacity to the HP bar drawn inside `NameOverheadGump` separately
  from the box background/text — the whole gump's `Alpha` is one knob.
