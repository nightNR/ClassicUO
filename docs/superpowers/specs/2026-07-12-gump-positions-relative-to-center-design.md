# Design: Save gump positions relative to window center (experimental)

**Date:** 2026-07-12
**Status:** Approved design, pre-implementation
**Scope:** Single implementation plan

## Problem

Open gump/window positions are persisted as absolute screen coordinates in
`gumps.xml`. When the client window later opens on a smaller display (or is
resized smaller), gumps saved near the far edges land outside the visible client
area and become unreachable. The user wants layout to survive a shrink: positions
anchored relative to the window center so a smaller window pulls everything inward
and the same arrangement stays visible.

## Chosen model

**Center-anchor offset** with **hard on-screen clamp**.

- Each gump keeps a fixed pixel offset from the window center.
- On restore into a differently-sized window, the center moves and every gump
  moves with it by half the size delta.
- If the resulting position would push the gump past a screen edge, it is clamped
  so the gump stays fully on-screen (stuck to the edge, never crossing it).

Rejected alternatives: proportional/fractional scaling (changes spacing, not
requested) and per-gump corner/edge docking (more complex, not requested).

## Setting

Per-profile, opt-in, default off. Matches the existing experimental-toggle
convention (e.g. `CastSpellsByOneClick`).

- File: `src/ClassicUO.Client/Configuration/Profile.cs`, experimental region.
- Field:
  ```csharp
  public bool SaveGumpsRelativeToCenter { get; set; }   // default false
  ```
- Serialized automatically to `profile.json`.
- UI: a checkbox in the experimental section of
  `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs`.

When off, all behavior below is skipped and gump save/restore is byte-for-byte
identical to today.

## Save side

File: `Profile.SaveGumps` (`Profile.cs`, root `<gumps>` element, ~line 410).

Write the client size at save time onto the root element:

```csharp
xml.WriteAttributeString("save_w", Client.Game.ClientBounds.Width.ToString());
xml.WriteAttributeString("save_h", Client.Game.ClientBounds.Height.ToString());
```

- Written **unconditionally** (independent of the setting). It is cheap, harmless,
  and makes the file self-describing so a user can toggle the setting on later and
  still get correct anchoring for already-saved layouts.
- Per-gump `x`/`y` remain absolute. `Gump.Save` is **not** changed. The file
  stays backward- and forward-compatible: old clients ignore the extra root
  attributes; new clients with the feature off ignore them too.

## Load side

File: `Profile.ReadGumps` (`Profile.cs`, where `gump.X = x; gump.Y = y;` is set,
~line 695).

Applies only when **both**: the profile flag is on, **and** the root carries
valid `save_w`/`save_h`.

```csharp
// parsed once from the <gumps> root:
bool haveSaveSize = int.TryParse(root.GetAttribute("save_w"), out int saveW)
                  & int.TryParse(root.GetAttribute("save_h"), out int saveH)
                  && saveW > 0 && saveH > 0;

// per gump, before assigning X/Y:
if (ProfileManager.CurrentProfile.SaveGumpsRelativeToCenter && haveSaveSize)
{
    int cw = Client.Game.ClientBounds.Width;
    int ch = Client.Game.ClientBounds.Height;

    x += (cw - saveW) / 2;   // center-anchor offset
    y += (ch - saveH) / 2;

    // hard clamp: never cross a screen edge
    x = Math.Clamp(x, 0, Math.Max(0, cw - gump.Width));
    y = Math.Clamp(y, 0, Math.Max(0, ch - gump.Height));
}

gump.X = x;
gump.Y = y;
```

- Same window size as save → zero delta → identical to today (then clamp is a
  no-op for anything already on-screen).
- `UIManager.SavePosition(...)` continues to cache the final (post-transform) x/y.

## Decisions

- **Q1 — flag on but old file (no `save_w`):** no transform, no clamp; behave
  exactly as today. Safe fallback.
- **Q2 — clamp when the window grew:** clamp always runs when the feature is on
  (only bites if the stored offset already sat off-screen). Accepted.

## Scope / limitations (v1)

- Applies to top-level saved gumps that flow through the `gump.X/Y` restore path.
- **Anchored gump groups** (`anchored_group_gump`, handled by `AnchorManager`) are
  **excluded** — members are already positioned relative to each other. Only the
  group's own anchor could be re-centered; deferred to a later iteration.
- Clamp uses `gump.Width`/`gump.Height` (`Bounds`) as known at restore time. A few
  gumps finalize size inside `Restore()`/`UpdateContents()`; for those the clamp
  may be slightly loose, but center-anchoring is still correct. Acceptable for an
  experimental feature.
- The existing `SetInScreen()` (resets to 0,0 only when fully off-screen) is left
  untouched; this feature does its own stronger clamp inline so unrelated call
  sites keep their current behavior.

## Testing

- Unit test around the offset+clamp math (pure arithmetic): saved size vs current
  size → expected x/y, including clamp at each edge and the zero-delta identity
  case. Extract the transform into a small static helper to make it testable
  without a live window, e.g.
  `GumpPositionHelper.CenterAnchor(x, y, saveW, saveH, curW, curH, gumpW, gumpH)`.
- Manual: enable setting, save layout at large window, relaunch at smaller
  ClientBounds, confirm gumps pull inward and none sit off-screen; toggle off →
  absolute behavior returns.

## Files touched

- `src/ClassicUO.Client/Configuration/Profile.cs` — new field; `save_w/save_h`
  write in `SaveGumps`; transform+clamp in `ReadGumps`.
- `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` — experimental checkbox.
- New: small static helper for the transform (location TBD in plan; likely near
  `Gump` or a UI util) + its unit test in `tests/ClassicUO.UnitTests`.
