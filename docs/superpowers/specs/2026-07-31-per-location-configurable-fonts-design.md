# Per-location configurable fonts (TTF, dynamic slots)

**Date:** 2026-07-31
**Status:** Approved design
**Scope:** Infrastructure only — face catalog, dynamic slot registry, memory
refactor, per-location profile choice, Options UI control, and the wiring API.
Individual UI locations (journal, names, spellbook, …) are migrated *later*,
one at a time, each as its own small increment on top of this infrastructure.

## Problem

TTF in-game rendering already exists: `FontsLoader` synthetic unicode slots
20–40 are rasterized on demand from TTF via `TtfGlyphSource`
(`src/ClassicUO.Assets/FontsLoader.cs`), registered at boot from a fixed table
(`AtlasFontRegistry.RegisterAll`, `src/ClassicUO.Client/Resources/Loader.cs`).

Two problems:

1. **Only two "locations" are user-selectable** — Chat (`Profile.ChatFont`) and
   Tooltip (`Profile.TooltipFont`), both via the radio-list `FontSelector` in
   `OptionsGump`. Everywhere else the font number is a hardcoded literal
   (`font: 6`, `isunicode: false`, …) across dozens of call sites. Those call
   sites never reach TTF regardless of user preference.
2. **Size is baked into the slot.** Each face occupies three fixed slots
   (`Cinzel 14 / 18 / 22`), so the radio picker shows one entry per (face,size).
   There is no size axis; adding a full 8–32 range per face as fixed slots would
   blow past the slot cap and the preallocated glyph array.

## Goals

- Let the user pick, **per UI location**, a font face + a pixel size (8–32).
- Keep "Classic" (the original MUL bitmap / ASCII rendering) selectable per
  location, and make it the default so nothing changes visually until the user
  opts in.
- Generate TTF (face,size) combinations into the atlas **on demand**, only for
  combinations actually selected — no preallocation of the full matrix.
- Make the wiring reusable so new locations are added incrementally with minimal
  per-location code.

## Non-goals (YAGNI)

- Per-face weight/style variants (bold/italic) as separate selections.
- Reference-counting / immediate reclaim of orphaned slots (handled by
  rebuild-at-boot instead).
- Force-applying TTF defaults or a bulk "apply to all" button.
- Migrating every location in this spec — only Chat and Tooltip are migrated
  here as validation; the rest follow one per increment.

## Design decisions (locked)

| Decision | Choice |
| --- | --- |
| What the profile stores per location | `(face, size)` pair; slot byte is runtime-only, never serialized |
| Face universe in the dropdown | All TTF faces + a `Classic` option (size dropdown hidden for Classic) |
| Orphaned slots on selection change | Rebuild at boot; no in-session reclaim |
| Default per location | `Classic`, mapped to that location's current hardcoded font — zero visual change, opt-in |

## Architecture

Five components.

### 1. Face catalog (decoupled from slots)

New `FontFaceCatalog` mapping a stable string `faceId` (e.g. `"inter"`,
`"cinzel"`, `"medievalsharp"`) to its embedded TTF bytes and display name. Seeds
the 7 faces currently embedded (Cinzel, Cormorant, Inter, SourceSans,
CLF-UoClassic, IM Fell English, MedievalSharp).

- `faceId` is what gets serialized into the profile — **never** a slot byte.
- Adding or removing a face does not invalidate stored profiles (an unknown
  `faceId` at load time resolves to Classic).
- Lives in `ClassicUO.Client/Resources` alongside the existing embedded-TTF
  loading (`Loader.cs`), reusing the `LoadTtf` byte cache.

### 2. Dynamic slot registry (replaces the fixed `RegisterAll`)

```
byte AtlasFontRegistry.Resolve(string faceId, byte size)
```

- Holds `Dictionary<(faceId, size), byte slot>`.
- **Dedup:** two locations both choosing "Inter 16" share one slot.
- On a miss: allocate the next free slot from the dynamic pool and call
  `FontsLoader.RegisterAtlasFont(slot, displayName, ttfBytes, size)`
  immediately, so a newly picked combination renders without a restart.
- **Dynamic pool:** slots 20…N. Bump `FontsLoader.MAX_UNICODE_FONTS` from 41 to
  **96** (dynamic slots 20–95 = 76 combinations; classic unicode/rune fonts stay
  at 0–12, with 13–19 left as buffer).
- **Exhaustion** (session-only edge, e.g. a user fiddling through many
  combinations): log a warning, fall back to Classic for that location, and
  surface a "restart to reclaim font slots" notice. Rebuild-at-boot clears
  orphaned combinations.
- **Boot:** `AtlasFontRegistry` gathers every `(faceId,size)` referenced by the
  current profile across all locations, dedups, and registers only those.
  Replaces the unconditional 21-slot `RegisterAll`.

### 3. Memory: jagged + lazy rows

`FontsLoader._fontDataUNICODE` today is `FontCharacterDataUnicode[MAX, 0x10000]`
— a single preallocation of `MAX_UNICODE_FONTS × 65536` structs
(`FontsLoader.cs:218`). Bumping `MAX` linearly enlarges this (~1.5 MB per slot,
allocated whether or not the slot is used).

Change it to a jagged `FontCharacterDataUnicode[MAX][]` where each inner
`[0x10000]` row is allocated **lazily** on the slot's first use (classic or
TTF). This:

- Removes the ~64 MB baseline preallocation.
- Makes a bumped `MAX` free until slots are actually used.
- Costs ~1.5 MB per slot only when that slot is really rasterized (~ the number
  of distinct combinations the user selected, typically well under 20).

All existing `_fontDataUNICODE[font, index]` accesses become
`_fontDataUNICODE[font][index]` with a row-ensure helper on the write paths
(`GetCharUni`, `LoadChar`/`LoadAtlasChar`).

### 4. Per-location profile choice

```csharp
struct FontChoice
{
    string Face;   // "" => Classic
    byte   Size;   // ignored when Classic
}
```

- `Face == ""` (default) → **Classic**: resolve to the location's classic font
  number + unicode flag (its current hardcoded literal). No visual change.
- Otherwise → `AtlasFontRegistry.Resolve(Face, Size)` → a TTF slot,
  `unicode = true`.
- **Migration** of the existing byte settings: `Profile.ChatFont` /
  `Profile.TooltipFont` (currently a slot byte) map forward — if the stored byte
  is a legacy fixed TTF slot (20–40), translate it to the equivalent
  `(faceId, size)`; otherwise store Classic with that classic number. Old
  profiles load unchanged in behavior.

Profiles are stored per the existing `Profile` serialization; `FontChoice`
serializes as two members (string + byte).

### 5. Location catalog + wiring API (grows incrementally)

```csharp
readonly struct LocationDef
{
    string id;              // stable key, e.g. "journal"
    string displayName;     // Options label
    byte   classicFont;     // the location's current hardcoded font number
    bool   classicUnicode;  // its current isunicode flag
}

(byte slot, bool unicode) Profile.ResolveFont(string locationId);
```

Call-site pattern (replaces a hardcoded literal):

```csharp
var (f, uni) = profile.ResolveFont(FontLocation.Journal);
RenderedText.Create(text, font: f, isunicode: uni);
```

**Options UI:** a new `FontLocationRow` control — a face dropdown (Combobox of
Classic + catalog faces) and a size dropdown (8–32), with the size dropdown
hidden/disabled when Classic is selected. Replaces the radio-list `FontSelector`
for locations that adopt the new system.

## Data flow

```
Options: location X → face dropdown + size dropdown(8..32)
  ↓ confirm
FontChoice { Face, Size } written to Profile[location X]
  ↓
AtlasFontRegistry.Resolve(Face, Size) → slot byte   (dedup / lazy RegisterAtlasFont)
  ↓ at draw time
profile.ResolveFont(X) → (slot, unicode=true)  |  Classic → (classicFont, classicUnicode)
  ↓
RenderedText.Create(text, font: slot, isunicode: unicode)
  ↓
LoadAtlasChar lazily rasterizes glyphs into the (lazily-allocated) atlas row
```

## Migration & scope of this increment

Included in this spec's implementation:

1. `FontFaceCatalog` (7 faces, faceId ↔ bytes ↔ display name).
2. `AtlasFontRegistry` rewritten to dynamic `Resolve` + dedup + lazy
   registration + boot rebuild-from-profile.
3. `FontsLoader`: bump `MAX_UNICODE_FONTS` to 96; convert `_fontDataUNICODE` to
   jagged lazy rows.
4. `Profile`: `FontChoice`, `LocationDef` catalog, `ResolveFont`; migrate
   `ChatFont` / `TooltipFont`.
5. `OptionsGump`: `FontLocationRow` control; wire Chat and Tooltip as the first
   two locations (validation of the whole path).

Deferred to later per-location increments (one each):

- Journal, overhead names, damage numbers, spellbook/combat book, party,
  system chat, and the remaining hardcoded call sites — each adds a `LocationDef`
  and replaces its hardcoded literal with `ResolveFont`.

## Error handling

- Unknown `faceId` at load (removed face) → Classic fallback, warn once.
- Corrupt/unloadable TTF → `RegisterAtlasFont` already leaves the slot unusable;
  `Resolve` returns Classic fallback for that location and warns.
- Slot-pool exhaustion → Classic fallback + restart notice (see §2).
- Size out of range (8–32) is enforced by the dropdown; `Resolve` clamps
  defensively.

## Testing

- **Registry:** dedup (same (face,size) → same slot), distinct combos → distinct
  slots, exhaustion returns fallback, boot rebuild registers only
  profile-referenced combos. (`ClassicUO.UnitTests/Assets`.)
- **Memory:** jagged lazy rows — a slot never drawn allocates no row; drawing a
  glyph allocates exactly that slot's row; classic-font paths still resolve.
- **Profile migration:** legacy `ChatFont`/`TooltipFont` bytes (both a legacy
  TTF slot and a classic number) map to the expected `FontChoice`.
- **ResolveFont:** Classic returns the location's classic (font, unicode); TTF
  returns (dynamic slot, true).
- Existing `AtlasFontRegistrationTests` / `TtfGlyphSourceTests` continue to pass
  (adjust for the dynamic registration surface).
