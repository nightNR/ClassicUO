# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

ClassicUO is an open-source reimplementation of the Ultima Online Classic Client,
written in C# on the [FNA-XNA](https://fna-xna.github.io/) framework. It runs
natively on Windows (DX11/OpenGL/Vulkan), Linux, macOS, and the browser. The
repo distributes no copyrighted game assets — a legally obtained UO Classic
Client data directory is required at runtime (`UltimaOnlineDirectory` in
`settings.json`).

## Toolchain

- `.NET 10` (`net10.0`), `LangVersion=Preview`, `AllowUnsafeBlocks=true`. Shared
  build settings live in `src/Directory.Build.props`.
- Submodules matter: clone with `--recursive` (FNA and other deps live under
  `external/`). `external/FNA` is gitignored as a working dir but referenced by
  project (`external/FNA/FNA.Core.csproj`).
- The client uses NativeAOT (`PublishAot=true`) for the default `cuo` artifact.

## Build / test / run

```bash
# Build everything
dotnet build ClassicUO.sln -c Debug

# Release distributable (writes to bin/dist). Use Git Bash on Windows.
cd scripts && bash build-naot.sh

# Run all unit tests
dotnet test tests/ClassicUO.UnitTests
dotnet test tests/ClassicUO.BootstrapHost.Tests

# Run a single test (xUnit) by fully-qualified name or substring
dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~SomeTestName"
```

Both test projects use **xUnit**. `ClassicUO.Client` exposes internals to
`ClassicUO.UnitTests` via `InternalsVisibleToAttribute`.

Build output: Debug → `bin/Debug/`, Release → `bin/Release/`, publish →
`bin/dist/`. External native deps (SDL, FAudio, etc. from `external/x64|lib64|osx`)
are copied next to the binary by MSBuild targets at build/publish time.

## Architecture: the three apphost / client split

The client logic is one NativeAOT C# library (`cuo`) that is *hosted* by a
separate launcher process. There are two hosts; understand which one you're
touching:

| Apphost                     | Runtime | Loads plugins | Notes |
| --------------------------- | ------- | ------------- | ----- |
| `ClassicUO.Bootstrap`       | net472  | v1 (legacy, e.g. Razor) | Ships as `ClassicUO.exe`; Windows/Mono/WINE |
| `ClassicUO.BootstrapHost`   | net10.0 | v2 (current contract)   | Native on all OSes, no WINE/Mono |

Both hosts `LoadLibrary`/`dlopen` the same `cuo` shared library and fill a
`HostBindings` function-pointer table. `cuo` itself is host-agnostic.

- `src/ClassicUO.Client` — the actual client. By default publishes `cuo.exe`
  (NativeAOT exe). Set `-p:BootstrapHostMode=true` to instead emit a shared
  library (`cuo.dll`/`.so`/`.dylib`) with `Initialize` exported via
  `[UnmanagedCallersOnly]` — that's the artifact an apphost loads.
- `src/ClassicUO.Bootstrap` — legacy net472 launcher.
- `src/ClassicUO.BootstrapHost` — current net10 launcher. Loads each v2 plugin
  into its own non-collectible `AssemblyLoadContext`. Uses ReadyToRun +
  composite (NOT full NativeAOT) on purpose: the host JITs plugin IL at runtime,
  which NativeAOT would forbid. See `src/ClassicUO.BootstrapHost/README.md`.

## Plugin v2 contract

`src/ClassicUO.PluginApi` is the public, reflection-free plugin contract
(`net10.0`, BSD-2). A v2 plugin is a class library implementing `IPlugin` that
registers itself from a `[ModuleInitializer]` via `PluginRegistry.Register(...)`
— the host runs the module constructor and drains the registry; no `GetTypes()`,
no `Activator.CreateInstance`. Discovered at `Data/Plugins/<name>/<name>.dll`
(folder name and DLL name must match).

Key surfaces on `IPluginContext`: `Packets` (observe/block server↔client
packets as `ReadOnlySpan<byte>` over the live buffer — never store the span),
`Input` (hotkey/mouse, return `false` to block), `Actions`, `Client`, `Game`
(game-thread marshaling via `Post`/`PostAsync`), plus lifecycle events. Out of
scope today: packet mutation, in-world UI overlays (`IGumpHost`), world-state
queries (`IWorld`), SDL event forwarding. Full reference:
`src/ClassicUO.PluginApi/README.md`. Worked example: `samples/HelloPlugin`.

This is the active line of work (branch `feat/cuo_plugin_v2`). The legacy v1
plugin path still lives in `src/ClassicUO.Client/Network/Plugin.cs` and
`PluginHost.cs`.

## Architecture: inside the client

- `Main.cs` / `Client.cs` / `GameController.cs` — entry, bootstrap, and the
  top-level game loop (update/draw tick driver).
- `Game/World.cs` — root of live world state (player, mobiles, items, map).
- `Game/Scenes/` — `Scene` subclasses drive the active screen: `LoginScene`,
  `GameScene` (with `GameSceneDrawingSorting`, `GameSceneInputHandler`,
  `RenderLists`), `MainScene`. The active scene owns update+draw.
- `Game/Managers/` — one manager per subsystem (UI, targeting, macros, hotkeys,
  party, journal, effects, audio, containers, …). Most are reached through
  `World` or the active scene. `UIManager` owns all gumps/controls.
- `Game/UI/` — `Gumps/` (windows) and `Controls/` (widgets); UO's UI is a tree
  of gumps built from these.
- `Network/` — `NetClient` (socket + send/receive), `PacketHandlers.cs`
  (incoming dispatch table), `OutgoingPackets.cs`, encryption (`Encryption/`),
  `Huffman.cs`, `CircularBuffer.cs`. This is where the UO wire protocol lives.
- `Game/Data/` and `ClassicUO.Assets` — UO data file (mul/uop) loading and the
  in-memory representation of art, tiles, cliloc, etc.
- `ClassicUO.IO` — low-level file/stream reading. `ClassicUO.Renderer` — the
  batched sprite/graphics layer over FNA. `ClassicUO.Utility` — shared helpers.

## Conventions

- Performance-sensitive: spans, pooled lists, and `unsafe` are used
  deliberately throughout the render and network paths. Match the surrounding
  style rather than allocating freely in hot loops.
- License headers are enforced (`ClassicUO.licenseheader`); new source files
  carry the project BSD header.
- `samples/`, `tools/ManifestCreator`, and `tools/stage-bootstraphost.ps1`
  support plugin and release workflows, not the client build itself.
