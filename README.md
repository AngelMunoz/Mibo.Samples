# Mibo.Raylib Samples

Sample projects demonstrating [Mibo.Raylib](https://github.com/AngelMunoz/Mibo) — an Elmish-based F# game framework built on raylib-cs.

For framework documentation and setup instructions, see the [Mibo.Raylib README](Mibo/README.md).

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

The `Mibo/` directory contains the [Mibo.Raylib](https://github.com/AngelMunoz/Mibo) framework as a git submodule on the `vnext` branch.

## Prerequisites

- .NET SDK 10 or later
- A working OpenGL setup

## Samples

### PlatformerSample

A 2D side-scrolling platformer with procedural world generation, sprite animation, lighting, particles, and sound. Uses Mibo's Elmish architecture with `InputMap`, `AnimatedSprite`, `CellGrid2D`, and `LightContext2D`.

```bash
dotnet run --project PlatformerSample
```

Controls: **WASD/Arrows** to move, **Space** to jump, **R** to respawn.

### ThreeDSample

A 3D platformer with procedurally generated voxel terrain, PBR lighting, shadow atlas, 3D character animation, minimap overlay, and physics. Showcases Mibo's `Renderer3D`, `ForwardPbrPipeline`, and `Animation3DState`.

```bash
dotnet run --project ThreeDSample
```

Controls: **WASD** (camera-relative movement), **Space** to jump, **Q/E** rotate camera, **PageUp/PageDown** tilt camera, **R** to respawn.

### SpaceBattle

A turn-based tactical strategy game on a hex grid with fog of war, laser combat, particle effects, faction-based turns (Human + AI), and animated unit movement. Demonstrates complex game state management, hex grid spatial queries, and multi-phase turn resolution.

```bash
dotnet run --project SpaceBattle
```

Controls: **Left-click** to select/move units, **Right-click** for unit info, **Scroll** to zoom, **WASD** to pan camera, **Space** to end turn, **R** to restart.

### FPSSample

A horror-themed first-person shooter built with Mibo's **Composable Systems**, **Commands**, and **Service-DI** patterns. The same game logic runs on two backends — raylib-cs and MonoGame — with zero game-logic duplication. Features per-system sub-models, a router-style `update` that translates events into cross-system `Cmd`, a `System` pipeline with a readonly snapshot boundary, and a blended `IAudioService` (one-shot SFX via `Cmd` events, looping footsteps derived from the snapshot). See [FPSSample/README.md](FPSSample/README.md) for the project layout and [FPSSample/Shared/README.md](FPSSample/Shared/README.md) for the full architecture guide.

```bash
# raylib backend (any platform)
dotnet run --project FPSSample/Raylib

# MonoGame DesktopGL backend (any platform)
dotnet run --project FPSSample/MonoDesktop

# MonoGame WindowsDX backend (Windows only, DirectX)
dotnet run --project FPSSample/MonoWindowsDX
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
dotnet run --project PingPong/Client
```

Controls: **Mouse Y-axis** to move your assigned paddle (Left or Right).

### BoneProbe

A CLI diagnostic tool for inspecting glTF/GLB models and verifying bone-palette math. Two modes: raw Assimp scene dump (meshes, bones, animation channels) and Mibo bone-palette verification (bind-pose invariant: `invBind[i] * worldPose[i] ≈ Identity`). Optimized for LLM consumption with compact, line-oriented output and optional verbosity/focus filtering.

```bash
# Raw mode dump
dotnet run --project BoneProbe -- raw ThreeDSample/assets/kenney_platformer-kit/Models/character-oobi.glb

# Palette mode with focus on Hips bones
dotnet run --project BoneProbe -- palette ThreeDSample/assets/kenney_platformer-kit/Models/character-oobi.glb -f Hips

# Summary verbosity (counts only)
dotnet run --project BoneProbe -- raw ThreeDSample/assets/kenney_platformer-kit/Models/character-oobi.glb -v summary
```

Controls: **`-v full|summary`** (detail level), **`-f <name>`** (substring filter on node/bone/clip names).

> Uses the `MonoGame.Framework.DesktopGL` (OpenGL) backend, so `BoneProbe` runs cross-platform on any .NET 8+ runtime.

## Building

```bash
dotnet build
```

## Publishing

```bash
dotnet publish -c Release
```
