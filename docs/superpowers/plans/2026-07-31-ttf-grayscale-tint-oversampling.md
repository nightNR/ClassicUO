# TTF Grayscale Atlas + Draw-Time Tint + Oversampling — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make colored in-game TTF text render solid and correctly hued by (B) rasterizing glyphs at 3× and downsampling for full-coverage interiors + smooth AA, and (A) caching TTF glyphs as a color-independent grayscale mask tinted at draw time via the existing `TEXT_RGB` shader path.

**Architecture:** TTF-only (slots 20–31). `TtfGlyphSource` rasterizes at 3× then box-downsamples. `FontsLoader` stops solidifying coverage (oversampling replaces it). `RenderedText.DrawGlyphs` draws TTF glyphs from the uncolored `GetEntry` mask via `UltimaBatcher2D.DrawTextGlyph` (vertex tint + `TEXT_RGB`, premultiplied) using the glyph's existing `baseColor` normalized to an rgb tint. Classic fonts, measure/layout, hit-testing untouched.

**Tech Stack:** C# net10.0, xUnit, StbTrueTypeSharp, FNA.

## Global Constraints

- TTF-only: everything keys off `FontsLoader.IsAtlasFont(font)` (slots 20–31). Classic unicode (0–19) and ASCII fonts keep their exact current rasterization, baking, and draw path.
- Oversample factor = **3**.
- Tinting reuses the existing premultiplied pipeline: white×coverage glyph × tint rgb via `DrawTextGlyph` → shader `TEXT_RGB` (`color.rgb *= tint`), default `BlendState.AlphaBlend` (premultiplied). Do not change blend state.
- Metric space stays 1× (downsampled), so `GetInfoUnicode`/`GetWidthUnicode` measure identically: `OffsetX = round(xoff3/3)`, `OffsetY = round((ascentPx3 + yoff3)/3)`, `Width`/`Height` = downsampled extents, clamped to `sbyte`.
- Coverage `null` for empty/space glyphs (preserves `HasPixels==false` / `UNICODE_SPACE_WIDTH`).
- New source lines carry no license churn (edits to existing files); any new file starts with `// SPDX-License-Identifier: BSD-2-Clause`.
- xUnit; `ClassicUO.Assets` internals visible to `ClassicUO.UnitTests`.

## File Structure

- `src/ClassicUO.Assets/TtfGlyphSource.cs` — 3× rasterization + a testable `internal static` box-downsample helper; store `_ascentRaw` for 3× metrics.
- `src/ClassicUO.Assets/FontsLoader.cs` — remove `CoverageSolidAt` + the solidify in `StrengthenCoverage` (and its call in the AA branch).
- `src/ClassicUO.Renderer/Batcher2D.cs` — add a dest-rect overload of `DrawTextGlyph` (for scaled draw parity).
- `src/ClassicUO.Client/Game/GameObjects/RenderedText.cs` — TTF draw branch: grayscale `GetEntry` + `DrawTextGlyph` tint; drop TTF `baseColor` bake.
- `src/ClassicUO.Renderer/FontGlyphAtlas.cs` — remove `GetColoredEntry` only if it becomes unused.
- Tests: `tests/ClassicUO.UnitTests/Assets/TtfGlyphSourceTests.cs`.

---

### Task 1: Oversampled rasterization in `TtfGlyphSource`

**Files:**
- Modify: `src/ClassicUO.Assets/TtfGlyphSource.cs`
- Modify: `tests/ClassicUO.UnitTests/Assets/TtfGlyphSourceTests.cs`

**Interfaces:**
- Produces: `internal static byte[] TtfGlyphSource.Downsample(byte[] src, int srcW, int srcH, int factor, out int dstW, out int dstH)` — box-average downsample; `dstW = (srcW + factor - 1) / factor`, `dstH` likewise; each output pixel = rounded average of its (≤factor×≤factor, edge-clamped) source block.
- `TryRasterize` now returns 3×-oversampled-then-downsampled coverage with 1×-space metrics (same signature).

- [ ] **Step 1: Write the failing test (downsample helper)**

Add to `tests/ClassicUO.UnitTests/Assets/TtfGlyphSourceTests.cs`:

```csharp
        [Fact]
        public void Downsample_FullBlocks_AverageTo255AndDivideDims()
        {
            // 6x3 source, factor 3 -> 2x1 output. Left 3x3 block all 255 -> 255.
            // Right 3x3 block all 0 -> 0.
            byte[] src = new byte[6 * 3];
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    src[y * 6 + x] = 255;

            byte[] dst = TtfGlyphSource.Downsample(src, 6, 3, 3, out int dw, out int dh);

            Assert.Equal(2, dw);
            Assert.Equal(1, dh);
            Assert.Equal(255, dst[0]);
            Assert.Equal(0, dst[1]);
        }

        [Fact]
        public void Downsample_HalfCoveredBlock_AveragesToMid()
        {
            // 3x3 all-255 except: make ~half the block 0 -> average ~113-142.
            byte[] src = new byte[3 * 3];
            for (int i = 0; i < src.Length; i++) src[i] = (byte)(i < 4 ? 255 : 0); // 4 of 9 set
            byte[] dst = TtfGlyphSource.Downsample(src, 3, 3, 3, out int dw, out int dh);
            Assert.Equal(1, dw);
            Assert.Equal(1, dh);
            Assert.Equal((byte)((255 * 4 + 4) / 9), dst[0]); // rounded average = 113
        }

        [Fact]
        public void Downsample_EdgeBlockSmallerThanFactor_AveragesOverActualPixels()
        {
            // 4x1 source, factor 3 -> 2x1: block0 = px0..2, block1 = px3 only.
            byte[] src = { 255, 255, 255, 100 };
            byte[] dst = TtfGlyphSource.Downsample(src, 4, 1, 3, out int dw, out int dh);
            Assert.Equal(2, dw);
            Assert.Equal(1, dh);
            Assert.Equal(255, dst[0]);
            Assert.Equal(100, dst[1]); // partial edge block = single pixel
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TtfGlyphSourceTests.Downsample"`
Expected: FAIL — `Downsample` not defined.

- [ ] **Step 3: Implement the downsample helper**

In `src/ClassicUO.Assets/TtfGlyphSource.cs`, add:

```csharp
        // Box-downsample an 8-bit coverage bitmap by an integer factor. Output
        // dimensions are ceil(src/factor); edge blocks average over only the
        // source pixels that exist. Rounded average per block.
        internal static byte[] Downsample(byte[] src, int srcW, int srcH, int factor, out int dstW, out int dstH)
        {
            dstW = (srcW + factor - 1) / factor;
            dstH = (srcH + factor - 1) / factor;

            byte[] dst = new byte[dstW * dstH];

            for (int dy = 0; dy < dstH; dy++)
            {
                int sy0 = dy * factor;
                int sy1 = System.Math.Min(sy0 + factor, srcH);

                for (int dx = 0; dx < dstW; dx++)
                {
                    int sx0 = dx * factor;
                    int sx1 = System.Math.Min(sx0 + factor, srcW);

                    int sum = 0;
                    int count = 0;

                    for (int sy = sy0; sy < sy1; sy++)
                    {
                        int row = sy * srcW;
                        for (int sx = sx0; sx < sx1; sx++)
                        {
                            sum += src[row + sx];
                            count++;
                        }
                    }

                    dst[dy * dstW + dx] = count == 0 ? (byte)0 : (byte)((sum + count / 2) / count);
                }
            }

            return dst;
        }
```

- [ ] **Step 4: Run to verify the helper passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TtfGlyphSourceTests.Downsample"`
Expected: PASS (3 facts).

- [ ] **Step 5: Store `_ascentRaw` and rasterize at 3×**

In `TtfGlyphSource.cs`, add a `private readonly int _ascentRaw;` field and an `internal const int Oversample = 3;`. Change the ctor to accept and store `ascentRaw`, and change `Create` to pass it. Replace the ctor + `Create`'s tail + `TryRasterize` as follows.

Ctor (add `ascentRaw`):

```csharp
        internal const int Oversample = 3;

        private TtfGlyphSource(byte[] ttf, stbtt_fontinfo info, float scale, int ascentRaw, int ascentPx, int pixelSize)
        {
            _ttf = ttf;
            _info = info;
            _scale = scale;
            _ascentRaw = ascentRaw;
            _ascentPx = ascentPx;
            PixelSize = pixelSize;
        }
```

In `Create`, change the final `return` to pass `ascentRaw`:

```csharp
            return new TtfGlyphSource(ttfBytes, info, scale, ascentRaw, ascentPx, pixelSize);
```

Replace `TryRasterize` body with the 3× version:

```csharp
        public unsafe bool TryRasterize(
            char c, out sbyte offsetX, out sbyte offsetY,
            out sbyte width, out sbyte height, out byte[] coverage)
        {
            offsetX = 0; offsetY = 0; width = 0; height = 0; coverage = null;

            int codepoint = c;

            if (stbtt_FindGlyphIndex(_info, codepoint) == 0)
            {
                return false; // glyph not present in this face
            }

            // Rasterize at Oversample x the target size for full-coverage
            // interiors + smooth AA, then box-downsample to target.
            float scaleN = _scale * Oversample;

            int w, h, xoff, yoff;
            byte* bitmap = stbtt_GetCodepointBitmap(_info, scaleN, scaleN, codepoint, &w, &h, &xoff, &yoff);

            if (bitmap == null || w <= 0 || h <= 0)
            {
                if (bitmap != null)
                {
                    stbtt_FreeBitmap(bitmap, null);
                }
                return false;
            }

            byte[] over = new byte[w * h];
            fixed (byte* pOver = over)
            {
                Buffer.MemoryCopy(bitmap, pOver, over.Length, over.Length);
            }
            stbtt_FreeBitmap(bitmap, null);

            byte[] cov = Downsample(over, w, h, Oversample, out int dstW, out int dstH);

            if (dstW <= 0 || dstH <= 0)
            {
                return false;
            }

            int ascentPxN = (int)MathF.Round(_ascentRaw * scaleN);

            offsetX = ClampToSByte((int)MathF.Round(xoff / (float)Oversample));
            offsetY = ClampToSByte((int)MathF.Round((ascentPxN + yoff) / (float)Oversample));
            width = ClampToSByte(dstW);
            height = ClampToSByte(dstH);
            coverage = cov;
            return true;
        }
```

- [ ] **Step 6: Update the existing interior-opacity test for oversampled quality**

The existing `TryRasterize_SolidGlyph_HasOpaqueInteriorAndPositiveMetrics` still holds (solid 'M' still has a 255 pixel and positive metrics). Add one guaranteeing AA edges remain (not all-or-nothing) — proves oversampling yields a gradient, not a hard 1-bit mask:

```csharp
        [Fact]
        public void TryRasterize_Oversampled_HasOpaqueInteriorAndSoftEdge()
        {
            TtfGlyphSource src = TtfGlyphSource.Create(InterTtf(), 22);
            Assert.True(src.TryRasterize('e', out _, out _, out sbyte w, out sbyte h, out byte[] cov));
            Assert.True(w > 0 && h > 0);

            bool hasOpaque = false, hasPartial = false;
            foreach (byte b in cov)
            {
                if (b == 255) hasOpaque = true;
                else if (b > 0) hasPartial = true;
            }
            Assert.True(hasOpaque, "expected fully-opaque interior pixels");
            Assert.True(hasPartial, "expected anti-aliased edge pixels");
        }
```

- [ ] **Step 7: Run the TtfGlyphSource suite**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~TtfGlyphSourceTests"`
Expected: PASS (all, including the prior 5 + downsample + oversample facts).

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Assets/TtfGlyphSource.cs tests/ClassicUO.UnitTests/Assets/TtfGlyphSourceTests.cs
git commit -m "feat(fonts): oversample TTF glyphs 3x + box-downsample for solid AA"
```

---

### Task 2: Remove the coverage-solidify hack

**Files:**
- Modify: `src/ClassicUO.Assets/FontsLoader.cs`
- Modify: `tests/ClassicUO.UnitTests/CoverageBlendTests.cs`

**Interfaces:**
- Removed: `FontsLoader.CoverageSolidAt`, `FontsLoader.StrengthenCoverage`. The AA branch in `RenderSingleGlyphUnicode` uses the raw (oversampled) coverage directly.

- [ ] **Step 1: Remove the StrengthenCoverage call in the AA branch**

In `src/ClassicUO.Assets/FontsLoader.cs`, `RenderSingleGlyphUnicode`, the 8bpp AA branch currently does:

```csharp
                        byte cov = ch.Coverage[y * dw + x];

                        if (cov == 0)
                        {
                            continue;
                        }

                        cov = StrengthenCoverage(cov);

                        int block = (y + pad) * bufW + (x + italicOffset + solidShift + pad);
                        pData[block] = BlendCoverage(pData[block], charcolor, cov);
```

Remove the `cov = StrengthenCoverage(cov);` line (oversampling now supplies solid interiors). The block becomes:

```csharp
                        byte cov = ch.Coverage[y * dw + x];

                        if (cov == 0)
                        {
                            continue;
                        }

                        int block = (y + pad) * bufW + (x + italicOffset + solidShift + pad);
                        pData[block] = BlendCoverage(pData[block], charcolor, cov);
```

- [ ] **Step 2: Delete `CoverageSolidAt` and `StrengthenCoverage`**

In `src/ClassicUO.Assets/FontsLoader.cs`, delete the `CoverageSolidAt` const (and its doc comment) and the entire `StrengthenCoverage` method. Leave `CoverageGamma`, `BlendCoverage`, and `CoverageIsSet` as they are (still used elsewhere / by tests).

- [ ] **Step 3: Remove the StrengthenCoverage tests**

In `tests/ClassicUO.UnitTests/CoverageBlendTests.cs`, delete the two tests that reference `StrengthenCoverage` (`StrengthenCoverage_PreservesEndpoints`, `StrengthenCoverage_LiftsMidtones`). Keep all `BlendCoverage` tests.

- [ ] **Step 4: Build + test**

Run: `dotnet build ClassicUO.sln -c Debug` → 0 errors (no residual `StrengthenCoverage`/`CoverageSolidAt` reference).
Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~CoverageBlend"` → PASS (BlendCoverage tests only).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Assets/FontsLoader.cs tests/ClassicUO.UnitTests/CoverageBlendTests.cs
git commit -m "refactor(fonts): drop coverage-solidify hack (oversampling supersedes it)"
```

---

### Task 3: TTF draw branch — grayscale glyph + premultiplied tint

**Files:**
- Modify: `src/ClassicUO.Renderer/Batcher2D.cs` (add dest-rect `DrawTextGlyph` overload)
- Modify: `src/ClassicUO.Client/Game/GameObjects/RenderedText.cs` (`DrawGlyphs` TTF branch)

**Interfaces:**
- Consumes: `FontsLoader.IsAtlasFont(int)`; `GlyphAtlasEntry { Texture, UV, BearingX, BearingY, AdvanceWidth, GlyphWidth, GlyphHeight }`; `FontGlyphAtlas.GetEntry(font, c, isUnicode, border, solid, italic)`.
- Produces: `UltimaBatcher2D.DrawTextGlyph(Texture2D texture, Rectangle destRect, Rectangle sourceRect, Vector3 tintRgb, float opacity, float depth)`.

- [ ] **Step 1: Add a dest-rect `DrawTextGlyph` overload**

In `src/ClassicUO.Renderer/Batcher2D.cs`, next to the existing `DrawTextGlyph` (position-based, ~line 636), add an overload that takes an explicit destination rectangle so scaled glyph draws keep parity with the current `batcher.Draw(texture, dstRect, uv, ...)`. It mirrors the existing method but sets the four positions from `destRect`:

```csharp
        /// <summary>
        /// Dest-rect variant of <see cref="DrawTextGlyph(Texture2D,Vector2,Rectangle,Vector3,float,float)"/>
        /// for scaled glyph draws (destRect may differ in size from sourceRect).
        /// </summary>
        public void DrawTextGlyph
        (
            Texture2D texture,
            Rectangle destRect,
            Rectangle sourceRect,
            Vector3 tintRgb,
            float opacity,
            float depth
        )
        {
            CalculateUVs(sourceRect, texture.Width, texture.Height,
                out float sourceX, out float sourceY, out float sourceW, out float sourceH);

            EnsureSize();

            ref PositionNormalTextureColor4 vertex = ref _vertexInfo[_numSprites];

            vertex.Position0.X = destRect.X;
            vertex.Position0.Y = destRect.Y;
            vertex.Position1.X = destRect.X + destRect.Width;
            vertex.Position1.Y = destRect.Y;
            vertex.Position2.X = destRect.X;
            vertex.Position2.Y = destRect.Y + destRect.Height;
            vertex.Position3.X = destRect.X + destRect.Width;
            vertex.Position3.Y = destRect.Y + destRect.Height;

            vertex.Position0.Z = depth;
            vertex.Position1.Z = depth;
            vertex.Position2.Z = depth;
            vertex.Position3.Z = depth;

            vertex.TextureCoordinate0.X = sourceX;
            vertex.TextureCoordinate0.Y = sourceY;
            vertex.TextureCoordinate1.X = sourceX + sourceW;
            vertex.TextureCoordinate1.Y = sourceY;
            vertex.TextureCoordinate2.X = sourceX;
            vertex.TextureCoordinate2.Y = sourceY + sourceH;
            vertex.TextureCoordinate3.X = sourceX + sourceW;
            vertex.TextureCoordinate3.Y = sourceY + sourceH;

            vertex.TextureCoordinate0.Z = 0;
            vertex.TextureCoordinate1.Z = 0;
            vertex.TextureCoordinate2.Z = 0;
            vertex.TextureCoordinate3.Z = 0;

            vertex.Normal0 = tintRgb;
            vertex.Normal1 = tintRgb;
            vertex.Normal2 = tintRgb;
            vertex.Normal3 = tintRgb;

            Vector3 hue = new Vector3(0, ShaderHueTranslator.SHADER_TEXT_RGB, opacity);
            vertex.Hue0 = hue;
            vertex.Hue1 = hue;
            vertex.Hue2 = hue;
            vertex.Hue3 = hue;

            _textureInfo[_numSprites] = texture;
            ++_numSprites;
        }
```

- [ ] **Step 2: Add a tint helper in `RenderedText.DrawGlyphs`**

In `src/ClassicUO.Client/Game/GameObjects/RenderedText.cs`, `DrawGlyphs`, add a small local that converts a baked ARGB `uint` (0 = white) into a normalized rgb `Vector3` tint. Place it just before the per-line `while (ptr != null)` loop:

```csharp
            // Convert a baked ARGB color (the same value the classic path bakes
            // into the glyph) to a normalized rgb tint. 0 => white (untinted).
            static Vector3 TintOf(uint argb)
            {
                if (argb == 0)
                {
                    return new Vector3(1f, 1f, 1f);
                }
                // baked color is 0xAARRGGBB via RgbaToArgb; low byte = R.
                float r = (argb & 0xFF) / 255f;
                float g = ((argb >> 8) & 0xFF) / 255f;
                float b = ((argb >> 16) & 0xFF) / 255f;
                return new Vector3(r, g, b);
            }
```

(Byte order verified: `GetPolygoneColor`→`Color16To32` yields `0x00BBGGRR`; `(<<8)|0xFF` then `RgbaToArgb` (`(x>>8)|(x<<24)`) gives `baseColor = 0xAABBGGRR` — low byte R, then G, then B. So `TintOf` above extracts r/g/b correctly. Do not swap.)

- [ ] **Step 3: Branch the per-glyph draw for TTF**

In `DrawGlyphs`, the per-glyph block currently selects an atlas entry and draws it. Locate:

```csharp
                    GlyphAtlasEntry entry;
                    if (charColor != 0)
                    {
                        entry = atlas.GetColoredEntry(
                            charFont, si, IsUnicode, charBorder, charSolid, charItalic, charColor
                        );
                    }
                    else
                    {
                        entry = atlas.GetEntry(
                            charFont, si, IsUnicode, charBorder, charSolid, charItalic
                        );
                    }
```

Replace the entry selection so TTF slots always use the color-independent `GetEntry`:

```csharp
                    bool isTtf = Client.Game.UO.FileManager.Fonts.IsAtlasFont(charFont);

                    GlyphAtlasEntry entry;
                    if (charColor != 0 && !isTtf)
                    {
                        entry = atlas.GetColoredEntry(
                            charFont, si, IsUnicode, charBorder, charSolid, charItalic, charColor
                        );
                    }
                    else
                    {
                        entry = atlas.GetEntry(
                            charFont, si, IsUnicode, charBorder, charSolid, charItalic
                        );
                    }
```

Then locate the draw call:

```csharp
                            batcher.Draw(
                                entry.Texture,
                                new Rectangle(drawX, drawY, drawW, drawH),
                                entry.UV,
                                charHueVector,
                                depth
                            );
```

Replace with a TTF branch that tints the grayscale glyph, else the existing draw:

```csharp
                            if (isTtf)
                            {
                                Vector3 tint = drawHue > 0
                                    ? TintOf(HuesHelper.RgbaToArgb(
                                        (Client.Game.UO.FileManager.Hues.GetPolygoneColor(Cell, drawHue) << 8) | 0xFF))
                                    : TintOf(charColor);

                                batcher.DrawTextGlyph(
                                    entry.Texture,
                                    new Rectangle(drawX, drawY, drawW, drawH),
                                    entry.UV,
                                    tint,
                                    alpha,
                                    depth
                                );
                            }
                            else
                            {
                                batcher.Draw(
                                    entry.Texture,
                                    new Rectangle(drawX, drawY, drawW, drawH),
                                    entry.UV,
                                    charHueVector,
                                    depth
                                );
                            }
```

Notes for the implementer:
- `charColor` already holds the glyph's baked ARGB color (`baseColor`, or the HTML per-char color set higher in the loop). For TTF, we no longer bake it — we tint with it instead. `TintOf(charColor)` gives the correct hue; `charColor==0` → white.
- `drawHue > 0` is the highlight override; tint with the full-intensity `drawHue` color the same way the classic path computes `baseColor`.
- `alpha` is the existing per-draw opacity; `TEXT_RGB` applies it as the scalar (`color * alpha`).

- [ ] **Step 4: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: 0 errors.

- [ ] **Step 5: Run the full unit suite (no regressions in measure/layout)**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS (measure/layout tests unaffected; TTF glyph/registration tests still pass).

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Renderer/Batcher2D.cs src/ClassicUO.Client/Game/GameObjects/RenderedText.cs
git commit -m "feat(fonts): tint TTF glyphs at draw via grayscale atlas + TEXT_RGB"
```

---

### Task 4: Remove now-dead colored-glyph baking (if unused)

**Files:**
- Modify: `src/ClassicUO.Renderer/FontGlyphAtlas.cs`
- Possibly modify: `src/ClassicUO.Assets/FontsLoader.cs`

**Interfaces:** none new.

- [ ] **Step 1: Check for remaining callers of `GetColoredEntry`**

Run: `git grep -n "GetColoredEntry"`
- If the ONLY references are its definition in `FontGlyphAtlas.cs` (and the TTF branch you just made bypass it), it is dead → remove `GetColoredEntry` and the `color`/`charcolor` plumbing it uniquely drives in `CreateEntry`. Keep `RenderSingleGlyphUnicode`'s `charcolor` parameter (the uncolored path passes 0 → white, still needed).
- If classic-font code still calls `GetColoredEntry` (e.g. some non-TTF colored path), LEAVE it — it's still used; skip the removal and note it.

- [ ] **Step 2: Remove if dead**

If dead per Step 1, delete `GetColoredEntry` from `FontGlyphAtlas.cs` and simplify `CreateEntry` to the single uncolored path. Do not touch `GetEntry` or `RenderSingleGlyphUnicode`'s white (charcolor 0) path.

- [ ] **Step 3: Build + full suite**

Run: `dotnet build ClassicUO.sln -c Debug` → 0 errors.
Run: `dotnet test tests/ClassicUO.UnitTests` → PASS.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(fonts): remove unused per-color glyph baking"
```

(If Step 1 found `GetColoredEntry` still in use, skip this task's commit and record that it stays.)

---

### Task 5: Manual in-client verification (user, Vulkan)

**Files:** none.

- [ ] **Step 1** — Build + copy the cuo client into the phoenix bundle; confirm the `AtlasFontRegistry: registered 12 TTF font slots (live StbTrueType rasterization)` marker in the console (build freshness).

- [ ] **Step 2** — With a TTF slot selected for overhead/chat and tooltip:
  - Colored overhead names / speech render **solid** and in the **correct hue** (no washed/see-through infill), with an intact black border and smooth edges.
  - White/default TTF text still correct.
  - Slovak/Czech glyphs (č, š, ž, ď, ľ …) render.

- [ ] **Step 3** — Switch a context to a classic font → renders exactly as before (unchanged).

- [ ] **Step 4** — Report; if colored text is now too thick/thin or mis-hued, the levers are `TtfGlyphSource.Oversample` (quality) and the `TintOf` byte order (hue correctness).

---

## Self-Review Notes

- **Spec coverage:** oversampling 3× + downsample (Task 1), delete solidify hack (Task 2), grayscale `GetEntry` + `DrawTextGlyph`/`TEXT_RGB` tint + drop TTF baseColor bake (Task 3), remove dead `GetColoredEntry` (Task 4), manual verify incl. SK/CZ + classic-unchanged (Task 5), TTF-only scope via `IsAtlasFont` (Task 3). Covered.
- **Metric parity:** downsampled 1×-space metrics keep `GetInfoUnicode` measurement identical; oversampling only improves coverage quality.
- **Tint correctness:** reuses the exact `baseColor` the classic path bakes (via `GetPolygoneColor`/HTML color), normalized to rgb — so hue matches; Task 3 Step 2 flags verifying `RgbaToArgb` byte order.
- **Type consistency:** `Downsample(byte[],int,int,int,out int,out int)`, `DrawTextGlyph(Texture2D,Rectangle,Rectangle,Vector3,float,float)`, `IsAtlasFont(int)`, `GlyphAtlasEntry` fields used consistently across tasks.
- **Build-green ordering:** Task 1 adds oversampling (StrengthenCoverage still runs — transient thick look, not shipped); Task 2 removes it; Task 3 switches the draw; Task 4 cleans up. Each task builds.
- **Flagged for implementer:** Task 4 Step 1 (only remove `GetColoredEntry` if truly unused). (Tint byte order already verified in Task 3 Step 2 — `0xAABBGGRR`, R low.)
```
