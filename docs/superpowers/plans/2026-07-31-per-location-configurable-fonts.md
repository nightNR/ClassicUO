# Per-location configurable fonts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user pick a TTF face + pixel size (or "Classic") per UI location in Options, generating only the selected (face,size) combinations into the font atlas on demand.

**Architecture:** A face catalog maps stable `faceId`s to embedded TTF bytes. A dynamic `AtlasFontRegistry` resolves a `(faceId,size)` pair to a `FontsLoader` unicode slot, deduping and registering lazily. `FontsLoader`'s unicode glyph store becomes jagged with lazily-allocated rows so a bumped slot cap costs no memory until used. `Profile` stores a `FontChoice` per location; `ResolveFont` turns a location id into a `(slot, unicode)` draw pair, falling back to the location's original hardcoded font when the choice is "Classic".

**Tech Stack:** C# / .NET 10, FNA-XNA, xUnit, `stb_truetype` (via existing `TtfGlyphSource`), System.Text.Json source-gen.

## Global Constraints

- Target framework `net10.0`, `LangVersion=Preview`, `AllowUnsafeBlocks=true`.
- New source files carry the BSD-2-Clause license header (`// SPDX-License-Identifier: BSD-2-Clause`).
- Tests use **xUnit**; `ClassicUO.Client` internals are visible to `ClassicUO.UnitTests`.
- Performance-sensitive paths: no per-frame allocation in glyph access; match surrounding `unsafe`/pooling style.
- `Profile` serializes via `ProfileJsonContext` source-gen (snake_case). Any new serialized type MUST be registered with `[JsonSerializable(...)]` on `ProfileJsonContext`.
- Classic (MUL/ASCII) rendering stays the default for every location — zero visual change until the user opts in.
- Dynamic TTF slot pool = unicode slots **20–95**; classic unicode/rune fonts occupy 0–12; 13–19 are reserved buffer.

---

### Task 1: Jagged lazy unicode glyph store + raised slot cap

Convert `FontsLoader._fontDataUNICODE` from a preallocated 2D `[MAX,65536]` array to a jagged `[MAX][]` with per-slot rows allocated on first use, and raise `MAX_UNICODE_FONTS` to 96. This removes the ~64 MB baseline and makes the bigger cap free until slots are used. Also add a reset path used later by the registry.

**Files:**
- Modify: `src/ClassicUO.Assets/FontsLoader.cs` (`MAX_UNICODE_FONTS` line 36; field line 78; alloc lines 218 & 381; access lines 232, 271, 331, 343; add helpers)
- Test: `tests/ClassicUO.UnitTests/Assets/FontsLoaderJaggedTests.cs`

**Interfaces:**
- Consumes: existing `RegisterAtlasFont(int slot, string, ReadOnlySpan<byte>, int)`, `GetWidthUnicode(byte, string)`, `RenderSingleGlyphUnicode(byte, char, bool, bool)`.
- Produces:
  - `public const int MAX_UNICODE_FONTS = 96;`
  - `public void ResetAtlasFont(int slot)` — clears a dynamic slot's atlas state (`_isAtlasFont`, `_atlasGlyphSources`, `_atlasFontNames`, `_atlasMissing`) and drops its glyph row so the slot is free and its memory reclaimed.
  - unchanged public glyph API (jagged conversion is internal).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ClassicUO.UnitTests/Assets/FontsLoaderJaggedTests.cs
// SPDX-License-Identifier: BSD-2-Clause
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests.Assets
{
    public class FontsLoaderJaggedTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ClassicUO.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new System.InvalidOperationException("ClassicUO.sln not found");
        }

        private static byte[] InterTtf() =>
            File.ReadAllBytes(Path.Combine(RepoRoot(), "Assets", "fonts", "Inter", "static", "Inter_18pt-Regular.ttf"));

        private static FontsLoader NewFonts() =>
            new UOFileManager(ClientVersion.CV_300, Path.GetTempPath()).Fonts;

        [Fact]
        public void Cap_IsNinetySix()
        {
            Assert.Equal(96, FontsLoader.MAX_UNICODE_FONTS);
        }

        [Fact]
        public void HighDynamicSlot_RastersAndReset_FreesSlot()
        {
            FontsLoader fonts = NewFonts();
            fonts.RegisterAtlasFont(95, "Inter 30", InterTtf(), 30);
            Assert.True(fonts.IsAtlasFont(95));
            Assert.True(fonts.GetWidthUnicode(95, "M") > 0);   // forces lazy row alloc on slot 95

            fonts.ResetAtlasFont(95);
            Assert.False(fonts.IsAtlasFont(95));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~FontsLoaderJaggedTests"`
Expected: FAIL — `MAX_UNICODE_FONTS` is 41; `ResetAtlasFont` and slot 95 are out of range.

- [ ] **Step 3: Bump the cap and jagged field**

In `src/ClassicUO.Assets/FontsLoader.cs` change line 36:

```csharp
        // 0-12 classic unicode/rune fonts; 13-19 reserved; 20-95 dynamic TTF atlas slots.
        public const int MAX_UNICODE_FONTS = 96;
```

Change the field (line 78) from 2D to jagged:

```csharp
        private FontCharacterDataUnicode[][] _fontDataUNICODE;
```

- [ ] **Step 4: Replace allocation with a lazy row helper**

Replace line 218 (`_fontDataUNICODE = new FontCharacterDataUnicode[_unicodeFontAddress.Length, 0x10000];`) with:

```csharp
            _fontDataUNICODE ??= new FontCharacterDataUnicode[_unicodeFontAddress.Length][];
```

Replace the identical guard at line 381 (inside `RegisterAtlasFont`) with the same jagged form:

```csharp
            _fontDataUNICODE ??= new FontCharacterDataUnicode[_unicodeFontAddress.Length][];
```

Add a row-ensure helper near `GetCharUni`:

```csharp
        private FontCharacterDataUnicode[] EnsureUniRow(byte font)
        {
            _fontDataUNICODE ??= new FontCharacterDataUnicode[_unicodeFontAddress.Length][];
            return _fontDataUNICODE[font] ??= new FontCharacterDataUnicode[0x10000];
        }
```

- [ ] **Step 5: Convert the four access sites to jagged**

Line 232 (`GetCharUni`):

```csharp
            ref var cc = ref EnsureUniRow(font)[index];
```

Line 271 (`LoadChar`, classic mul write):

```csharp
            ref var cc = ref EnsureUniRow(font)[index];
```

Lines 331 and 343 (`LoadAtlasChar`, TTF write) — replace the `_fontDataUNICODE[font, index] = ...` assignment and the `_fontDataUNICODE[font, index].Coverage` read with a local row:

```csharp
                FontCharacterDataUnicode[] row = EnsureUniRow((byte)font);
                row[index] = new FontCharacterDataUnicode
                {
                    OffsetX = offX,
                    OffsetY = offY,
                    Width = w,
                    Height = h,
                    Data = null,
                    Coverage = (w <= 0 || h <= 0 || coverage == null || coverage.Length == 0) ? null : coverage
                };

                if (row[index].Coverage == null)
                {
                    missing?.Add(index);
                }
```

- [ ] **Step 6: Add `ResetAtlasFont`**

Add near `RegisterAtlasFont`:

```csharp
        /// <summary>
        /// Frees a dynamic atlas slot: clears its TTF source/name/missing set and
        /// drops its glyph row so the slot can be reused and its memory reclaimed.
        /// Classic (non-atlas) slots are left untouched.
        /// </summary>
        public void ResetAtlasFont(int slot)
        {
            if (slot < 0 || slot >= MAX_UNICODE_FONTS || !_isAtlasFont[slot])
            {
                return;
            }

            _isAtlasFont[slot] = false;
            _atlasGlyphSources[slot] = null;
            _atlasFontNames[slot] = null;
            _atlasMissing[slot] = null;
            if (_fontDataUNICODE != null)
            {
                _fontDataUNICODE[slot] = null;
            }
        }
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~FontsLoaderJaggedTests"`
Expected: PASS (both facts).

- [ ] **Step 8: Run the existing atlas/glyph tests to confirm no regression**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AtlasFontRegistrationTests|FullyQualifiedName~TtfGlyphSourceTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/ClassicUO.Assets/FontsLoader.cs tests/ClassicUO.UnitTests/Assets/FontsLoaderJaggedTests.cs
git commit -m "refactor(fonts): jagged lazy unicode glyph store, raise slot cap to 96"
```

---

### Task 2: Font face catalog

A stable `faceId` → embedded TTF face mapping, decoupled from slots and sizes. Seeds the 7 currently-embedded faces reusing `AtlasFontRegistry`'s TTF byte cache.

**Files:**
- Create: `src/ClassicUO.Client/Resources/FontFaceCatalog.cs`
- Modify: `src/ClassicUO.Client/Resources/Loader.cs` (expose `LoadTtf` to the catalog — make it `internal static`)
- Test: `tests/ClassicUO.UnitTests/Resources/FontFaceCatalogTests.cs`

**Interfaces:**
- Consumes: `ClassicUO.Resources.AtlasFontRegistry.LoadTtf(string logicalName)` (existing private → make `internal static`), returning `byte[]`.
- Produces:
  - `readonly struct FontFace { public string Id; public string DisplayName; public string TtfResource; }`
  - `static class FontFaceCatalog` with:
    - `static IReadOnlyList<FontFace> Faces { get; }` — the 7 faces.
    - `static bool TryGet(string faceId, out FontFace face)`
    - `static byte[] GetBytes(string faceId)` — reuses `AtlasFontRegistry.LoadTtf`; null if unknown.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ClassicUO.UnitTests/Resources/FontFaceCatalogTests.cs
// SPDX-License-Identifier: BSD-2-Clause
using ClassicUO.Resources;
using Xunit;

namespace ClassicUO.UnitTests.Resources
{
    public class FontFaceCatalogTests
    {
        [Fact]
        public void Faces_ContainsSevenKnownFaces()
        {
            Assert.Equal(7, FontFaceCatalog.Faces.Count);
            Assert.Contains(FontFaceCatalog.Faces, f => f.Id == "inter");
            Assert.Contains(FontFaceCatalog.Faces, f => f.Id == "cinzel");
        }

        [Fact]
        public void TryGet_KnownFace_ReturnsIt()
        {
            Assert.True(FontFaceCatalog.TryGet("inter", out var face));
            Assert.Equal("Inter", face.DisplayName);
        }

        [Fact]
        public void TryGet_UnknownFace_False()
        {
            Assert.False(FontFaceCatalog.TryGet("nope", out _));
        }

        [Fact]
        public void GetBytes_KnownFace_ReturnsNonEmpty()
        {
            byte[] bytes = FontFaceCatalog.GetBytes("inter");
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~FontFaceCatalogTests"`
Expected: FAIL — `FontFaceCatalog` does not exist.

- [ ] **Step 3: Expose `LoadTtf`**

In `src/ClassicUO.Client/Resources/Loader.cs` change the `LoadTtf` signature (line 139) from `private static byte[] LoadTtf` to:

```csharp
        internal static byte[] LoadTtf(string logicalName)
```

- [ ] **Step 4: Create the catalog**

```csharp
// src/ClassicUO.Client/Resources/FontFaceCatalog.cs
// SPDX-License-Identifier: BSD-2-Clause
using System.Collections.Generic;

namespace ClassicUO.Resources
{
    public readonly struct FontFace
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string TtfResource;

        public FontFace(string id, string displayName, string ttfResource)
        {
            Id = id;
            DisplayName = displayName;
            TtfResource = ttfResource;
        }
    }

    public static class FontFaceCatalog
    {
        private static readonly FontFace[] _faces =
        {
            new FontFace("cinzel",        "Cinzel",          "Cinzel.ttf"),
            new FontFace("cormorant",     "Cormorant",       "Cormorant.ttf"),
            new FontFace("inter",         "Inter",           "Inter.ttf"),
            new FontFace("sourcesans",    "Source Sans",     "SourceSans.ttf"),
            new FontFace("clfuoclassic",  "CLF-UoClassic",   "CLFUoClassic.ttf"),
            new FontFace("imfellenglish", "IM Fell English", "IMFellEnglish.ttf"),
            new FontFace("medievalsharp", "MedievalSharp",   "MedievalSharp.ttf"),
        };

        public static IReadOnlyList<FontFace> Faces => _faces;

        public static bool TryGet(string faceId, out FontFace face)
        {
            foreach (FontFace f in _faces)
            {
                if (f.Id == faceId)
                {
                    face = f;
                    return true;
                }
            }
            face = default;
            return false;
        }

        public static byte[] GetBytes(string faceId)
        {
            return TryGet(faceId, out FontFace face) ? AtlasFontRegistry.LoadTtf(face.TtfResource) : null;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~FontFaceCatalogTests"`
Expected: PASS (all four facts).

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Resources/FontFaceCatalog.cs src/ClassicUO.Client/Resources/Loader.cs tests/ClassicUO.UnitTests/Resources/FontFaceCatalogTests.cs
git commit -m "feat(fonts): font face catalog decoupled from slots"
```

---

### Task 3: Dynamic slot registry

Replace the fixed boot-time `RegisterAll` with an on-demand registry: `(faceId,size) -> slot`, dedup, lazy `RegisterAtlasFont`, `Reset()` for per-profile clean state, and an exhaustion fallback.

**Files:**
- Modify: `src/ClassicUO.Client/Resources/Loader.cs` (rewrite `AtlasFontRegistry`: drop the fixed `Entries` registration, add dynamic API; keep `LoadTtf` + `_ttfCache`)
- Modify: `src/ClassicUO.Client/Client.cs:200-204` (stop calling the old `RegisterAll`; instead `AtlasFontRegistry.Attach(FileManager.Fonts)`)
- Test: `tests/ClassicUO.UnitTests/Resources/AtlasFontRegistryTests.cs`

**Interfaces:**
- Consumes: `FontsLoader.RegisterAtlasFont`, `FontsLoader.ResetAtlasFont` (Task 1), `FontFaceCatalog.GetBytes` / `TryGet` (Task 2).
- Produces (on `static class AtlasFontRegistry`):
  - `const int DynamicSlotFirst = 20;` `const int DynamicSlotLast = 95;`
  - `static void Attach(FontsLoader fonts)` — stores the loader; clears state.
  - `static bool TryResolve(string faceId, byte size, out byte slot)` — dedups; allocates + registers on miss; `false` if unknown face, corrupt TTF, or pool exhausted.
  - `static void Reset()` — frees all dynamic slots and clears the map (called on profile activation).
  - `static (byte faceIdx, byte size)? LegacySlotToFaceSize(byte legacySlot)` — maps old fixed slots 20–40 to `(faceId,size)` for profile migration (returns `null` outside 20–40).
  - Provide `LegacyFaceId(byte legacySlot)` returning the string faceId for a legacy slot (helper used by migration in Task 4).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ClassicUO.UnitTests/Resources/AtlasFontRegistryTests.cs
// SPDX-License-Identifier: BSD-2-Clause
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Resources;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests.Resources
{
    [Collection("AtlasRegistrySerial")]  // static state: do not parallelize
    public class AtlasFontRegistryTests
    {
        private static FontsLoader NewFonts() =>
            new UOFileManager(ClientVersion.CV_300, Path.GetTempPath()).Fonts;

        [Fact]
        public void Resolve_SameFaceSize_DedupsToSameSlot()
        {
            FontsLoader fonts = NewFonts();
            AtlasFontRegistry.Attach(fonts);
            AtlasFontRegistry.Reset();

            Assert.True(AtlasFontRegistry.TryResolve("inter", 16, out byte a));
            Assert.True(AtlasFontRegistry.TryResolve("inter", 16, out byte b));
            Assert.Equal(a, b);
            Assert.True(fonts.IsAtlasFont(a));
        }

        [Fact]
        public void Resolve_DifferentCombos_DistinctSlots()
        {
            FontsLoader fonts = NewFonts();
            AtlasFontRegistry.Attach(fonts);
            AtlasFontRegistry.Reset();

            Assert.True(AtlasFontRegistry.TryResolve("inter", 16, out byte a));
            Assert.True(AtlasFontRegistry.TryResolve("inter", 20, out byte b));
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Resolve_UnknownFace_False()
        {
            FontsLoader fonts = NewFonts();
            AtlasFontRegistry.Attach(fonts);
            AtlasFontRegistry.Reset();

            Assert.False(AtlasFontRegistry.TryResolve("nope", 16, out _));
        }

        [Fact]
        public void Reset_FreesSlotsForReuse()
        {
            FontsLoader fonts = NewFonts();
            AtlasFontRegistry.Attach(fonts);
            AtlasFontRegistry.Reset();

            Assert.True(AtlasFontRegistry.TryResolve("inter", 16, out byte first));
            AtlasFontRegistry.Reset();
            Assert.False(fonts.IsAtlasFont(first));

            // Next allocation reuses the first dynamic slot.
            Assert.True(AtlasFontRegistry.TryResolve("cinzel", 22, out byte again));
            Assert.Equal(AtlasFontRegistry.DynamicSlotFirst, again);
        }

        [Fact]
        public void LegacySlotToFaceSize_MapsOldFixedSlots()
        {
            Assert.Equal(("cinzel", (byte)14), AtlasFontRegistry.LegacySlotToFaceSize(20));
            Assert.Null(AtlasFontRegistry.LegacySlotToFaceSize(5));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AtlasFontRegistryTests"`
Expected: FAIL — new API absent.

- [ ] **Step 3: Rewrite `AtlasFontRegistry` in `Loader.cs`**

Replace the body of the `AtlasFontRegistry` class (keep the `Entry`/`Entries` table ONLY as a private legacy-migration table; keep `_ttfCache` + `LoadTtf`). New members:

```csharp
        public const int DynamicSlotFirst = 20;
        public const int DynamicSlotLast = 95;

        private static ClassicUO.Assets.FontsLoader _fonts;
        private static readonly Dictionary<(string face, byte size), byte> _map = new();
        private static byte _next = DynamicSlotFirst;

        public static void Attach(ClassicUO.Assets.FontsLoader fonts)
        {
            _fonts = fonts;
            Reset();
        }

        public static void Reset()
        {
            if (_fonts != null)
            {
                foreach (byte slot in _map.Values)
                {
                    _fonts.ResetAtlasFont(slot);
                }
            }
            _map.Clear();
            _next = DynamicSlotFirst;
        }

        public static bool TryResolve(string faceId, byte size, out byte slot)
        {
            slot = 0;
            if (_fonts == null || string.IsNullOrEmpty(faceId))
            {
                return false;
            }

            if (size < 8) size = 8;
            if (size > 32) size = 32;

            var key = (faceId, size);
            if (_map.TryGetValue(key, out slot))
            {
                return true;
            }

            byte[] bytes = FontFaceCatalog.GetBytes(faceId);
            if (bytes == null)
            {
                return false;                      // unknown face
            }

            if (_next > DynamicSlotLast)
            {
                Utility.Logging.Log.Warn($"AtlasFontRegistry: dynamic slot pool exhausted; '{faceId}' {size}px falls back to Classic. Restart to reclaim.");
                return false;                      // pool exhausted
            }

            byte candidate = _next;
            _fonts.RegisterAtlasFont(candidate, $"{faceId} {size}", bytes, size);
            if (!_fonts.IsAtlasFont(candidate))
            {
                return false;                      // corrupt TTF; slot left unusable
            }

            _map[key] = candidate;
            _next++;
            slot = candidate;
            return true;
        }

        public static (string face, byte size)? LegacySlotToFaceSize(byte legacySlot)
        {
            foreach (Entry e in Entries)
            {
                if (e.Slot == legacySlot)
                {
                    // e.DisplayName is like "Inter 18"; map to faceId + size.
                    int sp = e.DisplayName.LastIndexOf(' ');
                    string face = LegacyDisplayToFaceId(e.DisplayName.Substring(0, sp));
                    byte size = byte.Parse(e.DisplayName.Substring(sp + 1));
                    return (face, size);
                }
            }
            return null;
        }

        private static string LegacyDisplayToFaceId(string display) => display switch
        {
            "Cinzel" => "cinzel",
            "Cormorant" => "cormorant",
            "Inter" => "inter",
            "SourceSans" => "sourcesans",
            "CLF-UoClassic" => "clfuoclassic",
            "IM Fell English" => "imfellenglish",
            "MedievalSharp" => "medievalsharp",
            _ => "",
        };
```

Delete the old `RegisterAll(...)` method.

- [ ] **Step 4: Update the boot call in `Client.cs`**

In `src/ClassicUO.Client/Client.cs` replace the `AtlasFontRegistry.RegisterAll(FileManager.Fonts);` call (around line 204) with:

```csharp
            // Attach the dynamic TTF atlas registry; nothing is registered up
            // front — slots are created on demand when a profile's font choice
            // is first resolved. Reset()/repopulate happens on profile load.
            ClassicUO.Resources.AtlasFontRegistry.Attach(FileManager.Fonts);
```

- [ ] **Step 5: Add the xUnit serial collection**

Add to `tests/ClassicUO.UnitTests` (new file or existing collections file):

```csharp
// tests/ClassicUO.UnitTests/Resources/AtlasRegistrySerialCollection.cs
// SPDX-License-Identifier: BSD-2-Clause
using Xunit;

namespace ClassicUO.UnitTests.Resources
{
    [CollectionDefinition("AtlasRegistrySerial", DisableParallelization = true)]
    public class AtlasRegistrySerialCollection { }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~AtlasFontRegistryTests"`
Expected: PASS (all five facts).

- [ ] **Step 7: Build the client to confirm the boot call compiles**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: build succeeds (no reference to removed `RegisterAll`).

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Resources/Loader.cs src/ClassicUO.Client/Client.cs tests/ClassicUO.UnitTests/Resources/AtlasFontRegistryTests.cs tests/ClassicUO.UnitTests/Resources/AtlasRegistrySerialCollection.cs
git commit -m "feat(fonts): dynamic on-demand atlas slot registry with dedup and reset"
```

---

### Task 4: Profile font choice, location catalog, and resolution

Add per-location `FontChoice` storage, a location catalog carrying each location's classic fallback, `ResolveFont`, a registry reset on profile activation, and migration of the legacy `ChatFont`/`TooltipFont` bytes.

**Files:**
- Create: `src/ClassicUO.Client/Configuration/FontLocations.cs` (`FontChoice`, `FontLocation` ids, `LocationDef`, catalog)
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs` (add `LocationFonts` dict + `ResolveFont`; register `FontChoice` in `ProfileJsonContext`)
- Modify: `src/ClassicUO.Client/Configuration/ProfileManager.cs:37-56` (call `AtlasFontRegistry.Reset()` + migrate legacy bytes after a profile loads)
- Test: `tests/ClassicUO.UnitTests/Configuration/FontResolveTests.cs`

**Interfaces:**
- Consumes: `AtlasFontRegistry.TryResolve`, `AtlasFontRegistry.LegacySlotToFaceSize`, `AtlasFontRegistry.Reset` (Task 3).
- Produces:
  - `struct FontChoice { public string Face; public byte Size; }` — `Face` null/empty ⇒ Classic.
  - `static class FontLocation { public const string Chat = "chat"; public const string Tooltip = "tooltip"; }`
  - `readonly struct LocationDef { public string Id; public string DisplayName; public byte ClassicFont; public bool ClassicUnicode; }`
  - `static class FontLocationCatalog { static IReadOnlyList<LocationDef> Locations; static bool TryGet(string id, out LocationDef def); }`
  - On `Profile`: `Dictionary<string, FontChoice> LocationFonts { get; set; }`, and `public (byte slot, bool unicode) ResolveFont(string locationId)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ClassicUO.UnitTests/Configuration/FontResolveTests.cs
// SPDX-License-Identifier: BSD-2-Clause
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Resources;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests.Configuration
{
    [Collection("AtlasRegistrySerial")]
    public class FontResolveTests
    {
        private static void AttachFonts()
        {
            FontsLoader fonts = new UOFileManager(ClientVersion.CV_300, Path.GetTempPath()).Fonts;
            AtlasFontRegistry.Attach(fonts);
            AtlasFontRegistry.Reset();
        }

        [Fact]
        public void ResolveFont_DefaultChat_IsClassicFallback()
        {
            AttachFonts();
            var p = new Profile();
            // No LocationFonts entry => Classic => chat's classic (font 1, unicode true).
            (byte slot, bool uni) = p.ResolveFont(FontLocation.Chat);
            Assert.Equal((byte)1, slot);
            Assert.True(uni);
        }

        [Fact]
        public void ResolveFont_TtfChoice_ReturnsDynamicUnicodeSlot()
        {
            AttachFonts();
            var p = new Profile();
            p.LocationFonts[FontLocation.Chat] = new FontChoice { Face = "inter", Size = 18 };

            (byte slot, bool uni) = p.ResolveFont(FontLocation.Chat);
            Assert.True(slot >= AtlasFontRegistry.DynamicSlotFirst);
            Assert.True(uni);
        }

        [Fact]
        public void ResolveFont_UnknownFace_FallsBackToClassic()
        {
            AttachFonts();
            var p = new Profile();
            p.LocationFonts[FontLocation.Tooltip] = new FontChoice { Face = "ghost", Size = 18 };

            (byte slot, bool uni) = p.ResolveFont(FontLocation.Tooltip);
            Assert.Equal((byte)1, slot);   // tooltip classic font
            Assert.True(uni);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~FontResolveTests"`
Expected: FAIL — `FontChoice`, `FontLocation`, `LocationFonts`, `ResolveFont` absent.

- [ ] **Step 3: Create the locations file**

```csharp
// src/ClassicUO.Client/Configuration/FontLocations.cs
// SPDX-License-Identifier: BSD-2-Clause
using System.Collections.Generic;

namespace ClassicUO.Configuration
{
    // Face null/empty => Classic (use the location's classic fallback).
    public struct FontChoice
    {
        public string Face { get; set; }
        public byte Size { get; set; }
    }

    public static class FontLocation
    {
        public const string Chat = "chat";
        public const string Tooltip = "tooltip";
    }

    public readonly struct LocationDef
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly byte ClassicFont;
        public readonly bool ClassicUnicode;

        public LocationDef(string id, string displayName, byte classicFont, bool classicUnicode)
        {
            Id = id;
            DisplayName = displayName;
            ClassicFont = classicFont;
            ClassicUnicode = classicUnicode;
        }
    }

    public static class FontLocationCatalog
    {
        // Grows one entry per per-location increment. ClassicFont/ClassicUnicode
        // must equal the location's current hardcoded literal so Classic = zero change.
        private static readonly LocationDef[] _locations =
        {
            new LocationDef(FontLocation.Chat,    "Chat",    1, true),
            new LocationDef(FontLocation.Tooltip, "Tooltip", 1, true),
        };

        public static IReadOnlyList<LocationDef> Locations => _locations;

        public static bool TryGet(string id, out LocationDef def)
        {
            foreach (LocationDef d in _locations)
            {
                if (d.Id == id)
                {
                    def = d;
                    return true;
                }
            }
            def = default;
            return false;
        }
    }
}
```

- [ ] **Step 4: Add `LocationFonts` + `ResolveFont` to `Profile`**

In `src/ClassicUO.Client/Configuration/Profile.cs`, in the "fonts and speech" region near line 74, add:

```csharp
        public Dictionary<string, FontChoice> LocationFonts { get; set; } = new();

        /// <summary>
        /// Resolve a UI location's font to a (slot, unicode) draw pair. A Classic
        /// choice (or unknown/failed TTF) returns the location's classic fallback.
        /// </summary>
        public (byte slot, bool unicode) ResolveFont(string locationId)
        {
            FontLocationCatalog.TryGet(locationId, out LocationDef def);

            if (LocationFonts != null
                && LocationFonts.TryGetValue(locationId, out FontChoice choice)
                && !string.IsNullOrEmpty(choice.Face)
                && ClassicUO.Resources.AtlasFontRegistry.TryResolve(choice.Face, choice.Size, out byte slot))
            {
                return (slot, true);
            }

            return (def.ClassicFont, def.ClassicUnicode);
        }
```

Register `FontChoice` with the source-gen context: add to the attribute list on `ProfileJsonContext` (near line 27):

```csharp
    [JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, FontChoice>), GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(FontChoice), GenerationMode = JsonSourceGenerationMode.Metadata)]
```

- [ ] **Step 5: Migrate legacy bytes + reset registry on profile load**

In `src/ClassicUO.Client/Configuration/ProfileManager.cs`, after the loaded `Profile` becomes current inside `Load(...)` (around line 37–56, after deserialization succeeds and before returning), add:

```csharp
            // New per-profile atlas state, then migrate legacy ChatFont/TooltipFont
            // slot bytes into LocationFonts (once — only if not already present).
            ClassicUO.Resources.AtlasFontRegistry.Reset();
            MigrateLegacyFont(CurrentProfile, FontLocation.Chat, CurrentProfile.ChatFont);
            MigrateLegacyFont(CurrentProfile, FontLocation.Tooltip, CurrentProfile.TooltipFont);
```

Add the helper to `ProfileManager`:

```csharp
        private static void MigrateLegacyFont(Profile p, string location, byte legacySlot)
        {
            p.LocationFonts ??= new System.Collections.Generic.Dictionary<string, FontChoice>();
            if (p.LocationFonts.ContainsKey(location))
            {
                return; // already migrated / user-set
            }

            (string face, byte size)? mapped = ClassicUO.Resources.AtlasFontRegistry.LegacySlotToFaceSize(legacySlot);
            if (mapped.HasValue)
            {
                p.LocationFonts[location] = new FontChoice { Face = mapped.Value.face, Size = mapped.Value.size };
            }
            // else: legacy classic slot (< 20) -> leave absent => Classic fallback.
        }
```

(Adjust the exact insertion point to wherever `CurrentProfile` is assigned in `Load`; the reference line is `ProfileManager.cs:37`.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~FontResolveTests"`
Expected: PASS (all three facts).

- [ ] **Step 7: Build the client**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: build succeeds (source-gen picks up `FontChoice`).

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Configuration/FontLocations.cs src/ClassicUO.Client/Configuration/Profile.cs src/ClassicUO.Client/Configuration/ProfileManager.cs tests/ClassicUO.UnitTests/Configuration/FontResolveTests.cs
git commit -m "feat(fonts): per-location FontChoice, ResolveFont, and legacy migration"
```

---

### Task 5: Options UI row + wire Chat and Tooltip

Add a reusable `FontLocationRow` control (face dropdown + size dropdown, size hidden for Classic) and use it for Chat and Tooltip, replacing their `FontSelector` radio lists. This validates the whole path end to end.

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Controls/FontLocationRow.cs`
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` (replace `_fontSelectorChat` at 2349 & apply at 4757-4764; replace `_tooltip_font_selector` at 2280 & apply at 5063)
- Test: `tests/ClassicUO.UnitTests/UI/FontLocationRowTests.cs`

**Interfaces:**
- Consumes: `Combobox` control (`src/ClassicUO.Client/Game/UI/Controls/Combobox.cs`), `FontFaceCatalog.Faces`, `Profile.LocationFonts`, `FontChoice`.
- Produces (on `FontLocationRow : Control`):
  - `FontLocationRow(int x, int y, FontChoice current)` — first combobox item is "Classic", then catalog faces; size combobox lists 8..32; size hidden when Classic selected.
  - `FontChoice GetChoice()` — `Face=""` when Classic selected, else `(faceId, size)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ClassicUO.UnitTests/UI/FontLocationRowTests.cs
// SPDX-License-Identifier: BSD-2-Clause
using ClassicUO.Configuration;
using ClassicUO.Game.UI.Controls;
using Xunit;

namespace ClassicUO.UnitTests.UI
{
    public class FontLocationRowTests
    {
        [Fact]
        public void GetChoice_DefaultClassic_EmptyFace()
        {
            var row = new FontLocationRow(0, 0, new FontChoice { Face = "", Size = 0 });
            FontChoice c = row.GetChoice();
            Assert.True(string.IsNullOrEmpty(c.Face));
        }

        [Fact]
        public void GetChoice_RoundTripsTtfChoice()
        {
            var row = new FontLocationRow(0, 0, new FontChoice { Face = "inter", Size = 18 });
            FontChoice c = row.GetChoice();
            Assert.Equal("inter", c.Face);
            Assert.Equal((byte)18, c.Size);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~FontLocationRowTests"`
Expected: FAIL — `FontLocationRow` absent.

- [ ] **Step 3: Create `FontLocationRow`**

```csharp
// src/ClassicUO.Client/Game/UI/Controls/FontLocationRow.cs
// SPDX-License-Identifier: BSD-2-Clause
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Resources;

namespace ClassicUO.Game.UI.Controls
{
    internal sealed class FontLocationRow : Control
    {
        private const int SIZE_MIN = 8;
        private const int SIZE_MAX = 32;

        private readonly Combobox _face;
        private readonly Combobox _size;
        private readonly string[] _faceIds;   // index 0 = "" (Classic)

        public FontLocationRow(int x, int y, FontChoice current)
        {
            X = x;
            Y = y;
            AcceptMouseInput = true;

            var faceLabels = new List<string> { "Classic" };
            var ids = new List<string> { "" };
            foreach (FontFace f in FontFaceCatalog.Faces)
            {
                faceLabels.Add(f.DisplayName);
                ids.Add(f.Id);
            }
            _faceIds = ids.ToArray();

            int faceIndex = 0;
            if (!string.IsNullOrEmpty(current.Face))
            {
                int idx = System.Array.IndexOf(_faceIds, current.Face);
                if (idx > 0) faceIndex = idx;
            }

            _face = new Combobox(0, 0, 140, faceLabels.ToArray(), faceIndex);
            Add(_face);

            var sizes = new List<string>();
            for (int s = SIZE_MIN; s <= SIZE_MAX; s++) sizes.Add(s.ToString());
            int sizeIndex = System.Math.Clamp(current.Size, SIZE_MIN, SIZE_MAX) - SIZE_MIN;
            _size = new Combobox(150, 0, 60, sizes.ToArray(), sizeIndex) { IsVisible = faceIndex != 0 };
            Add(_size);

            _face.OnOptionSelected += (_, i) => _size.IsVisible = i != 0;

            Width = 210;
            Height = 25;
        }

        public FontChoice GetChoice()
        {
            int fi = _face.SelectedIndex;
            if (fi <= 0)
            {
                return new FontChoice { Face = "", Size = 0 };
            }
            return new FontChoice { Face = _faceIds[fi], Size = (byte)(_size.SelectedIndex + SIZE_MIN) };
        }
    }
}
```

(Verified against `Combobox.cs`: constructor `Combobox(int x, int y, int width, string[] items, int selected = -1, ...)`, `public int SelectedIndex`, and `public event EventHandler<int> OnOptionSelected` — the code above matches these exactly.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~FontLocationRowTests"`
Expected: PASS.

- [ ] **Step 5: Wire Chat in `OptionsGump`**

Replace the `_fontSelectorChat = new FontSelector(...)` construction (around 2349) with a `FontLocationRow` built from `_currentProfile.LocationFonts` for `FontLocation.Chat`:

```csharp
            _chatFontRow = new FontLocationRow(0, 0,
                _currentProfile.LocationFonts != null
                && _currentProfile.LocationFonts.TryGetValue(FontLocation.Chat, out var chatChoice)
                    ? chatChoice : new FontChoice { Face = "" });
            // add _chatFontRow to the same parent/offset the old selector used
```

Add the field near line 67:

```csharp
        private FontLocationRow _chatFontRow;
```

Replace the Chat apply block (4757-4764) with:

```csharp
            FontChoice chatChoice = _chatFontRow.GetChoice();
            _currentProfile.LocationFonts ??= new System.Collections.Generic.Dictionary<string, FontChoice>();
            _currentProfile.LocationFonts[FontLocation.Chat] = chatChoice;
            (byte chatSlot, bool chatUni) = _currentProfile.ResolveFont(FontLocation.Chat);
            _currentProfile.ChatFont = chatSlot; // keep legacy field in sync for existing readers
            UIManager.SystemChat.TextBoxControl.Font = chatSlot;
```

- [ ] **Step 6: Wire Tooltip in `OptionsGump`**

Replace the `_tooltip_font_selector = new FontSelector(...)` construction (2280) with a `FontLocationRow` for `FontLocation.Tooltip` (field `_tooltipFontRow`), and replace the apply at 5063 with:

```csharp
            FontChoice tipChoice = _tooltipFontRow.GetChoice();
            _currentProfile.LocationFonts ??= new System.Collections.Generic.Dictionary<string, FontChoice>();
            _currentProfile.LocationFonts[FontLocation.Tooltip] = tipChoice;
            (byte tipSlot, _) = _currentProfile.ResolveFont(FontLocation.Tooltip);
            _currentProfile.TooltipFont = tipSlot;
```

- [ ] **Step 7: Build and run the full unit suite**

Run: `dotnet build ClassicUO.sln -c Debug`
Then: `dotnet test tests/ClassicUO.UnitTests`
Expected: build succeeds; all tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/FontLocationRow.cs src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs tests/ClassicUO.UnitTests/UI/FontLocationRowTests.cs
git commit -m "feat(fonts): Options font row (face+size) wired for Chat and Tooltip"
```

---

## Adding a location later (per-increment recipe)

For each subsequent location (journal, overhead names, spellbook, …), one small increment:

1. Add a `LocationDef` to `FontLocationCatalog._locations` with the location's **current** hardcoded font number + unicode flag as the classic fallback.
2. Add a `FontLocation.<Name>` const id.
3. Replace that location's hardcoded `RenderedText.Create(..., font: N, isunicode: X)` (or `Label`/`TextBox` `font:`) call with:
   ```csharp
   var (f, uni) = profile.ResolveFont(FontLocation.<Name>);
   RenderedText.Create(text, font: f, isunicode: uni);
   ```
4. Add a `FontLocationRow` for it in `OptionsGump` + an apply line writing `LocationFonts[id]`.
5. Test: `ResolveFont(<Name>)` returns the classic fallback by default and a dynamic unicode slot when a TTF choice is set.

Each such increment is independently testable and shippable.

## Self-Review notes

- **Spec coverage:** face catalog (Task 2), dynamic registry + dedup + reset + exhaustion (Task 3), jagged lazy memory + cap bump (Task 1), FontChoice + ResolveFont + migration (Task 4), Options face+size row + Chat/Tooltip wiring (Task 5), per-location recipe (final section). All spec §1–§5 covered.
- **Type consistency:** `TryResolve(string,byte,out byte)`, `ResolveFont(string)->(byte,bool)`, `FontChoice{string Face;byte Size}`, `LocationDef{Id,DisplayName,ClassicFont,ClassicUnicode}` used identically across tasks.
- **Combobox API:** confirmed against `Combobox.cs` — constructor, `SelectedIndex`, and `OnOptionSelected` match the code in Task 5.
