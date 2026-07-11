# Status Bar Colors — Design

Date: 2026-07-10
Status: Approved design, pre-implementation

## Goal

Let the user define rules that recolor a mobile's health-bar background by
its **model (Graphic)** and **hue (color)**. When a health bar is opened
for a mobile matching a rule, its bar background uses the user-chosen color
instead of the standard notoriety color.

Rule = model + a hue list (empty = any hue) → a chosen bar color. Hues are
entered `|`-separated. First matching rule (in list order) wins.

## Core mechanic

The health-bar background hue is currently derived from notoriety
(`Notoriety.GetHue(mobile.NotorietyFlag)` → `_background.Hue`). A rule
overrides that single hue at the three sites where it is computed. Nothing
else on the bar changes (name text and HP/mana/stam fill are untouched —
user chose "bar background only").

## Data model & manager

New `StatusbarColorManager` (reachable via `World`, mirrors
`InfoBarManager`).

```
class StatusbarColorRule {
    ushort Graphic;        // body/model id
    List<ushort> Hues;     // empty => matches any hue
    ushort Color;          // the override bar-background hue
}
```

Manager state: `List<StatusbarColorRule> Rules`, plus `bool Enabled`
(master toggle, backed by a `Profile` bool, default `true`).

API:
- `bool TryGetColor(ushort graphic, ushort hue, out ushort color)` —
  `!Enabled → false`; else returns the `Color` of the first rule where
  `rule.Graphic == graphic` AND (`rule.Hues` empty OR `rule.Hues` contains
  `hue`); `false` if none.
- `void Add(StatusbarColorRule rule)` / `void Remove(rule)` / rules editable
  in place from the options UI.
- `void Initialize()` — load from XML.
- `void Save()` — write XML.

## Persistence

Per-profile XML `statusbar_colors.xml` in `ProfileManager.ProfilePath`
(same pattern as `InfoBarManager`'s `infobar.xml` /
`IgnoreManager`'s `ignore_list.xml`): `ReadRules()` / `SaveRules()` with
`XmlDocument` / `XmlTextWriter`. Each `<rule>` element has `graphic`,
`hues` (the raw `|`-joined string), and `color` attributes.

Master toggle persists via a `Profile.StatusbarColorsEnabled` bool
(default `true`), like the alias `AliasesEnabled` flag.

Scope: **profile only** (not global) — this is a per-setup preference, not
identity data.

## Options tab

New "Status Bar Colors" tab in `OptionsGump` (single OptionsGump; next free
page — alias took 13, so `PAGE = 14`).

- **Master toggle** checkbox at top, bound to
  `StatusbarColorManager.Enabled` + `Profile.StatusbarColorsEnabled`.
- Scrollable `DataBox` list; row control `StatusbarColorControl : Control`
  (modeled on `InfoBarBuilderControl` / `AliasEntryControl`):
  `[Graphic StbTextBox] [Hues StbTextBox] [ClickableColorBox color] [Delete]`.
  - Graphic box: hex/decimal model id.
  - Hues box: `|`-separated hue list (e.g. `0x0044|0x0022`); empty = any.
  - Color: `ClickableColorBox` swatch (the InfoBar hue picker control).
  - Edits write back into the rule; Delete removes it + `ReArrangeChildren`.
- **Add (target)** button → `TargetManager.SetTargeting(callback, ...)`;
  on picking a `Mobile`, prefill a new row with `mobile.Graphic` and a
  single hue `mobile.Hue`, default color.
- **Add (manual)** button → append an empty editable row.
- Cancel a pending target in the gump's `Dispose` (already present from the
  alias tab work).

Parse/format: the row reads its Hues textbox as `|`-split ushort tokens
(accept `0x`-hex and decimal; ignore blanks); an empty box → empty list
(any hue). The Graphic box parses one ushort likewise.

## Override points (bar background only)

`src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs` — three sites where
`barColor` is set from notoriety, each immediately before it is applied to
`_background.Hue`:

| Variant | Compute line | Applied |
| --- | --- | --- |
| `HealthBarGumpCustom` | `:611` | `:621` |
| `HealthBarGump` (classic) build | `:1712` | `:1714` |
| `HealthBarGump` (classic) update | `:1969` | `:1973` |

At each, after the existing notoriety `barColor` is computed, add:

```csharp
if (mobile != null &&
    World.StatusbarColorManager.TryGetColor(mobile.Graphic, mobile.Hue, out ushort overrideColor))
{
    barColor = overrideColor;
}
```

`mobile` (a `Mobile`, from `entity as Mobile`) is in scope at all three
sites; `mobile.Graphic` and `mobile.Hue` are `ushort` fields on
`GameObject`. `TryGetColor` returns false when the feature is disabled or no
rule matches, leaving the notoriety behavior untouched.

## Wiring

- `World.cs`: add `StatusbarColorManager StatusbarColorManager { get; }`,
  construct in the ctor next to the other managers.
- Load rules at game start (next to `AliasManager.Initialize()` in
  `GameScene`).
- Save on rule change (each Add/Remove/edit persists), mirroring
  `InfoBarManager`.

## Testing

- `StatusbarColorManager` unit tests (internals visible to
  `ClassicUO.UnitTests`): `TryGetColor` — exact graphic+hue match;
  empty-hue-list matches any hue; graphic match but hue not in list → no
  match; no graphic match → no match; `Enabled == false` → always false;
  first-match-wins when two rules match; XML round-trip (save then load
  reproduces rules incl. multi-hue and empty-hue).
- Hues parse/format round-trip (`"0x44|0x22"` ↔ `[0x44,0x22]`, empty ↔ `[]`).
- Override wiring and the options UI are build- + in-game-verified (health
  bars are not unit-testable without a live scene).

## Out of scope

- Recoloring name text or HP/mana/stam fill (user chose background only).
- Global (cross-character) rules — profile only.
- Matching on anything other than Graphic + Hue.
