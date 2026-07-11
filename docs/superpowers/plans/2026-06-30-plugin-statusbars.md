# Plugin v2 Status Bars Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give plugins three thin client primitives — priority-hue overlay (A), open/close status bar at a position with optional grouping (C) — and make party member bars honor the modern custom-bar toggle (B), keeping all policy in the plugin.

**Architecture:** Three new plugin→cuo action primitives (`OpenStatusBar`, `CloseStatusBar`, `SetOverlay`) are threaded through the same 4-layer boundary as `CastSpell`: `IStatusBars` (PluginApi) → `StatusBarsImpl` (BootstrapHost) → `ClientBindings` function pointers → static client entrypoints in `PluginStatusBars`. The client keeps two small static registries (serial→hue overlay, groupId→`AnchorGroup`). Feature B has no boundary surface: it is a shared `HealthBarFactory.Create` helper plus a routing change at the party-relevant spawn site.

**Tech Stack:** C#, .NET 10 (client is NativeAOT; BootstrapHost and tests are net10.0), xUnit + FluentAssertions, MonoGame/FNA gumps, Cdecl function-pointer interop.

## Global Constraints

- **Thin client, fat plugin.** No priority/rule policy in the client; the client renders thin generic primitives keyed by serial. (spec "Guiding constraint")
- **Append-only struct layout.** New function-pointer fields are appended to the END of `ClientBindings` in BOTH `src/ClassicUO.Client/PluginHost.cs` and `src/ClassicUO.BootstrapHost/HostBridge.cs`, in identical order. Never reorder or insert. `HostBindings` (the host→cuo event table) is NOT modified. (spec "Legacy `HostBindings` layout" risk)
- **Auto-marshal to the game thread.** Every `IStatusBars` method must be safe to call from any thread, marshaling to the game thread exactly like `GameActionsImpl.CastSpell`. (spec "Public contract")
- **`hue == 0` clears.** An overlay hue of 0 removes the registry entry. (spec "Feature A")
- **`groupId == 0` means no group (floating).** (spec "Feature C")
- **Custom-bar only for overlay.** The priority tint applies only to `HealthBarGumpCustom._outline`; the classic `HealthBarGump` is a documented no-op; state `_border[]` colors are never touched. (spec "Feature A", Non-goals)
- **Graphics-free tests only.** Unit tests run without a `GraphicsDevice` (see `tests/ClassicUO.UnitTests/Game/Scenes/RenderListsTests.cs`). Do NOT construct a `HealthBarGumpCustom`/`HealthBarGump` in a unit test — it builds `LineCHB`/`StbTextBox` which require `SolidColorTextureCache.GetTexture` and a live device. Test the pure registries and predicates; verify rendering manually.
- **Commit trailers.** Every commit message ends with these two lines:
  ```
  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW
  ```
- **Build/test commands (Windows PowerShell):**
  - `dotnet build`
  - `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~Name"`
  - `dotnet test tests/ClassicUO.BootstrapHost.Tests`

---

## File Structure

| File | Responsibility | New/Modify |
|------|----------------|------------|
| `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs` | Static overlay registry (`PluginStatusOverlays`), group registry (`PluginStatusBarGroups`), and the three static client entrypoints (`PluginStatusBars`) bound to `ClientBindings`. | New |
| `src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs` | `LineCHB.Hue` field + `_outline` tint in `HealthBarGumpCustom.Update` (A); `HealthBarFactory` helper (B). | Modify |
| `src/ClassicUO.Client/Game/Managers/AnchorManager.cs` | `AnchorGroup.IsEmpty` property (C). | Modify |
| `src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs` | Route the party-relevant drag-select spawn through `HealthBarFactory.Create` (B). | Modify |
| `src/ClassicUO.Client/PluginHost.cs` | Append 3 `ClientBindings` fields; wire delegates to `PluginStatusBars` statics. | Modify |
| `src/ClassicUO.PluginApi/IStatusBars.cs` | `IStatusBars` contract. | New |
| `src/ClassicUO.PluginApi/IPluginContext.cs` | `StatusBars` property. | Modify |
| `src/ClassicUO.BootstrapHost/HostBridge.cs` | Append 3 `ClientBindings` fields; test-only bindings setter. | Modify |
| `src/ClassicUO.BootstrapHost/PluginContextImpl.cs` | `StatusBarsImpl`; expose `StatusBars` on the context. | Modify |
| `samples/HelloPlugin/HelloPlugin.cs` | Manual demo: overlay + grouped open. | Modify |
| `tests/ClassicUO.UnitTests/Game/Managers/PluginStatusOverlaysTests.cs` | A registry tests. | New |
| `tests/ClassicUO.UnitTests/Game/Managers/PluginStatusBarGroupsTests.cs` | C group-registry + `AnchorGroup.IsEmpty` tests. | New |
| `tests/ClassicUO.UnitTests/Game/UI/Gumps/HealthBarFactoryTests.cs` | B predicate test. | New |
| `tests/ClassicUO.BootstrapHost.Tests/StatusBarsTests.cs` | Host binding-dispatch tests. | New |

### Spawn-site audit result (Feature B)

A repo-wide grep of `new HealthBarGump`/`new HealthBarGumpCustom` shows **every** creation site already gates on `ProfileManager.CurrentProfile.CustomBarsToggled`:

- `Game/Scenes/GameSceneInputHandler.cs:211-218` (drag-select) and `:1081-1109` (double-click)
- `Game/UI/Gumps/NameOverheadGump.cs:235-257`
- `Game/UI/Gumps/OptionsGump.cs:4289-4306` (toggle conversion)
- `Game/Managers/MacroManager.cs:705-711`, `Game/UI/Gumps/StatusGump.cs:82-85`, `Game/UI/Gumps/PaperdollGump.cs:655-677` (player bar)
- `Configuration/Profile.cs:555-561` and `:721-728` (saved-gump restore)

`PartyManager` (`Game/Managers/PartyManager.cs`) never constructs a bar — it only calls `RequestUpdateContents()` on bars found via `UIManager.GetGump<BaseHealthBarGump>(serial)`. `PacketHandlers.cs` only *disposes* bars (`:4207`, `:4370`). **There is no dedicated party-member auto-spawn path that forces the classic bar.** The canonical party-member spawn site is the drag-select loop at **`GameSceneInputHandler.cs:209-218`**. Feature B therefore reduces to: introduce one shared `HealthBarFactory.Create` decision helper, route that canonical site through it to lock in the behavior + guard against regressions, and verify the `inparty` rendering branch manually. (See "Spec gaps / risks" at the bottom.)

---

## Task 1: Overlay registry (Feature A core)

**Files:**
- Create: `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs`
- Test: `tests/ClassicUO.UnitTests/Game/Managers/PluginStatusOverlaysTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class ClassicUO.Game.Managers.PluginStatusOverlays` with
  `static void Set(uint serial, ushort hue)`, `static ushort Get(uint serial)`,
  `static void Clear(uint serial)`, `static void Reset()`. `Set` with `hue == 0`
  removes the entry. `Get` returns `0` when absent.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Managers/PluginStatusOverlaysTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class PluginStatusOverlaysTests
    {
        public PluginStatusOverlaysTests() => PluginStatusOverlays.Reset();

        [Fact]
        public void Get_ReturnsZero_WhenNoOverlaySet()
        {
            Assert.Equal((ushort)0, PluginStatusOverlays.Get(0x1234));
        }

        [Fact]
        public void Set_StoresHue_RetrievableByGet()
        {
            PluginStatusOverlays.Set(0x1234, 0x0021);
            Assert.Equal((ushort)0x0021, PluginStatusOverlays.Get(0x1234));
        }

        [Fact]
        public void Set_WithZeroHue_ClearsExistingOverlay()
        {
            PluginStatusOverlays.Set(0x1234, 0x0021);
            PluginStatusOverlays.Set(0x1234, 0);
            Assert.Equal((ushort)0, PluginStatusOverlays.Get(0x1234));
        }

        [Fact]
        public void Clear_RemovesOverlay()
        {
            PluginStatusOverlays.Set(0x1234, 0x0021);
            PluginStatusOverlays.Clear(0x1234);
            Assert.Equal((ushort)0, PluginStatusOverlays.Get(0x1234));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusOverlaysTests"`
Expected: FAIL — compile error, `PluginStatusOverlays` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Plugin-driven priority-highlight hues keyed by mobile serial. Policy
    /// (which serial gets which hue) lives entirely in the plugin; the client
    /// only stores and renders. A hue of 0 means "no overlay".
    /// </summary>
    internal static class PluginStatusOverlays
    {
        private static readonly Dictionary<uint, ushort> _overlays = new Dictionary<uint, ushort>();

        public static void Set(uint serial, ushort hue)
        {
            if (hue == 0)
            {
                _overlays.Remove(serial);
                return;
            }

            _overlays[serial] = hue;
        }

        public static ushort Get(uint serial)
        {
            return _overlays.TryGetValue(serial, out ushort hue) ? hue : (ushort)0;
        }

        public static void Clear(uint serial) => _overlays.Remove(serial);

        /// <summary>Test-only: drops every overlay so tests start clean.</summary>
        public static void Reset() => _overlays.Clear();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusOverlaysTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs tests/ClassicUO.UnitTests/Game/Managers/PluginStatusOverlaysTests.cs
git commit -m "$(cat <<'EOF'
feat(statusbars): add plugin priority-overlay registry

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW
EOF
)"
```

---

## Task 2: Tint the custom bar `_outline` from the overlay registry (Feature A render)

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs` (private `LineCHB` class ~1201-1248; `HealthBarGumpCustom.Update` ~369-688)

**Interfaces:**
- Consumes: `PluginStatusOverlays.Get(uint)` from Task 1.
- Produces: no public surface; behavioral change only. `_outline` always exists
  in all three `BuildGump` layouts (party ~754, player ~917, single-line ~1065),
  so the spec's "_outline availability" risk is confirmed resolved.

> **Why no unit test here:** constructing `HealthBarGumpCustom` requires a live
> `GraphicsDevice` (forbidden by Global Constraints). The registry that drives
> this tint is unit-tested in Task 1; the visible tint is verified manually in
> Task 9. This task is a build-verified change.

- [ ] **Step 1: Add a `Hue` field to `LineCHB` and use it when drawing**

In `src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs`, find the `LineCHB` members (~1219-1220):

```csharp
            public int LineWidth { get; set; }
            public Texture2D LineColor { get; set; }
```

Replace with:

```csharp
            public int LineWidth { get; set; }
            public Texture2D LineColor { get; set; }

            // Plugin priority-overlay hue applied to this line only. 0 = none.
            public ushort Hue { get; set; }
```

Then find `LineCHB.AddToRenderLists` (~1225):

```csharp
                Vector3 hueVector = ShaderHueTranslator.GetHueVector(0, false, Alpha);
```

Replace with:

```csharp
                Vector3 hueVector = ShaderHueTranslator.GetHueVector(Hue, false, Alpha);
```

- [ ] **Step 2: Add the priority-white texture and the tint helper**

Find the static texture cache in `HealthBarGumpCustom` (~325):

```csharp
        private static readonly Texture2D HPB_COLOR_BLACK = SolidColorTextureCache.GetTexture(Color.Black);
```

Add immediately after it:

```csharp
        // A white source texture so a UO priority hue colorizes the outline ring.
        private static readonly Texture2D HPB_COLOR_WHITE = SolidColorTextureCache.GetTexture(Color.White);
```

- [ ] **Step 3: Apply the overlay near the end of `HealthBarGumpCustom.Update`**

Find the end of `HealthBarGumpCustom.Update`, the war-mode block close + method close (~687-688):

```csharp
                }
            }
        }

        protected override void BuildGump()
```

Replace with (inserting the overlay application before the closing brace of `Update`):

```csharp
                }
            }

            ApplyPriorityOverlay();
        }

        // Feature A: tint the outline ring with the plugin's priority hue, if
        // any, leaving the state _border[] colors (low-HP red / war / target)
        // untouched. A white source texture + UO hue renders as the hue color;
        // hue 0 restores the default black ring.
        private void ApplyPriorityOverlay()
        {
            if (_outline == null)
            {
                return;
            }

            ushort overlayHue = PluginStatusOverlays.Get(LocalSerial);

            if (overlayHue != 0)
            {
                if (_outline.Hue != overlayHue)
                {
                    _outline.Hue = overlayHue;
                    _outline.LineColor = HPB_COLOR_WHITE;
                }
            }
            else if (_outline.Hue != 0)
            {
                _outline.Hue = 0;
                _outline.LineColor = HPB_COLOR_BLACK;
            }
        }

        protected override void BuildGump()
```

`PluginStatusOverlays` is in the same `ClassicUO.Game.Managers` namespace, already
imported at the top of the file (`using ClassicUO.Game.Managers;`).

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs
git commit -m "$(cat <<'EOF'
feat(statusbars): tint custom-bar outline from overlay registry

Adds LineCHB.Hue and applies the plugin priority hue to the custom
health bar's _outline ring each Update, leaving state borders intact.
Classic HealthBarGump is intentionally untouched (v1 no-op).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW
EOF
)"
```

---

## Task 3: `HealthBarFactory` + route the party spawn site (Feature B)

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs` (add `HealthBarFactory` in the `ClassicUO.Game.UI.Gumps` namespace)
- Modify: `src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs:209-218`
- Test: `tests/ClassicUO.UnitTests/Game/UI/Gumps/HealthBarFactoryTests.cs`

**Interfaces:**
- Consumes: `ClassicUO.Configuration.Profile.CustomBarsToggled`.
- Produces: `static class ClassicUO.Game.UI.Gumps.HealthBarFactory` with
  `static bool ShouldUseCustomBar(Configuration.Profile profile)` (pure; true iff
  `profile != null && profile.CustomBarsToggled`) and
  `static BaseHealthBarGump Create(World world, Entity entity)`
  (uses `ProfileManager.CurrentProfile`).

> **Why the test targets the predicate:** `ProfileManager.CurrentProfile` has a
> private setter, and `Create` constructs a gump (graphics). The decision logic
> is extracted into the pure `ShouldUseCustomBar(Profile)` so it is unit-testable
> with a plain `new Profile()`; `Create` is exercised at runtime/manually.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/UI/Gumps/HealthBarFactoryTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI.Gumps
{
    public class HealthBarFactoryTests
    {
        [Fact]
        public void ShouldUseCustomBar_True_WhenToggleOn()
        {
            var profile = new Profile { CustomBarsToggled = true };
            Assert.True(HealthBarFactory.ShouldUseCustomBar(profile));
        }

        [Fact]
        public void ShouldUseCustomBar_False_WhenToggleOff()
        {
            var profile = new Profile { CustomBarsToggled = false };
            Assert.False(HealthBarFactory.ShouldUseCustomBar(profile));
        }

        [Fact]
        public void ShouldUseCustomBar_False_WhenProfileNull()
        {
            Assert.False(HealthBarFactory.ShouldUseCustomBar(null));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~HealthBarFactoryTests"`
Expected: FAIL — `HealthBarFactory` does not exist.

- [ ] **Step 3: Add `HealthBarFactory`**

In `src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs`, find the end of the file — the final closing brace of the `namespace ClassicUO.Game.UI.Gumps` block (after the `HealthBarGump` class closes, ~1906-1907):

```csharp
        private enum ButtonParty
        {
            Heal1,
            Heal2
        }
    }
}
```

Replace with (adding the factory inside the same namespace):

```csharp
        private enum ButtonParty
        {
            Heal1,
            Heal2
        }
    }

    /// <summary>
    /// Single decision point for custom-vs-classic health bars. Every spawn
    /// site (including party members) should route through here so the
    /// <see cref="Configuration.Profile.CustomBarsToggled"/> choice is honored
    /// consistently. Feature B of the Plugin v2 status-bars work.
    /// </summary>
    internal static class HealthBarFactory
    {
        public static bool ShouldUseCustomBar(Configuration.Profile profile)
        {
            return profile != null && profile.CustomBarsToggled;
        }

        public static BaseHealthBarGump Create(World world, GameObjects.Entity entity)
        {
            return ShouldUseCustomBar(Configuration.ProfileManager.CurrentProfile)
                ? new HealthBarGumpCustom(world, entity)
                : new HealthBarGump(world, entity);
        }
    }
}
```

`World`, `GameObjects.Entity`, `Configuration.Profile`, and
`Configuration.ProfileManager` are all reachable from the existing `using`
directives at the top of `HealthBarGump.cs` (`ClassicUO.Configuration`,
`ClassicUO.Game.GameObjects`).

- [ ] **Step 4: Route the canonical party-member spawn site through the factory**

In `src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs`, find the drag-select bar creation (~209-218):

```csharp
                        BaseHealthBarGump hbgc;

                        if (useCHB)
                        {
                            hbgc = new HealthBarGumpCustom(_world, mobile);
                        }
                        else
                        {
                            hbgc = new HealthBarGump(_world, mobile);
                        }
```

Replace with:

```csharp
                        BaseHealthBarGump hbgc = HealthBarFactory.Create(_world, mobile);
```

(`useCHB` remains in use just above for the `rect` sizing at ~160-172, so it is
not removed.)

- [ ] **Step 5: Run the test and build to verify**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~HealthBarFactoryTests"`
Expected: PASS (3 tests).

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs src/ClassicUO.Client/Game/Scenes/GameSceneInputHandler.cs tests/ClassicUO.UnitTests/Game/UI/Gumps/HealthBarFactoryTests.cs
git commit -m "$(cat <<'EOF'
feat(statusbars): centralize custom-vs-classic bar choice (party bars)

Adds HealthBarFactory.Create / ShouldUseCustomBar and routes the
drag-select party-member spawn through it so party bars honor
CustomBarsToggled. No party-only forced-classic path existed.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW
EOF
)"
```

---

## Task 4: `AnchorGroup.IsEmpty` + group registry (Feature C state)

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/AnchorManager.cs` (`AnchorGroup` class ~263-511)
- Modify: `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs` (add `PluginStatusBarGroups`)
- Test: `tests/ClassicUO.UnitTests/Game/Managers/PluginStatusBarGroupsTests.cs`

**Interfaces:**
- Consumes: `AnchorManager.AnchorGroup` (parameterless ctor → empty matrix).
- Produces:
  - `AnchorManager.AnchorGroup.IsEmpty` (bool; true when every matrix cell is null).
  - `static class ClassicUO.Game.Managers.PluginStatusBarGroups` with
    `static void Track(int groupId, AnchorManager.AnchorGroup group)`,
    `static AnchorManager.AnchorGroup GetGroup(int groupId)` (null if absent),
    `static void PruneEmpty()` (drops null or `IsEmpty` groups),
    `static void Reset()` (test-only clear).

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/Managers/PluginStatusBarGroupsTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class PluginStatusBarGroupsTests
    {
        public PluginStatusBarGroupsTests() => PluginStatusBarGroups.Reset();

        [Fact]
        public void NewAnchorGroup_IsEmpty()
        {
            var group = new AnchorManager.AnchorGroup();
            Assert.True(group.IsEmpty);
        }

        [Fact]
        public void GetGroup_ReturnsNull_WhenUntracked()
        {
            Assert.Null(PluginStatusBarGroups.GetGroup(7));
        }

        [Fact]
        public void Track_ThenGetGroup_ReturnsSameInstance()
        {
            var group = new AnchorManager.AnchorGroup();
            PluginStatusBarGroups.Track(7, group);
            Assert.Same(group, PluginStatusBarGroups.GetGroup(7));
        }

        [Fact]
        public void PruneEmpty_RemovesEmptyGroups()
        {
            PluginStatusBarGroups.Track(7, new AnchorManager.AnchorGroup());
            PluginStatusBarGroups.PruneEmpty();
            Assert.Null(PluginStatusBarGroups.GetGroup(7));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusBarGroupsTests"`
Expected: FAIL — `AnchorGroup.IsEmpty` / `PluginStatusBarGroups` do not exist.

- [ ] **Step 3a: Add `IsEmpty` to `AnchorGroup`**

In `src/ClassicUO.Client/Game/Managers/AnchorManager.cs`, find the `AnchorGroup`
parameterless constructor (~274-277):

```csharp
            public AnchorGroup()
            {
                controlMatrix = new AnchorableGump[0, 0];
            }
```

Add immediately after it:

```csharp
            /// <summary>True when no control occupies any cell of the matrix.</summary>
            public bool IsEmpty
            {
                get
                {
                    for (int x = 0; x < controlMatrix.GetLength(0); x++)
                    {
                        for (int y = 0; y < controlMatrix.GetLength(1); y++)
                        {
                            if (controlMatrix[x, y] != null)
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                }
            }
```

- [ ] **Step 3b: Add `PluginStatusBarGroups`**

In `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs`, add this class inside
the `ClassicUO.Game.Managers` namespace (after `PluginStatusOverlays`). Also add
`using System.Collections.Generic;` and `using System.Linq;` at the top of the
file if not already present (Task 1 added `System.Collections.Generic`; add
`System.Linq`):

```csharp
    /// <summary>
    /// Maps plugin-supplied group ids to the existing anchor-system
    /// <see cref="AnchorManager.AnchorGroup"/> objects so plugins can snap status
    /// bars into a shared, drag-as-a-unit group. The anchor matrix machinery is
    /// reused unchanged; this only tracks id ownership and prunes dead groups.
    /// </summary>
    internal static class PluginStatusBarGroups
    {
        private static readonly Dictionary<int, AnchorManager.AnchorGroup> _groups =
            new Dictionary<int, AnchorManager.AnchorGroup>();

        public static void Track(int groupId, AnchorManager.AnchorGroup group)
        {
            if (groupId == 0 || group == null)
            {
                return;
            }

            _groups[groupId] = group;
        }

        public static AnchorManager.AnchorGroup GetGroup(int groupId)
        {
            return _groups.TryGetValue(groupId, out AnchorManager.AnchorGroup group) ? group : null;
        }

        public static void PruneEmpty()
        {
            List<int> dead = _groups
                .Where(kv => kv.Value == null || kv.Value.IsEmpty)
                .Select(kv => kv.Key)
                .ToList();

            foreach (int id in dead)
            {
                _groups.Remove(id);
            }
        }

        /// <summary>Test-only: drops all tracked groups.</summary>
        public static void Reset() => _groups.Clear();
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~PluginStatusBarGroupsTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/AnchorManager.cs src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs tests/ClassicUO.UnitTests/Game/Managers/PluginStatusBarGroupsTests.cs
git commit -m "$(cat <<'EOF'
feat(statusbars): add groupId->AnchorGroup registry and AnchorGroup.IsEmpty

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW
EOF
)"
```

---

## Task 5: Client entrypoints `OpenStatusBar` / `CloseStatusBar` / `SetOverlay` (Feature C open/close)

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs` (add `PluginStatusBars` static class)

**Interfaces:**
- Consumes: `PluginStatusOverlays.Set`, `PluginStatusBarGroups.Track/GetGroup/PruneEmpty`,
  `UIManager.GetGump<BaseHealthBarGump>(uint)`, `UIManager.Add`,
  `UIManager.AnchorManager` (`DropControl`, `DetachControl`, indexer),
  `HealthBarGumpCustom(World, uint)`, `Client.Game.UO.World`.
- Produces (these are the exact signatures the `ClientBindings` delegates bind to
  in Task 7):
  - `static void SetOverlay(uint serial, ushort hue)`
  - `static void CloseStatusBar(uint serial)`
  - `static void OpenStatusBar(uint serial, int x, int y, byte moveIfExists, int groupId)`

> **Why no new unit test:** these call `UIManager` / construct gumps (graphics).
> The registries they delegate to are unit-tested in Tasks 1 and 4; end-to-end
> behavior is verified manually in Task 9. This is a build-verified change.

- [ ] **Step 1: Add the `PluginStatusBars` entrypoints**

In `src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs`, add the following
class inside the `ClassicUO.Game.Managers` namespace, and add these usings at the
top of the file: `using ClassicUO.Game.UI.Gumps;`.

```csharp
    /// <summary>
    /// Static client-side targets for the three plugin->cuo status-bar
    /// primitives. Bound into <c>ClientBindings</c> in PluginHost.cs and called
    /// by the host's StatusBarsImpl. All run on the game thread (the host
    /// marshals before calling). Mirrors GameActions.CastSpell as a static
    /// binding target.
    /// </summary>
    internal static class PluginStatusBars
    {
        public static void SetOverlay(uint serial, ushort hue)
        {
            PluginStatusOverlays.Set(serial, hue);
        }

        public static void CloseStatusBar(uint serial)
        {
            BaseHealthBarGump bar = UIManager.GetGump<BaseHealthBarGump>(serial);

            if (bar == null)
            {
                return;
            }

            UIManager.AnchorManager.DetachControl(bar);
            bar.Dispose();

            PluginStatusBarGroups.PruneEmpty();
        }

        public static void OpenStatusBar(uint serial, int x, int y, byte moveIfExists, int groupId)
        {
            World world = Client.Game?.UO?.World;

            if (world == null)
            {
                return;
            }

            BaseHealthBarGump existing = UIManager.GetGump<BaseHealthBarGump>(serial);

            if (existing != null)
            {
                if (moveIfExists != 0)
                {
                    existing.X = x;
                    existing.Y = y;
                }

                return;
            }

            HealthBarGumpCustom bar = new HealthBarGumpCustom(world, serial)
            {
                X = x,
                Y = y
            };

            UIManager.Add(bar);

            if (groupId != 0)
            {
                AddToGroup(groupId, bar);
            }
        }

        // Seeds a new AnchorGroup for the first bar of a groupId; for later bars
        // it positions the new bar to the right of an existing member and reuses
        // AnchorManager.DropControl to slot it into the matrix.
        private static void AddToGroup(int groupId, BaseHealthBarGump bar)
        {
            AnchorManager.AnchorGroup group = PluginStatusBarGroups.GetGroup(groupId);

            if (group == null || group.IsEmpty)
            {
                group = new AnchorManager.AnchorGroup(bar);
                UIManager.AnchorManager[bar] = group;
                PluginStatusBarGroups.Track(groupId, group);

                return;
            }

            BaseHealthBarGump host = FindHost(group);

            if (host == null)
            {
                group = new AnchorManager.AnchorGroup(bar);
                UIManager.AnchorManager[bar] = group;
                PluginStatusBarGroups.Track(groupId, group);

                return;
            }

            // Place to the right of the host so GetAnchorDirection slots it east.
            bar.X = host.X + host.GroupMatrixWidth;
            bar.Y = host.Y;

            UIManager.AnchorManager.DropControl(bar, host);
        }

        private static BaseHealthBarGump FindHost(AnchorManager.AnchorGroup group)
        {
            foreach (Gump gump in UIManager.Gumps)
            {
                if (gump is BaseHealthBarGump bar && !bar.IsDisposed && UIManager.AnchorManager[bar] == group)
                {
                    return bar;
                }
            }

            return null;
        }
    }
```

`Gump` and `BaseHealthBarGump` come from `ClassicUO.Game.UI.Gumps`;
`Client` is `ClassicUO.Client` (root namespace). Add `using ClassicUO;` at the
top of the file if the `Client` static type is not resolved during build.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/PluginStatusBars.cs
git commit -m "$(cat <<'EOF'
feat(statusbars): add OpenStatusBar/CloseStatusBar/SetOverlay client entrypoints

Open creates/moves a custom bar by serial; group ids reuse the anchor
matrix; close detaches and prunes empty groups.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW
EOF
)"
```

---

## Task 6: Plugin contract — `IStatusBars` + `IPluginContext.StatusBars`

**Files:**
- Create: `src/ClassicUO.PluginApi/IStatusBars.cs`
- Modify: `src/ClassicUO.PluginApi/IPluginContext.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public interface ClassicUO.PluginApi.IStatusBars` with
  `void OpenStatusBar(uint serial, int x, int y, bool moveIfExists = true, int groupId = 0)`,
  `void CloseStatusBar(uint serial)`, `void SetOverlay(uint serial, ushort hue)`;
  and `IStatusBars StatusBars { get; }` on `IPluginContext`. Consumed by Task 8.

> **Why no standalone test:** an interface has no behavior. It is compiled and
> exercised by `StatusBarsImpl` (Task 8) and the host tests there. Deliverable:
> the PluginApi project builds.

- [ ] **Step 1: Create the interface**

Create `src/ClassicUO.PluginApi/IStatusBars.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.PluginApi;

/// <summary>
/// Thin, generic status-bar primitives keyed by mobile serial. Priority/
/// highlight policy lives in the plugin; the client only renders. All methods
/// auto-marshal to the game thread, so they are safe to call from any thread.
/// </summary>
public interface IStatusBars
{
    /// <summary>
    /// Opens a status (health) bar for <paramref name="serial"/> at screen
    /// (<paramref name="x"/>, <paramref name="y"/>). If a bar already exists for
    /// that serial: when <paramref name="moveIfExists"/> is true it is moved to
    /// (x, y); otherwise the call is a no-op. When <paramref name="groupId"/> is
    /// non-zero the bar is anchored into the shared group with that id.
    /// Auto-marshals to the game thread.
    /// </summary>
    void OpenStatusBar(uint serial, int x, int y, bool moveIfExists = true, int groupId = 0);

    /// <summary>Closes the status bar for <paramref name="serial"/> if present.
    /// Auto-marshals to the game thread.</summary>
    void CloseStatusBar(uint serial);

    /// <summary>
    /// Sets a priority-highlight hue on the status bar for
    /// <paramref name="serial"/>. <paramref name="hue"/> = 0 clears the
    /// highlight. The highlight persists until cleared or the mobile is removed;
    /// re-apply as needed. Auto-marshals to the game thread.
    /// Badge support is deferred (see spec).
    /// </summary>
    void SetOverlay(uint serial, ushort hue);
}
```

- [ ] **Step 2: Add the property to `IPluginContext`**

In `src/ClassicUO.PluginApi/IPluginContext.cs`, find (~32-33):

```csharp
    /// <summary>Player movement, spell casting, position queries.</summary>
    IGameActions Actions { get; }
```

Add immediately after:

```csharp
    /// <summary>Status-bar open/close at a position, grouping, and priority overlay hue.</summary>
    IStatusBars StatusBars { get; }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/ClassicUO.PluginApi/ClassicUO.PluginApi.csproj`
Expected: Build FAILS in dependent BootstrapHost (PluginContextImpl does not yet
implement `StatusBars`) — that is fine; the PluginApi project itself builds.
Verify PluginApi alone:
`dotnet build src/ClassicUO.PluginApi/ClassicUO.PluginApi.csproj` → succeeds for
the contract project. (The missing implementation is added in Task 8.)

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.PluginApi/IStatusBars.cs src/ClassicUO.PluginApi/IPluginContext.cs
git commit -m "$(cat <<'EOF'
feat(statusbars): add IStatusBars contract and IPluginContext.StatusBars

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW
EOF
)"
```

---

## Task 7: Append `ClientBindings` fields + wire client delegates (cuo side)

**Files:**
- Modify: `src/ClassicUO.Client/PluginHost.cs` (`ClientBindings` struct ~34-46; `UnmanagedAssistantHost` delegate fields; `Initialize` ~270-287)

**Interfaces:**
- Consumes: `PluginStatusBars.OpenStatusBar/CloseStatusBar/SetOverlay` (Task 5).
- Produces: three new trailing `ClientBindings` fields — `OpenStatusBarFn`,
  `CloseStatusBarFn`, `SetOverlayFn` — populated at `Initialize`. The host's
  matching struct is updated in Task 8; field order MUST match.

> **Why no unit test:** the client struct is consumed across the native boundary;
> there is no managed test seam in the client. Deliverable: the client builds and
> the field order matches the host (cross-checked in Task 8's tests). This honors
> the append-only Global Constraint.

- [ ] **Step 1: Append the three fields to `ClientBindings`**

In `src/ClassicUO.Client/PluginHost.cs`, find the end of the `ClientBindings`
struct (~44-46):

```csharp
        public IntPtr /*delegate*<ref int, ref int, ref int, bool>*/ GetPlayerPositionFn;
        public IntPtr ReflectionCmdFn;
    }
```

Replace with (append AFTER `ReflectionCmdFn`, never reorder):

```csharp
        public IntPtr /*delegate*<ref int, ref int, ref int, bool>*/ GetPlayerPositionFn;
        public IntPtr ReflectionCmdFn;
        public IntPtr /*delegate*<uint, int, int, byte, int, void>*/ OpenStatusBarFn;
        public IntPtr /*delegate*<uint, void>*/ CloseStatusBarFn;
        public IntPtr /*delegate*<uint, ushort, void>*/ SetOverlayFn;
    }
```

- [ ] **Step 2: Add the delegate types and fields**

In `src/ClassicUO.Client/PluginHost.cs`, find the `_reflectionCmd` field (~214-217):

```csharp
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr dOnPluginReflectionCommand(IntPtr cmdPtr);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dOnPluginReflectionCommand _reflectionCmd = reflectionCmd;
```

Add immediately after it:

```csharp
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dOpenStatusBar(uint serial, int x, int y, byte moveIfExists, int groupId);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dOpenStatusBar _openStatusBar = Game.Managers.PluginStatusBars.OpenStatusBar;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dCloseStatusBar(uint serial);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dCloseStatusBar _closeStatusBar = Game.Managers.PluginStatusBars.CloseStatusBar;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void dSetOverlay(uint serial, ushort hue);
        [MarshalAs(UnmanagedType.FunctionPtr)]
        private readonly dSetOverlay _setOverlay = Game.Managers.PluginStatusBars.SetOverlay;
```

- [ ] **Step 3: Populate the function pointers in `Initialize`**

In `src/ClassicUO.Client/PluginHost.cs`, find (~284-286):

```csharp
            cuoHost.ReflectionCmdFn = Marshal.GetFunctionPointerForDelegate(_reflectionCmd);

            _initialize((IntPtr)mem);
```

Replace with:

```csharp
            cuoHost.ReflectionCmdFn = Marshal.GetFunctionPointerForDelegate(_reflectionCmd);
            cuoHost.OpenStatusBarFn = Marshal.GetFunctionPointerForDelegate(_openStatusBar);
            cuoHost.CloseStatusBarFn = Marshal.GetFunctionPointerForDelegate(_closeStatusBar);
            cuoHost.SetOverlayFn = Marshal.GetFunctionPointerForDelegate(_setOverlay);

            _initialize((IntPtr)mem);
```

- [ ] **Step 4: Build the client**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/PluginHost.cs
git commit -m "$(cat <<'EOF'
feat(statusbars): wire ClientBindings for status-bar primitives (cuo side)

Appends OpenStatusBarFn/CloseStatusBarFn/SetOverlayFn to ClientBindings
(append-only) and binds them to the PluginStatusBars statics.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW
EOF
)"
```

---

## Task 8: Host `StatusBarsImpl` + append host `ClientBindings` fields

**Files:**
- Modify: `src/ClassicUO.BootstrapHost/HostBridge.cs` (`ClientBindings` struct ~314-326; add `SetClientBindingsForTest`)
- Modify: `src/ClassicUO.BootstrapHost/PluginContextImpl.cs` (add `StatusBarsImpl`; wire `StatusBars`)
- Test: `tests/ClassicUO.BootstrapHost.Tests/StatusBarsTests.cs`

**Interfaces:**
- Consumes: `IStatusBars` (Task 6); `HostBridge.ClientBindings`, `HostBridge.IsGameThread`,
  `HostBridge.PostToGameThread` (existing).
- Produces: `internal sealed class StatusBarsImpl : IStatusBars`;
  `PluginContextImpl.StatusBars`; new host `ClientBindings` fields
  `OpenStatusBarFn`/`CloseStatusBarFn`/`SetOverlayFn` (same order as Task 7);
  `internal void HostBridge.SetClientBindingsForTest(ClientBindings)`.

- [ ] **Step 1: Write the failing host test**

Create `tests/ClassicUO.BootstrapHost.Tests/StatusBarsTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace ClassicUO.BootstrapHost.Tests;

/// <summary>
/// Verifies each IStatusBars method dispatches into the matching ClientBindings
/// function pointer with the right argument marshaling. A fake binding records
/// the call; no cuo.dll is involved.
/// </summary>
public sealed unsafe class StatusBarsTests
{
    private static uint _serial;
    private static int _x, _y, _group;
    private static byte _move;
    private static ushort _hue;
    private static int _openCalls, _closeCalls, _overlayCalls;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureOpen(uint serial, int x, int y, byte move, int group)
    {
        _serial = serial; _x = x; _y = y; _move = move; _group = group; _openCalls++;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureClose(uint serial) { _serial = serial; _closeCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureOverlay(uint serial, ushort hue) { _serial = serial; _hue = hue; _overlayCalls++; }

    private static StatusBarsImpl NewImplWithBindings(ClientBindings bindings)
    {
        var bridge = new HostBridge();
        bridge.SetClientBindingsForTest(bindings);
        return new StatusBarsImpl(bridge);
    }

    [Fact]
    public void OpenStatusBar_InvokesBinding_WithArgs()
    {
        _openCalls = 0;
        var bindings = new ClientBindings
        {
            OpenStatusBarFn = (nint)(delegate* unmanaged[Cdecl]<uint, int, int, byte, int, void>)&CaptureOpen
        };
        var impl = NewImplWithBindings(bindings);

        impl.OpenStatusBar(0x1234, 10, 20, moveIfExists: true, groupId: 7);

        _openCalls.Should().Be(1);
        _serial.Should().Be(0x1234u);
        _x.Should().Be(10);
        _y.Should().Be(20);
        _move.Should().Be(1);
        _group.Should().Be(7);
    }

    [Fact]
    public void CloseStatusBar_InvokesBinding()
    {
        _closeCalls = 0;
        var bindings = new ClientBindings
        {
            CloseStatusBarFn = (nint)(delegate* unmanaged[Cdecl]<uint, void>)&CaptureClose
        };
        var impl = NewImplWithBindings(bindings);

        impl.CloseStatusBar(0xABCD);

        _closeCalls.Should().Be(1);
        _serial.Should().Be(0xABCDu);
    }

    [Fact]
    public void SetOverlay_InvokesBinding_WithHue()
    {
        _overlayCalls = 0;
        var bindings = new ClientBindings
        {
            SetOverlayFn = (nint)(delegate* unmanaged[Cdecl]<uint, ushort, void>)&CaptureOverlay
        };
        var impl = NewImplWithBindings(bindings);

        impl.SetOverlay(0x55, 0x0021);

        _overlayCalls.Should().Be(1);
        _serial.Should().Be(0x55u);
        _hue.Should().Be((ushort)0x0021);
    }

    [Fact]
    public void Methods_AreNoOps_WhenBindingMissing()
    {
        var impl = NewImplWithBindings(new ClientBindings());
        // Zeroed function pointers: must not throw.
        impl.OpenStatusBar(1, 0, 0);
        impl.CloseStatusBar(1);
        impl.SetOverlay(1, 1);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~StatusBarsTests"`
Expected: FAIL — `StatusBarsImpl`, `SetClientBindingsForTest`, and the new
`ClientBindings` fields do not exist.

- [ ] **Step 3a: Append the three host `ClientBindings` fields**

In `src/ClassicUO.BootstrapHost/HostBridge.cs`, find the end of the
`ClientBindings` struct (~324-326):

```csharp
    public nint GetPlayerPositionFn;   // bool(out int x, out int y, out int z)
    public nint ReflectionCmdFn;       // legacy reflection commands; unused by v2
}
```

Replace with (append AFTER `ReflectionCmdFn`, identical order to PluginHost.cs):

```csharp
    public nint GetPlayerPositionFn;   // bool(out int x, out int y, out int z)
    public nint ReflectionCmdFn;       // legacy reflection commands; unused by v2
    public nint OpenStatusBarFn;       // void(uint serial, int x, int y, byte moveIfExists, int groupId)
    public nint CloseStatusBarFn;      // void(uint serial)
    public nint SetOverlayFn;          // void(uint serial, ushort hue)
}
```

- [ ] **Step 3b: Add the test-only bindings setter**

In `src/ClassicUO.BootstrapHost/HostBridge.cs`, find `LoadPluginsForTest` (~35-41)
and add this method immediately after it:

```csharp
    /// <summary>Test-only: install a ClientBindings table and mark the current
    /// thread as the game thread, so action impls dispatch synchronously.</summary>
    internal void SetClientBindingsForTest(ClientBindings bindings)
    {
        _gameThread = Thread.CurrentThread;
        _clientBindings = bindings;
    }
```

- [ ] **Step 3c: Add `StatusBarsImpl` and wire it on the context**

In `src/ClassicUO.BootstrapHost/PluginContextImpl.cs`, find the field group (~14-18):

```csharp
    private readonly GameActionsImpl _actions;
    private readonly ClientImpl _client;
    private readonly DispatcherImpl _dispatcher;
```

Replace with:

```csharp
    private readonly GameActionsImpl _actions;
    private readonly StatusBarsImpl _statusBars;
    private readonly ClientImpl _client;
    private readonly DispatcherImpl _dispatcher;
```

Find the constructor assignments (~28-30):

```csharp
        _actions    = new GameActionsImpl(bridge);
        _client     = new ClientImpl(bridge);
        _dispatcher = new DispatcherImpl(bridge);
```

Replace with:

```csharp
        _actions    = new GameActionsImpl(bridge);
        _statusBars = new StatusBarsImpl(bridge);
        _client     = new ClientImpl(bridge);
        _dispatcher = new DispatcherImpl(bridge);
```

Find the service property group (~38-42):

```csharp
    public IGameActions Actions => _actions;
    public IClient Client => _client;
    public IDispatcher Game => _dispatcher;
```

Replace with:

```csharp
    public IGameActions Actions => _actions;
    public IStatusBars StatusBars => _statusBars;
    public IClient Client => _client;
    public IDispatcher Game => _dispatcher;
```

Find the end of `GameActionsImpl` (~185, its closing brace) and add the new impl
class immediately after it:

```csharp
internal sealed class StatusBarsImpl : IStatusBars
{
    private readonly HostBridge _bridge;
    public StatusBarsImpl(HostBridge bridge) { _bridge = bridge; }

    public unsafe void OpenStatusBar(uint serial, int x, int y, bool moveIfExists = true, int groupId = 0)
    {
        var fn = _bridge.ClientBindings.OpenStatusBarFn;
        if (fn == 0) return;
        byte move = moveIfExists ? (byte)1 : (byte)0;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<uint, int, int, byte, int, void>)fn)(serial, x, y, move, groupId);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, int, int, byte, int, void>)fn)(serial, x, y, move, groupId));
    }

    public unsafe void CloseStatusBar(uint serial)
    {
        var fn = _bridge.ClientBindings.CloseStatusBarFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<uint, void>)fn)(serial);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, void>)fn)(serial));
    }

    public unsafe void SetOverlay(uint serial, ushort hue)
    {
        var fn = _bridge.ClientBindings.SetOverlayFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<uint, ushort, void>)fn)(serial, hue);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, ushort, void>)fn)(serial, hue));
    }
}
```

- [ ] **Step 4: Run the host tests to verify they pass**

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests --filter "FullyQualifiedName~StatusBarsTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.BootstrapHost/HostBridge.cs src/ClassicUO.BootstrapHost/PluginContextImpl.cs tests/ClassicUO.BootstrapHost.Tests/StatusBarsTests.cs
git commit -m "$(cat <<'EOF'
feat(statusbars): add StatusBarsImpl and host ClientBindings fields

Mirrors GameActionsImpl.CastSpell: each method dispatches into the
appended ClientBindings pointer, marshaling to the game thread.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW
EOF
)"
```

---

## Task 9: Sample plugin demo + full-suite verification (manual deliverable)

**Files:**
- Modify: `samples/HelloPlugin/HelloPlugin.cs`

**Interfaces:**
- Consumes: `IPluginContext.StatusBars` (Task 6), `IStatusBars` methods (Task 8).
- Produces: nothing consumed downstream; demonstrates the surface for manual QA.

- [ ] **Step 1: Add a status-bar demo to the sample plugin**

In `samples/HelloPlugin/HelloPlugin.cs`, find the hotkey handler inside
`OnInitialize` (~61-66):

```csharp
        context.Input.Hotkey += (key, mod, pressed) =>
        {
            Log($"Hotkey:{key}/{mod}/{pressed}");
            // Block the dedicated test key 999; allow everything else.
            return key != 999;
        };
```

Replace with:

```csharp
        context.Input.Hotkey += (key, mod, pressed) =>
        {
            Log($"Hotkey:{key}/{mod}/{pressed}");

            // Manual status-bar demo (press-only). Replace 0x40000000 with a
            // real serial when testing against a live shard.
            if (pressed)
            {
                const uint demoSerial = 0x40000000;
                switch (key)
                {
                    case 1: // open a grouped pair
                        context.StatusBars.OpenStatusBar(demoSerial, 200, 200, true, 1);
                        context.StatusBars.OpenStatusBar(demoSerial + 1, 200, 260, true, 1);
                        break;
                    case 2: // priority highlight (UO hue 0x0021 = red-ish)
                        context.StatusBars.SetOverlay(demoSerial, 0x0021);
                        break;
                    case 3: // clear highlight
                        context.StatusBars.SetOverlay(demoSerial, 0);
                        break;
                    case 4: // close
                        context.StatusBars.CloseStatusBar(demoSerial);
                        context.StatusBars.CloseStatusBar(demoSerial + 1);
                        break;
                }
            }

            // Block the dedicated test key 999; allow everything else.
            return key != 999;
        };
```

- [ ] **Step 2: Build the sample**

Run: `dotnet build samples/HelloPlugin/HelloPlugin.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Run the full unit + host suites**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS (existing tests + 11 new from Tasks 1, 3, 4).

Run: `dotnet test tests/ClassicUO.BootstrapHost.Tests`
Expected: PASS (existing smoke tests + 4 new from Task 8).

- [ ] **Step 4: Manual verification checklist**

Build and run the BootstrapHost-staged client against a live shard, with the
sample plugin in `Data/Plugins/HelloPlugin/`, modern UI enabled
(`CustomBarsToggled` on). Confirm:
- **A:** SetOverlay tints the custom bar's outer ring to the chosen hue; the
  state border (red on low HP, poison green, war) still reads correctly; clearing
  with hue 0 restores the black ring. Classic bar (toggle off): no change (no-op).
- **C:** Two `OpenStatusBar` calls with the same `groupId` snap together and drag
  as one unit; `CloseStatusBar` removes them and empty groups are pruned;
  `moveIfExists:false` on an existing bar is a no-op, `true` moves it.
- **B:** With `CustomBarsToggled` on, drag-selecting a party member yields the
  modern custom bar with the party `inparty` layout (heal buttons, mana/stam
  lines, party name color, and the bar survives a party member's death); with the
  toggle off it is the classic art bar.

- [ ] **Step 5: Commit**

```bash
git add samples/HelloPlugin/HelloPlugin.cs
git commit -m "$(cat <<'EOF'
docs(statusbars): demo overlay + grouped open in HelloPlugin

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014zrVrH5u5nSUWJp1gNQuVW
EOF
)"
```

---

## Self-Review

**1. Spec coverage**

| Spec section | Task(s) |
|--------------|---------|
| Public contract `IStatusBars` (Open/Close/SetOverlay, defaults) | Task 6 |
| `IPluginContext.StatusBars` | Task 6 |
| Boundary: `ClientBindings` fns + `StatusBarsImpl` mirroring CastSpell | Tasks 7, 8 |
| Auto-marshal to game thread | Task 8 (`IsGameThread`/`PostToGameThread`) |
| A: overlay registry, hue 0 clears | Task 1 |
| A: tint `_outline`, state `_border[]` untouched, classic no-op | Task 2 |
| C: open/move/ignore (`moveIfExists`) | Task 5 |
| C: groupId → AnchorGroup registry, reuse anchor matrix, prune empties | Tasks 4, 5 |
| C: close detaches + prunes | Task 5 |
| B: party bars honor `CustomBarsToggled` | Task 3 |
| B: verify `inparty` rendering | Task 9 (manual) |
| Tests: client unit (A/C/B) | Tasks 1, 3, 4 |
| Tests: host binding-dispatch | Task 8 |
| Tests: manual HelloPlugin | Task 9 |
| Append-only struct layout risk | Tasks 7, 8 (append after `ReflectionCmdFn`, matched order) |
| `_outline` availability risk | Task 2 (confirmed present in all 3 layouts) |
| Group-lifetime / prune-on-dispose risk | Tasks 4, 5 (`PruneEmpty` on close; see gap note) |
| Deferred (badges, classic overlay) | Not planned (correct) |

**2. Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to Task N" — every code step shows complete code. The only "manual" steps (Tasks 2, 5, 9 rendering) are explicitly justified by the graphics-free test constraint, with real code supplied.

**3. Type consistency:** `PluginStatusOverlays.Set/Get/Clear/Reset`, `PluginStatusBarGroups.Track/GetGroup/PruneEmpty/Reset`, `AnchorGroup.IsEmpty`, `HealthBarFactory.ShouldUseCustomBar/Create`, `PluginStatusBars.OpenStatusBar/CloseStatusBar/SetOverlay`, and the `OpenStatusBarFn/CloseStatusBarFn/SetOverlayFn` field names are used identically across producing and consuming tasks. Native signatures match across all three layers: open `(uint,int,int,byte,int)`, close `(uint)`, overlay `(uint,ushort)`.

## Spec gaps / risks discovered while reading the code

- **Feature B premise is partly inaccurate.** The spec assumes a party-member
  auto-spawn path "that currently forces the classic bar." No such path exists:
  every `new HealthBarGump*` site already gates on `CustomBarsToggled`, and
  `PartyManager` only calls `RequestUpdateContents()` on bars created by those
  shared sites. B is therefore a consolidation (one `HealthBarFactory`) + the
  canonical spawn site (`GameSceneInputHandler.cs:209-218`) + manual `inparty`
  verification, not a bug fix. Plan reflects this.
- **Group pruning is not fully automatic.** The spec's "prune on host/last-member
  dispose" only happens here when the plugin calls `CloseStatusBar` (which calls
  `PruneEmpty`). Bars closed by the user/client (out of range, death,
  right-click) detach via `AnchorManager` but will NOT trigger `PruneEmpty`, so a
  stale empty-group entry can linger until the next `CloseStatusBar`. A fully
  robust fix would hook `BaseHealthBarGump.Dispose` to prune; that is a deliberate
  follow-up to keep the client footprint minimal per the guiding constraint. The
  registry only ever holds a reference to an `AnchorGroup` (not the gumps), so the
  leak is bounded and harmless, and `GetGroup` callers re-seed on `IsEmpty`.
- **Server "close status bar" packets miss custom bars (pre-existing, out of
  scope).** `PacketHandlers.cs:4207` and `:4370` use
  `UIManager.GetGump<HealthBarGump>(serial)` (classic subclass only), so when
  `CustomBarsToggled` is on those packets won't close a `HealthBarGumpCustom`.
  Unrelated to this work but worth flagging.
- **Overlay tint relies on a white source texture.** UO hues colorize
  grayscale/white art; the existing `_outline` texture is black, so Task 2 swaps
  it to a cached white texture while a hue is active and restores black on clear.
  Confirmed `_outline` exists in all three `BuildGump` layouts, so the spec's
  "_outline availability" risk does not materialize.
- **`bool` across the native boundary is modeled as `byte`** (`moveIfExists`),
  matching the existing `RequestMove` precedent, to avoid `BOOL` width ambiguity.

---

**Plan complete and saved to `docs/superpowers/plans/2026-06-30-plugin-statusbars.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
