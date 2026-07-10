# Character Alias — Design

Date: 2026-07-10
Status: Approved design, pre-implementation

## Goal

Let the user assign a personal display-name (alias) to any in-game
character. User targets a mobile, types an alias, and from then on the
client shows the alias instead of the server-sent name at (almost) every
display site — until the user removes the entry.

Example: player serial `0x000000A0`, real name `Bazinka`. User assigns
alias `Ducky`. Everywhere the client would show `Bazinka`, it now shows
`Ducky`.

Aliases are managed from a new tab in the in-game Options gump.

## Core principle

There is **no single name source** in the client. Four distinct fields
feed the display sites:

- `Entity.Name` — world-object name (name plate, health bar, party list)
- OPL name (`ObjectPropertiesListManager` / `ItemProperty.Name`) — tooltip
- `Mobile.Title` — paperdoll (server-sent name+title string)
- Journal `JournalEntry.Name` — separate field per journal line

The alias is applied as a **read-side override only**. We never mutate
`Entity.Name` or any stored field — the server rewrites those on the next
packet. Instead every display site routes its name through a single
resolver that swaps in the alias when one exists for that serial.

## AliasManager

New manager under `src/ClassicUO.Client/Game/Managers/AliasManager.cs`,
reachable via `World` like the other managers (mirrors `IgnoreManager`).

State — two dictionaries so each alias can be global or profile-scoped:

```
Dictionary<uint, string> _global    // serial -> alias, shared across all this-account chars
Dictionary<uint, string> _profile   // serial -> alias, this character only
```

Master switch: a single `Enabled` flag turns the whole feature off. When
off, `Resolve` short-circuits and returns the real name for every serial —
no substitution anywhere, aliases stay stored but dormant. Backed by a
`AliasesEnabled` bool auto-property on `Profile.cs` (default `true`),
per-character.

API:

```
bool    Enabled                                     // mirrors Profile.AliasesEnabled
string? GetAlias(uint serial)                       // null if disabled, else profile-then-global
string  Resolve(uint serial, string realName)       // !Enabled -> realName; else GetAlias(serial) ?? realName
void    Set(uint serial, string alias, bool global) // upsert into the chosen store, remove from the other
void    Remove(uint serial)                          // remove from whichever store holds it
bool    IsGlobal(uint serial)
IEnumerable<AliasEntry> Entries                      // for the options list (both stores, tagged)
```

Precedence: **profile beats global** — if the same serial has an alias in
both stores, `Resolve` returns the profile one.

`AliasEntry` value type: `{ uint Serial; string Alias; bool Global }`.
(`Serial` doubles as the real-name fallback key; the real name itself is
read live from the entity at display time, so a renamed character still
resolves.)

## Persistence

Per-alias scope, chosen by the row's Global checkbox — two stores:

- **Profile store** — `List<AliasEntry>` auto-property on
  `Configuration/Profile.cs` (mirrors `ChatHistory`, Profile.cs:203).
  Requires a `[JsonSerializable(typeof(AliasEntry))]` line in
  `ProfileJsonContext` (Profile.cs:24-45). Serializes automatically to the
  per-character `profile.json`.
- **Global store** — a small dedicated file written to the CUO **root**
  data dir (not the per-character profile path), following the
  `IgnoreManager` XML read/save pattern (`Game/Managers/IgnoreManager.cs`,
  `ReadIgnoreList`/`SaveIgnoreList`) but rooted so all characters share it.

`AliasManager` loads both on init and writes the relevant store on every
add / remove / global-toggle. Toggling a row's Global flag moves the entry
between the two stores.

## Options tab

`src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` (single OptionsGump; no
Modern variant exists).

- Add a left-rail tab button: `NiceButton(... ButtonAction.SwitchPage ...)`
  with the next free `ButtonParameter` page number (current tabs end ~12,
  so `PAGE = 13`), `IsSelected` handling as siblings.
- New `BuildAliasList()` method, `const int PAGE = 13`, called from the ctor
  alongside the other `BuildX()` calls, ending in `Add(rightArea, PAGE)`.
- **Master toggle** at the top of the tab: a `Checkbox` bound to
  `Profile.AliasesEnabled` (label e.g. "Enable character aliases"). Off =
  whole feature dormant, list below stays editable but has no display
  effect.
- Content = ScrollArea + DataBox list, pattern from `IgnoreManagerGump` /
  `IgnoreListControl` and the InfoBar tab (OptionsGump.cs:3327-3453).
- **Row control** (new `AliasEntryControl : Control`, modeled on
  `InfoBarBuilderControl`): `[Global checkbox] [alias StbTextBox]
  [real-name label] [Delete NiceButton]`.
  - Editing the alias textbox → `AliasManager.Set(serial, text, global)`.
  - Toggling Global → `AliasManager.Set(serial, alias, newGlobal)` (moves
    stores).
  - Delete → `AliasManager.Remove(serial)`, dispose row,
    `DataBox.ReArrangeChildren()`.
- **Add button** → start a target picker:
  `World.TargetManager.SetTargeting(obj => { if (obj is Entity e)
  { AliasManager.Set(e.Serial, e.Name, global:false); /* add row */ } },
  CursorType.Target, TargetType.Neutral)`. Uses the callback overload
  (`TargetManager.cs:150-155`) — no new `CursorTarget` enum needed. Cancel
  the target in the gump's `Dispose` if still targeting.

## Read sites to patch (route through `Resolve`)

| Site | File:line | Notes |
| --- | --- | --- |
| Name plate / overhead name gump | `NameOverheadGump.cs:136` | `Resolve(entity.Serial, entity.Name)` |
| Health bar name | `HealthBarGump.cs:531`, `:1903` | `_name` assignment |
| Party list | `PartyManager.cs:240` / `PartyGump.cs:108` | member name read |
| Tooltip (mobile) | `Tooltip.cs:221` | serial available at the OPL name read |
| Paperdoll | `PaperdollGump.cs:554` | see special handling below |
| Journal on-screen (classic) | `JournalGump.cs:273` | substitute name prefix |
| Journal on-screen (resizable) | `ResizableJournal.cs:429` | substitute name prefix |

### Paperdoll special handling

`Mobile.Title` is a server-composed string like `"Bazinka, the brave"`,
not a bare name. Alias substitution: if `Title` starts with the real name
(`Entity.Name`), replace only that prefix with the alias and keep the rest
of the title; otherwise leave `Title` untouched. Real name is available
from the same entity.

### Journal special handling

- Disk log is **left untouched** — the disk write happens in
  `JournalManager.Add` (`JournalManager.cs:63-70`) using the real `Name`
  parameter, *before* either journal gump renders. So on-screen
  substitution at the two render sites cannot affect the disk file. Disk =
  real name, guaranteed by construction.
- Substitute the alias only at `JournalGump.cs:273` and
  `ResizableJournal.cs:429`, where `"Name: Text"` is composed from the
  separate `entry.Name` field.
- **Hover tooltip showing the real name**: `ResizableJournal` only. Its
  lines are `Label` controls but are rendered manually into a private
  render list, so they receive no mouse events today — this requires adding
  per-line hit-testing in `JournalEntriesContainer`'s mouse handling,
  mapped to the same Y-offset layout math used at `ResizableJournal.cs:347-352`,
  and calling `SetTooltip(entry.Name)` for the hovered line. The classic
  `JournalGump` gets no hover (lines are non-Control `RenderedText` blobs;
  hit-testing there is out of scope).

## Explicitly out of scope

- **Overhead speech float text** (transient talk text over a mobile's head,
  via `MessageManager`). The user's own speech carries no name, and
  aliasing others' speech would mean fulltext-scanning every spoken line —
  not worth it. Left showing the original.
- Packet mutation / anything sent to the server. Alias is purely local
  display.
- Classic `JournalGump` hover tooltip.

## Testing

- `AliasManager` unit tests (project already exposes internals to
  `ClassicUO.UnitTests`): `Resolve` returns alias when set / real name when
  not; `Enabled = false` makes `Resolve` return real name even with an
  alias set; profile-beats-global precedence; `Set` with global toggle
  moves the entry between stores and out of the other; `Remove` clears from
  whichever store; round-trip persistence load/save for both stores.
- Paperdoll prefix replacement: title with matching name prefix →
  alias-prefixed; title without → unchanged.
- Manual verification per display site (name plate, health bar, party,
  tooltip, paperdoll, both journals, journal hover) with a live alias.
