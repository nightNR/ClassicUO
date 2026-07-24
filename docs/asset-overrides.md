# Asset Overrides

Drop replacement images and sounds into an `Overrides/` folder next to the
client to override built-in UO assets. No repacking, no tools.

Enabled automatically when `Overrides/` exists. Set
`"enable_asset_overrides": false` in `settings.json` to force it off.

## Folder layout

```
Overrides/
  Gump/     <id>.png            # UI graphics
  Art/      <id>.png            # static item art (natural static id)
  Land/     <id>.png            # land tiles
  Texmap/   <id>.png            # land texture maps
  Light/    <id>.png            # light sprites
  Anim/     <id>/<group>_<dir>_<frame>.png
  Sound/    <id>.wav            # PCM 22050 Hz, 16-bit, mono
```

- IDs are decimal (`1234.png`) or hex (`0x04D2.png`).
- Images are PNG (RGBA; `0` alpha = transparent).
- Sounds are WAV, PCM 22050 Hz / 16-bit / mono. Other formats are ignored
  (a warning is logged and the built-in sound plays).
- Overrides are scanned once at startup and decoded on first use. Restart the
  client to pick up added/changed files.

## Draw offset (optional)

Static art and animation frames draw relative to a center point. If your
replacement is a different size and needs precise placement, add a sidecar
JSON next to the image:

- single image: `<id>.json` — always the decimal id, even if the image
  itself is named in hex (e.g. `Art/0x10.png` needs sidecar `16.json`, not
  `0x10.json`)
- animation frame: `<group>_<dir>_<frame>.json`

```json
{ "centerX": 12, "centerY": 40 }
```

Without a sidecar, animation frames keep the built-in frame's center;
gumps, static art, texmaps, and lights use the default origin (top-left,
0,0).
