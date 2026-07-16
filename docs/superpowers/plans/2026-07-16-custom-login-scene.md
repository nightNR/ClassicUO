# Custom Login Scene (1280×720) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a compile-time-gated custom 1280×720 login scene (fullscreen PNG background, 9-slice form, PNG multi-state buttons + inputs, TrueType text) matching the "Dark Paradise" mockup, with a full-login state and an autologin reconnect state.

**Architecture:** A new compile constant `CUSTOM_LOGIN_SCENE` selects the custom scene at build time. TTF text renders through FontStashSharp behind an `ILoginFont` seam (bitmap-atlas fallback if FontStashSharp fails NativeAOT). PNG/TTF assets embed via the existing FileEmbed generator. New drawing-only controls (`TextureImage`, `NineSliceFrame`, `ImageButton`, `TtfLabel`, `TtfTextBox`) compose into `CustomLoginGump`, whose state (full form vs reconnect) is chosen by the shared pure `LoginReconnectPolicy`.

**Tech Stack:** C# `net10.0`, FNA-XNA, `UltimaBatcher2D`, FontStashSharp.FNA (new NuGet), FileEmbed source generator, xUnit. NativeAOT for the default `cuo` artifact.

## Global Constraints

- Target framework `net10.0`, `LangVersion=Preview`, `AllowUnsafeBlocks=true`. Default `cuo` artifact is NativeAOT (`PublishAot=true`, `ClassicUO.Client.csproj:8`).
- New source files: BSD-2 header `// SPDX-License-Identifier: BSD-2-Clause` as line 1.
- All new login-scene code gated behind `#if CUSTOM_LOGIN_SCENE`; the default build must produce the original UO login unchanged.
- Custom scene resolution: `1280×720`, always wrapped in `Client.Game.ScaleWithDpi(...)` (DPI-correct).
- Typography: Account/Password text + labels = Cormorant Garamond SemiBold, ~18–22 px, color `#D3C2A1`, ~90% opacity. Button labels = Cinzel SemiBold, UPPERCASE, letter-spacing 2–4 px, 24–30 px.
- Two states via `LoginReconnectPolicy.UseReconnectGump(AutoLogin, Username, Password, ForceFullLogin)`: State A = full form; State B = centered priority-red RECONNECT + QUIT, no inputs.
- Fatal-auth codes (`0x82` codes 0/2/3) route to State A with error, per `LoginReconnectPolicy.IsFatalAuthRejection`. Non-fatal (1/4–8) stays State B.
- Custom controls draw via `renderLists.AddGumpNoAtlas(batcher => { batcher.Draw(...); return true; })` with `ShaderHueTranslator.GetHueVector(0, false, Alpha)` (hue 0 = unmodified). Pattern reference: `Line.cs:24-49`.
- Exact on-screen coordinates are derived from the mockup during implementation (mockup 1672×941, scale ×0.766). Where this plan places controls, coordinates are `// layout: placeholder` and refined in Task 9's visual pass.

---

## File Structure

- Modify: `src/ClassicUO.Client/ClassicUO.Client.csproj` — `CustomLoginScene` property → `CUSTOM_LOGIN_SCENE` constant; FontStashSharp PackageReference.
- Create: `src/ClassicUO.Renderer/Fonts/ILoginFont.cs` — font-backend seam.
- Create: `src/ClassicUO.Renderer/Fonts/FontStashLoginFont.cs` — FontStashSharp backend + batcher renderer bridge.
- Create: `tests/ClassicUO.UnitTests/Renderer/LoginFontMeasureTests.cs` — measurement logic test (backend-agnostic surface).
- Modify: `src/ClassicUO.Client/Resources/Loader.cs` — FileEmbed entries for PNGs + TTFs.
- Create: `src/ClassicUO.Client/Game/UI/Login/LoginAssets.cs` — lazy Texture2D + font access.
- Create: `src/ClassicUO.Client/Game/UI/Gumps/Login/LoginReconnectPolicy.cs` — pure decision logic (shared with reconnect plan; create here if not already present).
- Create: `tests/ClassicUO.UnitTests/Game/UI/Gumps/LoginReconnectPolicyTests.cs` — policy tests (shared).
- Create: `src/ClassicUO.Client/Game/UI/Controls/TextureImage.cs`
- Create: `src/ClassicUO.Client/Game/UI/Controls/NineSliceFrame.cs`
- Create: `src/ClassicUO.Client/Game/UI/Controls/ImageButton.cs`
- Create: `src/ClassicUO.Client/Game/UI/Controls/TtfLabel.cs`
- Create: `src/ClassicUO.Client/Game/UI/Controls/TtfTextBox.cs`
- Create: `src/ClassicUO.Client/Game/UI/Gumps/Login/CustomLoginGump.cs`
- Modify: `src/ClassicUO.Client/Game/Scenes/LoginScene.cs` — `#if CUSTOM_LOGIN_SCENE` window size + gump wiring; `ForceFullLogin` + `HandleErrorCode` (shared with reconnect plan).
- Modify: `build-cuo.sh` / `build-cuo.ps1` — optional `CustomLoginScene` pass-through (Task 10).

---

## Task 1: Compile flag + FontStashSharp AOT spike + ILoginFont

This task de-risks the whole plan: it proves TTF text can render through `UltimaBatcher2D` AND survive a NativeAOT publish. If FontStashSharp fails AOT, STOP and switch to the bitmap-atlas fallback (see "AOT Fallback" note at end of task) before continuing.

**Files:**
- Modify: `src/ClassicUO.Client/ClassicUO.Client.csproj`
- Create: `src/ClassicUO.Renderer/Fonts/ILoginFont.cs`
- Create: `src/ClassicUO.Renderer/Fonts/FontStashLoginFont.cs`
- Test: `tests/ClassicUO.UnitTests/Renderer/LoginFontMeasureTests.cs`

**Interfaces:**
- Consumes: `UltimaBatcher2D.Draw(Texture2D, Vector2, Rectangle, Vector3, float)` (`Batcher2D.cs:597`); `ShaderHueTranslator.GetHueVector`.
- Produces:
  - `interface ILoginFont { Vector2 Measure(ReadOnlySpan<char> text, float size, float letterSpacing = 0); void Draw(UltimaBatcher2D batcher, ReadOnlySpan<char> text, float x, float y, float size, Color color, float opacity = 1f, float letterSpacing = 0); }`
  - `sealed class FontStashLoginFont : ILoginFont` with `FontStashLoginFont(GraphicsDevice device, byte[] ttfBytes)`.

- [ ] **Step 1: Add the compile flag to csproj**

In `src/ClassicUO.Client/ClassicUO.Client.csproj`, after the existing `Debug` PropertyGroup (line 56), add:

```xml
  <!--
    CustomLoginScene compiles the custom 1280x720 login scene instead of the
    default UO login. Usage: dotnet build ... -p:CustomLoginScene=true
  -->
  <PropertyGroup Condition="'$(CustomLoginScene)' == 'true'">
    <DefineConstants>$(DefineConstants);CUSTOM_LOGIN_SCENE</DefineConstants>
  </PropertyGroup>
```

- [ ] **Step 2: Add FontStashSharp package**

In `ClassicUO.Renderer.csproj`, add inside an `<ItemGroup>`:

```xml
    <PackageReference Include="FontStashSharp.FNA" Version="1.3.9" />
```

(Verify the latest 1.3.x on nuget.org; FontStashSharp.FNA references the FNA project. If version resolution fails against the vendored `external/FNA`, use the source package `FontStashSharp.Base` + `StbTrueTypeSharp` and the FNA renderer glue is what `FontStashLoginFont` implements anyway.)

Run: `dotnet restore ClassicUO.sln`
Expected: restore succeeds, FontStashSharp resolved.

- [ ] **Step 3: Write the ILoginFont seam**

Create `src/ClassicUO.Renderer/Fonts/ILoginFont.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using Microsoft.Xna.Framework;

namespace ClassicUO.Renderer.Fonts
{
    public interface ILoginFont
    {
        Vector2 Measure(ReadOnlySpan<char> text, float size, float letterSpacing = 0f);

        void Draw(
            UltimaBatcher2D batcher,
            ReadOnlySpan<char> text,
            float x,
            float y,
            float size,
            Color color,
            float opacity = 1f,
            float letterSpacing = 0f);
    }
}
```

- [ ] **Step 4: Write the FontStashSharp backend + bridge**

Create `src/ClassicUO.Renderer/Fonts/FontStashLoginFont.cs`. It wraps a `FontSystem`, and implements FontStashSharp's `IFontStashRenderer` by forwarding each glyph quad to `batcher.Draw`. `ITexture2DManager` is backed by FNA `Texture2D` create + `SetData`.

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using FontStashSharp;
using FontStashSharp.Interfaces;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace ClassicUO.Renderer.Fonts
{
    public sealed class FontStashLoginFont : ILoginFont
    {
        private readonly FontSystem _fontSystem;
        private readonly BatcherFontRenderer _renderer;

        public FontStashLoginFont(GraphicsDevice device, byte[] ttfBytes)
        {
            _fontSystem = new FontSystem();
            _fontSystem.AddFont(ttfBytes);
            _renderer = new BatcherFontRenderer(device);
        }

        public Vector2 Measure(ReadOnlySpan<char> text, float size, float letterSpacing = 0f)
        {
            var font = _fontSystem.GetFont(size);
            return font.MeasureString(text.ToString(), characterSpacing: letterSpacing);
        }

        public void Draw(
            UltimaBatcher2D batcher,
            ReadOnlySpan<char> text,
            float x,
            float y,
            float size,
            XnaColor color,
            float opacity = 1f,
            float letterSpacing = 0f)
        {
            var font = _fontSystem.GetFont(size);
            _renderer.Begin(batcher);
            font.DrawText(
                _renderer,
                text.ToString(),
                new Vector2(x, y),
                color * opacity,
                characterSpacing: letterSpacing);
        }

        // Bridges FontStashSharp glyph quads to UltimaBatcher2D + FNA textures.
        private sealed class BatcherFontRenderer : IFontStashRenderer, ITexture2DManager
        {
            private readonly GraphicsDevice _device;
            private UltimaBatcher2D _batcher;

            public BatcherFontRenderer(GraphicsDevice device) => _device = device;

            public ITexture2DManager TextureManager => this;

            public void Begin(UltimaBatcher2D batcher) => _batcher = batcher;

            // ITexture2DManager
            public object CreateTexture(int width, int height)
                => new Texture2D(_device, width, height);

            public System.Drawing.Point GetTextureSize(object texture)
            {
                var t = (Texture2D)texture;
                return new System.Drawing.Point(t.Width, t.Height);
            }

            public void SetTextureData(object texture, System.Drawing.Rectangle bounds, byte[] data)
            {
                var t = (Texture2D)texture;
                t.SetData(0, new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    data, 0, data.Length);
            }

            // IFontStashRenderer
            public void Draw(object texture, Vector2 pos, System.Drawing.Rectangle? src,
                XnaColor color, float rotation, Vector2 scale, float depth)
            {
                var t = (Texture2D)texture;
                Rectangle source = src.HasValue
                    ? new Rectangle(src.Value.X, src.Value.Y, src.Value.Width, src.Value.Height)
                    : new Rectangle(0, 0, t.Width, t.Height);

                Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, color.A / 255f, true);
                // Per-glyph draw; scale is baked into FontStashSharp's source size selection.
                _batcher.Draw(t, pos, source, hue, depth);
            }
        }
    }
}
```

> **Note:** FontStashSharp interface member names/signatures vary slightly by version (`IFontStashRenderer` vs `IFontStashRenderer2`, `DrawText` overloads, `SetTextureData` byte[] vs IntPtr). Adjust the implementing members to the resolved package's actual interfaces — the compiler will name any mismatch. The design (wrap `FontSystem`, forward glyph draws to `batcher.Draw`, back textures with FNA `Texture2D`) is stable.

- [ ] **Step 5: Write the measurement test**

Create `tests/ClassicUO.UnitTests/Renderer/LoginFontMeasureTests.cs`. This exercises only the pure measurement contract; it uses a stub `ILoginFont` to lock the interface shape (real FontStashSharp measurement needs a GraphicsDevice, unavailable in unit tests):

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Renderer.Fonts;
using Microsoft.Xna.Framework;
using Xunit;

namespace ClassicUO.UnitTests.Renderer
{
    public class LoginFontMeasureTests
    {
        private sealed class FixedFont : ILoginFont
        {
            public Vector2 Measure(ReadOnlySpan<char> text, float size, float letterSpacing = 0f)
                => new Vector2(text.Length * size * 0.5f + Math.Max(0, text.Length - 1) * letterSpacing, size);

            public void Draw(UltimaBatcher2D batcher, ReadOnlySpan<char> text, float x, float y,
                float size, Color color, float opacity = 1f, float letterSpacing = 0f) { }
        }

        [Fact]
        public void Measure_ScalesWithLengthAndSpacing()
        {
            ILoginFont f = new FixedFont();
            var a = f.Measure("AB", 20f);
            var b = f.Measure("AB", 20f, letterSpacing: 4f);
            Assert.True(b.X > a.X);
            Assert.Equal(20f, a.Y);
        }
    }
}
```

- [ ] **Step 6: Build + run test (JIT)**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: succeeds.
Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~LoginFontMeasureTests"`
Expected: PASS.

- [ ] **Step 7: NativeAOT publish smoke (the critical gate)**

Run: `dotnet publish src/ClassicUO.Client -c Release -r win-x64 -p:CustomLoginScene=true`
Expected: AOT publish completes WITHOUT trim/AOT errors referencing FontStashSharp reflection. Warnings about generic instantiation are acceptable if publish still succeeds and produces `cuo.exe`.

If publish FAILS due to FontStashSharp AOT incompatibility:
- STOP. Do not continue to Task 2.
- Switch backend: implement `BakedAtlasLoginFont : ILoginFont` instead, fed by a build-time-generated glyph atlas (texture PNG + JSON metrics) embedded via FileEmbed. A small offline tool (e.g. a `tools/FontBake` console using `System.Drawing`/`ImageSharp` or `StbTrueTypeSharp` at build time) renders Cinzel-SemiBold and CormorantGaramond-SemiBold at sizes {18,20,22,24,26,28,30} into atlases. `ILoginFont` call sites (Tasks 4–9) are unchanged.
- Record the pivot in the spec's "Open implementation risks" section and re-plan Task 1's font tasks accordingly.

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/ClassicUO.Client.csproj src/ClassicUO.Renderer/ClassicUO.Renderer.csproj src/ClassicUO.Renderer/Fonts/ tests/ClassicUO.UnitTests/Renderer/LoginFontMeasureTests.cs
git commit -m "feat(login): add CustomLoginScene flag + FontStashSharp login font backend"
```

---

## Task 2: Embed assets + LoginAssets

**Files:**
- Modify: `src/ClassicUO.Client/Resources/Loader.cs`
- Create: `src/ClassicUO.Client/Game/UI/Login/LoginAssets.cs`

**Interfaces:**
- Consumes: `Texture2D.FromStream`; `Loader` FileEmbed methods; `FontStashLoginFont` (Task 1).
- Produces:
  - `Loader` partial methods: `GetLoginBackground()`, `GetButtonNeutral/Hover/Down()`, `GetButtonPrioNeutral/Hover/MDown()`, `GetTextInputNeutral/Focus/Disabled()`, `GetFrameCornerTL/TR/BL/BR()`, `GetFrameSideL/R()`, `GetFrameTopSlice/BottomSlice()`, `GetFrameTopOrn/BottomOrn()`, `GetFrameCenter()`, `GetCinzelSemiBold()`, `GetCormorantSemiBold()` — each `ReadOnlySpan<byte>`.
  - `static class LoginAssets` exposing lazy `Texture2D` properties for each PNG and `ILoginFont Cinzel`, `ILoginFont Cormorant`, plus `void Reset()` to dispose (on scene unload).

- [ ] **Step 1: Add FileEmbed entries**

In `src/ClassicUO.Client/Resources/Loader.cs`, add inside the `Loader` class (paths relative to the Client project dir → `..\..\Assets\...`). Wrap in `#if CUSTOM_LOGIN_SCENE` so the default build embeds nothing:

```csharp
#if CUSTOM_LOGIN_SCENE
        [FileEmbed.FileEmbed("..\\..\\Assets\\background-image.png")]
        public static partial ReadOnlySpan<byte> GetLoginBackground();

        [FileEmbed.FileEmbed("..\\..\\Assets\\button-neutral.png")]
        public static partial ReadOnlySpan<byte> GetButtonNeutral();
        [FileEmbed.FileEmbed("..\\..\\Assets\\button-hover.png")]
        public static partial ReadOnlySpan<byte> GetButtonHover();
        [FileEmbed.FileEmbed("..\\..\\Assets\\button-down.png")]
        public static partial ReadOnlySpan<byte> GetButtonDown();

        [FileEmbed.FileEmbed("..\\..\\Assets\\button-prio-neutral.png")]
        public static partial ReadOnlySpan<byte> GetButtonPrioNeutral();
        [FileEmbed.FileEmbed("..\\..\\Assets\\button-prio-hover.png")]
        public static partial ReadOnlySpan<byte> GetButtonPrioHover();
        [FileEmbed.FileEmbed("..\\..\\Assets\\button-prio-mdown.png")]
        public static partial ReadOnlySpan<byte> GetButtonPrioMDown();

        [FileEmbed.FileEmbed("..\\..\\Assets\\text-input-neutral.png")]
        public static partial ReadOnlySpan<byte> GetTextInputNeutral();
        [FileEmbed.FileEmbed("..\\..\\Assets\\text-input-focus.png")]
        public static partial ReadOnlySpan<byte> GetTextInputFocus();
        [FileEmbed.FileEmbed("..\\..\\Assets\\text-input-disabled.png")]
        public static partial ReadOnlySpan<byte> GetTextInputDisabled();

        [FileEmbed.FileEmbed("..\\..\\Assets\\form-border\\frame_corner_top_left.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerTL();
        [FileEmbed.FileEmbed("..\\..\\Assets\\form-border\\frame_corner_top_right.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerTR();
        [FileEmbed.FileEmbed("..\\..\\Assets\\form-border\\frame_corner_bottom_left.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerBL();
        [FileEmbed.FileEmbed("..\\..\\Assets\\form-border\\frame_corner_bottom_right.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerBR();
        [FileEmbed.FileEmbed("..\\..\\Assets\\form-border\\frame_side_left_repeat.png")]
        public static partial ReadOnlySpan<byte> GetFrameSideL();
        [FileEmbed.FileEmbed("..\\..\\Assets\\form-border\\frame_side_right_repeat.png")]
        public static partial ReadOnlySpan<byte> GetFrameSideR();
        [FileEmbed.FileEmbed("..\\..\\Assets\\form-border\\frame_top_repeat_slice.png")]
        public static partial ReadOnlySpan<byte> GetFrameTopSlice();
        [FileEmbed.FileEmbed("..\\..\\Assets\\form-border\\frame_bottom_repeat_slice.png")]
        public static partial ReadOnlySpan<byte> GetFrameBottomSlice();
        [FileEmbed.FileEmbed("..\\..\\Assets\\form-border\\frame_center_stretch.png")]
        public static partial ReadOnlySpan<byte> GetFrameCenter();

        [FileEmbed.FileEmbed("..\\..\\Assets\\fonts\\Cinzel\\static\\Cinzel-SemiBold.ttf")]
        public static partial ReadOnlySpan<byte> GetCinzelSemiBold();
        [FileEmbed.FileEmbed("..\\..\\Assets\\fonts\\Cormorant_Garamond\\static\\CormorantGaramond-SemiBold.ttf")]
        public static partial ReadOnlySpan<byte> GetCormorantSemiBold();
#endif
```

(The top/bottom center ornaments are optional decoration; add `GetFrameTopOrn()`/`GetFrameBottomOrn()` the same way if Task 5/9 uses them.)

- [ ] **Step 2: Build to verify FileEmbed resolves the paths**

Run: `dotnet build src/ClassicUO.Client -c Debug -p:CustomLoginScene=true`
Expected: succeeds; FileEmbed generates the partial method bodies (fails loudly if any asset path is wrong).

- [ ] **Step 3: Write LoginAssets**

Create `src/ClassicUO.Client/Game/UI/Login/LoginAssets.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using System.IO;
using ClassicUO.Renderer.Fonts;
using ClassicUO.Resources;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Login
{
    internal static class LoginAssets
    {
        private static GraphicsDevice Device => Client.Game.GraphicsDevice;

        private static Texture2D _background, _btnNeutral, _btnHover, _btnDown,
            _btnPrioNeutral, _btnPrioHover, _btnPrioDown,
            _inputNeutral, _inputFocus, _inputDisabled,
            _cornerTL, _cornerTR, _cornerBL, _cornerBR,
            _sideL, _sideR, _topSlice, _bottomSlice, _center;

        private static ILoginFont _cinzel, _cormorant;

        private static Texture2D Load(ref Texture2D slot, System.ReadOnlySpan<byte> bytes)
        {
            if (slot == null)
            {
                using var ms = new MemoryStream(bytes.ToArray());
                slot = Texture2D.FromStream(Device, ms);
            }
            return slot;
        }

        public static Texture2D Background => Load(ref _background, Loader.GetLoginBackground());
        public static Texture2D ButtonNeutral => Load(ref _btnNeutral, Loader.GetButtonNeutral());
        public static Texture2D ButtonHover => Load(ref _btnHover, Loader.GetButtonHover());
        public static Texture2D ButtonDown => Load(ref _btnDown, Loader.GetButtonDown());
        public static Texture2D ButtonPrioNeutral => Load(ref _btnPrioNeutral, Loader.GetButtonPrioNeutral());
        public static Texture2D ButtonPrioHover => Load(ref _btnPrioHover, Loader.GetButtonPrioHover());
        public static Texture2D ButtonPrioDown => Load(ref _btnPrioDown, Loader.GetButtonPrioMDown());
        public static Texture2D InputNeutral => Load(ref _inputNeutral, Loader.GetTextInputNeutral());
        public static Texture2D InputFocus => Load(ref _inputFocus, Loader.GetTextInputFocus());
        public static Texture2D InputDisabled => Load(ref _inputDisabled, Loader.GetTextInputDisabled());
        public static Texture2D CornerTL => Load(ref _cornerTL, Loader.GetFrameCornerTL());
        public static Texture2D CornerTR => Load(ref _cornerTR, Loader.GetFrameCornerTR());
        public static Texture2D CornerBL => Load(ref _cornerBL, Loader.GetFrameCornerBL());
        public static Texture2D CornerBR => Load(ref _cornerBR, Loader.GetFrameCornerBR());
        public static Texture2D SideL => Load(ref _sideL, Loader.GetFrameSideL());
        public static Texture2D SideR => Load(ref _sideR, Loader.GetFrameSideR());
        public static Texture2D TopSlice => Load(ref _topSlice, Loader.GetFrameTopSlice());
        public static Texture2D BottomSlice => Load(ref _bottomSlice, Loader.GetFrameBottomSlice());
        public static Texture2D Center => Load(ref _center, Loader.GetFrameCenter());

        public static ILoginFont Cinzel =>
            _cinzel ??= new FontStashLoginFont(Device, Loader.GetCinzelSemiBold().ToArray());
        public static ILoginFont Cormorant =>
            _cormorant ??= new FontStashLoginFont(Device, Loader.GetCormorantSemiBold().ToArray());
    }
}
#endif
```

(If the AOT fallback baked-atlas backend was chosen in Task 1, swap the two font property bodies to construct `BakedAtlasLoginFont`.)

- [ ] **Step 4: Build**

Run: `dotnet build src/ClassicUO.Client -c Debug -p:CustomLoginScene=true`
Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Resources/Loader.cs src/ClassicUO.Client/Game/UI/Login/LoginAssets.cs
git commit -m "feat(login): embed custom login assets + LoginAssets accessor"
```

---

## Task 3: LoginReconnectPolicy (shared pure logic)

If the reconnect plan (`2026-07-16-autologin-reconnect-gump.md` Task 1) has already landed this file, SKIP creation and just confirm it exists; otherwise implement it here identically.

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Gumps/Login/LoginReconnectPolicy.cs`
- Test: `tests/ClassicUO.UnitTests/Game/UI/Gumps/LoginReconnectPolicyTests.cs`

**Interfaces:**
- Produces: `static bool LoginReconnectPolicy.UseReconnectGump(bool autoLogin, string username, string password, bool forceFullLogin)`; `static bool LoginReconnectPolicy.IsFatalAuthRejection(byte packetID, byte code)`.

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/UI/Gumps/LoginReconnectPolicyTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps.Login;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI.Gumps
{
    public class LoginReconnectPolicyTests
    {
        [Theory]
        [InlineData(true, "user", "pass", false, true)]
        [InlineData(false, "user", "pass", false, false)]
        [InlineData(true, "", "pass", false, false)]
        [InlineData(true, "user", "", false, false)]
        [InlineData(true, null, "pass", false, false)]
        [InlineData(true, "user", null, false, false)]
        [InlineData(true, "user", "pass", true, false)]
        public void UseReconnectGump_TruthTable(bool autoLogin, string user, string pass, bool force, bool expected)
        {
            Assert.Equal(expected, LoginReconnectPolicy.UseReconnectGump(autoLogin, user, pass, force));
        }

        [Theory]
        [InlineData(0x82, 0, true)]
        [InlineData(0x82, 2, true)]
        [InlineData(0x82, 3, true)]
        [InlineData(0x82, 1, false)]
        [InlineData(0x82, 4, false)]
        [InlineData(0x82, 8, false)]
        [InlineData(0x53, 0, false)]
        [InlineData(0x85, 0, false)]
        public void IsFatalAuthRejection_Classification(byte packetID, byte code, bool expected)
        {
            Assert.Equal(expected, LoginReconnectPolicy.IsFatalAuthRejection(packetID, code));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~LoginReconnectPolicyTests"`
Expected: FAIL (type missing) — unless the reconnect plan already created it, in which case it PASSES and you skip Step 3.

- [ ] **Step 3: Implement**

Create `src/ClassicUO.Client/Game/UI/Gumps/Login/LoginReconnectPolicy.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.Game.UI.Gumps.Login
{
    internal static class LoginReconnectPolicy
    {
        public static bool UseReconnectGump(bool autoLogin, string username, string password, bool forceFullLogin)
        {
            return autoLogin
                && !string.IsNullOrEmpty(username)
                && !string.IsNullOrEmpty(password)
                && !forceFullLogin;
        }

        public static bool IsFatalAuthRejection(byte packetID, byte code)
        {
            return packetID == 0x82 && (code == 0 || code == 2 || code == 3);
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~LoginReconnectPolicyTests"`
Expected: PASS (15 cases).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/Login/LoginReconnectPolicy.cs tests/ClassicUO.UnitTests/Game/UI/Gumps/LoginReconnectPolicyTests.cs
git commit -m "feat(login): add LoginReconnectPolicy decision logic"
```

---

## Task 4: TextureImage control

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/TextureImage.cs`

**Interfaces:**
- Consumes: `Texture2D`; `Control.AddToRenderLists`; batcher `Draw(Texture2D, Rectangle dest, Rectangle src, Vector3 hue, float depth)`.
- Produces: `internal class TextureImage : Control` with `TextureImage(Texture2D texture, int x, int y, int w, int h)`.

No unit test (graphics). Verified by build + Task 9 visual pass.

- [ ] **Step 1: Implement (mirrors `Line.cs:24-49`)**

Create `src/ClassicUO.Client/Game/UI/Controls/TextureImage.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls
{
    internal class TextureImage : Control
    {
        private readonly Texture2D _texture;

        public TextureImage(Texture2D texture, int x, int y, int w, int h)
        {
            _texture = texture;
            X = x;
            Y = y;
            Width = w;
            Height = h;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float layerDepth = layerDepthRef;
            Vector3 hueVector = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);
            var dest = new Rectangle(x, y, Width, Height);
            var src = new Rectangle(0, 0, _texture.Width, _texture.Height);

            renderLists.AddGumpNoAtlas(batcher =>
            {
                batcher.Draw(_texture, dest, src, hueVector, layerDepth);
                return true;
            });

            return true;
        }
    }
}
#endif
```

- [ ] **Step 2: Build**

Run: `dotnet build src/ClassicUO.Client -c Debug -p:CustomLoginScene=true`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/TextureImage.cs
git commit -m "feat(login): add TextureImage control"
```

---

## Task 5: NineSliceFrame control

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/NineSliceFrame.cs`

**Interfaces:**
- Consumes: `LoginAssets` corner/side/slice/center textures; batcher `Draw` + `DrawTiled` (`ResizePic.DrawInternal` pattern).
- Produces: `internal class NineSliceFrame : Control` with `NineSliceFrame(int x, int y, int w, int h)`.

No unit test (graphics). Verified by build + Task 9.

- [ ] **Step 1: Implement (adapts `ResizePic.DrawInternal:263-397` with LoginAssets textures)**

Create `src/ClassicUO.Client/Game/UI/Controls/NineSliceFrame.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using ClassicUO.Game.UI.Login;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls
{
    internal class NineSliceFrame : Control
    {
        public NineSliceFrame(int x, int y, int w, int h)
        {
            X = x;
            Y = y;
            Width = w;
            Height = h;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float depth = layerDepthRef;
            Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);

            Texture2D tl = LoginAssets.CornerTL, tr = LoginAssets.CornerTR;
            Texture2D bl = LoginAssets.CornerBL, br = LoginAssets.CornerBR;
            Texture2D sl = LoginAssets.SideL, sr = LoginAssets.SideR;
            Texture2D top = LoginAssets.TopSlice, bot = LoginAssets.BottomSlice;
            Texture2D ctr = LoginAssets.Center;

            renderLists.AddGumpNoAtlas(batcher =>
            {
                Rectangle Full(Texture2D t) => new Rectangle(0, 0, t.Width, t.Height);

                int leftW = tl.Width, rightW = tr.Width;
                int topH = tl.Height, botH = bl.Height;
                int midW = Width - leftW - rightW;
                int midH = Height - topH - botH;

                // center
                batcher.DrawTiled(ctr, new Rectangle(x + leftW, y + topH, midW, midH), Full(ctr), hue, depth);
                // edges
                batcher.DrawTiled(top, new Rectangle(x + leftW, y, midW, top.Height), Full(top), hue, depth);
                batcher.DrawTiled(bot, new Rectangle(x + leftW, y + Height - bot.Height, midW, bot.Height), Full(bot), hue, depth);
                batcher.DrawTiled(sl, new Rectangle(x, y + topH, sl.Width, midH), Full(sl), hue, depth);
                batcher.DrawTiled(sr, new Rectangle(x + Width - sr.Width, y + topH, sr.Width, midH), Full(sr), hue, depth);
                // corners
                batcher.Draw(tl, new Vector2(x, y), Full(tl), hue, depth);
                batcher.Draw(tr, new Vector2(x + Width - rightW, y), Full(tr), hue, depth);
                batcher.Draw(bl, new Vector2(x, y + Height - botH), Full(bl), hue, depth);
                batcher.Draw(br, new Vector2(x + Width - br.Width, y + Height - botH), Full(br), hue, depth);
                return true;
            });

            return true;
        }
    }
}
#endif
```

- [ ] **Step 2: Build**

Run: `dotnet build src/ClassicUO.Client -c Debug -p:CustomLoginScene=true`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/NineSliceFrame.cs
git commit -m "feat(login): add NineSliceFrame control"
```

---

## Task 6: ImageButton control

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/ImageButton.cs`

**Interfaces:**
- Consumes: `LoginAssets` button textures; `ILoginFont`; `Control` mouse events (`OnMouseEnter/Exit`, `OnMouseDown/Up`), `MouseIsOver`.
- Produces: `internal class ImageButton : Control` with `ImageButton(ButtonStyle style, string label, ILoginFont font, float fontSize, int x, int y, int w, int h)`, an `Action Clicked` callback (or `int ButtonId` + `OnButtonClick` — match the surrounding gump's dispatch), and `enum ButtonStyle { Dark, Priority }`.

No unit test (graphics + input). Verified by build + Task 9 manual.

- [ ] **Step 1: Implement**

Create `src/ClassicUO.Client/Game/UI/Controls/ImageButton.cs`. Three-state texture by hover/press, centered uppercase TTF label:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using System;
using ClassicUO.Game.UI.Login;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Renderer.Fonts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls
{
    internal enum ButtonStyle { Dark, Priority }

    internal class ImageButton : Control
    {
        private readonly ButtonStyle _style;
        private readonly string _label;
        private readonly ILoginFont _font;
        private readonly float _fontSize;
        private bool _pressed;

        public Action Clicked;

        public ImageButton(ButtonStyle style, string label, ILoginFont font, float fontSize,
            int x, int y, int w, int h)
        {
            _style = style;
            _label = (label ?? string.Empty).ToUpperInvariant();
            _font = font;
            _fontSize = fontSize;
            X = x; Y = y; Width = w; Height = h;
            AcceptMouseInput = true;
        }

        private Texture2D CurrentTexture()
        {
            if (_style == ButtonStyle.Priority)
                return _pressed ? LoginAssets.ButtonPrioDown
                     : MouseIsOver ? LoginAssets.ButtonPrioHover : LoginAssets.ButtonPrioNeutral;
            return _pressed ? LoginAssets.ButtonDown
                 : MouseIsOver ? LoginAssets.ButtonHover : LoginAssets.ButtonNeutral;
        }

        protected override void OnMouseDown(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left) _pressed = true;
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left && _pressed)
            {
                _pressed = false;
                if (Contains(x, y)) Clicked?.Invoke();
            }
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float depth = layerDepthRef;
            Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);
            Texture2D tex = CurrentTexture();
            var dest = new Rectangle(x, y, Width, Height);
            var src = new Rectangle(0, 0, tex.Width, tex.Height);

            // Letter-spacing per Global Constraints (buttons: 2-4px). Use 3.
            const float SPACING = 3f;
            Vector2 size = _font.Measure(_label, _fontSize, SPACING);
            float tx = x + (Width - size.X) / 2f;
            float ty = y + (Height - size.Y) / 2f;
            var labelColor = new Color(0xF0, 0xE6, 0xC8); // warm parchment; refine in Task 9

            renderLists.AddGumpNoAtlas(batcher =>
            {
                batcher.Draw(tex, dest, src, hue, depth);
                _font.Draw(batcher, _label, tx, ty, _fontSize, labelColor, 1f, SPACING);
                return true;
            });

            return true;
        }
    }
}
#endif
```

> **Note:** confirm `Control` exposes `MouseIsOver` and the `OnMouseDown/Up` override signatures (check `Control.cs`; buttons like `Button.cs` use them). If the base uses `OnMouseClick`/`MouseUp` events instead, wire `Clicked` through whatever the existing `Button` uses. Prefer matching `Button.cs`'s input handling exactly.

- [ ] **Step 2: Build**

Run: `dotnet build src/ClassicUO.Client -c Debug -p:CustomLoginScene=true`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/ImageButton.cs
git commit -m "feat(login): add ImageButton control (dark + priority styles)"
```

---

## Task 7: TtfLabel control

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/TtfLabel.cs`

**Interfaces:**
- Consumes: `ILoginFont`; batcher.
- Produces: `internal class TtfLabel : Control` with `TtfLabel(string text, ILoginFont font, float size, Color color, float opacity, int x, int y, float letterSpacing = 0)`, mutable `string Text`.

No unit test (graphics). Verified by build + Task 9.

- [ ] **Step 1: Implement**

Create `src/ClassicUO.Client/Game/UI/Controls/TtfLabel.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using ClassicUO.Renderer;
using ClassicUO.Renderer.Fonts;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Controls
{
    internal class TtfLabel : Control
    {
        private readonly ILoginFont _font;
        private readonly float _size;
        private readonly Color _color;
        private readonly float _opacity;
        private readonly float _letterSpacing;

        public string Text { get; set; }

        public TtfLabel(string text, ILoginFont font, float size, Color color, float opacity,
            int x, int y, float letterSpacing = 0f)
        {
            Text = text ?? string.Empty;
            _font = font;
            _size = size;
            _color = color;
            _opacity = opacity;
            _letterSpacing = letterSpacing;
            X = x;
            Y = y;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            if (string.IsNullOrEmpty(Text)) return true;

            string text = Text;
            float px = x, py = y, size = _size, sp = _letterSpacing, op = _opacity;
            Color col = _color;

            renderLists.AddGumpNoAtlas(batcher =>
            {
                _font.Draw(batcher, text, px, py, size, col, op, sp);
                return true;
            });

            return true;
        }
    }
}
#endif
```

- [ ] **Step 2: Build + Commit**

Run: `dotnet build src/ClassicUO.Client -c Debug -p:CustomLoginScene=true`
Expected: succeeds.

```bash
git add src/ClassicUO.Client/Game/UI/Controls/TtfLabel.cs
git commit -m "feat(login): add TtfLabel control"
```

---

## Task 8: TtfTextBox control

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/TtfTextBox.cs`

**Interfaces:**
- Consumes: `StbTextBox` (editing/caret internals), `LoginAssets` input textures, `ILoginFont`.
- Produces: `internal class TtfTextBox : Control` exposing `string Text`, `bool IsPassword`, `void SetText(string)`, and a keyboard-return event compatible with the scene's `Connect` call.

No unit test (graphics + input). Verified by build + Task 9 manual (typing, masking, focus swap).

- [ ] **Step 1: Study StbTextBox first**

Read `src/ClassicUO.Client/Game/UI/Controls/StbTextBox.cs` and the `PasswordStbTextBox` inner class in `LoginGump.cs:572+`. Decide: subclass `StbTextBox` and override only `AddToRenderLists`/draw to swap in the PNG background + TTF glyph rendering + TTF caret, OR compose a `StbTextBox` for editing state and render separately. Subclassing is preferred if `StbTextBox`'s draw is virtual; otherwise compose.

- [ ] **Step 2: Implement**

Create `src/ClassicUO.Client/Game/UI/Controls/TtfTextBox.cs`. Skeleton (fill the editing bridge per Step 1's decision):

```csharp
// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using ClassicUO.Game.UI.Login;
using ClassicUO.Renderer;
using ClassicUO.Renderer.Fonts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls
{
    internal class TtfTextBox : Control
    {
        private readonly ILoginFont _font;
        private readonly float _fontSize;
        private readonly bool _isPassword;
        private readonly StbTextBox _editor; // holds caret + edit state

        public TtfTextBox(ILoginFont font, float fontSize, int x, int y, int w, int h,
            bool isPassword, int maxChars = 32)
        {
            _font = font;
            _fontSize = fontSize;
            _isPassword = isPassword;
            X = x; Y = y; Width = w; Height = h;
            AcceptKeyboardInput = true;
            AcceptMouseInput = true;

            _editor = new StbTextBox(5, maxChars, w, false, hue: 0x034F) { Width = w, Height = h };
            // Keep the editor hidden (drives editing/caret only); we render via TTF.
        }

        public string Text => _editor.Text;
        public void SetText(string s) => _editor.SetText(s);

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float depth = layerDepthRef;
            Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);
            Texture2D bg = HasKeyboardFocus ? LoginAssets.InputFocus : LoginAssets.InputNeutral;
            var dest = new Rectangle(x, y, Width, Height);
            var bgSrc = new Rectangle(0, 0, bg.Width, bg.Height);

            string display = _isPassword ? new string('*', _editor.Text.Length) : _editor.Text;
            float tx = x + 12, ty = y + (Height - _fontSize) / 2f; // layout: placeholder padding
            var textColor = new Color(0xD3, 0xC2, 0xA1);           // #D3C2A1

            renderLists.AddGumpNoAtlas(batcher =>
            {
                batcher.Draw(bg, dest, bgSrc, hue, depth);
                _font.Draw(batcher, display, tx, ty, _fontSize, textColor, 0.9f);
                // caret: draw a thin TtfLabel "|" at measured caret offset when focused (refine Task 9)
                return true;
            });

            return true;
        }
    }
}
#endif
```

> The keyboard-input forwarding (routing key events from this control to `_editor`, focus handling, and the Enter-to-Connect hook) must match how `LoginGump` wires `StbTextBox` (`LoginGump.cs:488-497` `OnKeyboardReturn`). Mirror that: override `OnKeyboardReturn` to call the scene's `Connect`. Confirm `StbTextBox` exposes the members used (`Text`, `SetText`, caret position); adjust to its real API (read `StbTextBox.cs`).

- [ ] **Step 3: Build**

Run: `dotnet build src/ClassicUO.Client -c Debug -p:CustomLoginScene=true`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/TtfTextBox.cs
git commit -m "feat(login): add TtfTextBox control"
```

---

## Task 9: CustomLoginGump (states A + B) + visual layout

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Gumps/Login/CustomLoginGump.cs`

**Interfaces:**
- Consumes: all controls (Tasks 4–8), `LoginAssets`, `LoginReconnectPolicy`, `LoginScene` (`Connect`, `ForceFullLogin`, `Reconnect`, `CurrentLoginStep`, `PopupMessage`), `Settings.GlobalSettings`.
- Produces: `internal class CustomLoginGump : Gump` with `CustomLoginGump(World world, LoginScene scene)`.

No unit test (graphics). Verified by Task 10's manual smoke test.

- [ ] **Step 1: Implement the gump with both states**

Create `src/ClassicUO.Client/Game/UI/Gumps/Login/CustomLoginGump.cs`. Coordinates are placeholders derived from the mockup (1672×941 → ×0.766); refine visually while running (Task 10).

```csharp
// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using ClassicUO.Configuration;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Login;
using ClassicUO.Renderer.Fonts;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps.Login
{
    internal class CustomLoginGump : Gump
    {
        private const int W = 1280, H = 720;

        public CustomLoginGump(World world, LoginScene scene) : base(world, 0, 0)
        {
            CanCloseWithRightClick = false;
            AcceptKeyboardInput = false;

            var cormorant = LoginAssets.Cormorant;
            var cinzel = LoginAssets.Cinzel;
            var labelColor = new Color(0xD3, 0xC2, 0xA1);

            // Fullscreen background
            Add(new TextureImage(LoginAssets.Background, 0, 0, W, H));

            // Centered form panel (layout: placeholder)
            int panelW = 520, panelH = 360;
            int panelX = (W - panelW) / 2, panelY = 260;
            Add(new NineSliceFrame(panelX, panelY, panelW, panelH));

            bool reconnect = LoginReconnectPolicy.UseReconnectGump(
                Settings.GlobalSettings.AutoLogin,
                Settings.GlobalSettings.Username,
                Settings.GlobalSettings.Password,
                scene.ForceFullLogin);

            if (reconnect)
            {
                BuildReconnectState(scene, cormorant, cinzel, labelColor, panelX, panelY, panelW);
            }
            else
            {
                BuildFullFormState(scene, cormorant, cinzel, labelColor, panelX, panelY, panelW);
            }
        }

        private void BuildFullFormState(LoginScene scene, ILoginFont cormorant, ILoginFont cinzel,
            Color labelColor, int px, int py, int pw)
        {
            int fieldX = px + 40, fieldW = pw - 80;

            Add(new TtfLabel("Account Name", cormorant, 20f, labelColor, 0.9f, fieldX, py + 30));
            var account = new TtfTextBox(cormorant, 20f, fieldX, py + 56, fieldW, 40, isPassword: false);
            account.SetText(Settings.GlobalSettings.Username);
            Add(account);

            Add(new TtfLabel("Password", cormorant, 20f, labelColor, 0.9f, fieldX, py + 110));
            var password = new TtfTextBox(cormorant, 20f, fieldX, py + 136, fieldW, 40, isPassword: true);
            password.SetText(Crypter.Decrypt(Settings.GlobalSettings.Password));
            Add(password);

            if (!string.IsNullOrEmpty(scene.PopupMessage))
            {
                Add(new TtfLabel(scene.PopupMessage, cormorant, 18f, new Color(0xC0, 0x30, 0x30), 1f,
                    fieldX, py + 186));
            }

            Add(new ImageButton(ButtonStyle.Priority, "Login", cinzel, 26f, fieldX, py + 210, fieldW, 52)
            {
                Clicked = () => scene.Connect(account.Text, password.Text)
            });
            Add(new ImageButton(ButtonStyle.Dark, "Quit", cinzel, 26f, fieldX, py + 274, fieldW, 48)
            {
                Clicked = () => Client.Game.Exit()
            });
        }

        private void BuildReconnectState(LoginScene scene, ILoginFont cormorant, ILoginFont cinzel,
            Color labelColor, int px, int py, int pw)
        {
            int fieldX = px + 40, fieldW = pw - 80;

            string status = string.IsNullOrEmpty(scene.PopupMessage)
                ? $"Reconnecting as {Settings.GlobalSettings.Username}"
                : scene.PopupMessage;
            Add(new TtfLabel(status, cormorant, 20f, labelColor, 0.9f, fieldX, py + 60));

            Add(new ImageButton(ButtonStyle.Priority, "Reconnect", cinzel, 28f, fieldX, py + 130, fieldW, 60)
            {
                Clicked = () => scene.Connect(
                    Settings.GlobalSettings.Username,
                    Crypter.Decrypt(Settings.GlobalSettings.Password))
            });
            Add(new ImageButton(ButtonStyle.Dark, "Quit", cinzel, 26f, fieldX, py + 210, fieldW, 50)
            {
                Clicked = () => Client.Game.Exit()
            });
        }
    }
}
#endif
```

- [ ] **Step 2: Build**

Run: `dotnet build src/ClassicUO.Client -c Debug -p:CustomLoginScene=true`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/Login/CustomLoginGump.cs
git commit -m "feat(login): add CustomLoginGump with full-form and reconnect states"
```

---

## Task 10: LoginScene wiring + build scripts + manual verification

**Files:**
- Modify: `src/ClassicUO.Client/Game/Scenes/LoginScene.cs`
- Modify: `build-cuo.sh`, `build-cuo.ps1` (optional flag pass-through)

**Interfaces:**
- Consumes: `CustomLoginGump` (Task 9), `LoginReconnectPolicy` (Task 3).
- Produces: `#if CUSTOM_LOGIN_SCENE` behavior in `Load()`, `GetGumpForStep()`, `HandleErrorCode`; `internal bool LoginScene.ForceFullLogin`.

- [ ] **Step 1: Add ForceFullLogin + fatal-auth routing**

(If the reconnect plan already added `ForceFullLogin`, `_forceFullLogin`, `_keepPopupOnMain`, and the `HandleErrorCode` routing, SKIP this step.) Otherwise, in `LoginScene.cs`:

- After `private bool _autoLogin;` (line 47) add:
```csharp
        private bool _forceFullLogin;
        private bool _keepPopupOnMain;
```
- After `public bool CanAutologin => ...` (line 62) add:
```csharp
        internal bool ForceFullLogin
        {
            get => _forceFullLogin;
            set => _forceFullLogin = value;
        }
```
- Replace `HandleErrorCode` (lines 643-650) with the fatal-auth routing version:
```csharp
        public void HandleErrorCode(ref StackDataReader p)
        {
            byte packetID = p[0];
            byte code = p.ReadUInt8();

            PopupMessage = ServerErrorMessages.GetError(packetID, code, LoginDelay);
            LoginDelay = default;

            if (LoginReconnectPolicy.IsFatalAuthRejection(packetID, code))
            {
                _forceFullLogin = true;
                Reconnect = false;
                _keepPopupOnMain = true;
                CurrentLoginStep = LoginSteps.Main;
            }
            else
            {
                CurrentLoginStep = LoginSteps.PopUpMessage;
            }
        }
```
Add `using ClassicUO.Game.UI.Gumps.Login;` if not already present.

- [ ] **Step 2: Gate window size + gump construction**

In `Load()`, wrap the gump-add + window-size in the compile switch. Replace lines 74-75:
```csharp
            UIManager.Add(new LoginBackground(_world));
            UIManager.Add(_currentGump = new LoginGump(_world, this));
```
with:
```csharp
#if CUSTOM_LOGIN_SCENE
            UIManager.Add(_currentGump = new CustomLoginGump(_world, this));
#else
            UIManager.Add(new LoginBackground(_world));
            UIManager.Add(_currentGump = new LoginGump(_world, this));
#endif
```

Replace the window-size block (lines 94-97):
```csharp
            int width = Client.Game.ScaleWithDpi(640);
            int height = Client.Game.ScaleWithDpi(480);
            SDL.SDL_SetWindowMinimumSize(Client.Game.Window.Handle, width, height);
            Client.Game.SetWindowSize(width, height);
```
with:
```csharp
#if CUSTOM_LOGIN_SCENE
            int width = Client.Game.ScaleWithDpi(1280);
            int height = Client.Game.ScaleWithDpi(720);
#else
            int width = Client.Game.ScaleWithDpi(640);
            int height = Client.Game.ScaleWithDpi(480);
#endif
            SDL.SDL_SetWindowMinimumSize(Client.Game.Window.Handle, width, height);
            Client.Game.SetWindowSize(width, height);
```

In `GetGumpForStep()` `case LoginSteps.Main` (lines 194-197), replace:
```csharp
                case LoginSteps.Main:
                    PopupMessage = null;

                    return new LoginGump(_world,this);
```
with:
```csharp
                case LoginSteps.Main:
                    if (!_keepPopupOnMain)
                    {
                        PopupMessage = null;
                    }

                    _keepPopupOnMain = false;

#if CUSTOM_LOGIN_SCENE
                    return new CustomLoginGump(_world, this);
#else
                    return new LoginGump(_world, this);
#endif
```

- [ ] **Step 3: Build both configurations**

Run: `dotnet build src/ClassicUO.Client -c Debug`
Expected: succeeds (default UO login).
Run: `dotnet build src/ClassicUO.Client -c Debug -p:CustomLoginScene=true`
Expected: succeeds (custom scene).

- [ ] **Step 4: Optional build-script pass-through**

In `build-cuo.sh` / `build-cuo.ps1`, add an opt-in so releases can select the scene (only if the scripts drive the client publish — inspect first). Example (`build-cuo.sh`): allow `CUSTOM_LOGIN_SCENE=1` env → append `-p:CustomLoginScene=true` to the `dotnet publish`/`build` invocation. If the scripts don't build the client directly, skip this step.

- [ ] **Step 5: NativeAOT publish (both)**

Run: `dotnet publish src/ClassicUO.Client -c Release -r win-x64`
Expected: succeeds.
Run: `dotnet publish src/ClassicUO.Client -c Release -r win-x64 -p:CustomLoginScene=true`
Expected: succeeds (re-confirms Task 1's AOT gate with the full asset/control set).

- [ ] **Step 6: Manual smoke test (custom build)**

Run the `-p:CustomLoginScene=true` client against a test shard. Verify:
1. Fresh launch, autologin off → State A full form, window 1280×720, background + frame + Cormorant labels + Cinzel buttons render correctly.
2. Type account/password, LOGIN connects; QUIT exits. Password field masks input; focus swap changes input art (neutral↔focus).
3. autologin=true + valid saved creds → State B, centered RECONNECT + QUIT, no inputs; RECONNECT connects.
4. Wrong saved password (`0x82` code 0) → falls back to State A showing the error label; no retry loop.
5. Account-in-use (`0x82` code 1) → stays State B, keeps retrying.
6. Default build (no flag) → original 640×480 UO login unchanged.

- [ ] **Step 7: Commit**

```bash
git add src/ClassicUO.Client/Game/Scenes/LoginScene.cs build-cuo.sh build-cuo.ps1
git commit -m "feat(login): wire CustomLoginScene into LoginScene behind compile flag"
```

---

## Self-Review Notes

- **Spec coverage:**
  - §Font rendering (FontStashSharp + AOT fallback) → Task 1 (spike + `ILoginFont` + fallback gate at Step 7).
  - §Asset loading (FileEmbed) → Task 2.
  - §New controls (TextureImage/NineSliceFrame/ImageButton/TtfLabel/TtfTextBox) → Tasks 4–8.
  - §CustomLoginGump states A/B → Task 9.
  - §LoginScene changes (1280×720, `#if`, HandleErrorCode) → Task 10.
  - §Relationship to reconnect spec (`LoginReconnectPolicy`) → Task 3 (+ dedupe note).
  - §Typography (Cormorant labels/inputs, Cinzel uppercase buttons, #D3C2A1) → Tasks 6–9 constants.
  - §Two-state selection via policy → Task 9 branch.
  - §Compile-time gate → Task 1 Step 1 + Task 10 `#if`.
- **Type consistency:** `ILoginFont.Measure/Draw`, `LoginAssets.*`, `LoginReconnectPolicy.UseReconnectGump/IsFatalAuthRejection`, `ButtonStyle.{Dark,Priority}`, `TextureImage/NineSliceFrame/ImageButton/TtfLabel/TtfTextBox`, `CustomLoginGump(World, LoginScene)`, `LoginScene.ForceFullLogin`/`_keepPopupOnMain` used consistently across tasks.
- **Verification points flagged inline (not placeholders — real API confirmations the implementer makes against existing code):** FontStashSharp interface member names (Task 1 note); `Control` mouse-input API vs `Button.cs` (Task 6 note); `StbTextBox` public API + keyboard routing (Task 8 Steps 1–2); whether the reconnect plan already created `LoginReconnectPolicy`/`ForceFullLogin` (Tasks 3, 10 skip-guards); whether build scripts drive the client publish (Task 10 Step 4).
- **Known deferrals (explicitly in-scope-later, not gaps):** exact on-screen coordinates + label colors (Task 9/10 visual pass); source-art downscaling; server/character-select re-skin.
```
