# FPSSample

A first-person shooter built with Mibo's **Composable Systems**, **Commands**, and **Service-DI** patterns. The same game logic runs on two backends — [raylib-cs](https://github.com/raylib-cs/raylib-cs) and [MonoGame](https://www.monogame.net/) — without a single line of game-logic duplication.

## What this sample demonstrates

- **Router-style `update`** — `Systems.fs` dispatches each `Msg` to its owning sub-system and translates the sub-system's returned `Event` into cross-system `Cmd<Msg>` for other systems (mirroring SpaceBattle's `Program.fs`).
- **Per-system sub-models** — `GameModel` aggregates `PlayerModel`, `WeaponModel`, `EffectModel`, `EnemyModel`, `PickupModel`. Each system mutates only its own slice.
- **`System` pipeline with snapshot boundary** — the Tick handler runs `start | pipeMutable | toReadonly | pipe | finish`, enforcing at compile time that mutation phases finish before readonly query/dispatch phases read state.
- **Blended `IAudioService`** — one-shot SFX arrive as `AudioMsg` events via `Cmd` (drained by `Consume`); looping footsteps are derived from the snapshot each frame (idempotent start/stop against native `isPlaying`). No audio flags in Elmish.
- **Backend-agnostic core** — the entire game (`Systems.fs`, `Combat.fs`, `EnemyAi.fs`, `Physics.fs`) depends only on `System.Numerics` and `Mibo.Core`. Backends differ only in rendering, audio, and composition root.

For the full architecture walkthrough — router diagram, systems table, Intent/Event translation tables, message-flow walkthroughs (shoot, reload, enemy-attack, pickup, game-over), and the audio Consume+Update split — see **[Shared/README.md](Shared/README.md)**.

## Project layout

```
FPSSample/
├── Shared/         ← Backend-agnostic game logic (the core architecture lives here)
├── Raylib/         ← raylib-cs backend (3D rendering, audio, composition root)
├── MonoShared/     ← MonoGame backend (3D rendering, audio — shared by the two clients below)
├── MonoDesktop/    ← MonoGame DesktopGL thin client
├── MonoWindowsDX/  ← MonoGame WindowsDX thin client
├── Shared.Tests/   ← Expecto tests (subsystem correctness, Msg processing, Event→Cmd translation)
└── assets/         ← Shared game assets (models, sounds)
```

### Why `Shared/`?

`Shared/` contains **all** the game logic: sub-models, the router (`Systems.fs`), combat, enemy AI, physics, level generation, HUD layout math, and the `Env` service interfaces. It references only `Mibo.Core` (which has no raylib or MonoGame dependency) and `System.Numerics`. This means the gameplay code is 100% backend-agnostic — it compiles and runs identically on both raylib and MonoGame.

### Why `MonoShared/`?

MonoGame requires platform-specific host packages (`MonoGame.Framework.DesktopGL` vs `MonoGame.Framework.WindowsDX`) that can't coexist in a single project. `MonoShared/` holds all the MonoGame-specific rendering, audio, and animation code **once**, then the two thin client projects (`MonoDesktop/`, `MonoWindowsDX/`) each reference it and pull in their respective framework package. Each client is a 3-file entry point (`Program.fs`, `app.manifest` for WindowsDX) — no game logic duplicated.

### Why two MonoGame clients?

`MonoDesktop` (DesktopGL) runs on any .NET 10 platform (Windows, Linux, macOS) using OpenGL. `MonoWindowsDX` targets Windows specifically with DirectX. They share the same `MonoShared/` code and differ only in the framework package and a Windows-only `.fsproj` configuration. This lets you choose your native backend without touching game code.

## Running

### raylib backend

```bash
dotnet run --project FPSSample/Raylib
```

### MonoGame (DesktopGL — any platform)

```bash
dotnet run --project FPSSample/MonoDesktop
```

### MonoGame (WindowsDX — Windows only, DirectX)

```bash
dotnet run --project FPSSample/MonoWindowsDX
```

## Controls

- **WASD / Arrows** — move
- **Mouse** — look (camera yaw/pitch)
- **Left-click** — shoot
- **Right-click / R** — reload (also restart on game over)
- **Space** — jump
- **Left Shift** — sprint

## Gameplay

A horror-themed FPS arena: navigate a torch-lit level, shoot enemies (with muzzle flash, recoil, and smoke effects), pick up health/ammo, and survive. Enemies chase when in range, attack when close, and emit positional robotic/child-laugh/bite sounds for atmosphere. Taking damage triggers a screen hit-flash. When health hits zero, press **R** to restart.

## Testing

```bash
dotnet test FPSSample/Shared.Tests
```

Tests cover subsystem update correctness, per-`Msg` input/output processing, and Event→Cmd translation (109 tests). The test suite uses a recording `IAudioService` to verify that the router emits the correct `AudioMsg` values.