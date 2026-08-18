# Nameplate "My Corpse" Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "My Corpse" nameplate filter checkbox that shows the player's own corpse's nameplate independently of the generic "Corpses" filter.

**Architecture:** Track the player's own corpse serials (captured from the `0xAF` DisplayDeath packet, which the client currently discards for the local player) in a new `PlayerMobile.OwnCorpses` hash set. Add a `MyCorpse` bit to the existing `NameOverheadTypeAllowed` flags enum, consult it in `NameOverHeadManager.IsAllowed`, and expose it as a new checkbox in `NameOverHeadHandlerGump`.

**Tech Stack:** C# / .NET 10, xUnit for tests.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-18-nameplate-my-corpse-filter-design.md`
- "My Corpse" must work independently of "Corpses" (either flag alone shows the player's own corpse's nameplate) — confirmed design decision, not a subset relationship.
- `MyCorpse` must be included in `NameOverheadTypeAllowed.All` so the "All" checkbox in the gump keeps meaning "everything."
- No new visual highlight/color — this is a show/hide nameplate filter only, consistent with every other checkbox in `NameOverHeadHandlerGump`.
- Profile storage needs no migration — `NameOverheadTypeAllowed` is stored as a plain int bitmask; a new flag bit is backward compatible.
- Follow the existing BSD-2-Clause header (`// SPDX-License-Identifier: BSD-2-Clause`) at the top of any new file (none needed here — all changes are to existing files).

---

## File Structure

- Modify: `src/ClassicUO.Client/Game/GameObjects/PlayerMobile.cs` — add `OwnCorpses` hash set.
- Modify: `src/ClassicUO.Client/Game/Managers/NameOverHeadManager.cs` — add `MyCorpse` flag, update `IsAllowed`.
- Modify: `src/ClassicUO.Client/Network/PacketHandlers.cs` — capture the player's own corpse serial in `DisplayDeath`.
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/NameOverHeadHandlerGump.cs` — add the "My Corpse" checkbox.
- Modify: `src/ClassicUO.Client/Resources/ResGumps.resx` and `ResGumps.Designer.cs` — add the `MyCorpse` label string.
- Test: `tests/ClassicUO.UnitTests/Game/GameObjects/PlayerMobileOwnCorpsesTests.cs` (new)
- Test: `tests/ClassicUO.UnitTests/Game/Managers/NameOverHeadManagerIsAllowedTests.cs` (new)

---

### Task 1: `PlayerMobile.OwnCorpses` tracking set

**Files:**
- Modify: `src/ClassicUO.Client/Game/GameObjects/PlayerMobile.cs:52`
- Test: `tests/ClassicUO.UnitTests/Game/GameObjects/PlayerMobileOwnCorpsesTests.cs`

**Interfaces:**
- Produces: `PlayerMobile.OwnCorpses` — `public readonly HashSet<uint> OwnCorpses`, consumed by Task 2 (`NameOverHeadManager.IsAllowed`) and Task 3 (`PacketHandlers.DisplayDeath`).

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/GameObjects/PlayerMobileOwnCorpsesTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using Xunit;

namespace ClassicUO.UnitTests.Game.GameObjects
{
    public class PlayerMobileOwnCorpsesTests
    {
        [Fact]
        public void OwnCorpses_StartsEmpty_AndIsMutable()
        {
            var world = new World();
            var player = new PlayerMobile(world, 0x00000001);

            Assert.Empty(player.OwnCorpses);

            player.OwnCorpses.Add(0x40000001);

            Assert.Single(player.OwnCorpses);
            Assert.Contains(0x40000001u, player.OwnCorpses);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PlayerMobileOwnCorpsesTests"`
Expected: FAIL — build error, `OwnCorpses` does not exist on `PlayerMobile`.

- [ ] **Step 3: Add the field**

In `src/ClassicUO.Client/Game/GameObjects/PlayerMobile.cs`, next to the existing corpse-tracking sets (line 52):

```csharp
        public readonly HashSet<uint> AutoOpenedCorpses = new HashSet<uint>();
        public readonly HashSet<uint> ManualOpenedCorpses = new HashSet<uint>();
        public readonly HashSet<uint> OwnCorpses = new HashSet<uint>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PlayerMobileOwnCorpsesTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/GameObjects/PlayerMobile.cs tests/ClassicUO.UnitTests/Game/GameObjects/PlayerMobileOwnCorpsesTests.cs
git commit -m "Track the player's own corpse serials on PlayerMobile"
```

---

### Task 2: `MyCorpse` filter flag + `IsAllowed` logic

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/NameOverHeadManager.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/NameOverHeadManagerIsAllowedTests.cs`

**Interfaces:**
- Consumes: `PlayerMobile.OwnCorpses` (Task 1); `World.CreatePlayer(uint serial)`; `World.Player`.
- Produces: `NameOverheadTypeAllowed.MyCorpse` flag and updated `NameOverHeadManager.IsAllowed(Entity)` corpse branch, consumed by Task 4 (gump checkbox) purely through the existing `TypeAllowed` bitmask — no new public surface needed there.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClassicUO.UnitTests/Game/Managers/NameOverHeadManagerIsAllowedTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Reflection;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class NameOverHeadManagerIsAllowedTests
    {
        private static void SetCurrentProfile(Profile profile)
        {
            typeof(ProfileManager)
                .GetProperty("CurrentProfile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, profile);
        }

        private static (World world, NameOverHeadManager sut) CreateWorldWithPlayer(NameOverheadTypeAllowed typeAllowed)
        {
            SetCurrentProfile(new Profile { NameOverheadTypeAllowed = typeAllowed });

            var world = new World();
            world.CreatePlayer(0x00000001);

            return (world, new NameOverHeadManager(world));
        }

        private static Item CreateCorpse(World world, uint serial)
        {
            return new Item(world) { Serial = serial, Graphic = 0x2006 };
        }

        [Fact]
        public void OwnCorpse_Shown_WhenOnlyMyCorpseFlagSet()
        {
            var (world, sut) = CreateWorldWithPlayer(NameOverheadTypeAllowed.MyCorpse);
            var corpse = CreateCorpse(world, 0x40000001);
            world.Player.OwnCorpses.Add(corpse.Serial);

            Assert.True(sut.IsAllowed(corpse));
        }

        [Fact]
        public void OwnCorpse_Shown_WhenOnlyCorpsesFlagSet()
        {
            var (world, sut) = CreateWorldWithPlayer(NameOverheadTypeAllowed.Corpses);
            var corpse = CreateCorpse(world, 0x40000001);
            world.Player.OwnCorpses.Add(corpse.Serial);

            Assert.True(sut.IsAllowed(corpse));
        }

        [Fact]
        public void OwnCorpse_Hidden_WhenNeitherFlagSet()
        {
            var (world, sut) = CreateWorldWithPlayer(NameOverheadTypeAllowed.Items);
            var corpse = CreateCorpse(world, 0x40000001);
            world.Player.OwnCorpses.Add(corpse.Serial);

            Assert.False(sut.IsAllowed(corpse));
        }

        [Fact]
        public void StrangerCorpse_NotShown_ByMyCorpseFlagAlone()
        {
            var (world, sut) = CreateWorldWithPlayer(NameOverheadTypeAllowed.MyCorpse);
            var corpse = CreateCorpse(world, 0x40000002);
            // Note: not added to world.Player.OwnCorpses.

            Assert.False(sut.IsAllowed(corpse));
        }

        [Fact]
        public void All_Flag_IncludesMyCorpse()
        {
            Assert.True(NameOverheadTypeAllowed.All.HasFlag(NameOverheadTypeAllowed.MyCorpse));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~NameOverHeadManagerIsAllowedTests"`
Expected: FAIL — build error, `NameOverheadTypeAllowed.MyCorpse` does not exist.

- [ ] **Step 3: Add the flag and update `IsAllowed`**

In `src/ClassicUO.Client/Game/Managers/NameOverHeadManager.cs`, replace the enum:

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

And replace the corpse branch inside `IsAllowed`:

```csharp
                if (SerialHelper.IsItem(entity.Serial))
        {
            if (entity is Item item)
            {
                if (item.IsCorpse)
                {
                    if (_world.Player != null && _world.Player.OwnCorpses.Contains(item.Serial) && TypeAllowed.HasFlag(NameOverheadTypeAllowed.MyCorpse))
                        return true;

                    return TypeAllowed.HasFlag(NameOverheadTypeAllowed.Corpses);
                }

                if (!GameObject.CanBeDrawn(entity.World, item.DisplayedGraphic))
                    return false;
            }

            return TypeAllowed.HasFlag(NameOverheadTypeAllowed.Items);
        }
```

(Only the `if (item.IsCorpse)` line changed from a one-line return to the block above; everything else in the method is unchanged.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~NameOverHeadManagerIsAllowedTests"`
Expected: PASS (5 tests)

- [ ] **Step 5: Run the full unit test suite to check for regressions**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS, no new failures.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/NameOverHeadManager.cs tests/ClassicUO.UnitTests/Game/Managers/NameOverHeadManagerIsAllowedTests.cs
git commit -m "Add independent My Corpse nameplate filter flag"
```

---

### Task 3: Capture the player's own corpse serial from `DisplayDeath`

**Files:**
- Modify: `src/ClassicUO.Client/Network/PacketHandlers.cs:3709-3725`

**Interfaces:**
- Consumes: `PlayerMobile.OwnCorpses` (Task 1).
- Produces: nothing new consumed by later tasks — this wires the real gameplay data source for `OwnCorpses`, which Tasks 1–2 already cover in isolation with manual `Add` calls.

**Note on testing:** `DisplayDeath` is a `private static void DisplayDeath(World world, ref StackDataReader p)` method taking a `ref` parameter of a `ref struct` (`StackDataReader`). Neither normal calls (it's private) nor reflection (`MethodInfo.Invoke` cannot pass `ref struct` arguments through `object[]`) can exercise it from a test in this codebase — the same category of gap `TargetManagerTryResolveObjectTests.ReturnsTrue_ForLandObject` documents for `Land.TileData`. This task is covered by manual verification only (see Step 3).

- [ ] **Step 1: Make the change**

In `src/ClassicUO.Client/Network/PacketHandlers.cs`, `DisplayDeath` currently reads (lines 3709–3725):

```csharp
        private static void DisplayDeath(World world, ref StackDataReader p)
        {
            if (!world.InGame)
            {
                return;
            }

            uint serial = p.ReadUInt32BE();
            uint corpseSerial = p.ReadUInt32BE();
            uint running = p.ReadUInt32BE();

            Mobile owner = world.Mobiles.Get(serial);

            if (owner == null || serial == world.Player)
            {
                return;
            }

            serial |= 0x80000000;
```

Replace it with:

```csharp
        private static void DisplayDeath(World world, ref StackDataReader p)
        {
            if (!world.InGame)
            {
                return;
            }

            uint serial = p.ReadUInt32BE();
            uint corpseSerial = p.ReadUInt32BE();
            uint running = p.ReadUInt32BE();

            if (serial == world.Player)
            {
                if (SerialHelper.IsValid(corpseSerial))
                {
                    world.Player.OwnCorpses.Add(corpseSerial);
                }

                return;
            }

            Mobile owner = world.Mobiles.Get(serial);

            if (owner == null)
            {
                return;
            }

            serial |= 0x80000000;
```

(The remainder of the method — the `if (world.Mobiles.Remove(owner.Serial))` block onward — is unchanged.)

- [ ] **Step 2: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: builds with no new errors/warnings.

- [ ] **Step 3: Manual verification (required — see note above)**

Run the client against a test shard/character, let the character die, and confirm no exception is thrown and normal death handling (screen effects, corpse spawning) still works exactly as before. This step only needs to confirm the early-return-for-self path is exercised without regressing existing behavior; the actual "My Corpse" nameplate visibility is verified end-to-end in Task 4's manual test.

- [ ] **Step 4: Run the full unit test suite to check for regressions**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS, no new failures.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Network/PacketHandlers.cs
git commit -m "Capture the player's own corpse serial from DisplayDeath"
```

---

### Task 4: "My Corpse" checkbox in `NameOverHeadHandlerGump`

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/NameOverHeadHandlerGump.cs`
- Modify: `src/ClassicUO.Client/Resources/ResGumps.resx`
- Modify: `src/ClassicUO.Client/Resources/ResGumps.Designer.cs`

**Interfaces:**
- Consumes: `NameOverheadTypeAllowed.MyCorpse` (Task 2); `ResGumps.MyCorpse` (added in this task).
- Produces: nothing consumed by later tasks (this is the last task).

- [ ] **Step 1: Add the resource string**

In `src/ClassicUO.Client/Resources/ResGumps.resx`, add next to the existing `StayActive` entry (around line 1674):

```xml
  <data name="MyCorpse" xml:space="preserve">
    <value>My Corpse</value>
  </data>
```

In `src/ClassicUO.Client/Resources/ResGumps.Designer.cs`, add next to the existing `StayActive` property (around line 4216), keeping the same generated-property style:

```csharp
        /// <summary>
        ///   Looks up a localized string similar to My Corpse.
        /// </summary>
        public static string MyCorpse {
            get {
                return ResourceManager.GetString("MyCorpse", resourceCulture);
            }
        }
```

- [ ] **Step 2: Add the checkbox to the gump**

In `src/ClassicUO.Client/Game/UI/Gumps/NameOverHeadHandlerGump.cs`:

Change the checkbox variable declaration (line 40) from:

```csharp
            Checkbox stayActive, cbAll, cbItems, cbCorpses, cbInnocent, cbAlly, cbGray, cbCriminal, cbEnemy, cbMurderer, cbInvulnerable;
```

to:

```csharp
            Checkbox stayActive, cbAll, cbItems, cbCorpses, cbMyCorpse, cbInnocent, cbAlly, cbGray, cbCriminal, cbEnemy, cbMurderer, cbInvulnerable;
```

Add the new checkbox right after `cbCorpses` is added (after line 85):

```csharp
            Add(cbCorpses = MakeCheckbox("Corpses", typeAllowed.HasFlag(NameOverheadTypeAllowed.Corpses), ref currentY));
            Add(cbMyCorpse = MakeCheckbox(ResGumps.MyCorpse, typeAllowed.HasFlag(NameOverheadTypeAllowed.MyCorpse), ref currentY));
            Add(cbInnocent = MakeCheckbox("Innocent", typeAllowed.HasFlag(NameOverheadTypeAllowed.Innocent), ref currentY));
```

Add it to the `individualBoxes` array (after the `cbCorpses` entry, around line 104):

```csharp
            var individualBoxes = new[]
            {
                (cb: cbItems, flag: NameOverheadTypeAllowed.Items),
                (cb: cbCorpses, flag: NameOverheadTypeAllowed.Corpses),
                (cb: cbMyCorpse, flag: NameOverheadTypeAllowed.MyCorpse),
                (cb: cbInnocent, flag: NameOverheadTypeAllowed.Innocent),
                (cb: cbAlly, flag: NameOverheadTypeAllowed.Ally),
                (cb: cbGray, flag: NameOverheadTypeAllowed.Gray),
                (cb: cbCriminal, flag: NameOverheadTypeAllowed.Criminal),
                (cb: cbEnemy, flag: NameOverheadTypeAllowed.Enemy),
                (cb: cbMurderer, flag: NameOverheadTypeAllowed.Murderer),
                (cb: cbInvulnerable, flag: NameOverheadTypeAllowed.Invulnerable),
            };
```

No other change is needed — the "All" cascade logic and the per-checkbox `ValueChanged` wiring already iterate `individualBoxes` generically.

- [ ] **Step 3: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: builds with no new errors/warnings.

- [ ] **Step 4: Run the full unit test suite**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS, no new failures.

- [ ] **Step 5: Manual verification**

1. Launch the client (`cd scripts && bash build-naot.sh`, or run the Debug build), log in, open the nameplate filter gump (existing hotkey/menu entry that calls `NameOverHeadManager.Open()`).
2. Confirm a new "My Corpse" checkbox appears below "Corpses", and that toggling "All" checks/unchecks it along with the rest.
3. Uncheck "Corpses", check only "My Corpse". Die in-game (or use a test shard command). Confirm the nameplate appears over your own corpse, and does *not* appear over any other corpse on the ground.
4. Check "Corpses" and uncheck "My Corpse". Confirm your own corpse's nameplate still appears (since "Corpses" alone also covers it), consistent with the design's "either flag shows it" behavior.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/NameOverHeadHandlerGump.cs src/ClassicUO.Client/Resources/ResGumps.resx src/ClassicUO.Client/Resources/ResGumps.Designer.cs
git commit -m "Add My Corpse checkbox to the nameplate filter gump"
```
