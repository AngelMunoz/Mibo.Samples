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

### MinimalEvsm

A minimal shadow mapping demo (directional sun + spot light) using raw raylib-cs without Mibo's Elmish loop. Demonstrates low-level shader setup, shadow map rendering, and manual game loop control.

```bash
dotnet run --project MinimalEvsm
```

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

### PingPong

A networked multiplayer Pong game with a client-server architecture over WebSockets. The server runs game logic and broadcasts state; the client renders locally and sends input.

```bash
# Start the server first
dotnet run --project PingPong/Server

# Then start one or two clients
dotnet run --project PingPong/Client
```

Controls: **Mouse Y-axis** to move your assigned paddle (Left or Right).

## Building

```bash
dotnet build
```

## Publishing

```bash
dotnet publish -c Release
```
