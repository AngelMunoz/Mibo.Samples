# Implementation Plan: Mibo 3D v2 — Extracted Gather + Per-Group Shading + Staged Pipeline

> **Spec:** `2026-06-24-mibo-3d-v2-extracted-gather-shading-staged-pipeline.md` (repo root).
> **Predecessor work:** the unified DSL (Phases 0–4, committed on `spike/3dpipelinework`). Builds on top of it — does not redo it.
> **Scope:** `Mibo.MonoGame` 3D backend only (prototype here; Raylib port deferred to v2 proper).
> **Verification:** no tests (deferred). Each phase: `dotnet build` + `dotnet fantomas .` + visual check via `MonoThreeD` (renders identically to today until the new feature is exercised).

## Branch / repo guardrails (from AGENTS.md + port plan)

- Framework work happens **inside `Mibo/`** on branch `spike/3dpipelinework` (currently there). Do not switch branches. Do not push without permission. Never force-push.
- Sample work (`MonoThreeD/`) at the samples repo root on `feat/monogame3d`.
- Run `dotnet fantomas .` before staging. Never `Option.get`/`ValueOption.get`. `gh` PRs use a markdown body file.
- **Changelog:** read `Mibo/CHANGELOG.md` before editing; match its style; never reflow existing entries; edit only when the change lands.
- MonoGame source is at `E:\MonoGame` — read it for API facts, don't reflect/guess.
- Canonical reference for shader/doubt is `Mibo/src/Mibo.Raylib/`.

## Canonical reference: the 2D precedent for beginEffect/endEffect

The exact shape we mirror (MonoGame 2D):
- Command: `Command2D.fs:277-278` — `BeginShader of shader: Effect * layer` / `EndShader of layer`.
- DSL: `Draw.fs:402` (`beginShader`) / `Draw.fs:411` (`endShader`).
- Dispatch: `Renderer2D.fs:1466-1474` — `BeginShader` flushes the frame + sets `state.Shader`; `EndShader` flushes + clears.

3D differences from 2D (do NOT copy blindly): 3D's scope must also **upload the gathered scene to the user effect** (2D uploads nothing). That upload is the `SceneUpload` module extracted in Phase 1.

---

## Phase ordering

| Phase | What | Compiles? | Visual change? |
|---|---|---|---|
| **1** | Extract `SceneData` + `ShadowPass` from `ForwardPipeline` (pure move; pipeline calls the modules) | Yes | No (identical output) |
| **2** | Extract `SceneUpload` (the upload-to-effect helper), rewire the 3 handlers to call it | Yes | No (identical output) |
| **3** | Introduce `ForwardPipelineBase`; `ForwardPipeline` becomes a thin subclass (no behavior change) | Yes | No (identical output) |
| **4** | Add `BeginEffect`/`EndEffect` commands + DSL; `Execute` tracks scope state; `Shade` takes `activeEffect` | Yes | No unless a scope is used |
| **5** | Add a toon-effect sample to `MonoThreeD` to exercise `beginEffect`/`endEffect` end-to-end | Yes | Yes (new toon draws) |
| **6** | Changelog + final review | Yes | No |

Each phase ends with build + fantomas + (Phases 1–4) a visual confirmation that `MonoThreeD` still renders the three objects correctly (cube, animated character, static model).

---

## Phase 1 — Extract `SceneData` + `ShadowPass`

**Goal:** move the prescan (camera + lights + shadow origin) and the shadow pass out of `ForwardPipeline`'s private guts into reusable public modules. **No behavior change** — the pipeline calls the modules instead of inline code.

**Files:**
- Create: `Mibo/src/Mibo.MonoGame/Graphics3D/Pipelines/SceneData.fs`
- Create: `Mibo/src/Mibo.MonoGame/Graphics3D/Pipelines/ShadowPass.fs`
- Modify: `Mibo/src/Mibo.MonoGame/Graphics3D/Pipelines/ForwardPipeline.fs` (the `Execute` prescan + `runShadowPass` now delegate)
- Modify: `Mibo/src/Mibo.MonoGame/Mibo.MonoGame.fsproj` (add `<Compile>` entries before `ForwardPipeline.fs`)

### Task 1.1 — `SceneData.fs`

- [ ] **Step 1.** Create `SceneData.fs`. Move `LightBuffers` type + the prescan walk out of `ForwardPipeline`'s `module private ForwardHelpers`. Public API:

```fsharp
namespace Mibo.Elmish.Graphics3D.Pipelines

open Microsoft.Xna.Framework
open Mibo.Elmish.Graphics3D

[<Struct>]
type SceneData = {
  Camera: Camera3D
  View: Matrix
  Projection: Matrix
  Lights: LightBuffers
  ShadowOrigin: Vector3 voption
}

// LightBuffers moves here from ForwardHelpers (made public/internal)
type LightBuffers = { ... }   // same fields as today

module SceneData =
  // The prescan walk currently inline in Execute (~ForwardPipeline.fs:2030-2058).
  // Walks the buffer once: BeginCamera/BeginCameraConfig → camera; lights; SetShadowOrigin.
  let gather (buffer: RenderBuffer3D) : SceneData = ...
```

- [ ] **Step 2.** `dotnet build` — expect breakage in `ForwardPipeline.fs` (it still references the moved `LightBuffers`/fields). Defer fix to 1.3.

### Task 1.2 — `ShadowPass.fs`

- [ ] **Step 1.** Create `ShadowPass.fs`. Move `ShadowMeshDraw`/`ShadowSkinnedDraw`/`ShadowEffectParams`/`buildShadowParams` and the body of `runShadowPass` out of `ForwardPipeline` into a public module:

```fsharp
module ShadowPass =
  // Collect casters from the buffer (gated by EnableShadows/DisableShadows), cull per light.
  // Body = the caster-collection loop currently inside runShadowPass (~ForwardPipeline.fs:1738-1780).
  let collectCasters (buffer, scene: byref<SceneData>) : struct (ShadowMeshDraw[] * int * ShadowSkinnedDraw[] * int)
  // Render casters to the atlas; upload shadow uniforms to the PBR effect.
  // Body = the render loop currently inside runShadowPass (~ForwardPipeline.fs:1781-1870).
  let render (gd, scene: byref<SceneData>, casters, atlas, depthEffect, depthParams, pbrParams) : unit
```

The signature will be verbose (it currently threads a lot of pipeline state: shadowAtlas, shadowEffect, shadowParams, shadowRaster, shadowDraws, shadowSkinnedDraws, bonePaletteScratch, pointShadowSlots, spotShadowSlots). **For Phase 1 keep it faithful — pass those as args; don't refactor the threading yet.** Phase 3 (staged base) tidies the state ownership.

- [ ] **Step 2.** `dotnet build` — expect the same `ForwardPipeline.fs` breakage.

### Task 1.3 — Rewire `ForwardPipeline` to call the modules

- [ ] **Step 1.** In `ForwardPipeline.fs`:
  - `Execute`'s prescan loop becomes `let mutable scene = SceneData.gather buffer` (keep the local `state` ForwardState for camera-scope tracking in the forward pass — those are separate concerns; `SceneData` is the gather, `ForwardState` is the per-draw camera scope. Reconcile in Phase 3 if they overlap).
  - `runShadowPass` becomes a thin wrapper calling `ShadowPass.collectCasters` + `ShadowPass.render`, passing the pipeline's shadow resources as args.
  - Remove the moved code from `module private ForwardHelpers`.

- [ ] **Step 2.** `dotnet build src/Mibo.MonoGame/Mibo.MonoGame.fsproj` — must succeed.
- [ ] **Step 3.** `dotnet fantomas .`
- [ ] **Step 4.** Run `MonoThreeD` — must render identically to pre-Phase-1 (cube textured, character textured+animated, static model textured). **No visual change is the gate.**
- [ ] **Step 5.** Commit (in `Mibo/`): `refactor(monogame3d): extract SceneData + ShadowPass from ForwardPipeline`. Ask before pushing.

---

## Phase 2 — Extract `SceneUpload`

**Goal:** the upload-to-effect helper that both use cases share. The three handlers (`handleDrawModel`/`handleDrawAnimatedModel`/`handleDrawPrimitive`) currently call `uploadPbrMaterial`/`bindPbrTextures`/`uploadPbrLights` with `PbrEffectParams`. Generalize into `SceneUpload.uploadToEffect(effect, scene, shadows, bones, material)` that resolves params by name on *any* effect.

**Files:**
- Create: `Mibo/src/Mibo.MonoGame/Graphics3D/Pipelines/SceneUpload.fs`
- Modify: `ForwardPipeline.fs` (handlers call `SceneUpload`)

### Task 2.1 — `SceneUpload.fs`

- [ ] **Step 1.** Create `SceneUpload.fs`:

```fsharp
module SceneUpload =
  // Resolves the PBR uniform names on ANY effect (by Parameters["name"]).
  // Absent uniforms return null → setX no-ops (already the helper behavior).
  // This is the single upload both the default pipeline and beginEffect scopes use.
  let uploadToEffect
    (effect: Effect, scene: byref<SceneData>, shadows: ShadowResult voption,
     transform: Matrix, normalMatrix: Matrix, bones: Matrix[] voption, material: Material3D)
    : unit =
    // 1. matrices: matModel, viewProj, normalMatrix, cameraPos
    // 2. material: albedoColor, texture0..4, roughness, metallic, emissionColor, opacity, tiling, useNormalMap
    // 3. lights: ambient, dirLight, pointLight*[], spotLight*[]
    // 4. shadows: shadowAtlas, shadowViewProjs[], shadowUVOffsets[], shadowTexelSize, *LightShadowIdx[], dirLightCastsShadows
    // 5. bones: boneMatrices[128] (only when bones is ValueSome)
    ...
```

Note: this consolidates `uploadPbrMaterial` + `bindPbrTextures` + `uploadPbrLights` into one effect-agnostic call. The per-effect `EffectParameter` caching (`PbrEffectParams`) stays for the PBR hot path (avoids re-resolving names every draw); `SceneUpload` resolves by name each call — fine for the user-effect path (not hot) and correct for the PBR path. **Confirm in Phase 2 review whether to keep `PbrEffectParams` caching for PBR and use `SceneUpload` only for user effects** — recommendation: keep caching for PBR (perf), `SceneUpload` for user effects. The handlers branch on `activeEffect` (Phase 4).

- [ ] **Step 2.** Rewire the three handlers to call `SceneUpload.uploadToEffect` (still hardcoded to the PBR effect in this phase — the `activeEffect` branch comes in Phase 4). Keep `PbrEffectParams` caching for the PBR path; have `SceneUpload` resolve by name.
- [ ] **Step 3.** `dotnet build` — must succeed.
- [ ] **Step 4.** `dotnet fantomas .`
- [ ] **Step 5.** Run `MonoThreeD` — identical output. **No visual change is the gate.**
- [ ] **Step 6.** Commit: `refactor(monogame3d): extract SceneUpload (effect-agnostic scene upload)`. Ask before pushing.

---

## Phase 3 — `ForwardPipelineBase` + thin `ForwardPipeline`

**Goal:** introduce the staged base. `ForwardPipeline` becomes a subclass overriding `Shade` with PBR. **No behavior change.**

**Files:**
- Modify: `ForwardPipeline.fs` — split into `ForwardPipelineBase` (orchestration + default stage impls) and `ForwardPipeline` (PBR `Shade`). May split into two files if it gets large.
- Modify: `Mibo.MonoGame.fsproj` if split.

### Task 3.1 — Introduce `ForwardPipelineBase`

- [ ] **Step 1.** Define `[<AbstractClass>] ForwardPipelineBase` with:
  - `abstract Gather` (default: `SceneData.gather`)
  - `abstract Shadow` (default: `ShadowPass.collectCasters` + `.render`)
  - `abstract Shade: gd * byref<SceneData> * ShadowResult * activeEffect: Effect voption * Command3D draw -> unit` (abstract — PBR/Toon/etc override)
  - `abstract PostProcess` (default: today's post handling)
  - `interface IRenderPipeline3D` with `Execute` = orchestration calling the four stages. The forward-pass loop tracks camera scope (`ForwardState`) + (in Phase 4) effect scope.

- [ ] **Step 2.** `ForwardPipeline` becomes:
```fsharp
type ForwardPipeline(...) =
  inherit ForwardPipelineBase(...)
  override _.Shade(gd, scene, shadows, activeEffect, draw) =
    let effect = match activeEffect with ValueSome e -> e | ValueNone -> <pbrEffect>
    // bind effect, SceneUpload.uploadToEffect(effect, ...), drawPart/mesh.Draw
```

- [ ] **Step 3.** Tidy the shadow-resource ownership (Phase 1 left them as pass-through args): `ForwardPipelineBase` owns the shadow atlas/effects/scratch arrays as instance fields; `Shadow.render` takes them from `this`.

- [ ] **Step 4.** `dotnet build` — must succeed. `MonoThreeD` builds unchanged (`Renderer3D.create (ForwardPipeline()) view` — constructor signature preserved).
- [ ] **Step 5.** `dotnet fantomas .`
- [ ] **Step 6.** Run `MonoThreeD` — identical output. **No visual change is the gate.**
- [ ] **Step 7.** Commit: `refactor(monogame3d): introduce ForwardPipelineBase; ForwardPipeline is a thin PBR subclass`. Ask before pushing.

---

## Phase 4 — `BeginEffect`/`EndEffect` commands + DSL + scope tracking

**Goal:** use case A. Two new commands; `Execute` tracks effect scope; `Shade` receives the active effect.

**Files:**
- Modify: `Command3D.fs` (2 new cases)
- Modify: `Draw3D.fs` (2 new DSL entries)
- Modify: `ForwardPipelineBase.fs` / `ForwardPipeline.fs` (`Execute` tracks scope; `EndCamera` closes any open scope per §7.2)

### Task 4.1 — Commands + DSL

- [ ] **Step 1.** `Command3D.fs`: add `| BeginEffect of effect: Effect` and `| EndEffect` (additive; no field changes to draw cases). Add `Command3D.beginEffect`/`endEffect` factories.
- [ ] **Step 2.** `Draw3D.fs`: add `beginEffect (effect: Effect) (buffer)` / `endEffect (buffer)` — buffer-last, pipeable. Add a doc comment referencing the §3 inheritance model (you inherit scene data, not PBR).
- [ ] **Step 3.** `dotnet build` — expect breakage in `ForwardPipelineBase.Execute` (the forward-pass match is non-exhaustive now: `BeginEffect`/`EndEffect` unhandled). Defer to 4.2.

### Task 4.2 — Scope tracking in `Execute`

- [ ] **Step 1.** `ForwardPipelineBase.Execute` forward-pass loop:
  - Add `let mutable activeEffect: Effect voption = ValueNone`.
  - `| Command3D.BeginEffect e -> activeEffect <- ValueSome e`
  - `| Command3D.EndEffect -> activeEffect <- ValueNone`
  - On `Command3D.EndCamera` (and `BeginCamera`/`BeginCameraConfig` starting a new camera block): reset `activeEffect <- ValueNone` (§7.2 — scopes don't persist across cameras).
  - Each draw case passes `activeEffect` to `Shade`.

- [ ] **Step 2.** `ForwardPipeline.Shade`: `let effect = match activeEffect with ValueSome e -> e | ValueNone -> pbrEffect`. For the PBR-cached path, when `activeEffect.IsSome`, use `SceneUpload.uploadToEffect(userEffect, ...)` (name-resolved); when `None`, use the cached `PbrEffectParams` fast path. Select technique: for user effects, the effect's own `CurrentTechnique` (user-owned); for PBR, the `Standard`/`Skinned`/`Instanced` selection as today.

- [ ] **Step 3.** `dotnet build` — must succeed.
- [ ] **Step 4.** `dotnet fantomas .`
- [ ] **Step 5.** Run `MonoThreeD` (no scopes used) — identical output. **No visual change is the gate** (scopes aren't exercised yet).
- [ ] **Step 6.** Commit (in `Mibo/`): `feat(monogame3d): beginEffect/endEffect — per-group shading on the default pipeline`. Ask before pushing.

---

## Phase 5 — Toon sample in `MonoThreeD`

**Goal:** exercise `beginEffect`/`endEffect` end-to-end with a real custom effect.

**Files:**
- Create: `MonoThreeD/Content/Shaders/Toon.fx` (+ compile via the existing `script.fsx` pattern → embed? For a sample, load from file at runtime is simpler; decide in-task)
- Modify: `MonoThreeD/Program.fs` — add a toon-scoped draw alongside the existing default-path draws.

### Task 5.1 — Toon effect + scoped draw

- [ ] **Step 1.** Author a minimal `Toon.fx` declaring the §3 contract subset: `matModel`, `viewProj`, `normalMatrix`, `cameraPos`, `dirLightDir`, `dirLightColor`, `dirLightIntensity`, `ambientColor`, `ambientIntensity`, `albedoColor`, `texture0` (albedo), `boneMatrices[128]` (so an animated model in the toon scope works). BRDF: banded N·L + rim. Keep it SM3.0-safe (§6.3 of the port plan: no `dFdx`, `[loop]`+break for any light loops).
- [ ] **Step 2.** In `MonoThreeD/Program.fs`, add a toon-scoped draw (e.g. a second copy of the model offset further, drawn through `Draw3D.beginEffect toonEffect ... Draw3D.endEffect`). Keep the existing default-path draws for comparison.
- [ ] **Step 3.** Build + run. Expected: the toon-scoped model renders with banded lighting (inherits directional + ambient + texture + bones); the default-path models still render PBR.
- [ ] **Step 4.** `dotnet fantomas .` (samples repo).
- [ ] **Step 5.** Commit (samples repo): `feat(monogame3d): toon-effect sample exercising beginEffect/endEffect`. Submodule bump if the framework changed. Ask before pushing.

---

## Phase 6 — Changelog + final review

### Task 6.1 — Changelog
- [ ] **Step 1.** Read `Mibo/CHANGELOG.md`. Under `## [Unreleased]`:
  - `### Added`: `SceneData`/`ShadowPass`/`SceneUpload` reusable modules; `ForwardPipelineBase` staged base; `Draw3D.beginEffect`/`endEffect` + `Command3D.BeginEffect`/`EndEffect`.
  - `### Changed`: `ForwardPipeline` refactored into a thin `ForwardPipelineBase` subclass (internal reorganization; `IRenderPipeline3D`/`IRenderer` contracts unchanged; default DSL path unchanged).
- [ ] **Step 2.** Commit: `docs(changelog): mibo 3d v2 — extracted gather, per-group shading, staged pipeline`.

### Task 6.2 — Final review
- [ ] `Option.get`/`ValueOption.get` grep on touched files — none introduced.
- [ ] `dotnet fantomas .` clean.
- [ ] `dotnet build` green (framework + sample).
- [ ] `MonoThreeD` final visual: default-path draws unchanged; toon-scoped draw works.
- [ ] Ask before pushing / opening PRs.

---

## Self-review notes

- **Spec coverage:** §0 two use cases → Phase 4 (A) + Phase 3 (B). §1 layering → Phase 3. §2 extraction → Phases 1–2. §3 beginEffect inheritance → Phase 4 Task 4.2 + Phase 5 (toon sample proves it). §4 staged base → Phase 3. §5 breaks/doesn't → each phase keeps the default path unchanged (gates). §7 decisions → 4.2 (voption activeEffect), 4.2 (EndCamera closes scope), 5 (toon casts via EnableShadows toggle), 4.1 (spelling `Effect`).
- **Incremental safety:** Phases 1–4 each gate on "MonoThreeD renders identically" (no visual change) — so a regression is caught at the phase boundary, not at the end. The new behavior only appears in Phase 5 (toon sample).
- **The one judgment call to flag in review:** Phase 2 Task 2.1 recommends keeping `PbrEffectParams` caching for the PBR hot path and using name-resolved `SceneUpload` only for user effects. If you'd rather unify on `SceneUpload` everywhere (simpler, one path, slightly slower PBR), say so and the plan adjusts.
