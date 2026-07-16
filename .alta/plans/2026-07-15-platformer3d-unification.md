# Unify ThreeDSample + MonoThreeD into Platformer3D

- Status: Approved
- Plan file: `.alta/plans/2026-07-15-platformer3d-unification.md`
- Created: 2026-07-15
- Task: Replace the duplicated `ThreeDSample/` (raylib) and `MonoThreeD/` (MonoGame WindowsDX) with a single `Platformer3D/` that mirrors the 2D `Platformer/` structure — shared pure sub-system logic + per-backend thin clients with decomposed, isolated sub-systems.
- Git: not ignored — commit this plan with related work.

## Objective

- **One pass** to the final state: a `Platformer3D/` folder that mirrors the existing 2D `Platformer/` structure (`Shared/` + `Raylib/` + `MonoGame/` + `Shared.Tests/`), with the game logic de-coupled from backend types and decomposed into isolated sub-systems.
- **Mirror the 2D Platformer/ exactly** — it already solved this exact problem. Use it as the structural blueprint, not the abstract patterns in the READMEs.
- **Enforce subsystem isolation** (per `FPSSample/Shared/README.md`, `SpaceBattle/README.md`): each sub-system owns its model + message type + update function; cross-system communication is declarative; the router translates events via `Cmd.map`; read access goes through query objects.
- **Non-goals:** No DesktopGL client (WindowsDX only, matching `Platformer/MonoGame`). No `Mibo/` framework changes unless a blocker is found. No new rendering features.

## Context and evidence

### The blueprint: 2D Platformer/
- `Platformer/Shared/Shared.fsproj` → references **only `Mibo.Core`**. Contains: `Constants.fs` (gameplay constants, `System.Numerics`, **no asset paths**), `Types.fs` (BlockType DU, pure sub-model types), and pure sub-system modules: `Physics.fs` (`module PhysicsSystem`), `WorldGen.fs`, `Particles.fs` (`ParticleMsg.SpawnConfetti`), `Minimap.fs` (`module Minimap`, `MinimapSystem`, `MinimapReady of Mibo.Color[] * w * h`), `DayNight.fs` (`module DayNight`, `DayNightSystem`), `Animation.fs`.
- `Platformer/Raylib/Raylib.fsproj` → `Shared` + `Mibo.Raylib`. `Types.fs` defines a flat `Model()` class that **composes shared sub-system models + backend state** (see snippet below), a `Msg` union, and conversion helpers. `Systems.fs` is the router: calls shared sub-system updates, translates events via `Cmd.map`, handles backend glue (`Raylib.PlaySound`, texture upload, sprite animation). `View.fs`, `Program.fs`, `MinimapView.fs`.
- `Platformer/MonoGame/MonoGame.fsproj` → `Shared` + `Mibo.MonoGame` (WindowsDX). Same file set, XNA types.
- `Platformer/Shared.Tests/Shared.Tests.fsproj` → `Shared` + Expecto.

**The Raylib backend `Model` composition pattern** (`Platformer/Raylib/Types.fs`) — this is the exact shape to reproduce in 3D:
```fsharp
type Model() =
  // Shared sub-system models
  member val Physics = PhysicsSystem.init() with get, set
  member val Chunks = WorldGen.Chunks.init 0 with get, set
  member val ParticleState = Particles.init() with get, set
  member val DayNight = DayNightSystem.init() with get, set
  member val Animation = Animation.init() with get, set
  member val Minimap = MinimapSystem.init() with get, set
  member val Diag = Diagnostics.init() with get, set
  // Input
  member val Actions = ActionState.empty ...
  // Backend-specific state
  member val Camera = ... with get, set
  member val Assets = ... with get, set
  member val MinimapTexture = ... with get, set
```

### Current 3D state
- `ThreeDSample/` (2,014 LOC, 10 files) and `MonoThreeD/` (2,109 LOC, 10 files) are near-duplicates. Diff changed-line counts: Constants=137, Types=35, DayNight=60, MinimapView=188, Physics=14, Systems=80, View=138, Program=84.
- **Root cause of duplication:** backend-specific types sprinkled through every file — `Color` (raylib vs XNA), `Vector3`/`Vector2` (System.Numerics vs XNA), `BoundingBox` (`Raylib_cs` vs `Mibo.Layout3D`), plus `Model`, `Sound`, `Texture2D`, `Image`, `Font`, `Animation3DState`/`AnimatedModel`.
- The current `GameModel` is **monolithic** (flat ~25-field class). The `Tick` handler runs a `System.pipeMutable` pipeline of inline `*System` functions with **no sub-system isolation** (no per-system models/messages, no declarative events, no `Cmd.map`, no query objects). The jump→confetti→sound coupling is inline (`spawnConfetti` directly calls `Raylib.PlaySound`).

### Backend-agnostic types already in Mibo.Core (confirmed)
- `Mibo.Color` — backend-agnostic color struct (`Mibo/src/Mibo.Core/Color.fs`). Both backends have `.ToMiboColor()` extension methods.
- `System.Numerics.Vector3`/`Vector2` — raylib sample uses these natively; MonoGame converts at the boundary (`pos.ToNumerics()`, `Vector3(x,y,z)`).
- `Mibo.Layout3D.BoundingBox` — already used by MonoThreeD's `Chunk`; raylib wraps it.
- `Mibo.Elmish.Graphics3D.PointLight3D` — `Mibo/src/Mibo.Core/Graphics3D/Light3D.fs` (backend-agnostic, System.Numerics).
- `Mibo.Animation.Animation3DState` / `Animation3DClipsInfo` — pure playback types in `Mibo/src/Mibo.Core/Animation3D.fs` (no `.Model`). Backend-specific versions exist: raylib `Animation3DState` carries `.Model`; MonoGame uses `AnimatedModel` (Model+Mesh+State). The target-clip selection ("idle"/"walk"/"jump") is pure and shareable; playback stays per-backend.

## Design notes

### Organizing principle: "do what Platformer/ does, in 3D"
The 2D Platformer is the single source of truth for structure. The 3D work is: port the same folder/fsproj/module layout, de-couple the backend types to the same boundary, and decompose the flat model into the same sub-system composition pattern. No separate "unify then decompose" phases — the end state is built directly.

### Shared project (`Platformer3D/Shared/`, → `Mibo.Core` only)
Pure sub-system logic + types. Zero references to `Raylib_cs` or `Microsoft.Xna.Framework`.

- `Constants.fs` — gameplay constants (`cellSize`, `gravity`, `jumpSpeed`, camera/chunk params, `playerHeight`, etc.), `System.Numerics`. No asset paths. `KenneyModels` as **bare logical names** (e.g. `let blockGrass = "block-grass"` — no basePath, no extension).
- `Types.fs` — `GameAction`, `BlockType` DU + `modelName`/`modelVerticalOffset`/`modelRotation` (pure float math)/`isSolid`/`isCollectible`/`isDecoration`/`isLightSource`. `Chunk` (Mibo.Layout3D.BoundingBox). Pure sub-model records: `LightingModel`, `ParticleModel`, `MinimapData`, `DiagnosticsData` (all Mibo.Color/System.Numerics; no Texture/Font). `confettiColors` as `Mibo.Color[]`.
- `Physics.fs` — `module PhysicsSystem`: `init`, `update(dt, actions, cameraYaw, chunks) → PhysicsModel * PhysicsEvent`. Owns pos/vel/grounded/facing/score/camera. Emits `PhysicsEvent.Jumped(pos)` (replaces inline `spawnConfetti` + `PlaySound`). Pure helpers `resolveCollision`, `computeMoveDirection`, `computeCameraPosition` stay here.
- `WorldGen.fs` — `module WorldGen.Chunks`: `init(seed)`, `update(playerPos) → ChunksModel * ChunkMsg`. `generateChunk`, `evictDistantChunks` pure.
- `Particles.fs` — `module Particles`: `init`, `ParticleMsg = Tick dt | SpawnConfetti pos`, `update → ParticleModel`. Mibo.Color arrays.
- `DayNight.fs` — `module DayNight`/`DayNightSystem`: all `Color` → `Mibo.Color`, `Vector3` → System.Numerics.
- `Lighting.fs` — `module LightingSystem`: derives sky/ambient/directional from DayNight into `LightingModel` (Mibo.Color).
- `Minimap.fs` — `module Minimap`/`MinimapSystem`: pure generation → `Mibo.Color[] * width * height`. `MinimapReady` event. Rewrite raylib's `Image`/`GenImageColor`/native-pointer pixel loop as array writes (pixel-mapping logic preserved, allocation as `Mibo.Color[]`).
- `Diagnostics.fs` — `module Diagnostics`: fps/counts (no Font).
- `Animation.fs` — `module Animation`: derives target clip ("idle"/"walk"/"jump") from physics state. Pure; playback is per-backend.

### Per-backend clients (`Raylib/` and `MonoGame/`)
Each backend owns: `Types.fs` (composing `Model`), `Systems.fs` (router), `View.fs`, `Program.fs`, `MinimapView.fs`, `DiagnosticsView.fs`.

**`Types.fs` (per-backend)** — flat `Model()` composing shared sub-system models + backend state (reproduce the 2D Platformer pattern):
```fsharp
type Model() =
  // Shared sub-system models
  member val Physics = PhysicsSystem.init()
  member val Chunks = WorldGen.Chunks.init 0
  member val Particles = Particles.init()
  member val DayNight = DayNightSystem.init()
  member val Lighting = LightingModel()        // or init()
  member val Minimap = MinimapSystem.init()
  member val Diag = Diagnostics.init()
  member val Actions = ActionState.empty
  member val InputMap = InputMap.empty
  // Backend-specific state
  member val PlayerAnim = ...   // raylib: Animation3DState; MonoGame: AnimatedModel
  member val ModelCache = ...
  member val JumpSound = ...    // raylib: Sound; MonoGame: SoundEffect
  member val ParticleTexture = ...
  member val MinimapTexture = ...
  member val VisibleLights = ...
```
Plus the backend `Msg` union wrapping sub-system messages + backend messages (`Tick`, `InputMapped`, `ChunkCreated`, `MinimapReady of Mibo.Color[] * w * h`, `MushroomLightsReady`), and `KenneyModels` path composition (raylib: `"assets/kenney_platformer-kit/Models/" + name + ".glb"`; MonoGame: `"kenney_platformer-kit/Models/" + name`, no extension).

**`Systems.fs` (per-backend router)** — mirrors `Platformer/Raylib/Systems.fs`:
- Dispatch each `Msg` to the relevant shared sub-system `update`.
- Translate events via `Cmd.map` (e.g. `PhysicsEvent.Jumped` → spawn confetti particles + play jump sound + set flag).
- Backend glue: `Raylib.PlaySound` / `SoundEffect.Play`, minimap `uploadTexture` (Mibo.Color[] → native), animation playback (`Animation3DState.blendTo/update` / `AnimatedModel.update`), mushroom-light collection.
- Cross-system reads via query objects (e.g. minimap reads `model.Physics.Position`, lighting reads `model.DayNight.TimeOfDay`).

**`View.fs` (per-backend)** — 3D scene rendering. `instancedCtx.getKey = BlockType.modelName`; `resolveMeshesAndMaterial` composes `KenneyModels.fullPath name`. Player model via backend animation type.

**MonoGame specifics:** XNA `Vector3` converted at boundaries (`init`, `View.fs`, MonoGame API calls); `AnimatedModel` (bundles Model+Mesh+State); animation clips from raw `.glb` via Assimp; `Texture2D.SetData` for particle/minimap; `Content.mgcb` + `diagnostics.spritefont` + `Toon.fx` migrated from `MonoThreeD/Content/`.

### Rejected alternatives
- **FPSSample-shape (shared router + Env services):** would add `IAudioService`/`IPlayerAnimationService` + a snapshot boundary. Platformer-shape (router per-backend) is simpler and matches the 2D reference — kept per user decision.
- **Phased (unify then decompose):** rejected per user feedback — confusing. One pass straight to the final state.

## Assumptions

- **Module/namespace naming** follows 2D Platformer: `Platformer3D.Shared.*`, `Platformer3D.Raylib.*`, `Platformer3D.MonoGame.*`; fsproj files `Shared.fsproj`/`Raylib.fsproj`/`MonoGame.fsproj`/`Shared.Tests.fsproj`.
- **Old dirs deleted** after verification — confirmed by user ("the existing 3d platformers may be deleted once we're done moving on").
- **Assets migrate** from existing dirs (no new asset creation): raylib `assets/kenney_platformer-kit/Models/*.glb` + `sfx_jump.ogg`; MonoGame `Content.mgcb` + `diagnostics.spritefont` + `Toon.fx` + compiled models + raw `character-oobi.glb`.
- `LightingModel`/`ParticleModel`/`MinimapData`/`DiagnosticsData` go fully to Shared (Mibo.Color/System.Numerics); only backend `Texture2D`/`Font`/`Sound` handles live in the backend `Model`.
- **Color conversion:** use explicit `op_Implicit` calls rather than manual constructors (e.g. `Color.op_Implicit` to convert between Mibo.Color and native backend Color). User confirmed there's no problem with this approach — prefer it over hand-built color constructors.
- **Verification target:** we are on macOS — MonoGame (WindowsDX) **cannot run** here. Verify that MonoGame **compiles** cleanly and mirrors the raylib changes, but do **not** attempt to `dotnet run` the MonoGame project. Only the raylib backend must actually run.

## Risks and challenges

- **MonoGame Vector3 boundary friction:** pervasive conversions at MonoGame API boundaries. Mechanical, caught at compile time (type mismatch), but easy to miss a site.
- **Minimap rewrite:** raylib's `generateMinimapImage` uses `Raylib.GenImageColor` + `FSharp.NativeInterop` pixel loops. Shared version produces `Mibo.Color[]` directly — moderate rewrite of allocation/draw, pixel-mapping logic preserved.
- **Animation divergence:** raylib `Animation3DState` (`.Model`, `applyToModel`) vs MonoGame `AnimatedModel`. Target-clip selection shared; playback per-backend (like 2D Platformer's per-backend sprite playback).
- **Content pipeline asset paths:** MonoGame strips extensions + uses `Content/` root; raylib uses `.glb` + `assets/` prefix. `KenneyModels` path composition differs per backend.
- **`fantomas` mandatory** before commit (AGENTS.md #3); never `Option.get`/`ValueOption.get` (#4); never push without permission (#1); never force push (#2).

## Implementation checklist

- [ ] Create `Platformer3D/` structure: `Shared/`, `Raylib/`, `MonoGame/`, `Shared.Tests/`, `assets/`.
- [ ] Create `Platformer3D/Shared/Shared.fsproj` (→ `Mibo.Core`, `net10.0`, `IsPackable=false`).
- [ ] `Shared/Constants.fs`: gameplay constants (System.Numerics); `KenneyModels` as bare logical names (no basePath/extension).
- [ ] `Shared/Types.fs`: `GameAction`, `BlockType` + pure queries, `Chunk` (Mibo.Layout3D.BoundingBox), pure sub-model records (Mibo.Color/System.Numerics), `confettiColors: Mibo.Color[]`.
- [ ] `Shared/Physics.fs`: `module PhysicsSystem` — `init`/`update`, `PhysicsEvent.Jumped` event, pure helpers (`resolveCollision`/`computeMoveDirection`/`computeCameraPosition`).
- [ ] `Shared/WorldGen.fs`: `module WorldGen.Chunks` — `init(seed)`/`update(playerPos)`, `ChunkMsg`, pure `generateChunk`/`evictDistantChunks`.
- [ ] `Shared/Particles.fs`: `module Particles` — `init`/`ParticleMsg`/`update` (Mibo.Color arrays).
- [ ] `Shared/DayNight.fs`: `module DayNight`/`DayNightSystem` — all Color→Mibo.Color, Vector3→System.Numerics.
- [ ] `Shared/Lighting.fs`: `module LightingSystem` — derives from DayNight into `LightingModel`.
- [ ] `Shared/Minimap.fs`: `module Minimap`/`MinimapSystem` — pure generation → `Mibo.Color[] * w * h`, `MinimapReady` event (rewrite Image API → array writes).
- [ ] `Shared/Diagnostics.fs`: `module Diagnostics` — fps/counts (no Font).
- [ ] `Shared/Animation.fs`: `module Animation` — derives target clip from physics state.
- [ ] Create `Platformer3D/Raylib/Raylib.fsproj` (→ `Shared` + `Mibo.Raylib`, `net10.0`, Exe).
- [ ] Raylib `Types.fs`: `Model` composing shared sub-system models + raylib backend state; `Msg`; `KenneyModels` (basePath + `.glb` + `fullPath`).
- [ ] Raylib `Systems.fs`: router — dispatch to shared updates, translate events via `Cmd.map`, raylib glue (PlaySound, Animation3DState, minimap upload, mushroom lights).
- [ ] Raylib `View.fs`: 3D scene; `instancedCtx.getKey = BlockType.modelName`; compose paths via `KenneyModels.fullPath`.
- [ ] Raylib `MinimapView.fs`: `uploadTexture` (Mibo.Color[] → Raylib_cs.Color[] → UpdateTexture) + 2D overlay.
- [ ] Raylib `DiagnosticsView.fs`: 2D diagnostics overlay.
- [ ] Raylib `Program.fs`: composition root, `loadAssets`, `init`, renderer pipeline (`ForwardPbrPipeline`).
- [ ] Copy raylib assets → `Platformer3D/assets/` (`kenney_platformer-kit/Models/*.glb`, `sfx_jump.ogg`); Raylib.fsproj references via `..\assets\**\*`.
- [ ] Create `Platformer3D/MonoGame/MonoGame.fsproj` (→ `Shared` + `Mibo.MonoGame`, `net10.0-windows`, WindowsDX, Exe).
- [ ] MonoGame `Types.fs`/`Systems.fs`/`View.fs`/`MinimapView.fs`/`DiagnosticsView.fs`/`Program.fs`: same structure, XNA types, `AnimatedModel`, `SoundEffect`, `Texture2D.SetData`, `KenneyModels` (no extension), Vector3 boundary conversions.
- [ ] Migrate MonoGame `Content/` (`Content.mgcb`, `diagnostics.spritefont`, `Toon.fx`, compiled models) → `Platformer3D/MonoGame/Content/`. Keep raw `character-oobi.glb` → `animations/` copy rule.
- [ ] Create `Platformer3D/Shared.Tests/Shared.Tests.fsproj` (→ `Shared` + Expecto). Add sub-system tests (event emission, model isolation — `Platformer/Shared.Tests/ReachabilityTests.fs` as pattern).
- [ ] Update `Mibo.Samples.slnx`: remove `ThreeDSample` + `MonoThreeD`; add `<Folder Name="/Platformer3D/">` with all 4 projects.
- [ ] Run `dotnet build` — entire solution compiles with zero errors.
- [ ] Run `dotnet run --project Platformer3D/Raylib` — verify raylib sample runs.
- [ ] Run `dotnet run --project Platformer3D/MonoGame` — **cannot run on macOS** (WindowsDX). Verify it compiles and mirrors raylib; do not run.
- [ ] Run `dotnet test Platformer3D/Shared.Tests` — tests pass.
- [ ] Run `dotnet fantomas .` — format all F# files (clean diff).
- [ ] Grep `Platformer3D/Shared/` for `Raylib_cs` / `Microsoft.Xna.Framework` — confirm zero matches.
- [ ] Delete `ThreeDSample/` and `MonoThreeD/` directories after full verification passes.

## Verification checklist

- [ ] `dotnet build` — zero errors across Shared, Raylib, MonoGame, Shared.Tests.
- [ ] `dotnet run --project Platformer3D/Raylib` — movement (WASD/arrows), jump (Space) with confetti + sound, camera rotation (Q/E/PageUp/Down), chunk streaming, minimap overlay, diagnostics overlay, day/night lighting cycle, collectibles increment score.
- [ ] ~~`dotnet run --project Platformer3D/MonoGame`~~ — **cannot run on macOS** (WindowsDX). Verify it compiles and mirrors the raylib changes; do not attempt to run.
- [ ] `dotnet test Platformer3D/Shared.Tests` — all tests pass.
- [ ] `dotnet fantomas .` — no formatting changes.
- [ ] Shared has zero references to `Raylib_cs` or `Microsoft.Xna.Framework`.
- [ ] Both backends produce visually identical gameplay (same world seed → same chunks).
- [ ] Each sub-system module in Shared compiles with no cross-references to other sub-system update functions.

## Handoff notes

- **Reference implementation:** `Platformer/` (2D) is the structural blueprint — match its fsproj layout, module/namespace naming, backend `Model` composition pattern, and router style. `FPSSample/Shared/README.md` + `SpaceBattle/README.md` are the isolation-pattern reference (events, `Cmd.map`, query objects).
- **Mibo submodule:** Do not modify `Mibo/`. If a framework blocker is found, stop and report — it needs a separate framework PR per AGENTS.md.
- **Color conversion:** use explicit `op_Implicit` calls (e.g. `Color.op_Implicit`) rather than manual color constructors — user confirmed this is preferred. Both backends have `.ToMiboColor()` on native Color; use at render boundaries only.
- **Vector3 for MonoGame:** `System.Numerics.Vector3` → `Microsoft.Xna.Framework.Vector3` via `Vector3(n.X, n.Y, n.Z)` or `.ToNumerics()`; apply in MonoGame `init`, `View.fs`, and MonoGame API calls.
- **fantomas mandatory** before commit; never `Option.get`/`ValueOption.get`; never push without permission; never force push.
