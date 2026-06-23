# Design: Unified 3D Draw API — PBR Everywhere

> **Repo:** `Mibo` framework (backend `Mibo.MonoGame`), on branch `feat/monogame3d`.
> **Spike:** `MonoThreeD/` in `Mibo.Samples`.
> **Status:** Design — awaiting review.

## 1. Problem

The 3D draw surface grew a command zoo (`DrawMesh` / `DrawMeshEffect` / `DrawModel` /
`DrawSkinnedMesh` / `DrawMeshPBR` / `DrawMeshInstanced`) because of a de-risking decision
during the port (plan §4.1): in MonoGame a `ModelMeshPart` **already owns an `Effect`**, so
pairing it with a `Material3D` means "two materials fight over one draw." The resolution was to
split the world in two:

- **Native `Effect` path** — `DrawMesh`/`DrawModel`/`DrawSkinnedMesh` bind the part's own
  `BasicEffect`/`SkinnedEffect`.
- **PBR path** — `DrawMeshPBR`/`DrawMeshInstanced` bind the custom PBR effect, but **only** on
  effectless `PrimitiveMesh`.

This split is the source of two real problems the user hit in the spike:

1. **Confusing user-facing surface.** A user must decide "model vs skinned vs PBR vs instanced."
   The user's desired mental model is just two cases: *one model* (static or animated) and
   *many copies of the same thing* (static bulk).
2. **Visibly broken behavior.** `DrawModel`/`DrawSkinnedMesh` bind native effects, so a textured
   model renders **flat white** (the native path never reaches PBR, and the `applyLighting`
   wiring produces flat-lit output). The PBR cube renders **black** (the model path is a dead
   end for textures, and the PBR path itself is regressed in the working copy). Point/spot lights
   only ever upload to the PBR effect (`uploadPbrLights`) and therefore **never light imported
   models** today.

## 2. Goal & Non-Goals

### Goal

Collapse the user-facing 3D draw DSL to two mental models, both of which auto-route through the
custom PBR effect and therefore participate in **PBR + directional/point/spot lights + shadows**
with no user decision-making:

- **One model** — static (`drawModel`) or animated (`drawAnimatedModel`).
- **Many copies** — static bulk (`drawInstanced`), camera-culled via the existing grid renderers.

`BasicEffect`/`SkinnedEffect` survive only as (a) the escape hatch for users who bring their own
effect (`DrawMeshEffect`), and (b) the **source** from which a model's authored look is read when
the pipeline swaps to PBR.

### Non-Goals

- **Not** touching the dual model-load (content-pipeline XNB for mesh/textures + AssimpNetter for
  animation clips/skeleton). A MonoGame content-pipeline rework is coming in 3.5.x; we revisit the
  split then. The dual option stays as-is.
- **Not** designing per-instance independent animation. Instanced draws are **static**. We document
  that animated rendering requires the single-model path. (A future "shared pose across an instance
  batch" is possible later but is explicitly out of scope.)
- **Not** collapsing the internal `Command3D` DU. The DU is an implementation detail (users use the
  DSL); the zero-cost ArrayPool buffer and the `Layout3D` renderer contract stay intact. Approach A.
- **Not** fixing the existing PBR shader regressions as part of this design. The current spike is
  broken (flat-white models, black cube); repairing those is an **implementation task** for the plan
  that realizes this design, not a design decision. The design assumes a *working* PBR effect is the
  foundation and defines what "working" must mean (§7).

## 3. Core Principle

**Every model and primitive draw auto-binds the custom PBR effect.** The linchpin that makes this
possible is a missing helper, specced in plan B1 but never written:

> **`Material3D.fromModelMeshPart`** reads a `ModelMeshPart`'s baked `BasicEffect`/`SkinnedEffect`
> (DiffuseColor, Texture, Alpha, texture-enabled flag) into a `Material3D`, so when the pipeline
> swaps the part's effect for the PBR effect the model keeps its authored color and texture instead
> of going flat-white.

This mirrors the 2D backend's pattern exactly. `LightCommands.litAnimatedSprite`
(`Graphics2D/Lighting/LightCommands.fs`) *extracts* texture/source/origin/color/normal-map from an
`AnimatedSprite` and hands a derived `SpriteState` to the lit pipeline. The 3D analog: extract
material params from the part's native effect and hand a `Material3D` to the PBR pipeline.

`Material3D` is **no longer "PBR-only, pairs only with `PrimitiveMesh`."** It becomes the universal
material carrier for the PBR pipeline, derived from any source (a part's native effect, or a
hand-authored struct for primitives).

## 4. Target DSL (user-facing)

Mirrors 2D's `Draw`/`LightDraw` module shape: distinct, non-overloaded names; the buffer is always
the last argument; `inline`.

### Geometry (single model)

```fsharp
/// Static model. Auto-PBR + lights + shadows. The part's baked native effect is
/// read via Material3D.fromModelMeshPart so the model keeps its authored look.
Draw3D.drawModel model transform buffer

/// Animated model. Mirrors 2D's litAnimatedSprite: takes the runtime state value
/// + a transform; derives bones internally; auto-PBR + lights + shadows.
Draw3D.drawAnimatedModel animatedModel transform buffer
```

`drawModel` is **static only.** Animation is opt-in via the dedicated `drawAnimatedModel` entry —
the user passes an `AnimatedModel` value (§5), never a raw `Matrix[]` bones array. No overloading.

### Geometry (bulk)

```fsharp
/// Static instanced bulk (terrain/props). Auto-PBR + lights + shadows.
/// Used by CellGridRenderer3D/HexGrid3DRenderer after camera culling.
Draw3D.drawInstanced mesh transforms material instanceCount buffer
```

This is the existing `drawMeshInstanced`, renamed for clarity (`drawInstanced`). It stays on
`PrimitiveMesh` + `Material3D` because instance vertex streams require effectless geometry.

### New (DSL): `drawPrimitive`

```fsharp
/// Effectless primitive with an authored Material3D. Auto-PBR + lights + shadows.
Draw3D.drawPrimitive mesh transform material buffer
```

This is `drawMeshPBR` renamed and is the single-primitive case (vs `drawInstanced`'s bulk case).
Kept separate because a primitive carries no native effect to read, so it can't ride `drawModel`.

`drawBillboard`, `drawBillboardBatch`, `drawLine3D`, the camera/light/shadow commands, `drawImmediate`,
`drop` keep their current names and shapes.

### Removed / replaced from the DSL

| Today                               | After                                   | Why                                            |
| ----------------------------------- | --------------------------------------- | ---------------------------------------------- |
| `Draw3D.drawMesh`                   | (internal only; use `drawModel`)        | Single-part case folds into `drawModel`        |
| `Draw3D.drawSkinnedMesh`            | `Draw3D.drawAnimatedModel`              | Bones come from `AnimatedModel`, not raw array |
| `Draw3D.drawMeshPBR`                | `Draw3D.drawPrimitive` (primitive)      | See §6 — primitive vs model decision           |
| `Draw3D.drawMeshInstanced`          | `Draw3D.drawInstanced`                  | Rename only                                    |

`drawMeshEffect` stays (the "I brought my own Effect" escape hatch).

## 5. `AnimatedModel` — mirror of `AnimatedSprite`

The 2D backend's `AnimatedSprite` (struct, `Animation.fs`) is the template: a runtime-state value
carrying a reference to **shared** immutable clip/skeleton data, with a module of pure update
functions. 3D already has the shared data half (`Animation3DClips`, `AnimatedMesh` in
`Animation3D.fs`) and a state type (`Animation3DState`). What it lacks is the 2D-style *bundled*
value the DSL can consume.

### New: `AnimatedModel` value type

```fsharp
/// Runtime state for a single animated 3D entity. Mirrors AnimatedSprite:
/// holds a reference to shared mesh/skeleton + clip data and the live playback
/// state. Store one per entity in your Elmish model.
[<Struct>]
type AnimatedModel = {
  /// The MonoGame model to draw (meshes/textures from the content pipeline).
  Model: Model
  /// Shared skeleton data (bone names, parents, inverse-bind). ValueNone if the
  /// model has no bones — drawAnimatedModel then falls back to static drawModel.
  Mesh: AnimatedMesh voption
  /// Live playback state (current clip, frame, blend, speed, loop).
  State: Animation3DState
}
```

### New: `module AnimatedModel`

Pure functions mirroring `module AnimatedSprite` (create / play / playByIndex / playIfNot /
restart / update / isFinished / isPlaying / duration / currentClipName / withSpeed / withLoop).
Each returns a new `AnimatedModel`. `update dt model` advances the `State` in place via the
existing `Animation3DState.update`. **No bone computation here** — that happens at draw time
(one palette per draw, not per update), matching how 2D keeps `update` allocation-free.

The DSL entry:

```fsharp
module Draw3D =
  let drawAnimatedModel (am: AnimatedModel) (transform: Matrix) (buffer: RenderBuffer3D) =
    // Internal: compute the bone palette from am.State (existing
    // Animation3DState.computeBonePalette), then emit the skinned command.
    buffer
```

The bones array is computed *inside* the DSL wrapper (or inside the command, at dispatch time) —
the user never sees a `Matrix[]`. This is the direct 3D analog of `litAnimatedSprite` calling
`AnimatedSprite.currentSource` internally to derive the sprite rect.

## 6. Command DU (internal) — Approach A

The `Command3D` DU is kept as the zero-cost internal carrier. Its draw cases are **re-typed** so
they carry the unified inputs the PBR pipeline needs:

```fsharp
type Command3D =
  | DrawModel of model: Model * transform: Matrix
  | DrawAnimatedModel of model: Model * transform: Matrix * bones: Matrix[]
  | DrawInstanced of mesh: PrimitiveMesh * transforms: Matrix[] * material: Material3D * instanceCount: int
  | DrawMeshEffect of meshPart: ModelMeshPart * transform: Matrix * effect: Effect  // escape hatch
  | DrawBillboard ...
  | DrawBillboardBatch ...
  | DrawLine3D ...
  | BeginCamera ... | BeginCameraConfig ... | EndCamera
  | SetShadowOrigin ... | lights ... | EnableShadows | DisableShadows
  | DrawImmediate ...
```

Notes:

- **`DrawModel`** now always means "route through PBR" (the pipeline reads the model's part effects
  into `Material3D` and binds the PBR effect). It subsumes the old `DrawMesh` (single part) and
  old `DrawModel` (whole model). The pipeline iterates `model.Meshes`/`model.MeshParts` as today.
- **`DrawAnimatedModel`** carries a precomputed `bones` palette (the DSL wrapper computes it from
  `AnimatedModel` so the value-type command stays small). This replaces `DrawSkinnedMesh`.
- **`DrawMeshPBR` becomes `DrawPrimitive(PrimitiveMesh, Matrix, Material3D)`.** A primitive is
  effectless geometry, so it cannot ride the `Model`-based `DrawModel` case (which reads material
  from the part's native effect). Keeping a dedicated primitive case — rather than forcing the
  primitive through `DrawInstanced` with `count=1` — keeps `Material3D`-authored single primitive
  draws first-class and avoids count=1 hackery. The `Layout3D` renderer's instanced primitive output
  stays on `DrawInstanced`. (Listed in §10 as a confirmation point, but this is the decided shape.)
- `DrawMesh`, `DrawSkinnedMesh`, `DrawMeshInstanced` removed.

The `Layout3D` renderer contract (`getMeshesAndMaterial: 'T -> struct (PrimitiveMesh * Material3D)[]`)
is **unchanged** — it already emits instanced primitive draws, which become `DrawInstanced`.

## 7. Pipeline changes (`ForwardPipeline`)

The dispatch rewrite is the heart of this design. Every model draw now routes through PBR.

### `handleDrawModel(model, transform)`

For each `ModelMesh`/`ModelMeshPart`:

1. `let mat = Material3D.fromModelMeshPart part` — read DiffuseColor/Texture/Alpha/TextureEnabled
   from the part's `BasicEffect`/`SkinnedEffect` into a `Material3D` (albedo color, albedo map,
   opacity; roughness/metallic default to sensible non-metal values).
2. Bind the PBR effect (`ensurePbrEffect`), technique `Standard`.
3. Upload `mat` + camera matrices + lights (existing `uploadPbrMaterial`/`uploadPbrLights`).
4. Compose the world matrix with the bone hierarchy
   (`model.CopyAbsoluteBoneTransformsTo` → `boneTransforms[mesh.ParentBone.Index] * transform`),
   exactly as the current `handleDrawModel` does (lines 801-818).
5. Draw the part (the part's *own* effect is ignored; the PBR effect draws its vertex/index buffers
   via `drawPart`-style `DrawIndexedPrimitives`).

### `handleDrawAnimatedModel(model, transform, bones)`

Same as above but technique `Skinned`, uploading the bone palette to `boneMatrices[128]`. Only parts
whose vertex declaration carries `BLENDINDICES0`/`BLENDWEIGHT0` use the `Skinned` technique; parts
without skinning data fall back to `Standard`. Replaces `handleDrawSkinnedMesh`.

### `handleDrawInstanced` / `handleDrawPrimitive`

`handleDrawInstanced` is the existing instanced dispatch (technique `Instanced`), renamed.
`handleDrawPrimitive` (new, if we keep the thin case) is technique `Standard` on a `PrimitiveMesh` —
essentially today's `handleDrawMeshPBR` minus the BasicEffect fallback.

### Native `BasicEffect` path — gone from the model dispatch

The `applyLighting(effect: IEffectLights)` path is no longer used for models. It survives only for
billboards/lines (which use native `BasicEffect` unlit, as today). This is what fixes the
flat-white-model bug at the design level: there is no longer a model code path that binds a native
lit effect.

### Shadow pass

`DrawAnimatedModel` casts shadows via the existing `DepthSkinned` technique path (today's shadow
pass already collects `DrawSkinnedMesh` casters — ForwardPipeline.fs:1674). The caster-collection
match is retyped to `DrawAnimatedModel`. No new shadow work.

## 8. What "the PBR effect is working" must mean (implementation gate)

Because the spec is design-only and the spike is broken, the plan that realizes this design must
verify the PBR foundation first. The acceptance bar for "PBR works," before building the new API:

1. A `PrimitiveMesh` cube with an albedo texture renders **textured and lit** (not black, not
   flat-white) under a directional + ambient light.
2. A textured model loaded from the content pipeline renders **with its authored texture and
   color**, lit, once routed through PBR via `Material3D.fromModelMeshPart`.
3. Point/spot lights visibly affect both the cube and the model (the whole point of §3).
4. Directional shadows fall on both.
5. Sampler states are set for **all** texture slots the PS reads (0..5), not just slot 0 — a likely
   contributor to the current black-cube symptom and a required fix.

If any of these fail, the shader/pipeline bug is fixed first; the unified API builds on top.

## 9. Migration / compatibility

This is a breaking change to the 3D DSL and `Command3D`. A **clean break (no deprecation
shims)** is the decided approach: the 3D backend is unreleased — nothing on `feat/monogame3d` has
been merged to `main`, and the only consumers are the in-flight `MonoThreeD`/`ThreeDSample` ports.
The spike `MonoThreeD/Program.fs` migrates with the design.

**Changelog guardrail:** `Mibo/CHANGELOG.md` follows KeepAChangelog strictly (`## [Unreleased]` →
`### Added`/`### Changed`/`### Fixed`/`### Removed`). This work lands entries under `### Changed`
(breaking DSL/DU retyping) and `### Removed` (dropped `DrawMesh`/`DrawSkinnedMesh`/`DrawMeshPBR`
cases + DSL helpers). **The changelog must be edited carefully** — read it before editing, match the
existing entry style, and never reflow/merge existing entries. No changelog edit happens until the
implementing code actually lands.

Changelog entry under `## [Unreleased]` → `### Changed` in `Mibo/CHANGELOG.md`:
"3D draw DSL unified to `drawModel` / `drawAnimatedModel` / `drawInstanced`; all model/primitive
draws now route through the custom PBR effect (point/spot lights + shadows apply to imported
models)."

## 10. Open questions for the plan (not the design)

- ~~`DrawPrimitive` thin case vs. `DrawInstanced` count=1 reuse~~ — resolved in §6 (`DrawPrimitive`
  is the decided shape; confirm no struct-size regression).
- Whether `Material3D.fromModelMeshPart` should also pull a normal/metallic/roughness map from the
  part's `Tag`/`SkinningData` when the importer bakes them. **Decided: albedo color + albedo map +
  opacity only for now.** Map extraction (normal/roughness/metallic) is deferred until the
  content-pipeline rework lands and we can reliably read importer-baked map references.
- Exact struct-size impact on the ArrayPool command buffer from retyping cases (the DU is already
  `[<Struct>]` with the largest case sizing the union; confirm no regressions in allocation).
