# Status Bar Colors — Vivid Rendering Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make the user's status-bar rule color render vividly (a semi-transparent solid fill + colored border) instead of the washed-out dark-base tint, with a global opacity slider.

**Architecture:** A new `StatusbarColorFill : Control` draws a white-base solid rect recolored by the rule hue (the vivid path used by `ClickableColorBox`/`LineCHB`) at a global opacity, plus a 4-edge border at full alpha. Each health-bar variant adds it right after `_background` (draws above the bg, below the HP bars). A `BaseHealthBarGump.UpdateStatusColorFill(mobile)` helper toggles it per-frame from `StatusbarColorManager.TryGetColor`. Opacity is `Profile.StatusbarColorOpacity` (0-100), set by an `HSliderBar` in the options tab.

**Tech Stack:** C#/.NET 10, FNA UI, `ShaderHueTranslator`/`SolidColorTextureCache` rendering.

## Global Constraints

- New source files carry `// SPDX-License-Identifier: BSD-2-Clause`.
- Build: `dotnet build ClassicUO.sln -c Debug` (0 NEW warnings; 4 pre-existing HighlightTests warnings are fine).
- Health-bar rendering is not unit-testable without a live scene — Tasks are build-verified + deferred in-game visual check.
- Do not recolor HP fill or name text. Do not remove the existing `barColor → _background.Hue` override.
- The fill must draw BEHIND the HP bars (added after `_background`, before the bar controls); the local player's own bar is excluded (`mobile != World.Player`), consistent with the base feature.

## File Structure

- Create: `src/ClassicUO.Client/Game/UI/Controls/StatusbarColorFill.cs`.
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs` — `StatusbarColorOpacity` int.
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` — opacity slider in the Status Bar Colors tab.
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs` — overlay field + Adds + `UpdateStatusColorFill` helper + calls from both `Update` methods.

---

### Task 1: Global opacity setting + slider

**Files:**
- Modify: `Profile.cs` (add near `TooltipBackgroundOpacity` ~line 145)
- Modify: `OptionsGump.cs` (the `BuildStatusbarColors()` method + a field for the slider)

**Interfaces produced:** `Profile.StatusbarColorOpacity` (int, default 80).

- [ ] **Step 1: Add profile field**

In `Profile.cs`, near `public int TooltipBackgroundOpacity { get; set; } = 70;`:

```csharp
        public int StatusbarColorOpacity { get; set; } = 80;
```

- [ ] **Step 2: Add the slider to the Status Bar Colors tab**

In `OptionsGump.cs`, in `BuildStatusbarColors()`, after the master `enabledBox` is added and before the databox/add-buttons, add a labeled slider (mirror the `AddLabel`+`AddHSlider` pattern already used elsewhere in this gump). Use a field so it can be referenced:

Add a field near the other OptionsGump slider fields (e.g. next to `_tooltip_background_opacity`):

```csharp
        private HSliderBar _statusbar_color_opacity;
```

In `BuildStatusbarColors()` (place it just under the enable checkbox, adjusting `startX`/`startY` locals or explicit coords so it doesn't overlap the Add buttons at y=35 — put the slider at y≈35 and shift the Add buttons down to y≈60, or place the slider on its own row above the buttons):

```csharp
            Label opacityLabel = new Label("Color opacity", true, HUE_FONT, font: FONT) { X = 5, Y = 38 };
            rightArea.Add(opacityLabel);

            _statusbar_color_opacity = new HSliderBar
            (
                opacityLabel.X + opacityLabel.Width + 8,
                38,
                150,
                0,
                100,
                _currentProfile != null ? _currentProfile.StatusbarColorOpacity : 80,
                HSliderBarStyle.MetalWidgetRecessedBar,
                true,
                FONT,
                HUE_FONT
            );
            _statusbar_color_opacity.ValueChanged += (s, e) =>
            {
                if (_currentProfile != null)
                    _currentProfile.StatusbarColorOpacity = _statusbar_color_opacity.Value;
            };
            rightArea.Add(_statusbar_color_opacity);
```

Then move the existing `addTarget`/`addManual` buttons (currently at `y = 35`) down to `y = 65` so they sit below the slider, and the `databox` at `y = 70` → `y = 95` (adjust the `DataBox` ctor Y and keep rows relative). Verify the exact current Y values in `BuildStatusbarColors` and shift consistently so nothing overlaps.

Note: confirm the `HSliderBar` ctor signature and `HSliderBarStyle` enum against `Game/UI/Controls/HSliderBar.cs` (ctor is `(x, y, w, min, max, value, HSliderBarStyle style, bool hasText, byte font, ushort color, ...)`; `.Value` getter + `ValueChanged` event). `AddLabel`/`AddHSlider` helpers exist in OptionsGump (~4955) and may be used instead of the raw ctor if they fit within the ScrollArea layout — either is fine.

- [ ] **Step 3: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: succeeds, no new warnings.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Configuration/Profile.cs src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(statusbar-colors): global color opacity slider"
```

---

### Task 2: Vivid fill+border overlay

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/StatusbarColorFill.cs`
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs`

**Interfaces:**
- Consumes: `Profile.StatusbarColorOpacity` (Task 1), `World.StatusbarColorManager.TryGetColor`.
- Produces: `StatusbarColorFill { ushort ColorHue; float FillAlpha; int BorderSize }`; `BaseHealthBarGump._statusColorFill`; `BaseHealthBarGump.UpdateStatusColorFill(Mobile)`.

- [ ] **Step 1: Create the overlay control**

Create `src/ClassicUO.Client/Game/UI/Controls/StatusbarColorFill.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Controls
{
    internal sealed class StatusbarColorFill : Control
    {
        public StatusbarColorFill()
        {
            AcceptMouseInput = false;
        }

        public ushort ColorHue { get; set; }
        public float FillAlpha { get; set; } = 0.8f;
        public int BorderSize { get; set; } = 1;

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float layerDepth = layerDepthRef;

            int w = Width, h = Height, b = BorderSize;
            ushort hue = ColorHue;
            float fillAlpha = FillAlpha;

            renderLists.AddGumpNoAtlas(
                batcher =>
                {
                    var tex = SolidColorTextureCache.GetTexture(Color.White);
                    Vector3 fill = ShaderHueTranslator.GetHueVector(hue, false, fillAlpha);
                    Vector3 border = ShaderHueTranslator.GetHueVector(hue, false, 1f);

                    batcher.Draw(tex, new Rectangle(x, y, w, h), fill, layerDepth);              // fill
                    batcher.Draw(tex, new Rectangle(x, y, w, b), border, layerDepth);            // top
                    batcher.Draw(tex, new Rectangle(x, y + h - b, w, b), border, layerDepth);    // bottom
                    batcher.Draw(tex, new Rectangle(x, y, b, h), border, layerDepth);            // left
                    batcher.Draw(tex, new Rectangle(x + w - b, y, b, h), border, layerDepth);    // right
                    return true;
                }
            );

            return true;
        }
    }
}
```

Note: verify `batcher.Draw(Texture2D, Rectangle, Vector3, float)` matches the signature used in `AlphaBlendControl.AddToRenderLists`/`LineCHB.AddToRenderLists` (it does — same overload). `GetHueVector(int hue, bool partial, float alpha)` recolors the White texture to the UO hue at `alpha`.

- [ ] **Step 2: Add the overlay field + helper to `BaseHealthBarGump`**

In `HealthBarGump.cs`, in `BaseHealthBarGump`, add a field near `_background`:

```csharp
        protected StatusbarColorFill _statusColorFill;
```

Add the helper (anywhere in `BaseHealthBarGump`):

```csharp
        protected void UpdateStatusColorFill(Mobile mobile)
        {
            if (_statusColorFill == null)
                return;

            if (mobile != null && mobile != World.Player &&
                World.StatusbarColorManager.TryGetColor(mobile.Graphic, mobile.Hue, out ushort fillHue))
            {
                _statusColorFill.Width = Width;
                _statusColorFill.Height = Height;
                _statusColorFill.ColorHue = fillHue;
                _statusColorFill.FillAlpha = (ProfileManager.CurrentProfile != null ? ProfileManager.CurrentProfile.StatusbarColorOpacity : 80) / 100f;
                _statusColorFill.IsVisible = true;
            }
            else
            {
                _statusColorFill.IsVisible = false;
            }
        }
```

Confirm `ProfileManager` is imported in the file (it is — used elsewhere in HealthBarGump). `Width`/`Height` are the gump's own (already set to the bar size in both variants).

- [ ] **Step 3: Add the overlay control right after each `_background` Add**

Find every site that does `Add(_background = ...)` in `HealthBarGump.cs` (custom: ~835, ~1032, ~1208; classic: party ~1543, non-party ~1724, and the war-mode background construction if separate). Immediately after each such `Add(_background = ...)` line, insert:

```csharp
            Add(_statusColorFill = new StatusbarColorFill { Width = Width, Height = Height, IsVisible = false });
```

This guarantees the overlay is a child added AFTER `_background` but BEFORE the HP-bar controls, so it renders above the background and below the bars. (Use the same indentation as the surrounding `Add(...)` calls.) For the classic non-party case where `Width`/`Height` are set from the art a few lines later, the `Update` helper re-syncs `Width`/`Height` each frame, so an initial 0 size self-corrects on the first tick.

- [ ] **Step 4: Call the helper from both `Update` methods**

In the custom variant `Update()` (~line 397) and the classic variant `Update()` (~line 1782), after the block that computes `barColor` and sets `_background.Hue`, add:

```csharp
                UpdateStatusColorFill(mobile);
```

Confirm `mobile` is the in-scope `Mobile` local at each call site (it is — `entity as Mobile`). If a variant's `Update` early-returns before this point when `entity`/`mobile` is null, place the call on a path where `mobile` is defined (calling with a null mobile is safe — the helper hides the fill).

- [ ] **Step 5: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: succeeds, no new warnings.

- [ ] **Step 6: Manual verification (deferred to in-game)**

Add a rule with a bright color for a known NPC; open its health bar (both `CustomBarsToggled` on and off): the bar shows a strong colored fill behind the HP bars + a colored border; the HP bar and name remain readable; the opacity slider visibly changes fill strength; a non-matching NPC and the local player's own bar are unaffected; disabling the master toggle removes the fill.

- [ ] **Step 7: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/StatusbarColorFill.cs src/ClassicUO.Client/Game/UI/Gumps/HealthBarGump.cs
git commit -m "feat(statusbar-colors): vivid fill+border overlay driven by opacity"
```

---

## Self-Review

- **Spec coverage:** vivid white-base fill (Task 2 control) ✓; colored border (Task 2 control 4-edge draw) ✓; global opacity slider + profile field (Task 1) ✓; overlay above bg / below bars via post-`_background` Add (Task 2 Step 3) ✓; per-frame toggle + player-exclusion + gump-sized (Task 2 helper) ✓; both variants (Task 2 Step 4) ✓; existing `_background.Hue` override left intact ✓; no HP-fill/name recolor ✓.
- **Type consistency:** `StatusbarColorFill { ColorHue: ushort; FillAlpha: float; BorderSize: int }`; `_statusColorFill`; `UpdateStatusColorFill(Mobile)`; `Profile.StatusbarColorOpacity: int`; `Profile.StatusbarColorOpacity / 100f → FillAlpha` consistent between Tasks.
- **Confirm-against-source (flagged inline):** `HSliderBar` ctor + `HSliderBarStyle` + `.Value`/`ValueChanged`; `batcher.Draw(Texture2D, Rectangle, Vector3, float)` overload; every `Add(_background = ...)` site (find all, incl. war-mode); `mobile` in scope at both `Update` call points; `BuildStatusbarColors` Y-layout shift so slider/buttons/list don't overlap.
