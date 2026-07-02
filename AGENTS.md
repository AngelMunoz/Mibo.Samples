# Mibo.Samples

Sample games that validate the Mibo framework through real usage and test API consistency, usefulness, and ergonomics. The `Mibo/` directory is a [git submodule](https://github.com/AngelMunoz/Mibo) containing the framework source.

## Purpose

This repo exists for two reasons:

1. **Validate framework changes.** When working on a feature/fix branch in the Mibo submodule, build and run the samples against it. If samples break, the branch is not ready for a PR — full stop.
2. **Test API ergonomics.** Build or extend a sample against the latest `main` to evaluate how the framework's API reads and feels in practice. Inconsistencies or friction found here feed back into framework design.

## Imperatives

1. **NEVER PUSH WITHOUT PERMISSION.** Always ask before pushing to the remote.
2. **NEVER FORCE PUSH.** Tell the user they have to force push instead of you.
3. **Always run `dotnet fantomas .` before committing code.** Format all F# files before staging.
4. **Never use `Option.get` or `ValueOption.get`.** Always pattern match (`match`, `function`, `if ... then`) or use safe alternatives (`Option.defaultValue`, `Option.map`, `Array.choose`, etc.) to handle option values. Unchecked `.get` calls crash at runtime on `None`.
5. Pull requests made with the `gh` command should use a markdown file as the PR body, not inline escaped markdown strings.

## Repository Layout

```
Mibo.Samples/
├── Mibo/                    ← git submodule — framework source (Mibo.Core, Mibo.Raylib, Mibo.MonoGame)
├── PlatformerSample/        ← 2D raylib platformer (procedural world, sprite animation, lighting)
├── ThreeDSample/            ← 3D raylib platformer (voxel terrain, PBR, skeletal animation, shadow atlas)
├── SpaceBattle/             ← Turn-based hex strategy (fog of war, AI, phased turns)
├── MonoPlatformer/          ← MonoGame DesktopGL port of PlatformerSample
├── MonoThreeD/              ← MonoGame DesktopGL port of ThreeDSample
├── FPSSample/               ← Cross-backend FPS (shared core, raylib + MonoGame thin clients)
│   ├── Shared/              ← Backend-agnostic game logic, systems, physics, AI
│   ├── Raylib/              ← raylib backend
│   ├── MonoShared/          ← MonoGame shared backend (composition root, View, AudioService)
│   ├── MonoDesktop/         ← MonoGame DesktopGL thin client
│   └── MonoWindowsDX/       ← MonoGame WindowsDX thin client
├── PingPong/                ← Networked multiplayer (client/server, WebSockets)
├── BoneProbe/               ← CLI diagnostic tool (raw Assimp dump + bind-pose invariant check)
├── Mibo.Samples.slnx        ← solution file
└── README.md
```

**Backend matrix:** raylib-cs and MonoGame (DesktopGL + WindowsDX). Cross-backend samples (`FPSSample`, `PingPong`) isolate backend-specific code in thin client projects while sharing all game logic.

## Sample Architecture (Enforced)

Every game sample MUST follow the **routed sub-system** architecture. Implementation-level choices (mutable vs immutable models, struct records, `System` pipelines, snapshot barriers) are covered in [scaling.md](Mibo/docs/scaling.md) — pick the rung that fits your game. The rules below are mandatory regardless.

### The root `update` is a router, not game logic

`Program.fs` (or a dedicated `Systems.fs`) routes messages to sub-systems and translates cross-system events into new `Cmd<Msg>`. It contains **no game logic** — only dispatch and translation.

```
  Msg ──┬──▶ Input.update      ──▶ model.Input
        ├──▶ Physics.update    ──▶ model.Player
        ├──▶ EnemyAi.update    ──▶ model.Enemy + EnemyEvent
        └──▶ Pickup.update      ──▶ model.Pickup + PickupEvent

  EnemyEvent ──▶ router ──▶ Cmd<AudioMsg> + Cmd<PlayerMsg>
```

### Each sub-system owns its slice

A sub-system owns its **model**, **message type**, and **update function**. It mutates/returns **only** its own state. It never imports or calls another sub-system's update function or reaches into another sub-system's model.

### Cross-system communication is declarative

Sub-systems never call each other directly. They return **declarative values** (Intents, Events) — pure data describing "what happened" or "what should happen". The router translates each into `Cmd<Msg>` for the relevant systems. The emitting system does not know (or import) its consumers.

```
Phase.Intent.PerformMove   ──▶  router  ──▶  AnimationMsg + UnitsMsg + InputMsg
WeaponEvent.EnemyKilled    ──▶  router  ──▶  AudioMsg + PlayerMsg
```

### Read access goes through query objects

When a sub-system needs to **read** another's state, the router builds a read-only query record (closures over the model) and passes it in — never a direct reference to another sub-system's model.

### `Cmd.map` lifts sub-commands into the root `Msg`

Sub-system commands are lifted via `Cmd.map` (e.g. `Cmd.map PhaseMsg`, `Cmd.map EnemyMsg`).

**Reference implementations:** [SpaceBattle/README.md](SpaceBattle/README.md) (Intent/Event, turn-based) and [FPSSample/Shared/README.md](FPSSample/Shared/README.md) (Event + `System` pipeline + snapshot, 60fps action). Both follow these rules.

## Working with the Mibo Submodule

`Mibo/` is a git submodule pointing at `main` on `AngelMunoz/Mibo`. To validate a framework feature/fix branch:

```bash
cd Mibo
git checkout <feature-branch>
cd ..
dotnet build
```

To update the committed submodule ref after validation:

```bash
cd Mibo && git checkout main && git pull && cd ..
git add Mibo
git commit -m "chore: update Mibo submodule ref"
```

A sample fix often requires a concurrent framework change. Work in both repos, validate together, then open the framework PR first — the samples PR can only merge once the framework PR lands.

## Building, Running & Testing

```bash
dotnet build                          # build everything
dotnet run --project PlatformerSample # run a sample
dotnet test FPSSample/Shared.Tests    # run the FPS test suite
dotnet fantomas .                     # format all F# files (required before commit)
```

## Performance Considerations

Samples are games — frame budgets matter. Keep the hot path (per-tick update/render) lean:

- Avoid heap allocations in update/render loops.
- Prefer arrays and spans over lists.
- Favor ArrayPool over allocating new arrays where possible.
- Functional patterns are fine, but use mutable state when profiling shows GC pressure.
- For the full scaling ladder (mutable models, struct messages, `System` pipelines), see [scaling.md](Mibo/docs/scaling.md).

## Conventions

- Prefer **interpolated strings** (`$"..."`) over formatted strings (`sprintf`/`printfn` with `%s`, `%d`, etc.).
- Pattern match on option types — never use `.Value` or `Option.get`/`ValueOption.get`.
- For raw raylib-cs FFI and matrix quirks (void* bug, GLSL conventions), see [Mibo/AGENTS.md](Mibo/AGENTS.md).
