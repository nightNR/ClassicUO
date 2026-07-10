# Status Bar Colors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recolor a mobile's health-bar background by user-defined rules (model Graphic + hue list) instead of the notoriety color, configured from a new Options tab.

**Architecture:** A `StatusbarColorManager` (via `World`, mirrors `InfoBarManager`) holds a list of `StatusbarColorRule { ushort Graphic; List<ushort> Hues; ushort Color }` and a master `Enabled` flag. `TryGetColor(graphic, hue, out color)` returns the first matching rule's color. The three health-bar sites that compute `barColor` from notoriety consult it and override when it matches. Rules persist to a per-profile XML.

**Tech Stack:** C#/.NET 10, FNA UI (`Gump`/`Control`), xUnit tests, `XmlTextWriter`/`XmlDocument` persistence.

## Global Constraints

- `.NET 10`; new source files carry `// SPDX-License-Identifier: BSD-2-Clause`.
- xUnit; `ClassicUO.Client` internals visible to `ClassicUO.UnitTests`.
- Build: `dotnet build ClassicUO.sln -c Debug`. Tests: `dotnet test tests/ClassicUO.UnitTests`.
- Match surrounding style; the `TryGetColor` call sits in the health-bar update loop — keep it allocation-free (list scan + `List.Contains`).
- Recolor the bar BACKGROUND only (`_background.Hue`); never touch name text or HP/mana/stam fill. Profile-scoped (not global).
- Empty hue list = matches any hue. First matching rule (list order) wins.

## File Structure

- Create: `src/ClassicUO.Client/Game/Managers/StatusbarColorManager.cs` — manager + `StatusbarColorRule` + hue parse/format helpers.
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs` — `StatusbarColorsEnabled` bool.
- Modify: `src/ClassicUO.Client/Game/World.cs` — field + construction.
- Modify: `src/ClassicUO.Client/Game/Scenes/GameScene.cs` — `Initialize()` call.
- Create: `src/ClassicUO.Client/Game/UI/Controls/StatusbarColorControl.cs` — one list row.
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` — new tab.
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs` — 3 override sites.
- Create: `tests/ClassicUO.UnitTests/StatusbarColorManagerTests.cs`.

---

### Task 1: StatusbarColorManager — rules, matching, persistence

**Files:**
- Create: `src/ClassicUO.Client/Game/Managers/StatusbarColorManager.cs`
- Modify: `Profile.cs` (add flag near other bools, e.g. next to `CustomBarsToggled` ~line 227)
- Modify: `World.cs` (property + ctor, next to `InfoBars`/`IgnoreManager`)
- Modify: `GameScene.cs` (Initialize call, next to `_world.AliasManager.Initialize();`)
- Create: `tests/ClassicUO.UnitTests/StatusbarColorManagerTests.cs`

**Interfaces produced:**
- `StatusbarColorRule { ushort Graphic; List<ushort> Hues; ushort Color }` (namespace `ClassicUO.Game.Managers`)
- `StatusbarColorManager(World world)`, `bool Enabled { get; set; }`, `List<StatusbarColorRule> Rules`, `bool TryGetColor(ushort graphic, ushort hue, out ushort color)`, `void Add(StatusbarColorRule)`, `void Remove(StatusbarColorRule)`, `void Initialize()`, `void Save()`, internal `ReadRules(string path)` / `SaveRules(string path)`, `string XmlPathOverride`
- static helpers `List<ushort> ParseHues(string)`, `string FormatHues(IEnumerable<ushort>)`, `bool TryParseUShort(string, out ushort)`

- [ ] **Step 1: Write the failing tests**

Create `tests/ClassicUO.UnitTests/StatusbarColorManagerTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.IO;
using System.Threading;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class StatusbarColorManagerTests
    {
        private static int _seq;

        private static StatusbarColorManager New()
        {
            var m = new StatusbarColorManager(null);
            m.XmlPathOverride = Path.Combine(Path.GetTempPath(), $"cuo_sbc_{Interlocked.Increment(ref _seq)}.xml");
            if (File.Exists(m.XmlPathOverride)) File.Delete(m.XmlPathOverride);
            return m;
        }

        private static StatusbarColorRule Rule(ushort g, ushort color, params ushort[] hues) =>
            new StatusbarColorRule { Graphic = g, Color = color, Hues = new List<ushort>(hues) };

        [Fact]
        public void TryGetColor_ExactGraphicAndHue()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0044, 0x0022));
            Assert.True(m.TryGetColor(0x00C8, 0x0022, out var c));
            Assert.Equal((ushort)0x0044, c);
        }

        [Fact]
        public void TryGetColor_EmptyHueList_MatchesAnyHue()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0044));            // no hues => any
            Assert.True(m.TryGetColor(0x00C8, 0x9999, out var c));
            Assert.Equal((ushort)0x0044, c);
        }

        [Fact]
        public void TryGetColor_GraphicMatchHueMiss_NoMatch()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0044, 0x0022));
            Assert.False(m.TryGetColor(0x00C8, 0x0033, out _));
        }

        [Fact]
        public void TryGetColor_NoGraphicMatch_NoMatch()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0044));
            Assert.False(m.TryGetColor(0x00C9, 0x0044, out _));
        }

        [Fact]
        public void TryGetColor_Disabled_ReturnsFalse()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0044));
            m.Enabled = false;
            Assert.False(m.TryGetColor(0x00C8, 0x0044, out _));
        }

        [Fact]
        public void TryGetColor_FirstMatchWins()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0011));            // any hue
            m.Add(Rule(0x00C8, 0x0022, 0x0055));
            Assert.True(m.TryGetColor(0x00C8, 0x0055, out var c));
            Assert.Equal((ushort)0x0011, c);        // first rule wins
        }

        [Fact]
        public void Xml_RoundTrips_RulesIncludingMultiAndEmptyHues()
        {
            var path = Path.Combine(Path.GetTempPath(), $"cuo_sbc_rt_{Interlocked.Increment(ref _seq)}.xml");
            if (File.Exists(path)) File.Delete(path);

            var a = New();
            a.Add(Rule(0x00C8, 0x0044, 0x0022, 0x0033));
            a.Add(Rule(0x0190, 0x0055));            // empty hues
            a.SaveRules(path);

            var b = New();
            b.ReadRules(path);
            Assert.True(b.TryGetColor(0x00C8, 0x0033, out var c1));
            Assert.Equal((ushort)0x0044, c1);
            Assert.True(b.TryGetColor(0x0190, 0x1234, out var c2)); // empty => any
            Assert.Equal((ushort)0x0055, c2);

            File.Delete(path);
        }

        [Theory]
        [InlineData("0x44|0x22", new ushort[] { 0x44, 0x22 })]
        [InlineData("68|34", new ushort[] { 68, 34 })]
        [InlineData("", new ushort[] { })]
        [InlineData("  0x44 | 0x22 ", new ushort[] { 0x44, 0x22 })]
        public void ParseHues_Works(string input, ushort[] expected)
        {
            Assert.Equal(expected, StatusbarColorManager.ParseHues(input).ToArray());
        }
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~StatusbarColorManagerTests"`
Expected: FAIL (type/method don't exist — compile errors).

- [ ] **Step 3: Implement the manager**

Create `src/ClassicUO.Client/Game/Managers/StatusbarColorManager.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal sealed class StatusbarColorRule
    {
        public ushort Graphic;
        public List<ushort> Hues = new List<ushort>();
        public ushort Color;
    }

    internal sealed class StatusbarColorManager
    {
        private readonly World _world;
        private readonly List<StatusbarColorRule> _rules = new List<StatusbarColorRule>();

        public StatusbarColorManager(World world) { _world = world; }

        public bool Enabled { get; set; } = true;

        public List<StatusbarColorRule> Rules => _rules;

        internal string XmlPathOverride { get; set; }

        private string XmlPath =>
            XmlPathOverride ?? Path.Combine(ProfileManager.ProfilePath, "statusbar_colors.xml");

        public void Add(StatusbarColorRule rule) => _rules.Add(rule);
        public void Remove(StatusbarColorRule rule) => _rules.Remove(rule);

        public bool TryGetColor(ushort graphic, ushort hue, out ushort color)
        {
            color = 0;

            if (!Enabled)
                return false;

            for (int i = 0; i < _rules.Count; i++)
            {
                StatusbarColorRule r = _rules[i];
                if (r.Graphic != graphic)
                    continue;
                if (r.Hues.Count == 0 || r.Hues.Contains(hue))
                {
                    color = r.Color;
                    return true;
                }
            }

            return false;
        }

        public void Initialize()
        {
            _rules.Clear();

            Profile profile = ProfileManager.CurrentProfile;
            if (profile != null)
                Enabled = profile.StatusbarColorsEnabled;

            ReadRules(XmlPath);
        }

        public void Save() => SaveRules(XmlPath);

        internal void ReadRules(string path)
        {
            _rules.Clear();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            XmlDocument doc = new XmlDocument();
            try { doc.Load(path); }
            catch (Exception ex) { Log.Error(ex.ToString()); return; }

            XmlElement root = doc["statusbarcolors"];
            if (root == null)
                return;

            foreach (XmlElement xml in root.GetElementsByTagName("rule"))
            {
                if (!TryParseUShort(xml.GetAttribute("graphic"), out ushort graphic))
                    continue;
                TryParseUShort(xml.GetAttribute("color"), out ushort color);

                _rules.Add(new StatusbarColorRule
                {
                    Graphic = graphic,
                    Color = color,
                    Hues = ParseHues(xml.GetAttribute("hues"))
                });
            }
        }

        internal void SaveRules(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            using (XmlTextWriter xml = new XmlTextWriter(path, Encoding.UTF8)
            {
                Formatting = Formatting.Indented,
                IndentChar = '\t',
                Indentation = 1
            })
            {
                xml.WriteStartDocument(true);
                xml.WriteStartElement("statusbarcolors");

                foreach (StatusbarColorRule r in _rules)
                {
                    xml.WriteStartElement("rule");
                    xml.WriteAttributeString("graphic", r.Graphic.ToString());
                    xml.WriteAttributeString("hues", FormatHues(r.Hues));
                    xml.WriteAttributeString("color", r.Color.ToString());
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
                xml.WriteEndDocument();
            }
        }

        public static List<ushort> ParseHues(string text)
        {
            var list = new List<ushort>();
            if (string.IsNullOrWhiteSpace(text))
                return list;

            foreach (string part in text.Split('|'))
            {
                if (TryParseUShort(part.Trim(), out ushort h))
                    list.Add(h);
            }

            return list;
        }

        public static string FormatHues(IEnumerable<ushort> hues)
        {
            return string.Join("|", System.Linq.Enumerable.Select(hues, h => "0x" + h.ToString("X")));
        }

        public static bool TryParseUShort(string s, out ushort value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ushort.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

            return ushort.TryParse(s, out value);
        }
    }
}
```

Note: add `using ClassicUO;` if `CUOEnviroment`-style unresolved (not used here). `System.Linq` is referenced fully-qualified to avoid a using churn; a top-level `using System.Linq;` is equally fine.

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~StatusbarColorManagerTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Add profile flag**

In `Profile.cs`, near `CustomBarsToggled` (~line 227):

```csharp
        public bool StatusbarColorsEnabled { get; set; } = true;
```

- [ ] **Step 6: Wire into World**

In `World.cs`, next to the `InfoBars`/`IgnoreManager` property declarations:

```csharp
        public StatusbarColorManager StatusbarColorManager { get; }
```

In the `World()` ctor, next to the other manager constructions (e.g. after `IgnoreManager = new IgnoreManager(this);`):

```csharp
            StatusbarColorManager = new StatusbarColorManager(this);
```

- [ ] **Step 7: Load at game start**

In `GameScene.cs`, after `_world.AliasManager.Initialize();`:

```csharp
            _world.StatusbarColorManager.Initialize();
```

- [ ] **Step 8: Full suite + build**

Run: `dotnet test tests/ClassicUO.UnitTests` then `dotnet build ClassicUO.sln -c Debug`
Expected: all pass, 0 warnings.

- [ ] **Step 9: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/StatusbarColorManager.cs src/ClassicUO.Client/Configuration/Profile.cs src/ClassicUO.Client/Game/World.cs src/ClassicUO.Client/Game/Scenes/GameScene.cs tests/ClassicUO.UnitTests/StatusbarColorManagerTests.cs
git commit -m "feat(statusbar-colors): rule manager with matching and persistence"
```

---

### Task 2: Options tab — rule list, target/manual add, master toggle

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/StatusbarColorControl.cs`
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs`

**Interfaces:**
- Consumes: `World.StatusbarColorManager` (`Rules`, `Add`, `Remove`, `Save`, `Enabled`), `StatusbarColorRule`, `StatusbarColorManager.ParseHues`/`FormatHues`/`TryParseUShort`, `TargetManager.SetTargeting(Action<GameObject>, uint, TargetType)`, `ClickableColorBox`.

- [ ] **Step 1: Create the row control**

Create `src/ClassicUO.Client/Game/UI/Controls/StatusbarColorControl.cs` (modeled on `InfoBarBuilderControl` and `AliasEntryControl`):

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using ClassicUO.Resources;

namespace ClassicUO.Game.UI.Controls
{
    internal sealed class StatusbarColorControl : Control
    {
        private readonly StbTextBox _graphicBox;
        private readonly StbTextBox _huesBox;
        private readonly ClickableColorBox _colorBox;
        private readonly StatusbarColorRule _rule;
        private readonly Gump _gump;

        public StatusbarColorControl(Gump gump, StatusbarColorRule rule)
        {
            _gump = gump;
            _rule = rule;

            _graphicBox = new StbTextBox(0xFF, 10, 90) { X = 5, Y = 0, Width = 90, Height = 26 };
            _graphicBox.SetText("0x" + rule.Graphic.ToString("X"));
            _graphicBox.TextChanged += (s, e) => Commit();

            _huesBox = new StbTextBox(0xFF, 40, 150) { X = 100, Y = 0, Width = 150, Height = 26 };
            _huesBox.SetText(StatusbarColorManager.FormatHues(rule.Hues));
            _huesBox.TextChanged += (s, e) => Commit();

            _colorBox = new ClickableColorBox(_gump.World, 260, 0, 13, 14, rule.Color);

            NiceButton deleteButton = new NiceButton(300, 0, 60, 25, ButtonAction.Activate, ResGumps.Delete) { ButtonParameter = 999 };
            deleteButton.MouseUp += (sender, e) =>
            {
                _gump.World.StatusbarColorManager.Remove(_rule);
                _gump.World.StatusbarColorManager.Save();
                Dispose();
                ((DataBox)Parent)?.ReArrangeChildren();
            };

            Add(new ResizePic(0x0BB8) { X = 0, Y = 0, Width = 95, Height = 26 });
            Add(new ResizePic(0x0BB8) { X = 100, Y = 0, Width = 155, Height = 26 });
            Add(_graphicBox);
            Add(_huesBox);
            Add(_colorBox);
            Add(deleteButton);

            Width = 365;
            Height = 26;
        }

        private void Commit()
        {
            if (StatusbarColorManager.TryParseUShort(_graphicBox.Text, out ushort g))
                _rule.Graphic = g;

            _rule.Hues = StatusbarColorManager.ParseHues(_huesBox.Text);
            _rule.Color = _colorBox.Hue;

            _gump.World.StatusbarColorManager.Save();
        }
    }
}
```

Note: confirm `ClickableColorBox` exposes a `Hue` property (used in `InfoBarBuilderControl` as `labelColor.Hue`). The color box updates its own `Hue` when clicked; `Commit` reads it. If the color must be captured on color-change specifically, also hook the color box's change event if one exists; otherwise `Commit` on the textbox edits plus the periodic save-on-close is sufficient — but simplest correctness: also call `_gump.World.StatusbarColorManager.Save()` from a lightweight override or when the tab closes. Verify `ClickableColorBox` API in `Game/UI/Controls/`.

- [ ] **Step 2: Add the tab button**

In `OptionsGump.cs`, after the alias tab button (page 13), add another SwitchPage tab using the running `i++` counter:

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
                    "Status Bar Colors"
                ) { ButtonParameter = 14 }
            );
```

- [ ] **Step 3: Add `BuildStatusbarColors()` and call it**

In `OptionsGump.cs`, add `BuildStatusbarColors();` in the ctor next to `BuildAliases();` (before `ChangePage(1);`). Then add the method (next to `BuildAliases`):

```csharp
        private void BuildStatusbarColors()
        {
            const int PAGE = 14;

            ScrollArea rightArea = new ScrollArea(190, 20, WIDTH - 210, 420, true);

            Checkbox enabledBox = new Checkbox(0x00D2, 0x00D3, "Enable status bar colors", FONT, HUE_FONT)
            {
                IsChecked = World.StatusbarColorManager.Enabled,
                X = 5,
                Y = 5
            };
            enabledBox.ValueChanged += (s, e) =>
            {
                World.StatusbarColorManager.Enabled = enabledBox.IsChecked;
                if (_currentProfile != null)
                    _currentProfile.StatusbarColorsEnabled = enabledBox.IsChecked;
            };
            rightArea.Add(enabledBox);

            DataBox databox = new DataBox(0, 70, 0, 0) { WantUpdateSize = true };

            void AddRow(StatusbarColorRule rule)
            {
                var row = new StatusbarColorControl(this, rule) { Y = databox.Children.Count * 26 };
                databox.Add(row);
                databox.ReArrangeChildren();
            }

            foreach (StatusbarColorRule rule in World.StatusbarColorManager.Rules)
            {
                var row = new StatusbarColorControl(this, rule) { Y = databox.Children.Count * 26 };
                databox.Add(row);
            }

            NiceButton addTarget = new NiceButton(5, 35, 130, 25, ButtonAction.Activate, "Add (target)") { ButtonParameter = 999 };
            addTarget.MouseUp += (sender, e) =>
            {
                World.TargetManager.SetTargeting(
                    obj =>
                    {
                        if (obj is Game.GameObjects.Mobile m)
                        {
                            var rule = new StatusbarColorRule
                            {
                                Graphic = m.Graphic,
                                Hues = new System.Collections.Generic.List<ushort> { m.Hue },
                                Color = 0
                            };
                            World.StatusbarColorManager.Add(rule);
                            World.StatusbarColorManager.Save();
                            AddRow(rule);
                        }
                    },
                    CursorType.Target,
                    TargetType.Neutral
                );
            };

            NiceButton addManual = new NiceButton(140, 35, 130, 25, ButtonAction.Activate, "Add (manual)") { ButtonParameter = 999 };
            addManual.MouseUp += (sender, e) =>
            {
                var rule = new StatusbarColorRule { Graphic = 0, Hues = new System.Collections.Generic.List<ushort>(), Color = 0 };
                World.StatusbarColorManager.Add(rule);
                World.StatusbarColorManager.Save();
                AddRow(rule);
            };

            rightArea.Add(addTarget);
            rightArea.Add(addManual);
            rightArea.Add(databox);

            Add(rightArea, PAGE);
        }
```

Note: confirm `DataBox` ctor `(x,y,w,h)` and `FONT`/`HUE_FONT` constants (both already used by `BuildAliases`). The target-cancel-on-dispose guard added during the alias work already covers this tab. Match the real `ClickableColorBox` ctor signature (in `InfoBarBuilderControl` it is `new ClickableColorBox(_gump.World, x, y, w, h, hue)`).

- [ ] **Step 4: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: succeeds; resolve any signature mismatch (`ClickableColorBox`, `StbTextBox.TextChanged`, `DataBox`) per the notes — all three are confirmed to exist from the alias/InfoBar work.

- [ ] **Step 5: Manual verification (deferred to in-game)**

Options → Status Bar Colors: master checkbox; Add (target) picks a mobile and fills a row with its graphic + hue; Add (manual) adds an empty row; editing graphic/hues, picking a color, and Delete all persist across reopen.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/StatusbarColorControl.cs src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(statusbar-colors): options tab with target/manual add and color picker"
```

---

### Task 3: Health-bar background override (3 sites)

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs:611`, `:1712`, `:1969`

**Interfaces:**
- Consumes: `World.StatusbarColorManager.TryGetColor(ushort, ushort, out ushort)`.

- [ ] **Step 1: Custom variant** — `HealthBarGump.cs:611`

After the existing line:

```csharp
                ushort barColor = mobile != null ? Notoriety.GetHue(mobile.NotorietyFlag) : (ushort) 912;
```
insert:
```csharp
                if (mobile != null && World.StatusbarColorManager.TryGetColor(mobile.Graphic, mobile.Hue, out ushort sbcColor))
                {
                    barColor = sbcColor;
                }
```

- [ ] **Step 2: Classic build** — `HealthBarGump.cs:1712`

After:

```csharp
                    ushort barColor = entity == null || entity == World.Player || mobile == null || mobile.NotorietyFlag == NotorietyFlag.Criminal || mobile.NotorietyFlag == NotorietyFlag.Gray ? (ushort) 0 : Notoriety.GetHue(mobile.NotorietyFlag);
```
insert:
```csharp
                    if (mobile != null && World.StatusbarColorManager.TryGetColor(mobile.Graphic, mobile.Hue, out ushort sbcColorBuild))
                    {
                        barColor = sbcColorBuild;
                    }
```

- [ ] **Step 3: Classic update** — `HealthBarGump.cs:1969`

After:

```csharp
                ushort barColor = entity == World.Player || mobile == null || mobile.NotorietyFlag == NotorietyFlag.Criminal || mobile.NotorietyFlag == NotorietyFlag.Gray ? (ushort) 0 : Notoriety.GetHue(mobile.NotorietyFlag);
```
insert:
```csharp
                if (mobile != null && World.StatusbarColorManager.TryGetColor(mobile.Graphic, mobile.Hue, out ushort sbcColorUpd))
                {
                    barColor = sbcColorUpd;
                }
```

Note: at each site verify `mobile` is the in-scope `Mobile` local (it is: `entity as Mobile`) and that the following code applies `barColor` to `_background.Hue` (lines 621 / 1714 / 1973). Distinct out-var names (`sbcColor`/`sbcColorBuild`/`sbcColorUpd`) avoid collisions. The dead/black-BG overrides that follow (custom 625-631) still win afterward — that's intended (a dead mobile stays the death color).

- [ ] **Step 4: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: succeeds, 0 warnings.

- [ ] **Step 5: Manual verification (deferred to in-game)**

Add a rule for a known NPC's graphic (+ optional hue), open its health bar (both `CustomBarsToggled` on and off) → background shows the chosen color; a non-matching NPC stays notoriety-colored; disabling the master toggle reverts all.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs
git commit -m "feat(statusbar-colors): override health-bar background from rules"
```

---

## Self-Review

- **Spec coverage:** rule model + Graphic/Hue matching + empty-hue-any + first-match (Task 1) ✓; `|`-parse (Task 1 ParseHues) ✓; profile XML persistence + master toggle (Task 1) ✓; options tab with target + manual add + color picker (Task 2) ✓; 3 background override sites (Task 3) ✓; background-only / profile-only / no name+fill recolor — respected (Task 3 touches only `barColor`→`_background.Hue`) ✓.
- **Type consistency:** `TryGetColor(ushort, ushort, out ushort)`, `StatusbarColorRule { Graphic; Hues; Color }`, `ParseHues`/`FormatHues`/`TryParseUShort`, `Enabled`, `Rules`, `Add`/`Remove`/`Save`/`Initialize`, `XmlPathOverride`/`ReadRules`/`SaveRules` used consistently across tasks.
- **Confirm-against-source (flagged inline, each with fallback):** `ClickableColorBox` ctor + `Hue` property; `StbTextBox.TextChanged`; `DataBox(x,y,w,h)`; `Mobile.Graphic`/`Hue` `ushort` in scope at the 3 sites (verified in investigation); the color-picker change may need an explicit save hook if `ClickableColorBox` has a change event.
