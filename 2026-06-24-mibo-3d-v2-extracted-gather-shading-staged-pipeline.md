# Mibo 3D v2: Extracted Scene Gather + Per-Group Shading + Staged Pipeline

> **Scope:** `Mibo.MonoGame` 3D backend (the half-baked 3D renderer on `feat/monogame3d` / `spike/3dpipelinework`). Prototype here; port to `Mibo.Raylib` later for v2.
> **Predecessor:** `docs/superpowers/specs/2026-06-23-unified-3d-draw-pbr-everywhere-design.md` (the unified DSL; shipped).
> **Status:** Design — awaiting review.

## 0. The two use cases (distinct, both required)

This work delivers **two separate things**. They are not the same mechanism and neither subsumes the other.

**Use case A — "I want this here" (per-group shading on the default pipeline).**
A user scopes a custom `Effect` over a group of draws:

```fsharp
buffer
|> Draw3D.beginEffect toonEffect
|> Draw3D.drawModel modelA transformA
|> Draw3D.drawAnimatedModel character charTransform
|> Draw3D.endEffect
```

The default pipeline keeps running; for draws inside the scope it binds the user's effect **and uploads the gathered scene to it** (lights, shadows, bones, material — see §3). For draws outside any scope, it uses the pipeline's default PBR effect. This is a **command-level feature consumed by the default pipeline.**

**Use case B — "I want a different default pipeline" (whole-pipeline replacement).**
A user swaps the entire pipeline (Forward PBR → Forward+ → their own) by passing a different `IRenderPipeline3D` to `Renderer3D.create`. The default shading strategy changes globally. This is the **pipeline-plugin swap.** It requires the staged base + extracted gather so "swap the pipeline" doesn't mean "rewrite 1500 lines."

**Both depend on the same prerequisite: extracting the scene gather + shadow pass + upload into reusable pieces (§2).** Without that extraction, A can't upload to the user effect and B can't reuse the gather. The extraction is the shared foundation.

## 1. Layer diagram — today vs after

### Today
```
MiboGame (frame loop)
   │ Draw(ctx, model, gameTime) once per frame
   ▼
IRenderer<'Model>                    ← Core: Draw(ctx, model, gameTime)
   │ implemented by
   ▼
Renderer3D<'Model>                   ← owns RenderBuffer3D + RT pool; clears, runs view(),
   │                                 calls pipeline.Execute, releases RTs. Knows nothing visual.
   ▼
IRenderPipeline3D                    ← Initialize / Execute(gameCtx, buffer, rtPool) / Shutdown
   │ implemented by
   ▼
ForwardPipeline                      ← GATHER + SHADOW + SHADE all private in one Execute blob
```

### After
```
IRenderer<'Model>                          (unchanged)
   ▼
Renderer3D<'Model>                         (unchanged)
   ▼
IRenderPipeline3D                          (unchanged contract)
   ▼
ForwardPipelineBase                        ← NEW: implements IRenderPipeline3D, orchestrates
   ▲                                         Gather → Shadow → (per draw: Shade) → Post.
   │  subclass                                Default stage impls call the modules in §2.
   ├── ForwardPipeline                     ← default (PBR). Now thin.
   ├── ToonPipeline (user)                 ← overrides Shade only
   └── ForwardPlusPipeline (user)          ← overrides Gather (tile grid) + Shade
   ── DeferredPipeline (user)              ← implements IRenderPipeline3D directly,
                                              reuses §2 modules but NOT the Forward stages
                                              (different pass structure — correctly separate)
```

`ForwardPipeline` after this work = a thin subclass whose `Shade` binds the PBR effect. The current 1500-line `ForwardPipeline` body is **reorganized, not rewritten** — the same code moves into `ForwardPipelineBase` (orchestration), `SceneData` (gather), `ShadowPass` (shadow), and `SceneUpload` (the upload that both use cases share).

## 2. Where the internal gathering functions end up, and why

### Today — all private, fused inside `ForwardPipeline.fs`
```
module private ForwardHelpers
  type LightBuffers
  uploadPbrLights, uploadPbrMaterial, bindPbrTextures, applyLighting
  runShadowPass (shadow gather + render to atlas)
ForwardPipeline.Execute — fuses gather + shadow + shade + post into one method
```

### After — public, reusable modules
```
SceneData         — gather: walk the buffer once for camera + lights + shadow origin
  type SceneData  = { Camera; Lights: LightBuffers; ShadowOrigin }
  gather(buffer) : SceneData            // the prescan both pipelines do today

ShadowPass        — shadow: collect casters (gated by EnableShadows), cull per light, render to atlas
  collectCasters(buffer, scene) : CasterList
  render(gd, casters, scene, atlas) : ShadowResult
  // ShadowAtlas type already public; only the pass logic moves out

SceneUpload       — THE KEY MODULE: push gathered scene + shadows + bones + material into ANY effect
  uploadToEffect(effect, scene, shadows, bones, material)
  // uniform-name contract (§3) — uploads by name; absent uniforms no-op (already the case)

ForwardPipelineBase — orchestrates Gather → Shadow → (per draw: Shade) → Post
ForwardPipeline     — Shade = bind PBR effect + SceneUpload.uploadToEffect(pbrEffect, ...)
```

**Why these locations:** so both use cases consume the same pieces.
- **A** (`beginEffect`/`endEffect`): the default pipeline, when it sees draws inside a scope, calls `SceneUpload.uploadToEffect(userEffect, ...)` instead of `uploadToEffect(pbrEffect, ...)`. Same upload, different target.
- **B** (custom pipeline subclass): overrides only `Shade`; inherits `SceneData.gather`, `ShadowPass`, and calls `SceneUpload.uploadToEffect(theirEffect, ...)`.

Zero new allocation: `SceneData` is a struct wrapping the per-frame ResizeArrays already reused today; `ShadowResult` carries the atlas binding the shadow pass already produces. The gather already runs every frame — extracting it routes its output to a different upload target, it doesn't add work.

## 3. The `beginEffect`/`endEffect` use case — and the honest inheritance model

**No named passes.** Mirrors the 2D `beginShader`/`endShader` shape (Command2D.fs:277, Draw.fs:402/411): the effect is the identity of the scope, no name string.

```fsharp
type Command3D =
  // ... existing draw/camera/light/shadow cases unchanged ...
  | BeginEffect of effect: Effect
  | EndEffect
```

DSL (additive, mirrors 2D):
```fsharp
Draw3D.beginEffect effect buffer
Draw3D.endEffect buffer
```

### How it dispatches

`ForwardPipelineBase.Execute` walks the buffer a second time (after gather + shadow) tracking scope state:
- `BeginEffect(effect)` → set active effect = user effect.
- `EndEffect` → clear active effect (next draws use the default PBR effect).
- Each draw → `Shade` binds the active effect (user or PBR) and runs `SceneUpload.uploadToEffect(activeEffect, scene, shadows, bones, material)`, then issues the drawcall.

### How it opts into PBR / Shadows / Animation — the honest answer

This is the part the 2D precedent can't teach us, because 2D's `beginShader` uploads **nothing** to the user shader. In 3D, `beginEffect` **uploads the gathered scene to the user effect by uniform name.** What you get depends entirely on what your effect declares:

| Thing | Inherited inside `beginEffect`? | Condition on the user effect |
|---|---|---|
| Lights (ambient/dir/point/spot) | Yes | declares `dirLightDir`, `pointLight*[]`, `ambientColor`, etc. |
| Shadows (atlas + ViewProjs) | Yes | declares `shadowAtlas`, `shadowViewProjs[]`, `shadowTexelSize`, `*LightShadowIdx[]` |
| Animation (bone palette) | Yes | the draw is `drawAnimatedModel` **and** effect declares `boneMatrices[128]` |
| Material (albedo/maps) | Yes | declares `albedoColor`, `texture0`, `tiling`, etc. |
| **PBR (the Cook-Torrance BRDF)** | **No** | — |

**You cannot "opt into PBR" via `beginEffect`.** PBR is the default BRDF; `beginEffect` is precisely the act of *not* using it for those draws. What you opt *into* is the **gathered scene data** — lights, shadows, bones, material — uploaded to your effect. You get exactly the uniforms your shader declares (declare only `dirLightDir` → directional-only toon; declare the full set → everything except the PBR math). If you want PBR you don't use `beginEffect`.

This is the one honest cost of the design, and it's the same uniform-name contract our own `ForwardPbr.fx` already implements — we're publishing what's implicit, not inventing a new constraint. Unreferenced uniforms return `null`/`-1` from `Parameters["x"]`/`GetShaderLocation` and the `setX` helpers no-op (already true in both backends), so a toon shader declaring only the lighting subset just doesn't get the maps it doesn't read.

## 4. The staged pipeline base (use case B)

```fsharp
[<AbstractClass>]
type ForwardPipelineBase() =
  abstract Gather:      buffer: RenderBuffer3D -> SceneData
  abstract Shadow:      gd * byref<SceneData> * buffer -> ShadowResult
  abstract Shade:       gd * byref<SceneData> * ShadowResult * activeEffect: Effect voption * Command3D -> unit
  abstract PostProcess: ... -> unit
  interface IRenderPipeline3D with
    member this.Execute(...) =
      let mutable scene = this.Gather buffer
      let shadow = this.Shadow(gd, &scene, buffer)
      // walk buffer: BeginEffect/EndEffect flip activeEffect; draws call this.Shade(..., activeEffect, draw)
      this.RunPasses(gd, &scene, shadow, buffer)
      this.PostProcess(...)
```

- `Shade` takes the **active effect** (user scope effect or PBR default) — so both use cases go through the same shading path; A just supplies a user effect via the scope, B supplies its own effect in its `Shade` override.
- Default `Shade` = PBR (`ForwardPipeline`). A `ToonPipeline` overrides `Shade` to bind its toon effect and still calls `SceneUpload.uploadToEffect`. `ForwardPlusPipeline` overrides `Gather` (tile grid) + `Shade` (read tiles).

**Deferred does not inherit this base** — it implements `IRenderPipeline3D` directly and reuses `SceneData`/`ShadowPass`/`SceneUpload` but writes its own G-buffer + lighting passes. Different pass architecture, correctly separate.

## 5. What breaks vs what doesn't

| Change | Breaks DSL default path? | Breaks `Command3D`? | Breaks pipeline internals? | 3D release status |
|---|---|---|---|---|
| Extract `SceneData` / `ShadowPass` / `SceneUpload` | No | No | Yes — private→public | Unreleased, no consumers |
| `ForwardPipelineBase` + thin `ForwardPipeline` | No | No | Yes — reorganization | Unreleased, no consumers |
| `BeginEffect`/`EndEffect` commands + DSL | No (additive) | Yes — 2 new cases | Yes — pipeline tracks scope | Unreleased, only `MonoThreeD` migrates |
| `IRenderer`/`IRenderPipeline3D` contracts | **No** | **No** | **No** | Stable |

The default DSL path (`drawModel`/`drawAnimatedModel`/`drawInstanced`/`drawPrimitive`) is unchanged line-for-line. All new power is opt-in. Breakage is confined to unreleased internals. `MonoThreeD` is the only caller and migrates trivially (it uses the default path; no `beginEffect`).

## 6. Non-Goals

- **No named passes.** Effect is the scope identity (mirrors 2D).
- **`Material3D` stays pure** — no effect handle. The effect comes from the scope (use case A) or the pipeline (use case B), never the material.
- **No nested scopes.** `BeginEffect` inside an open `BeginEffect` is a user error (documented; base asserts/ignores).
- **No pass-local lights** for v2. Lights are gathered once (scene-global) and uploaded to every effect. Pass-local lights deferred.
- **Deferred is not unified with Forward.** Different pass architecture; reuses the data layer only.
- **Not touching Raylib this round.** Prototype in MonoGame 3D; port to Raylib for v2 later.

## 7. Decisions (confirmed)

1. **`activeEffect` is `Effect voption`.** `None` = use the pipeline's default PBR effect; `Some userEffect` = bind the user effect for that draw.
2. **`EndCamera` implicitly closes any open `BeginEffect` scope.** Cameras draw separately; a scope does not persist across `EndCamera`. (Documented; `ForwardPipelineBase` tracks scope state and resets it when the camera block ends.)
3. **A `BeginEffect` scope casts shadows** — shadow casters are gathered from draws regardless of scope (shadows are scene-global), so a toon model still casts using the depth shader. The existing `EnableShadows`/`DisableShadows` toggle is the override: a scope active under `DisableShadows` casts no shadow, matching today's behavior.
4. **Spelling is `beginEffect`/`endEffect`** for the MonoGame backend — consistent with MonoGame terminology (the type is `Effect`). The Raylib port (later, for v2) will spell it `beginShader`/`endShader` to match that backend's terminology and the existing 2D surface.
