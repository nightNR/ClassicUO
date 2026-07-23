# FontAtlasBaker

Build-time console tool that rasterizes the project's TTF font sources
(under `Assets/fonts/<Family>/static/`) into the committed `*.uofont`
binary atlas files under `Assets/fonts/atlas/`, using
`ClassicUO.Assets.AtlasFontFile` for serialization and StbTrueTypeSharp
for headless TrueType rasterization (no `GraphicsDevice` required).

This project is intentionally **not** part of `ClassicUO.sln` and is never
built by `dotnet build ClassicUO.sln`. The baked `.uofont` files are
committed to the repo, so the tool only needs to be re-run when a source
TTF changes, a new family/size is added, or the atlas format changes.

## Regenerating the atlases

```bash
dotnet run --project tools/FontAtlasBaker -c Release
```

Baked families/sizes (14/18/22 px), codepoint range `0x20`..`0x17F`
(ASCII + Latin-1 Supplement + Latin Extended-A, which covers Slovak/Czech
diacritics):

- `Cinzel` <- `Assets/fonts/Cinzel/static/Cinzel-SemiBold.ttf`
- `Cormorant` <- `Assets/fonts/Cormorant_Garamond/static/CormorantGaramond-SemiBold.ttf`
- `Inter` <- `Assets/fonts/Inter/static/Inter_18pt-Regular.ttf`
- `SourceSans` <- `Assets/fonts/Source_Sans_3/static/SourceSans3-Regular.ttf`

The tool warns to stderr for any codepoint a font lacks a glyph for, and
hard-fails (non-zero exit code) if any Slovak/Czech letter is missing from
any family.
