# Center-Relative Gump Positions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in, per-profile setting that restores saved gump positions relative to the window center (with a hard on-screen clamp) so layouts survive moving the client to a smaller display.

**Architecture:** On save, stamp the client size at save time onto the `<gumps>` root element. On load, when the setting is on and that size is present, shift each gump's absolute x/y by half the window-size delta (center anchor), then clamp so no gump crosses a screen edge. The offset+clamp math is a pure static helper so it is unit-testable without a live window.

**Tech Stack:** C# (`net10.0`), xUnit tests, FNA. Existing XML gump persistence in `Profile.cs`.

## Global Constraints

- Every new source file starts with `// SPDX-License-Identifier: BSD-2-Clause` (license header enforced).
- Target framework `net10.0`, `LangVersion=Preview`; match surrounding code style (no free allocations in hot paths — not relevant here, all cold-path config code).
- Setting is **per-profile**, **default false**, and when off the save/restore path must be byte-for-byte identical to today.
- Do not change `Gump.Save` or the per-gump `x`/`y` attribute format (file must stay backward/forward compatible).
- Anchored gump groups (`anchored_group_gump`) are out of scope.

---

### Task 1: Pure center-anchor + clamp helper

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Gumps/GumpPositionHelper.cs`
- Test: `tests/ClassicUO.UnitTests/Game/UI/Gumps/GumpPositionHelperTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static class GumpPositionHelper` in namespace `ClassicUO.Game.UI.Gumps` with:
  `public static (int x, int y) CenterAnchor(int x, int y, int saveW, int saveH, int curW, int curH, int gumpW, int gumpH)`.
  Returns the transformed, clamped top-left position.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/UI/Gumps/GumpPositionHelperTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI.Gumps
{
    public class GumpPositionHelperTests
    {
        [Fact]
        public void CenterAnchor_SameSize_ReturnsInputUnchanged()
        {
            var (x, y) = GumpPositionHelper.CenterAnchor(100, 50, 800, 600, 800, 600, 40, 30);
            Assert.Equal(100, x);
            Assert.Equal(50, y);
        }

        [Fact]
        public void CenterAnchor_SmallerWindow_ShiftsByHalfDelta()
        {
            // width delta -200 -> x shifts by -100; height delta -100 -> y shifts by -50
            var (x, y) = GumpPositionHelper.CenterAnchor(400, 300, 800, 600, 600, 500, 40, 30);
            Assert.Equal(300, x);
            Assert.Equal(250, y);
        }

        [Fact]
        public void CenterAnchor_ClampsToLeftTopEdge()
        {
            // shift would drive x/y negative; clamp to 0
            var (x, y) = GumpPositionHelper.CenterAnchor(10, 10, 2000, 2000, 400, 400, 40, 30);
            Assert.Equal(0, x);
            Assert.Equal(0, y);
        }

        [Fact]
        public void CenterAnchor_ClampsToRightBottomEdge()
        {
            // gump 40x30 in a 400x400 window: max x = 360, max y = 370
            var (x, y) = GumpPositionHelper.CenterAnchor(5000, 5000, 400, 400, 400, 400, 40, 30);
            Assert.Equal(360, x);
            Assert.Equal(370, y);
        }

        [Fact]
        public void CenterAnchor_GumpWiderThanWindow_ClampsToZero()
        {
            // max(0, curW - gumpW) is negative -> upper bound floored to 0
            var (x, y) = GumpPositionHelper.CenterAnchor(100, 100, 400, 400, 50, 50, 200, 200);
            Assert.Equal(0, x);
            Assert.Equal(0, y);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~GumpPositionHelperTests"`
Expected: FAIL — `GumpPositionHelper` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `src/ClassicUO.Client/Game/UI/Gumps/GumpPositionHelper.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Pure math for the experimental "save gump positions relative to window
    /// center" feature. UI-free so it can be unit-tested without a live window.
    /// </summary>
    internal static class GumpPositionHelper
    {
        /// <summary>
        /// Re-anchors an absolute gump position saved in a window of
        /// <paramref name="saveW"/> x <paramref name="saveH"/> so it keeps the
        /// same offset from the window center in the current
        /// <paramref name="curW"/> x <paramref name="curH"/> window, then clamps
        /// it fully on-screen.
        /// </summary>
        public static (int x, int y) CenterAnchor(int x, int y, int saveW, int saveH, int curW, int curH, int gumpW, int gumpH)
        {
            x += (curW - saveW) / 2;
            y += (curH - saveH) / 2;

            x = Math.Clamp(x, 0, Math.Max(0, curW - gumpW));
            y = Math.Clamp(y, 0, Math.Max(0, curH - gumpH));

            return (x, y);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~GumpPositionHelperTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/GumpPositionHelper.cs tests/ClassicUO.UnitTests/Game/UI/Gumps/GumpPositionHelperTests.cs
git commit -m "feat(gumps): add center-anchor position helper with on-screen clamp"
```

---

### Task 2: Profile setting field

**Files:**
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs` (experimental region, near line 188 next to `CastSpellsByOneClick`)

**Interfaces:**
- Consumes: nothing.
- Produces: `public bool SaveGumpsRelativeToCenter { get; set; }` on `Profile` (default false), read via `ProfileManager.CurrentProfile.SaveGumpsRelativeToCenter`.

- [ ] **Step 1: Add the field**

In `Profile.cs`, in the experimental section, immediately after line 188
(`public bool CastSpellsByOneClick { get; set; }`), add:

```csharp
        public bool SaveGumpsRelativeToCenter { get; set; }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeds. (New auto-property serializes to `profile.json` automatically; default `false`.)

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Client/Configuration/Profile.cs
git commit -m "feat(gumps): add per-profile SaveGumpsRelativeToCenter setting"
```

---

### Task 3: Stamp window size on save

**Files:**
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs` — `SaveGumps`, just after `xml.WriteStartElement("gumps");` (line 410)

**Interfaces:**
- Consumes: `Client.Game.ClientBounds` (`Rectangle`, `.Width`/`.Height`).
- Produces: `save_w` / `save_h` attributes on the root `<gumps>` element in `gumps.xml`.

- [ ] **Step 1: Write the attributes**

In `SaveGumps`, between line 410 (`xml.WriteStartElement("gumps");`) and line 412
(`UIManager.AnchorManager.Save(xml);`), insert:

```csharp
                xml.WriteAttributeString("save_w", Client.Game.ClientBounds.Width.ToString());
                xml.WriteAttributeString("save_h", Client.Game.ClientBounds.Height.ToString());
```

(Written unconditionally, regardless of the setting, so the file is self-describing.)

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeds.

- [ ] **Step 3: Manual verification**

Run the client, log in, log out. Open the profile's `gumps.xml` and confirm the
root element now reads like `<gumps save_w="1920" save_h="1080">` with the current
client size. Per-gump `<gump ... x= y= .../>` entries are unchanged.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Configuration/Profile.cs
git commit -m "feat(gumps): stamp client size onto gumps.xml root on save"
```

---

### Task 4: Apply center-anchor on load

**Files:**
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs` — `ReadGumps`: parse root size (near line 545 where `root` is obtained) and transform x/y (near lines 695-696)

**Interfaces:**
- Consumes: `GumpPositionHelper.CenterAnchor(...)` (Task 1); `Profile.SaveGumpsRelativeToCenter` (Task 2); `save_w`/`save_h` root attributes (Task 3); `Client.Game.ClientBounds`; `gump.Width`, `gump.Height`.
- Produces: transformed `gump.X`/`gump.Y` for top-level gumps when the feature is on.

- [ ] **Step 1: Parse the saved size from the root**

In `ReadGumps`, right after line 547 (`if (root != null)` opening brace, line 548),
before the `foreach` at line 549, add:

```csharp
                    bool haveSaveSize =
                        int.TryParse(root.GetAttribute("save_w"), out int saveW) &
                        int.TryParse(root.GetAttribute("save_h"), out int saveH) &&
                        saveW > 0 && saveH > 0;
```

- [ ] **Step 2: Transform before assigning X/Y**

Replace the current assignment at lines 695-696:

```csharp
                            gump.X = x;
                            gump.Y = y;
```

with:

```csharp
                            if (ProfileManager.CurrentProfile != null &&
                                ProfileManager.CurrentProfile.SaveGumpsRelativeToCenter &&
                                haveSaveSize)
                            {
                                Rectangle cb = Client.Game.ClientBounds;

                                (x, y) = GumpPositionHelper.CenterAnchor(
                                    x, y,
                                    saveW, saveH,
                                    cb.Width, cb.Height,
                                    gump.Width, gump.Height);
                            }

                            gump.X = x;
                            gump.Y = y;
```

Note: the subsequent `UIManager.SavePosition(gump.LocalSerial, new Point(x, y))`
call (line 700) already uses `x`/`y`, so it now caches the transformed position —
correct, no change needed.

- [ ] **Step 3: Ensure required usings**

Confirm `Profile.cs` already has `using Microsoft.Xna.Framework;` (for `Rectangle`,
`Point`) and `using ClassicUO.Game.UI.Gumps;`. Both are already present (the file
constructs gumps and uses `Point` at line 700). If a build error reports a missing
type, add the corresponding using at the top of the file.

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeds.

- [ ] **Step 5: Manual verification**

1. Enable the setting (Task 5 provides the checkbox; until then set
   `"save_gumps_relative_to_center": true` directly in `profile.json`).
2. At a large window, arrange gumps near the corners, log out.
3. Relaunch with a smaller client window (resize down / smaller monitor).
4. Confirm gumps pull inward toward center and none sit off-screen.
5. Toggle the setting off, relaunch: absolute positions return (may clip
   off-screen, as before).

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Configuration/Profile.cs
git commit -m "feat(gumps): re-anchor gump positions to window center on load when enabled"
```

---

### Task 5: Options gump checkbox

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` — field decl (line 39 block), checkbox creation (experimental section near line 592), apply (near line 4656)

**Interfaces:**
- Consumes: `Profile.SaveGumpsRelativeToCenter` (Task 2); existing `AddCheckBox(...)` helper and `_currentProfile`.
- Produces: user-facing toggle that reads/writes the setting.

- [ ] **Step 1: Declare the checkbox field**

In `OptionsGump.cs` line 39, append `_saveGumpsRelativeToCenter` to the existing
experimental `Checkbox` declaration list. Change the end of that line from:

```csharp
..., _customBars, _customBarsBBG, _statValuesOnBars, _saveHealthbars;
```

to:

```csharp
..., _customBars, _customBarsBBG, _statValuesOnBars, _saveHealthbars, _saveGumpsRelativeToCenter;
```

- [ ] **Step 2: Add the checkbox to the experimental section**

In the experimental section, right after the `_autoOpenDoors` block that ends at
line 592, add a new row:

```csharp
            section.Add
            (
                _saveGumpsRelativeToCenter = AddCheckBox
                (
                    null,
                    "Save gump positions relative to window center",
                    _currentProfile.SaveGumpsRelativeToCenter,
                    startX,
                    startY
                )
            );
```

(A literal label string is used to avoid adding a `ResGumps` resource entry; match
neighboring `ResGumps.*` usage later if localization is desired.)

- [ ] **Step 3: Apply the checkbox on save**

Near line 4656 (`_currentProfile.AutoOpenDoors = _autoOpenDoors.IsChecked;`), add
below it:

```csharp
            _currentProfile.SaveGumpsRelativeToCenter = _saveGumpsRelativeToCenter.IsChecked;
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeds.

- [ ] **Step 5: Manual verification**

Open Options → experimental section: the new checkbox appears, reflects the current
profile value, and toggling + applying persists to `profile.json` as
`save_gumps_relative_to_center`.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(gumps): add options checkbox for center-relative gump positions"
```

---

## Final verification

- [ ] Run the full unit suite: `dotnet test tests/ClassicUO.UnitTests`
  Expected: all pass, including `GumpPositionHelperTests`.
- [ ] Full build: `dotnet build ClassicUO.sln -c Debug` — succeeds.
- [ ] End-to-end manual pass per Task 4 Step 5 with the checkbox from Task 5.

## Notes / deferred

- Anchored gump groups (`anchored_group_gump`) are not re-centered; only their
  internal relative layout is preserved (unchanged from today).
- Clamp uses gump size known at restore; gumps that finalize size later in
  `Restore()`/`UpdateContents()` may clamp slightly loose but remain center-anchored.
- `SetInScreen()` is intentionally left unchanged; this feature clamps inline.
