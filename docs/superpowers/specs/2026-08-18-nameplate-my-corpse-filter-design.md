# Nameplate filter: "My Corpse"

Date: 2026-08-18

## Problem

Nameplate filter gump (`NameOverHeadHandlerGump`, opened via the nameplate
toggle) lets the player show/hide overhead nameplates by category (Items,
Corpses, notoriety colors, etc). There is no way to specifically highlight
the player's own corpse after death — it is only visible if the generic
"Corpses" filter is on, same as every other corpse on the ground.

## Goal

Add a "My Corpse" checkbox to the nameplate filter that, independently of
the "Corpses" checkbox, shows the nameplate over the player's own corpse(s)
after death.

## Design

### Tracking the player's own corpse

`PacketHandlers.DisplayDeath` (opcode `0xAF`) currently does this:

```csharp
Mobile owner = world.Mobiles.Get(serial);

if (owner == null || serial == world.Player)
{
    return;
}
```

When `serial == world.Player`, the packet — including its `corpseSerial`
field — is discarded entirely. This is the point where the server tells us
which corpse belongs to the local player. Instead of returning immediately,
capture the corpse serial before bailing out of the rest of the (otherwise
inapplicable, since the player keeps their own mobile serial) handling:

```csharp
if (owner == null)
{
    return;
}

if (serial == world.Player)
{
    if (SerialHelper.IsValid(corpseSerial))
    {
        world.Player.OwnCorpses.Add(corpseSerial);
    }

    return;
}
```

Storage: add `public readonly HashSet<uint> OwnCorpses = new HashSet<uint>();`
to `PlayerMobile`, next to the existing `AutoOpenedCorpses` /
`ManualOpenedCorpses` hash sets. A `HashSet` (not a single field) because a
player can die more than once per session, and older corpses remain the
player's own corpses.

No pruning/eviction is needed — corpse items are already removed from
`world.Items` by the normal decay/out-of-range cleanup path, so the hash set
only ever grows by a small, bounded amount per death.

### New filter flag

Extend the existing flags enum in `NameOverHeadManager.cs`:

```csharp
[Flags]
internal enum NameOverheadTypeAllowed
{
    None = 0,
    Items = 1 << 0,
    Corpses = 1 << 1,
    Innocent = 1 << 2,
    Ally = 1 << 3,
    Gray = 1 << 4,
    Criminal = 1 << 5,
    Enemy = 1 << 6,
    Murderer = 1 << 7,
    Invulnerable = 1 << 8,
    MyCorpse = 1 << 9,
    AllMobiles = Innocent | Ally | Gray | Criminal | Enemy | Murderer | Invulnerable,
    All = Items | Corpses | AllMobiles | MyCorpse
}
```

This is stored as a plain int bitmask in the profile (`NameOverheadTypeAllowed`
property on `Profile`), so adding a new bit is backward compatible with
existing saved profiles — no migration needed.

### Filter logic

`NameOverHeadManager.IsAllowed`, corpse branch, becomes:

```csharp
if (item.IsCorpse)
{
    if (_world.Player.OwnCorpses.Contains(item.Serial) && TypeAllowed.HasFlag(NameOverheadTypeAllowed.MyCorpse))
        return true;

    return TypeAllowed.HasFlag(NameOverheadTypeAllowed.Corpses);
}
```

So: the player's own corpse is shown if either `MyCorpse` or `Corpses` is
checked; every other corpse only follows the `Corpses` checkbox.

### UI

`NameOverHeadHandlerGump`: add a `cbMyCorpse` checkbox created the same way
as the others (`MakeCheckbox`), added to the `individualBoxes` array so it
participates in the existing "All" cascade logic without any special-casing.
Label sourced from a new `ResGumps.MyCorpse` resource string ("My Corpse"),
following the pattern of the other labels that already go through
`ResGumps` (note some existing checkboxes in this gump use raw string
literals rather than `ResGumps` — for the new checkbox, use `ResGumps` since
that's the more correct existing pattern in the codebase, not the literal
one).

## Out of scope

- Any visual highlight beyond the existing nameplate show/hide behavior
  (no special color, no glow) — consistent with how every other filter in
  this gump works.
- Multiplayer/other-player "their corpse" tracking — out of scope, this is
  specifically about the local player's own corpse.
- Pruning `OwnCorpses` proactively; relies on normal item lifecycle.

## Testing

- Existing unit test patterns for `NameOverHeadManager`/flags, if any exist,
  should be extended to cover: own corpse shown when only `MyCorpse` is set
  and `Corpses` is off; own corpse still shown when only `Corpses` is set;
  a stranger's corpse never shown by `MyCorpse` alone.
- Manual verification: die in-game, confirm nameplate appears over own
  corpse with only "My Corpse" checked and "Corpses" unchecked, and that
  other corpses on the ground stay unlabeled.
