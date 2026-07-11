# Custom healthbar: numeric stat values

## Goal

Custom healthbars currently convey stats only as bar length. Add a centered
`current/max` number inside each bar. Because the number does not fit an 8px
bar, thicken the bar. Opt-in, so existing layouts are untouched by default.

## Scope

- Only `HealthBarGumpCustom` (line-primitive bars). Classic art-based
  `HealthBarGump` is untouched (fixed UO art cannot be thickened/overlaid
  cleanly).
- Bars that get a number:
  - HP: `entity.Hits` / `entity.HitsMax` — all custom bars (self, party, other
    mobiles).
  - Mana / Stamina: `mobile.Mana/ManaMax`, `mobile.Stamina/StaminaMax` — only
    where those bars already render (self + party).
- Format: `"{cur}/{max}"` (e.g. `84/100`).

## Toggle

New profile bool `ShowStatValuesOnBars`, default `false`
(`Configuration/Profile.cs`). Wired as a checkbox in `OptionsGump.cs` next to
the existing custom-bar checkboxes (`UseCustomHPBars` / `UseBlackBackgr`),
reusing the existing `AddCheckBox` pattern and load/save wiring. New resource
string for the label.

When `false`: bars render exactly as today (8px, y = 27/36/45 multiline,
y = 21 singleline). No labels created. Zero behavior change.

## Layout (toggle on)

Gump outer heights stay the same (`HPB_HEIGHT_MULTILINE = 60`,
`HPB_HEIGHT_SINGLELINE = 36`) — bars are made thicker in place, no gump resize,
so anchoring and saved positions are unaffected.

`BuildGump` derives bar geometry from the toggle:

| var        | toggle off | toggle on |
| ---------- | ---------- | --------- |
| bar height | 8          | 11        |
| pitch      | 9          | 12        |
| multiline top (bars) | 27 | 21 (⇒ 21 / 33 / 45, last ends 56) |
| singleline top | 21   | 21 (height 11, ends 32) |

The outline `LineCHB` is recomputed from these values. Name textbox position
and border boxes are unchanged.

## Text rendering

Reuse the `Label` control the way `StatusGump` already renders its
`Hits/HitsMax` readout: ASCII `font: 1`, `FontStyle.BlackBorder`, white hue for
contrast over any bar color. One label per visible bar: `_hpText`,
`_manaText`, `_stamText` (fields on `HealthBarGumpCustom`, created in
`BuildGump` only when the toggle is on, added last so they draw over the bars).

Centering: on each text change set
`X = HPB_BAR_SPACELEFT + (HPB_BAR_WIDTH - label.Width) / 2` and
`Y = barTop + rowPitch*i + (barHeight - label.Height) / 2`.

`cur/max` fits horizontally: worst case `9999/9999` ≈ 63px < 100px bar width, so
no horizontal clipping — the thickening is purely vertical.

## Update loop

In `HealthBarGumpCustom.Update()`, alongside the existing `LineWidth` updates,
refresh each label's `Text` only when its underlying value changed (mirror the
existing change guards to avoid per-frame `RenderedText` rebuilds). Re-center X
after a text change (width may change).

Visibility follows the matching bar:
- HP label hidden when `_bars[0].IsVisible == false` (out of range / dead).
- Mana/Stam labels hidden when not self/party or out of range, same as
  `_bars[1]/_bars[2]`.

## Cleanup / lifecycle

- Null the label fields in `UpdateContents()` (which clears children and
  rebuilds), matching how `_hpLineRed` etc. are reset.
- `Label.Dispose` destroys its `RenderedText`; children are disposed with the
  gump, so no extra disposal path is needed beyond nulling on rebuild.

## Out of scope

- Classic (art) healthbars.
- Percent / cur-only formats (chosen format is `cur/max`).
- Configurable font/size/color (fixed white ASCII font 1 + black border).
