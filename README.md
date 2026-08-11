# Mibo Samples

Sample projects demonstrating [Mibo](https://github.com/AngelMunoz/Mibo) — an Elmish-based F# game framework with raylib-cs and MonoGame backends.

For framework documentation and setup instructions, see the [Mibo README](Mibo/README.md).

## Getting Started

Clone the repo with submodules:

```bash
git clone --recurse-submodules git@github.com:AngelMunoz/Mibo.Samples.git
cd Mibo.Samples
```

If you already cloned without submodules:

```bash
git submodule update --init --recursive
```

The `Mibo/` directory contains the [Mibo](https://github.com/AngelMunoz/Mibo) framework as a git submodule on the `main` branch.

## Prerequisites

- .NET SDK 10 or later
- A working OpenGL setup

## Samples

All samples share a single repo-root `assets/` directory (referenced, never duplicated — each `.fsproj` copies only the subset it needs into its output).

### Platformer

A 2D side-scrolling platformer with procedural world generation, sprite animation, lighting, particles, and sound. Uses Mibo's Elmish architecture with `InputMap`, `AnimatedSprite`, `CellGrid2D`, and `LightContext2D`.

```bash
# raylib backend (any platform)
dotnet run --project Platformer/Raylib

# MonoGame backends
dotnet run --project Platformer/MonoDX12   # DirectX 12 (Windows)
dotnet run --project Platformer/MonoVK     # Vulkan
```

Controls: **WASD/Arrows** to move, **Space** to jump, **R** to respawn.

### Platformer3D

A 3D platformer with procedurally generated voxel terrain, PBR lighting, shadow atlas, 3D character animation, minimap overlay, and physics. Showcases Mibo's `Renderer3D`, `ForwardPbrPipeline`, and `Animation3DState`.

```bash
# raylib backend (any platform)
dotnet run --project Platformer3D/Raylib

# MonoGame backends
dotnet run --project Platformer3D/MonoDX12   # DirectX 12 (Windows)
dotnet run --project Platformer3D/MonoDX11   # DirectX 11 (Windows)
dotnet run --project Platformer3D/MonoGL     # OpenGL
dotnet run --project Platformer3D/MonoVK     # Vulkan
```

Controls: **WASD** (camera-relative movement), **Space** to jump, **Q/E** rotate camera, **PageUp/PageDown** tilt camera, **R** to respawn.

### SpaceBattle

A turn-based tactical strategy game on a hex grid with fog of war, laser combat, particle effects, faction-based turns (Human + AI), and animated unit movement. Demonstrates complex game state management, hex grid spatial queries, and multi-phase turn resolution.

```bash
dotnet run --project SpaceBattle
```

Controls: **Left-click** to select/move units, **Right-click** for unit info, **Scroll** to zoom, **WASD** to pan camera, **Space** to end turn, **R** to restart.

### ModelProbe

A minimal 3D rendering probe for the MonoGame backends: five kenney blocks rendered non-instanced (`Draw3D.drawModel`), the same blocks instanced (`Draw3D.drawInstanced`), and both on a floor with PBR lighting and shadow atlas — three zones in one frame for side-by-side backend comparison.

```bash
dotnet run --project ModelProbe/MonoDX12     # DirectX 12
dotnet run --project ModelProbe/MonoVK       # Vulkan
```

Controls: **Arrows** to orbit, **W/S** to zoom, **A/D** and **PageUp/PageDown** to pan, **0–3** camera presets per zone.

### Defli

A tower-defense game running on Mibo's **adaptive data** architecture (`Mibo.Adaptive` — AdaptiveSlop-powered roots/projections with no `Msg`/`Cmd`/`Sub`): the sim is a composition root of adaptive roots and projections, the router translates system events in place, and the renderers read a forced `RenderFrame` snapshot — no graph access at draw time. The same sim runs on raylib and four MonoGame backends. See [Defli/README.md](Defli/README.md) for the adaptive trace assessment.

```bash
# raylib backend (any platform)
dotnet run --project Defli/Raylib

# MonoGame backends
dotnet run --project Defli/MonoDX12   # DirectX 12 (Windows)
dotnet run --project Defli/MonoDX11   # DirectX 11 (Windows)
dotnet run --project Defli/MonoGL     # OpenGL
dotnet run --project Defli/MonoVK     # Vulkan
```

Controls: **Left-click** to build the selected tower, **1/2/3** to select arrow/frost/cannon, **right-click** to upgrade, **Space/Enter** to start the next wave, **WASD/arrows or middle-drag** to pan, **wheel** to zoom, **Home** to reset the camera, **F3** diagnostics, **R** to restart after game over.

```bash
# Run the test suite
dotnet test Defli/Shared.Tests
```

### FPSSample

A horror-themed first-person shooter built with Mibo's **Composable Systems**, **Commands**, and **Service-DI** patterns. The same game logic runs on two backends — raylib-cs and MonoGame — with zero game-logic duplication. Features per-system sub-models, a router-style `update` that translates events into cross-system `Cmd`, a `System` pipeline with a readonly snapshot boundary, and a blended `IAudioService` (one-shot SFX via `Cmd` events, looping footsteps derived from the snapshot). See [FPSSample/README.md](FPSSample/README.md) for the project layout and [FPSSample/Shared/README.md](FPSSample/Shared/README.md) for the full architecture guide.

```bash
# raylib backend (any platform)
dotnet run --project FPSSample/Raylib

# MonoGame DesktopGL backend (any platform)
dotnet run --project FPSSample/MonoGL

# MonoGame WindowsDX (DirectX 11) backend (Windows only)
dotnet run --project FPSSample/MonoDX11

# MonoGame DirectX 12 backend (Windows only)
dotnet run --project FPSSample/MonoDX12

# MonoGame Vulkan backend
dotnet run --project FPSSample/MonoVK
```

Controls: **WASD/Arrows** to move, **Mouse** to look, **Left-click** to shoot, **Right-click/R** to reload (also restart on game over), **Space** to jump, **Left Shift** to sprint.

```bash
# Run the test suite
dotnet test FPSSample/Shared.Tests
```

### PingPong

A networked multiplayer Pong game with a client-server architecture over WebSockets. The server runs game logic and broadcasts state; the client renders locally and sends input.

```bash
# Start the server first
dotnet run --project PingPong/Server

# Then start one or two clients
dotnet run --project PingPong/Raylib
```

Controls: **Mouse Y-axis** to move your assigned paddle (Left or Right).

### BoneProbe

A CLI diagnostic tool for inspecting glTF/GLB models and verifying bone-palette math. Two modes: raw Assimp scene dump (meshes, bones, animation channels) and Mibo bone-palette verification (bind-pose invariant: `invBind[i] * worldPose[i] ≈ Identity`). Optimized for LLM consumption with compact, line-oriented output and optional verbosity/focus filtering.

```bash
# Raw mode dump
dotnet run --project BoneProbe -- raw assets/kenney_platformer-kit/Models/character-oobi.glb

# Palette mode with focus on Hips bones
dotnet run --project BoneProbe -- palette assets/kenney_platformer-kit/Models/character-oobi.glb -f Hips

# Summary verbosity (counts only)
dotnet run --project BoneProbe -- raw assets/kenney_platformer-kit/Models/character-oobi.glb -v summary
```

Controls: **`-v full|summary`** (detail level), **`-f <name>`** (substring filter on node/bone/clip names).

> Uses the `MonoGame.Framework.DesktopGL` (OpenGL) backend, so `BoneProbe` runs cross-platform on any .NET 8+ runtime.

### AnimatedInstancing

A skinned + instanced rendering probe: crowds of 500–10k animated mannequins drawn with `animatedModelInstanced` to measure vertex-texture-fetch skinning throughput. On the OpenGL backend the instanced skinning silently falls back to per-instance skinned draws (the MonoGL client exists to verify that path).

```bash
# raylib backend (any platform)
dotnet run --project AnimatedInstancing/Raylib

# MonoGame backends
dotnet run --project AnimatedInstancing/MonoDX12   # DirectX 12 (Windows)
dotnet run --project AnimatedInstancing/MonoDX11   # DirectX 11 (Windows)
dotnet run --project AnimatedInstancing/MonoGL     # OpenGL (fallback path)
dotnet run --project AnimatedInstancing/MonoVK     # Vulkan
```

## Building

```bash
dotnet build
```

## Publishing

```bash
dotnet publish -c Release
```
