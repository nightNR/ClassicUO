# Character Alias Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user assign a personal alias to any in-game character (via a target-picker in a new Options tab) and show that alias instead of the server name at every display site, with a master on/off toggle and per-alias global/profile scope.

**Architecture:** A new `AliasManager` (reachable via `World`) holds two `serial → alias` dictionaries (global + profile) and a single `Resolve(serial, realName)` read-side resolver. Every display site routes its name through `Resolve`; nothing mutates stored entity fields. Profile-scoped aliases persist in `profile.json`; global ones in a shared XML file. The journal keeps the real name on disk (disk write already runs before render) and substitutes only at the two render sites, plus a real-name hover tooltip in the resizable journal.

**Tech Stack:** C# / .NET 10, FNA-XNA UI (`Gump`/`Control` tree), xUnit tests (`tests/ClassicUO.UnitTests`), System.Text.Json source-gen for profile, `XmlTextWriter`/`XmlDocument` for the global store.

## Global Constraints

- `.NET 10` (`net10.0`), `LangVersion=Preview`, `AllowUnsafeBlocks=true`.
- New source files carry the BSD-2 header: `// SPDX-License-Identifier: BSD-2-Clause`.
- Match surrounding style; no free allocation in hot render loops (the `Resolve` call is a dictionary lookup — allocation-free).
- Tests are xUnit; `ClassicUO.Client` exposes internals to `ClassicUO.UnitTests`.
- Build: `dotnet build ClassicUO.sln -c Debug`. Tests: `dotnet test tests/ClassicUO.UnitTests`.
- Never mutate `Entity.Name`, `Mobile.Title`, `JournalEntry.Name`, or OPL data — alias is display-only.

---

## File Structure

- Create: `src/ClassicUO.Client/Game/Managers/AliasManager.cs` — the manager + `AliasEntry` model.
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs` — `AliasesEnabled` bool, `CharacterAliases` list, JSON registration.
- Modify: `src/ClassicUO.Client/Game/World.cs` — field, construction.
- Modify: `src/ClassicUO.Client/Game/Scenes/GameScene.cs` — `Initialize()` call.
- Create: `src/ClassicUO.Client/Game/UI/Controls/AliasEntryControl.cs` — one list row.
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` — new tab.
- Modify display sites: `NameOverheadGump.cs`, `HealthBarGump.cs`, `PartyManager.cs`, `PartyGump.cs`, `Tooltip.cs`, `PaperdollGump.cs`, `JournalGump.cs`, `ResizableJournal.cs`.
- Create: `tests/ClassicUO.UnitTests/AliasManagerTests.cs`.

---

### Task 1: Profile properties + `AliasEntry` model

Adds the persisted profile fields and the shared DTO. Compiles standalone; no behavior yet.

**Files:**
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs:203` (add near `ChatHistory`), `:25` (JSON registration)
- Create: `src/ClassicUO.Client/Game/Managers/AliasManager.cs` (just the `AliasEntry` class for now)

**Interfaces:**
- Produces: `class AliasEntry { uint Serial; string Alias; bool Global }` (namespace `ClassicUO.Game.Managers`); `Profile.AliasesEnabled` (bool, default `true`); `Profile.CharacterAliases` (`List<AliasEntry>`).

- [ ] **Step 1: Create `AliasEntry`**

Create `src/ClassicUO.Client/Game/Managers/AliasManager.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal sealed class AliasEntry
    {
        public uint Serial { get; set; }
        public string Alias { get; set; }
        public bool Global { get; set; }
    }
}
```

- [ ] **Step 2: Add profile properties**

In `src/ClassicUO.Client/Configuration/Profile.cs`, next to line 203 (`public List<ChatHistoryEntry> ChatHistory ...`), add:

```csharp
        public bool AliasesEnabled { get; set; } = true;
        public List<ClassicUO.Game.Managers.AliasEntry> CharacterAliases { get; set; } = new List<ClassicUO.Game.Managers.AliasEntry>();
```

- [ ] **Step 3: Register `AliasEntry` for JSON source-gen**

In `src/ClassicUO.Client/Configuration/Profile.cs`, after line 25 (the `ChatHistoryEntry` `JsonSerializable`), add:

```csharp
    [JsonSerializable(typeof(ClassicUO.Game.Managers.AliasEntry), GenerationMode = JsonSourceGenerationMode.Metadata)]
```

- [ ] **Step 4: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds (no test yet — this is model + config scaffolding folded into Task 2's test cycle).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/AliasManager.cs src/ClassicUO.Client/Configuration/Profile.cs
git commit -m "feat(alias): add AliasEntry model and profile fields"
```

---

### Task 2: `AliasManager` — resolve, mutate, persist

The heart of the feature: two dictionaries, the `Resolve` resolver, add/remove, the master `Enabled` flag, global XML persistence, profile-list sync, and `World` wiring.

**Files:**
- Modify: `src/ClassicUO.Client/Game/Managers/AliasManager.cs`
- Modify: `src/ClassicUO.Client/Game/World.cs:61-124` (add property), `:31-47` (construct)
- Modify: `src/ClassicUO.Client/Game/Scenes/GameScene.cs:116` (call `Initialize`)
- Create: `tests/ClassicUO.UnitTests/AliasManagerTests.cs`

**Interfaces:**
- Consumes: `AliasEntry`, `Profile.CharacterAliases`, `Profile.AliasesEnabled` from Task 1.
- Produces:
  - `AliasManager(World world)`
  - `bool Enabled { get; set; }` (default `true`)
  - `string Resolve(uint serial, string realName)` — `!Enabled → realName`; else alias if present else `realName`
  - `string GetAlias(uint serial)` — profile store first, then global; `null` if none
  - `void Set(uint serial, string alias, bool global)` — upsert; removes from the other store; persists
  - `void Remove(uint serial)` — remove from whichever store; persists
  - `bool IsGlobal(uint serial)`
  - `IReadOnlyList<AliasEntry> Entries` — snapshot of both stores for the options list
  - `void Initialize()` — load global XML + profile list, set `Enabled` from profile
  - internal `void ReadGlobal(string path)` / `void SaveGlobal(string path)` — path-injectable for tests

- [ ] **Step 1: Write the failing tests**

Create `tests/ClassicUO.UnitTests/AliasManagerTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.IO;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class AliasManagerTests
    {
        private static AliasManager New() => new AliasManager(null);

        [Fact]
        public void Resolve_ReturnsRealName_WhenNoAlias()
        {
            var m = New();
            Assert.Equal("Bazinka", m.Resolve(0xA0, "Bazinka"));
        }

        [Fact]
        public void Resolve_ReturnsAlias_WhenSet()
        {
            var m = New();
            m.Set(0xA0, "Ducky", global: false);
            Assert.Equal("Ducky", m.Resolve(0xA0, "Bazinka"));
        }

        [Fact]
        public void Resolve_ReturnsRealName_WhenDisabled()
        {
            var m = New();
            m.Set(0xA0, "Ducky", global: false);
            m.Enabled = false;
            Assert.Equal("Bazinka", m.Resolve(0xA0, "Bazinka"));
        }

        [Fact]
        public void ProfileAlias_BeatsGlobalAlias_ForSameSerial()
        {
            var m = New();
            m.Set(0xA0, "GlobalName", global: true);
            m.Set(0xA0, "ProfileName", global: false);
            Assert.Equal("ProfileName", m.Resolve(0xA0, "Bazinka"));
            Assert.False(m.IsGlobal(0xA0));
        }

        [Fact]
        public void Set_MovesEntry_BetweenStores()
        {
            var m = New();
            m.Set(0xA0, "Ducky", global: false);
            Assert.False(m.IsGlobal(0xA0));
            m.Set(0xA0, "Ducky", global: true);
            Assert.True(m.IsGlobal(0xA0));
            Assert.Single(m.Entries);
        }

        [Fact]
        public void Remove_ClearsAlias()
        {
            var m = New();
            m.Set(0xA0, "Ducky", global: true);
            m.Remove(0xA0);
            Assert.Equal("Bazinka", m.Resolve(0xA0, "Bazinka"));
            Assert.Empty(m.Entries);
        }

        [Fact]
        public void GlobalStore_RoundTrips_ThroughXml()
        {
            string path = Path.Combine(Path.GetTempPath(), "cuo_alias_test.xml");
            if (File.Exists(path)) File.Delete(path);

            var a = New();
            a.Set(0xA0, "Ducky", global: true);
            a.Set(0xB1, "Piggy", global: true);
            a.SaveGlobal(path);

            var b = New();
            b.ReadGlobal(path);
            Assert.Equal("Ducky", b.Resolve(0xA0, "x"));
            Assert.Equal("Piggy", b.Resolve(0xB1, "y"));
            Assert.True(b.IsGlobal(0xA0));

            File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AliasManagerTests"`
Expected: FAIL — `AliasManager` has no `Resolve`/`Set`/`Remove`/`Entries`/`IsGlobal`/`Enabled`/`SaveGlobal`/`ReadGlobal` (compile errors).

- [ ] **Step 3: Implement the manager**

Replace the contents of `src/ClassicUO.Client/Game/Managers/AliasManager.cs` with (keep the `AliasEntry` class from Task 1):

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal sealed class AliasEntry
    {
        public uint Serial { get; set; }
        public string Alias { get; set; }
        public bool Global { get; set; }
    }

    internal sealed class AliasManager
    {
        private readonly World _world;
        private readonly Dictionary<uint, string> _global = new Dictionary<uint, string>();
        private readonly Dictionary<uint, string> _profile = new Dictionary<uint, string>();

        public AliasManager(World world) { _world = world; }

        public bool Enabled { get; set; } = true;

        private static string GlobalPath =>
            Path.Combine(CUOEnviroment.ExecutablePath, "Data", "aliases_global.xml");

        public string GetAlias(uint serial)
        {
            if (_profile.TryGetValue(serial, out string p))
                return p;
            if (_global.TryGetValue(serial, out string g))
                return g;
            return null;
        }

        public bool IsGlobal(uint serial) => !_profile.ContainsKey(serial) && _global.ContainsKey(serial);

        public string Resolve(uint serial, string realName)
        {
            if (!Enabled)
                return realName;

            string alias = GetAlias(serial);
            return string.IsNullOrEmpty(alias) ? realName : alias;
        }

        public void Set(uint serial, string alias, bool global)
        {
            if (string.IsNullOrEmpty(alias))
            {
                Remove(serial);
                return;
            }

            _global.Remove(serial);
            _profile.Remove(serial);

            if (global)
                _global[serial] = alias;
            else
                _profile[serial] = alias;

            Persist();
        }

        public void Remove(uint serial)
        {
            _global.Remove(serial);
            _profile.Remove(serial);
            Persist();
        }

        public IReadOnlyList<AliasEntry> Entries
        {
            get
            {
                var list = new List<AliasEntry>(_profile.Count + _global.Count);
                foreach (var kv in _profile)
                    list.Add(new AliasEntry { Serial = kv.Key, Alias = kv.Value, Global = false });
                foreach (var kv in _global)
                    list.Add(new AliasEntry { Serial = kv.Key, Alias = kv.Value, Global = true });
                return list;
            }
        }

        public void Initialize()
        {
            _global.Clear();
            _profile.Clear();

            ReadGlobal(GlobalPath);

            var profile = ProfileManager.CurrentProfile;
            if (profile != null)
            {
                Enabled = profile.AliasesEnabled;
                if (profile.CharacterAliases != null)
                {
                    foreach (var e in profile.CharacterAliases)
                        if (e != null && !string.IsNullOrEmpty(e.Alias))
                            _profile[e.Serial] = e.Alias;
                }
            }
        }

        private void Persist()
        {
            SaveGlobal(GlobalPath);

            var profile = ProfileManager.CurrentProfile;
            if (profile != null)
            {
                var list = new List<AliasEntry>(_profile.Count);
                foreach (var kv in _profile)
                    list.Add(new AliasEntry { Serial = kv.Key, Alias = kv.Value, Global = false });
                profile.CharacterAliases = list;
            }
        }

        internal void ReadGlobal(string path)
        {
            if (!File.Exists(path))
                return;

            XmlDocument doc = new XmlDocument();
            try { doc.Load(path); }
            catch (System.Exception ex) { Log.Error(ex.ToString()); return; }

            XmlElement root = doc["aliases"];
            if (root == null)
                return;

            foreach (XmlElement xml in root.ChildNodes)
            {
                if (xml.Name != "info")
                    continue;

                string serialText = xml.GetAttribute("serial");
                string alias = xml.GetAttribute("alias");
                if (uint.TryParse(serialText, out uint serial) && !string.IsNullOrEmpty(alias))
                    _global[serial] = alias;
            }
        }

        internal void SaveGlobal(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (XmlTextWriter xml = new XmlTextWriter(path, Encoding.UTF8)
            {
                Formatting = Formatting.Indented,
                IndentChar = '\t',
                Indentation = 1
            })
            {
                xml.WriteStartDocument(true);
                xml.WriteStartElement("aliases");

                foreach (var kv in _global)
                {
                    xml.WriteStartElement("info");
                    xml.WriteAttributeString("serial", kv.Key.ToString());
                    xml.WriteAttributeString("alias", kv.Value);
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
                xml.WriteEndDocument();
            }
        }
    }
}
```

Note: `CUOEnviroment` is in namespace `ClassicUO`; add `using ClassicUO;` at the top if the build reports it unresolved.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AliasManagerTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Wire into `World`**

In `src/ClassicUO.Client/Game/World.cs`, next to the `IgnoreManager` property (line 81) add:

```csharp
        public AliasManager AliasManager { get; }
```

In the `World()` ctor after line 40 (`IgnoreManager = new IgnoreManager(this);`) add:

```csharp
            AliasManager = new AliasManager(this);
```

- [ ] **Step 6: Load aliases at game start**

In `src/ClassicUO.Client/Game/Scenes/GameScene.cs`, after line 116 (`_world.CommandManager.Initialize();`) add:

```csharp
            _world.AliasManager.Initialize();
```

- [ ] **Step 7: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/AliasManager.cs src/ClassicUO.Client/Game/World.cs src/ClassicUO.Client/Game/Scenes/GameScene.cs tests/ClassicUO.UnitTests/AliasManagerTests.cs
git commit -m "feat(alias): AliasManager with resolve, stores, and persistence"
```

---

### Task 3: Options tab — list, master toggle, target-picker add

Adds the "Aliases" tab to the Options gump: a master enable checkbox, a scrollable list of alias rows, and an Add button that starts a target cursor.

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/AliasEntryControl.cs`
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` — tab button (~218), `BuildAliases()` method + ctor call (~427)

**Interfaces:**
- Consumes: `World.AliasManager` (`Entries`, `Set`, `Remove`, `Enabled`) and `TargetManager.SetTargeting(Action<GameObject>, uint, TargetType)`, `CursorType.Target`, `TargetType.Neutral`.
- Produces: `AliasEntryControl(Gump gump, AliasEntry entry)` with `MouseUp`-driven delete; a new options page `PAGE = 13`.

- [ ] **Step 1: Create the row control**

Create `src/ClassicUO.Client/Game/UI/Controls/AliasEntryControl.cs` (modeled on `InfoBarBuilderControl.cs`):

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using ClassicUO.Resources;

namespace ClassicUO.Game.UI.Controls
{
    internal sealed class AliasEntryControl : Control
    {
        private readonly StbTextBox _aliasBox;
        private readonly Checkbox _globalBox;
        private readonly uint _serial;
        private readonly Gump _gump;

        public AliasEntryControl(Gump gump, AliasEntry entry)
        {
            _gump = gump;
            _serial = entry.Serial;

            _globalBox = new Checkbox(0x00D2, 0x00D3) { X = 5, Y = 5, IsChecked = entry.Global };
            _globalBox.ValueChanged += (s, e) =>
                _gump.World.AliasManager.Set(_serial, _aliasBox.Text, _globalBox.IsChecked);

            _aliasBox = new StbTextBox(0xFF, 30, 130) { X = 30, Y = 0, Width = 130, Height = 26 };
            _aliasBox.SetText(entry.Alias);
            _aliasBox.TextChanged += (s, e) =>
                _gump.World.AliasManager.Set(_serial, _aliasBox.Text, _globalBox.IsChecked);

            Label nameLabel = new Label($"0x{_serial:X8}", true, 0x0386, 200, 1) { X = 175, Y = 5 };

            NiceButton deleteButton = new NiceButton(390, 0, 60, 25, ButtonAction.Activate, ResGumps.Delete) { ButtonParameter = 999 };
            deleteButton.MouseUp += (sender, e) =>
            {
                _gump.World.AliasManager.Remove(_serial);
                Dispose();
                ((DataBox)Parent)?.ReArrangeChildren();
            };

            Add(new ResizePic(0x0BB8) { X = 25, Y = 0, Width = 140, Height = 26 });
            Add(_globalBox);
            Add(_aliasBox);
            Add(nameLabel);
            Add(deleteButton);

            Width = 450;
            Height = 26;
        }
    }
}
```

Note: verify `StbTextBox` exposes a `TextChanged` event; if not, persist on the row's `OnFocusLost`/gump close instead — check `StbTextBox.cs`. The `nameLabel` shows the serial as `0xXXXXXXXX`; if the entity is in view its real name could be resolved via `_gump.World.Mobiles.Get(_serial)?.Name`, but the serial is always available and is the stable identifier.

- [ ] **Step 2: Add the tab button**

In `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs`, after the last `SwitchPage` tab button in the left rail (after line 218's Video button block — place it alongside the others, using the running `i++` counter), add a tab. Use the next free page number; existing pages run through 12, so use `13`:

```csharp
            Add
            (
                new NiceButton
                (
                    10,
                    10 + 30 * i++,
                    140,
                    25,
                    ButtonAction.SwitchPage,
                    "Aliases"
                ) { ButtonParameter = 13 }
            );
```

(If a `ResGumps.Aliases` string resource is preferred over the literal, add it to the resource file; the literal is acceptable for the first cut.)

- [ ] **Step 3: Add `BuildAliases()` and call it**

In `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs`, after line 427 (`BuildExperimental();`) but before `ChangePage(1);` (line 429) add:

```csharp
            BuildAliases();
```

Then add the method (place it next to `BuildInfoBar`, ~line 3327):

```csharp
        private void BuildAliases()
        {
            const int PAGE = 13;

            ScrollArea rightArea = new ScrollArea(190, 20, WIDTH - 210, 420, true);

            Checkbox enabledBox = new Checkbox(0x00D2, 0x00D3, "Enable character aliases", FONT, HUE_FONT)
            {
                IsChecked = World.AliasManager.Enabled,
                X = 5,
                Y = 5
            };
            enabledBox.ValueChanged += (s, e) =>
            {
                World.AliasManager.Enabled = enabledBox.IsChecked;
                if (_currentProfile != null)
                    _currentProfile.AliasesEnabled = enabledBox.IsChecked;
            };
            rightArea.Add(enabledBox);

            NiceButton addButton = new NiceButton(5, 35, 130, 25, ButtonAction.Activate, "Add (target)") { ButtonParameter = 999 };

            DataBox databox = new DataBox(0, 70, 0, 0) { WantUpdateSize = true };

            foreach (AliasEntry entry in World.AliasManager.Entries)
            {
                var row = new AliasEntryControl(this, entry) { Y = databox.Children.Count * 26 };
                databox.Add(row);
            }

            addButton.MouseUp += (sender, e) =>
            {
                World.TargetManager.SetTargeting(
                    obj =>
                    {
                        if (obj is Game.GameObjects.Entity ent)
                        {
                            World.AliasManager.Set(ent.Serial, ent.Name ?? string.Empty, global: false);
                            var row = new AliasEntryControl(this, new AliasEntry { Serial = ent.Serial, Alias = ent.Name ?? string.Empty, Global = false })
                            {
                                Y = databox.Children.Count * 26
                            };
                            databox.Add(row);
                            databox.ReArrangeChildren();
                        }
                    },
                    CursorType.Target,
                    TargetType.Neutral
                );
            };

            rightArea.Add(addButton);
            rightArea.Add(databox);

            Add(rightArea, PAGE);
        }
```

Note: confirm the exact `DataBox` constructor signature against `Game/UI/Controls/DataBox.cs`; if it differs, use the same construction the `BuildInfoBar` databox uses (`_databox`). `FONT` and `HUE_FONT` are the constants used by `AddCheckBox` (OptionsGump.cs:4719) and are in scope. Adding via the new-target callback: cancel the pending target in the gump's existing `OnButtonClick`/`Dispose` if `World.TargetManager.IsTargeting` (mirror `IgnoreManagerGump.cs:145`) — add that guard to `OptionsGump`'s `Dispose` if not already present.

- [ ] **Step 4: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: succeeds. Fix any signature mismatches surfaced (`StbTextBox.TextChanged`, `DataBox` ctor) per the notes above.

- [ ] **Step 5: Manual verification**

Launch the client, open Options → Aliases tab. Verify: master checkbox reflects state; "Add (target)" starts a target cursor; clicking a mobile adds a row with an editable alias box and a Global checkbox; Delete removes the row; reopening Options shows persisted rows.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/AliasEntryControl.cs src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(alias): options tab with target-picker, list, and master toggle"
```

---

### Task 4: Route non-journal display sites through `Resolve`

Patches name plate, both health-bar sites, party (manager + gump), tooltip, and paperdoll.

**Files:**
- Modify: `NameOverheadGump.cs:136`, `HealthBarGump.cs:531` and `:1903`, `PartyManager.cs:240`, `PartyGump.cs:110`, `Tooltip.cs:245-246`, `PaperdollGump.cs:556`

**Interfaces:**
- Consumes: `World.AliasManager.Resolve(uint, string)`.

- [ ] **Step 1: Name plate** — `NameOverheadGump.cs:136`

Replace:

```csharp
                string t = entity.Name;
```
with:
```csharp
                string t = World.AliasManager.Resolve(entity.Serial, entity.Name);
```

- [ ] **Step 2: Health bar (both sites)** — `HealthBarGump.cs:531` and `:1903`

At site A (line 529-531) replace the assignment:

```csharp
                if (!string.IsNullOrEmpty(entity.Name) && _name != entity.Name)
                {
                    _name = entity.Name;
```
with:
```csharp
                string _resolved = World.AliasManager.Resolve(entity.Serial, entity.Name);
                if (!string.IsNullOrEmpty(entity.Name) && _name != _resolved)
                {
                    _name = _resolved;
```

At site B (line 1901-1903) replace:

```csharp
                if (!string.IsNullOrEmpty(entity.Name) && !(inparty && LocalSerial == World.Player.Serial) && _name != entity.Name)
                {
                    _name = entity.Name;
```
with:
```csharp
                string _resolvedB = World.AliasManager.Resolve(entity.Serial, entity.Name);
                if (!string.IsNullOrEmpty(entity.Name) && !(inparty && LocalSerial == World.Player.Serial) && _name != _resolvedB)
                {
                    _name = _resolvedB;
```

- [ ] **Step 3: Party manager** — `PartyManager.cs:240`

Replace:

```csharp
                    _name = mobile.Name;
```
with:
```csharp
                    _name = _world.AliasManager.Resolve(Serial, mobile.Name);
```

- [ ] **Step 4: Party gump** — `PartyGump.cs:110`

Replace:

```csharp
                    name = World.Party.Members[i].Name;
```
with (the member `.Name` already routes through the manager after Step 3, so this line needs no change — confirm `World.Party.Members[i].Name` reads `PartyManager.Name`; if it reads a raw field instead, wrap it: `name = World.AliasManager.Resolve(World.Party.Members[i].Serial, World.Party.Members[i].Name);`).

- [ ] **Step 5: Tooltip** — `Tooltip.cs:245-246`

Replace:

```csharp
                            sb.Append(name);
                            sbHTML.Append(name);
```
with:
```csharp
                            string aliasName = _world.AliasManager.Resolve(serial, name);
                            sb.Append(aliasName);
                            sbHTML.Append(aliasName);
```

- [ ] **Step 6: Paperdoll** — `PaperdollGump.cs:554-556`

`Mobile.Title` is a composed `"Name, the title"` string. Substitute only the name prefix. Replace:

```csharp
            if (mobile != null && mobile.Title != _titleLabel.Text)
            {
                UpdateTitle(mobile.Title);
            }
```
with:
```csharp
            if (mobile != null)
            {
                string title = mobile.Title;
                if (!string.IsNullOrEmpty(mobile.Name) && !string.IsNullOrEmpty(title) && title.StartsWith(mobile.Name))
                {
                    string alias = World.AliasManager.Resolve(mobile.Serial, mobile.Name);
                    if (!string.Equals(alias, mobile.Name))
                        title = alias + title.Substring(mobile.Name.Length);
                }

                if (title != _titleLabel.Text)
                {
                    UpdateTitle(title);
                }
            }
```

- [ ] **Step 7: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: succeeds.

- [ ] **Step 8: Manual verification**

With an alias set for a nearby mobile: name plate, its health bar (drag one out), party list (if partied), hover tooltip, and paperdoll (double-click) all show the alias. Toggle the master checkbox off → all revert to real name.

- [ ] **Step 9: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/NameOverheadGump.cs src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs src/ClassicUO.Client/Game/Managers/PartyManager.cs src/ClassicUO.Client/Game/UI/Gumps/PartyGump.cs src/ClassicUO.Client/Game/UI/Tooltip.cs src/ClassicUO.Client/Game/UI/Gumps/PaperdollGump.cs
git commit -m "feat(alias): apply alias at name plate, health bar, party, tooltip, paperdoll"
```

---

### Task 5: Journal on-screen substitution (disk unchanged)

Substitute the alias in the composed `"Name: Text"` string in both journal gumps. The disk log is written earlier in `JournalManager.Add`, so it keeps the real name automatically — do not touch `JournalManager`.

**Files:**
- Modify: `JournalGump.cs:267-273`, `ResizableJournal.cs:429`

**Interfaces:**
- Consumes: `World.AliasManager.Resolve(uint, string)`. Journal entries carry a serial — confirm `JournalEntry.Serial` exists (set from `Journal.Add(text, hue, name, serial, ...)`, `GameScene.cs:275`). If `JournalEntry` lacks a `Serial` field, add one (populate it in `JournalManager.Add`), since alias keys on serial.

- [ ] **Step 1: Confirm `JournalEntry.Serial`**

Read `src/ClassicUO.Client/Game/Managers/JournalManager.cs:130-143` and `:23-44`. If `Serial` is not stored on `JournalEntry`, add `public uint Serial;` to the class and `entry.Serial = serial;` in `Add` (the `serial` parameter already exists at the call site `GameScene.cs:275`). Commit that as its own step before substituting.

- [ ] **Step 2: Classic journal** — `JournalGump.cs:267`

Replace:

```csharp
            var usrSend = entry.Name != string.Empty ? $"{entry.Name}" : string.Empty;
```
with:
```csharp
            string aliasName = World.AliasManager.Resolve(entry.Serial, entry.Name);
            var usrSend = !string.IsNullOrEmpty(aliasName) ? $"{aliasName}" : string.Empty;
```

Note: the ignore check on line 270 (`IgnoredCharsList.Contains(usrSend)`) is name-based; leave it against `entry.Name` to preserve ignore behavior. Change line 270 to use `entry.Name`:

```csharp
            if (!string.IsNullOrEmpty(entry.Name) && World.IgnoreManager.IgnoredCharsList.Contains(entry.Name))
                return;
```

- [ ] **Step 3: Resizable journal** — `ResizableJournal.cs:429`

Replace:

```csharp
                        new Label($"{e.Name}: {e.Text}", e.IsUnicode, e.Hue, Width - BORDER_WIDTH - timeS.Width, font: e.Font),
```
with:
```csharp
                        new Label($"{ResolveAlias(e)}: {e.Text}", e.IsUnicode, e.Hue, Width - BORDER_WIDTH - timeS.Width, font: e.Font),
```

And add a helper in the `JournalEntriesContainer` class (it holds a `_resizableJournal` reference; reach `World` via the gump). Add near the top of `JournalEntriesContainer`:

```csharp
            private string ResolveAlias(JournalEntry e)
            {
                var mgr = _resizableJournal?.World?.AliasManager;
                return mgr != null ? mgr.Resolve(e.Serial, e.Name) : e.Name;
            }
```

Note: confirm `ResizableJournal` (a `Gump`) exposes `World`; all gumps do. If `_resizableJournal.World` is not accessible from the nested class, pass `World` into `JournalEntriesContainer`'s constructor.

- [ ] **Step 4: Build + verify disk untouched**

Run: `dotnet build ClassicUO.sln -c Debug`
Then enable `SaveJournalToFile`, set an alias, trigger a journal line from that character. On screen: alias. In `Data/Client/JournalLogs/*_journal.txt`: real name. Confirm both.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/JournalGump.cs src/ClassicUO.Client/Game/UI/Gumps/ResizableJournal.cs src/ClassicUO.Client/Game/Managers/JournalManager.cs
git commit -m "feat(alias): show alias in journal on-screen, keep real name on disk"
```

---

### Task 6: Real-name hover tooltip in the resizable journal

`ResizableJournal` renders lines manually (no per-line control), so add hit-testing in `JournalEntriesContainer` that maps the mouse Y to a line and shows that line's real `entry.Name` as a tooltip. Classic `JournalGump` gets no hover (out of scope).

**Files:**
- Modify: `ResizableJournal.cs` — `JournalEntriesContainer` (add `OnMouseOver` + a per-line Y map)

**Interfaces:**
- Consumes: `journalDatas` deque and the same layout math as `AddToRenderLists` (`ResizableJournal.cs:347-352`), `SetTooltip(string)` (`Control.cs:359`), `ClearTooltip()`.

- [ ] **Step 1: Store real name per line**

`JournalData` must carry the real name for the hover. In `ResizableJournal.cs`, find the `JournalData` class and add a `public string RealName;` field; set it in `AddEntry` when constructing `JournalData` (`RealName = e.Name`). Read the `JournalData` definition first to match its constructor.

- [ ] **Step 2: Add hover hit-testing**

In `JournalEntriesContainer`, add an `OnMouseOver` override that walks `journalDatas` accumulating `EntryText.Height` (mirroring the visible-range math at lines 347-352, offset by `_scrollBar.Value`) to find the hovered line, then sets the tooltip:

```csharp
            protected override void OnMouseOver(int x, int y)
            {
                int my = 0;
                int mouseY = y + _scrollBar.Value;

                foreach (JournalData journalEntry in journalDatas)
                {
                    if (journalEntry == null || string.IsNullOrEmpty(journalEntry.EntryText.Text))
                        continue;
                    if (!CanBeDrawn(journalEntry.TextType, journalEntry.MessageType))
                        continue;

                    int h = journalEntry.EntryText.Height;
                    if (mouseY >= my && mouseY < my + h)
                    {
                        if (!string.IsNullOrEmpty(journalEntry.RealName))
                            SetTooltip(journalEntry.RealName);
                        else
                            ClearTooltip();
                        return;
                    }
                    my += h;
                }

                ClearTooltip();
            }
```

Note: confirm the exact coordinate convention against the render math at line 347 (`my - _scrollBar.Value`); the render loop starts `my = y` (absolute) and subtracts `_scrollBar.Value`, so here the local accumulator starts at 0 and compares against `y + _scrollBar.Value`. Adjust if a first manual test shows a vertical offset. `CanBeDrawn`, `journalDatas`, and `_scrollBar` are all in scope inside the container. `SetTooltip`/`ClearTooltip` are on the base `Control`.

- [ ] **Step 3: Build + manual verify**

Run: `dotnet build ClassicUO.sln -c Debug`
Open the resizable journal, alias a character who has spoken, hover its (aliased) line → tooltip shows the real name. Hover a system line → no name tooltip. Scroll and re-hover → tooltip still maps to the correct line.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/ResizableJournal.cs
git commit -m "feat(alias): real-name hover tooltip on resizable journal lines"
```

---

## Self-Review

- **Spec coverage:** AliasManager + Resolve (Task 2) ✓; global/profile stores + precedence (Task 2) ✓; per-alias Global checkbox (Task 3) ✓; master Enabled toggle (Tasks 2+3) ✓; persistence both stores (Tasks 1+2) ✓; options tab + target picker (Task 3) ✓; name plate / health bar / party / tooltip / paperdoll (Task 4) ✓; journal on-screen + disk-untouched (Task 5) ✓; resizable-journal hover (Task 6) ✓; overhead speech float text excluded — no task, as specified ✓; classic-journal hover excluded ✓.
- **Type consistency:** `Resolve(uint, string)`, `Set(uint, string, bool)`, `Remove(uint)`, `IsGlobal(uint)`, `Entries`, `Enabled`, `Initialize`, `ReadGlobal/SaveGlobal(string)` are used identically across Tasks 2–6. `AliasEntry { Serial, Alias, Global }` consistent.
- **Open confirmations flagged inline (verify against source during implementation):** `StbTextBox.TextChanged` event; `DataBox` ctor signature; `JournalEntry.Serial` presence (Task 5 Step 1 adds it if missing); `ResizableJournal.World` reachability from the nested container; hover coordinate offset (Task 6 Step 2). Each has a fallback in its note.
