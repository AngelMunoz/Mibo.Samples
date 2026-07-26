# Bone pose queries, palette reuse & attachment draws (Mibo #86)

## Goal

Close AngelMunoz/Mibo#86 in full — all three shapes from the issue, on **both** backends
(MonoGame + raylib), surfaced through the fluent `Draw` DSL:

1. **Bone query** — current model-space world transform of a bone (by name or index) for an
   animated-model instance in the current frame.
2. **Palette reuse** — one pose evaluation per instance per frame, shared between the skinned
   draw and any number of bone queries/attachments; caller owns the computed pose.
3. **Attachment draws** — draw a static mesh parented to a bone of an animated model.
4. **raylib de-mutation (opt-in)** — a new raylib `AnimatedModel` record draws through the
   existing GPU-skinned path (`DrawSkinnedMesh`) instead of mutating the `Model` via
   `UpdateModelAnimation`, so the same model can be drawn with several different poses
   (today: last-writer-wins). The legacy `Animation3DState` path keeps mutating — no breaks.

Scope decision (user): **skinned + instanced is designed but not implemented in this PR** —
the plan includes a design spec (Step 4) that lands in the docs for a follow-up PR.

Hard constraints (from user):

- **No new `Command3D` cases.** Attachments lower to the existing plain-mesh draw cases
  (`DrawPrimitive` on MonoGame, `DrawMesh` on raylib). `Command3D` files are not touched at all.
- **No piped DSL.** The deprecated `Draw3D.*` module functions get nothing new and are not
  modified.
- Verified against raylib 6.0 / raylib-cs 8.0.0 (`E:/raylib`, `E:/raylib-cs`) — do not rely on
  5.x API shapes.

## Design overview

New concept: **`BonePose`** (per backend, `[<Struct>]` record) — the result of evaluating an
animated model's pose once:

```fsharp
// MonoGame: Matrix; raylib: Matrix4x4
type BonePose = {
  WorldPoses: Matrix[]  // model-space bone transform, current frame (attachment/query data)
  Palette:    Matrix[]  // InverseBindPose[i] * WorldPoses[i] (shader skinning palette)
}
```

- MonoGame: produced by refactoring the existing `computeBonePalette` body — it already computes
  `worldPoses` internally and discards them (`Mibo.MonoGame/Animation3D.fs:681-697`).
- raylib: produced by sampling `state.Clips` keyframe poses (same math as the existing
  `AnimatedMesh.computeBoneMatrices`, `Mibo.Raylib/Animation3D.fs:403-482`, extended to honor the
  blend state the way `applyToModel` does). raylib 6 keyframe poses are model-space
  (`UpdateModelAnimation` does `invert(bindPose) * currentPose` with no parent walk —
  `E:/raylib/src/rmodels.c:2348`), so `WorldPoses` is the sampled pose directly; no parent
  composition needed.

Bone addressing: new backend-neutral `[<Struct>] type BoneRef = ByName of string | ByIndex of int`
in `Mibo.Core/Animation3D.fs` (namespace `Mibo.Animation`, next to the shared clock). Name lookup
goes through a name→index dictionary retained on each backend's `AnimatedMesh` (both backends
already build/have access to one at load time and throw it away).

Attachment composition order (documented, row-vector convention used by both backends):
`attachmentWorld = localTransform * boneWorld * instanceTransform`
— the attached draw inherits the instance's full world transform including scale; `localTransform`
is the caller's grip offset/rotation/scale.

Missing bones are graceful: queries return `ValueNone`, attachment witnesses emit **no command**
(no-op), never an exception.

The `pose` parameter threads through the DSL as an **optional parameter** (PR #85 pattern — source
compatible, IL signature change, samples recompile). When omitted, the witness computes the pose
internally exactly as today.

## Matrix conventions (verified — read before touching the math)

- **MonoGame / AssimpNet**: Assimp matrices arrive in column-vector convention and are
  **transposed at load time** — the inverse-bind `OffsetMatrix` (`Animation3D.fs:738-747`) and
  the node-local bind pose `node.Transform` (`Animation3D.fs:801-806`) both go through
  `Matrix.Transpose`, matching what MonoGame's own OpenAssetImporter does. Clip keyframe
  transforms are built directly in row-vector convention (`buildTrsMatrix`). Consequence:
  everything downstream — `InverseBindPose`, `BindLocalPoses`, sampled local poses, and the
  composed `worldPoses` (`local * worldParent`, local-on-the-LEFT, row-vector,
  `Animation3D.fs:670-679`) — is already in MonoGame's row-vector convention. `BonePose.WorldPoses`
  is therefore consumed **as-is**: no transpose, no inversion, no
  `Matrix.Invert(InverseBindPose[i]) * palette[i]` recovery anywhere in the query/attachment
  path. The attachment composition `localTransform * boneWorld * instanceTransform` is plain
  row-vector composition.
- **raylib**: `fromModel` stores `InverseBindPose[i] = Matrix4x4.Invert(buildMatrix bindPose[i])`
  (`Animation3D.fs:377-380`); keyframe poses are model-space and used directly (raylib 6's
  `UpdateModelAnimation` does `invert(bindPose) * currentPose` with no parent walk —
  `E:/raylib/src/rmodels.c:2348`). So raylib `WorldPoses[i]` is simply the sampled pose matrix —
  again consumed as-is.
- Tests must respect these conventions: synthetic rigs in MonoGame tests are authored directly
  in row-vector convention (no fake "Assimp" column-vector inputs to transpose).

## Step 1 — Mibo.Core

`src/Mibo.Core/Animation3D.fs`:
- Add `[<RequireQualifiedAccess; Struct>] type BoneRef = ByName of name: string | ByIndex of index: int`
  with XML docs (name = authoring-friendly, index = fast path; missing bone → ValueNone/no-op).

`src/Mibo.Core/Graphics/Draw.fs`:
- `animatedModel` (:1184), `animatedModelWith` (:1193), `animatedModelWithPerMesh` (:1202):
  add optional `?pose: 'Pose`. Witness constraints gain a trailing `'Pose voption`:
  `'B: (member AddAnimatedModel: 'A * 'X * 'Pose voption -> unit)` (same for `With`/`WithPerMesh`).
  Body: materialize `defaultArg pose` → `ValueSome`/`ValueNone` and pass through.
  Update doc comments: pose lets the caller share one evaluation between the draw and bone
  queries/attachments. On raylib it is honored by the new `AnimatedModel` witness (GPU path)
  and ignored by the legacy `Animation3DState` witness (mutating path) — docs say so.
- New `attachedMesh` member (placed right after `skinnedMesh`, ~:1225):

  ```fsharp
  static member inline attachedMesh<'B, 'A, 'X, 'M, 'Mat, 'Pose
    when 'B: (member AddAttachedMesh: 'A * BoneRef * 'X * 'M * 'Mat * 'X * 'Pose voption -> unit)>
    (buffer, animModel: 'A, bone: BoneRef, localTransform: 'X,
     mesh: 'M, material: 'Mat, transform: 'X, ?pose: 'Pose) : 'B
  ```
  Doc: draws `mesh` parented to `bone` of `animModel`; world =
  `localTransform * boneWorld * transform`; unknown bone → no-op; pass the same `pose` given to
  `animatedModel` to avoid a second evaluation.
- `skinnedMesh` doc comment: keep raylib-only note, add pointer to `animatedModel(..., pose)` as
  the MonoGame explicit-palette path.

## Step 2 — Mibo.MonoGame

`src/Mibo.MonoGame/Animation3D.fs`:
- `AnimatedMesh` (:102-131): add field `BoneLookup: IReadOnlyDictionary<string, int>`.
  Populate in `fromScene` from the existing local `nameToIndex` dict (:753-759) — keep building it
  exactly where it is, just store it on the record.
- `module AnimatedMesh`: add `tryFindBoneIndex (name: string) (mesh: AnimatedMesh) : int voption`.
- Add `BonePose` struct record + `module BonePose`:
  - `worldAt (index: int) (pose: BonePose) : Matrix voption` (bounds-checked).
  - `tryGetWorld (name: string) (mesh: AnimatedMesh) (pose: BonePose) : Matrix voption`.
- `module Animation3DState`: add `computePose (mesh: AnimatedMesh) (state: Animation3DState) : BonePose`
  — move the body of `computeBonePalette` (:635-699) here, returning both `worldPoses` and
  `matrices` (palette). `localPoses` stays a local scratch. Reimplement `computeBonePalette` as
  `(computePose mesh state).Palette` (signature unchanged; doc notes `computePose` when world
  poses are also needed). Same allocation count as today (3 arrays), but one evaluation now
  serves draw + queries + attachments.
- `module AnimatedModel`: add
  - `computePose (am: AnimatedModel) : BonePose voption` (ValueNone when `am.Mesh` is ValueNone).
  - `tryGetBoneWorld (bone: BoneRef) (am: AnimatedModel) : Matrix voption` — convenience that
    computes the pose internally; doc marks it as recompute-per-call, pointing to `computePose`
    for multi-query frames.

`src/Mibo.MonoGame/Graphics3D/RenderBuffer3D.fs` (all witnesses live in this augmentation, :139-229):
- `AddAnimatedModel`, `AddAnimatedModelWith`, `AddAnimatedModelWithPerMesh`: add trailing
  `pose: BonePose voption`. ValueSome → use `pose.Palette`; ValueNone → current behavior
  (compute from mesh/state; empty array when no mesh).
- New `AddAttachedMesh` (flat `ValueOption` pipeline — one `bind`, one `iter`, no nested
  matches; `BonePose.worldAt` bounds-checks, so `ByIndex` needs no separate guard):

  ```fsharp
  member inline b.AddAttachedMesh
    (am: AnimatedModel, bone: BoneRef, localTransform: Matrix,
     mesh: PrimitiveMesh, material: Material3D, transform: Matrix, pose: BonePose voption) =
    am.Mesh
    |> ValueOption.bind (fun animMesh ->
      let pose' =
        match pose with
        | ValueSome p -> p
        | ValueNone -> Animation3DState.computePose animMesh am.State

      match bone with
      | BoneRef.ByIndex i -> BonePose.worldAt i pose'
      | BoneRef.ByName name ->
        AnimatedMesh.tryFindBoneIndex name animMesh
        |> ValueOption.bind (fun i -> BonePose.worldAt i pose'))
    |> ValueOption.iter (fun boneWorld ->
      b.Add(Command3D.DrawPrimitive(mesh, localTransform * boneWorld * transform, material)))
  ```

  raylib's `AddAttachedMesh` uses the same shape, emitting `Command3D.DrawMesh`.

No pipeline changes: `DrawAnimatedModel*` cases and `PbrShading.fs` consumption are untouched.

## Step 3 — Mibo.Raylib

`src/Mibo.Raylib/Animation3D.fs`:
- `AnimatedMesh` (:340-344): add `BoneNames: string[]`, `BoneParents: int[]`,
  `BoneLookup: IReadOnlyDictionary<string, int>`. Populate in `fromModel` (:368-391) from
  `model.Skeleton.BonesAsSpan()` (`BoneInfo.NameToString()`, `.Parent` — raylib-cs
  `E:/raylib-cs/Raylib-cs/types/Model.cs:11-33,66-74`).
- `module AnimatedMesh`: add `tryFindBoneIndex`.
- Add raylib `BonePose` (`Matrix4x4[]` fields) + `module BonePose` (`worldAt`, `tryGetWorld`).
- `module Animation3DState`: add `computePose (mesh: AnimatedMesh) (state: Animation3DState) : BonePose`
  — sample current clip/frame (and blend target when `isBlending`, mirroring the `applyToModel`
  branch at :279-296) from `state.Clips` keyframe poses with the same TRS sampling math as
  `AnimatedMesh.computeBoneMatrices`; `WorldPoses[i]` = sampled model-space pose,
  `Palette[i]` = `InverseBindPose[i] * WorldPoses[i]`. No model mutation.
- New `[<Struct>] type AnimatedModel = { Mesh: AnimatedMesh; State: Animation3DState }` +
  `module AnimatedModel` with `create`, `computePose`, `tryGetBoneWorld` (mirrors the MonoGame
  surface; `State` already carries the `Model` on raylib). Existing raylib call sites pass bare
  `Animation3DState` to `animatedModel` — that keeps working unchanged (mutating path).
  `AnimatedModel` is the opt-in **GPU skinning path**: no model mutation, per-instance poses.

`src/Mibo.Raylib/Graphics3D/RenderBuffer3D.fs`:
- Existing `AddAnimatedModel` / `AddAnimatedModelWith` (taking `Animation3DState`, :182-215):
  add trailing `pose: BonePose voption`, **ignored** (doc: the mutating path derives nothing
  from a palette; use the `AnimatedModel` overload for the GPU path).
- New overloads taking the `AnimatedModel` record — the opt-in de-mutated path:

  ```fsharp
  member inline b.AddAnimatedModel(am: AnimatedModel, transform: Matrix4x4, pose: BonePose voption) =
    let p =
      match pose with
      | ValueSome p -> p
      | ValueNone -> Animation3DState.computePose am.Mesh am.State

    let model = am.State.Model
    let meshes = model.MeshesAsSpan()

    for i = 0 to meshes.Length - 1 do
      let mat = Material3D.fromRaylibMaterial (model material for mesh i)  // via MeshMaterial[i]
      b.Add(Command3D.DrawSkinnedMesh(meshes[i], transform, mat, p.Palette))
  ```

  Emits one existing `DrawSkinnedMesh` per mesh — no `applyToModel`, no mutation; `pose` is
  honored. Implementation details to verify during coding: `Material3D.fromRaylibMaterial` per
  mesh via `model.MeshMaterial` index (same pattern as `docs/graphics3d/instancing.md:74-79`),
  and whether `model.Transform` must be composed into `transform` (raylib's `DrawModel` applies
  it internally; the `DrawSkinnedMesh` path takes the transform explicitly).
  `AddAnimatedModelWith` mirrors with an explicit material. SRTP note: the buffer will carry
  overloads on both `Animation3DState` and `AnimatedModel` — F# resolves SRTP member
  constraints against the concrete buffer at each call site; if overload resolution proves
  finicky in the inline DSL member, fall back to a distinct witness name for the new path.
- New `AddAttachedMesh(am: AnimatedModel, bone, localTransform, mesh, material, transform, pose)`:
  same logic as MonoGame, emitting `Command3D.DrawMesh(mesh, localTransform * boneWorld * transform, material)`.
- Also add the raylib `module Command3D` inline builder only if a new case were added — none is,
  so `Command3D.fs` is untouched.

## Step 4 — Skinned instancing: design spec (documented, NOT implemented in this PR)

Goal for the follow-up: one draw call for N instances of the same animated model, each with
its own pose. This section lands in `docs/graphics3d/instancing.md` (replacing the bare
"not supported" note with "not yet — here is the design") so the follow-up PR can execute it.

**Mechanism: bone-palette texture** (not uniforms):

- A flat `boneMatrices[N * BONES]` uniform array blows past vs_3_0 constant limits (OpenGL
  profile ≈ 256 registers = 64 matrices total), and raylib's per-bone `SetShaderValueMatrix`
  upload is already 128 calls per draw — neither scales to N instances.
- Layout: RGBA32F texture, 4 texels per matrix (row-major), width = `boneCount * 4`, height =
  instance count; chunk the draw when instances exceed max texture height.
- Per-instance palette index: MonoGame can't use `SV_InstanceID` at vs_3_0 → add
  `float PaletteOffset : TEXCOORD6` to a new instance vertex layout (extends the
  `VertexInstanceWorld`/`VertexInstanceWorldColor` pattern in `Primitive3D.fs`). raylib GLSL
  330 has `gl_InstanceID` → sample by it directly; this also sidesteps raylib's
  `DrawMeshInstanced` only streaming the transform VBO (no extra per-instance channels without
  rl-level code).

**MonoGame work items (follow-up PR):**

- New command `DrawAnimatedModelInstanced` (Model-based — `PrimitiveMesh` has no bone channels;
  flat `palettes: Matrix[]` of `count * boneCount`).
- Shaders: `SkinnedInstanced` technique in `ForwardPbr.fx` (instance stream inputs + LBS via
  `tex2Dlod` on the palette texture) + `DepthSkinnedInstanced` in `DepthShadow.fx`; recompile
  all 4 profiles via `Shaders/script.fsx`.
- Pipeline: palette-texture pool + upload next to `stageInstanceData` (`PbrShading.fs:536-603`);
  shadow batching in `ShadowPass.fs`.
- DSL: `Draw.animatedModelInstanced`; witness computes one `BonePose` per instance (reusing
  `computePose` from Step 2) and packs the flat palette.

**raylib work items (follow-up PR):**

- New command `DrawSkinnedMeshInstanced` (mesh + transforms + flat palettes + material + count).
- Shaders: `forwardVertexSkinnedInstanced` (+ depth variant) in `Pipelines/Shaders.fs` —
  `instanceTransform` attribute + bone attributes + palette texture fetch by `gl_InstanceID`.
- Pipeline: new variant; shadow-pass `InstancedMeshDraw` (`ForwardPbrPipeline.fs:399-408`)
  must carry bones.

**Open questions for the follow-up:** palette texture format/precision across the dx12/vk mgfx
profiles; whether a cheaper "shared pose, many transforms" intermediate mode (one palette, N
instances — no texture needed, uniform array suffices) is worth shipping first.

## Step 5 — Tests

`src/Mibo.MonoGame.Tests/` — new `Animation3DTests.fs` (register in fsproj):
- Build a synthetic 3-bone `AnimatedMesh` by record literal (root → child → grandchild, one
  channelless bone using `BindLocalPoses` fallback) + synthetic clip.
- `computePose`: exact world-pose composition numbers (parent translation propagates),
  `Palette[i] = InverseBindPose[i] * WorldPoses[i]`.
- `tryFindBoneIndex`: hit, miss, index fast path bounds.
- `AddAttachedMesh`: records one `DrawPrimitive` with `local * boneWorld * transform`; unknown
  bone name records nothing (buffer count unchanged); shared `pose` value is honored.

`src/Mibo.Raylib.Tests/Animation3DTests.fs` (extend; reuse the existing `ModelAnimation`
marshal helper, :16-65):
- `computePose` sampling at exact frames and fractional lerp; blend path.
- `tryFindBoneIndex` / `BonePose.worldAt` on a record-literal `AnimatedMesh` (no native model
  needed).
- `AddAnimatedModel(AnimatedModel)` witness: emits one `DrawSkinnedMesh` per mesh carrying the
  shared palette; a passed `pose` is forwarded untouched (inspect recorded commands, mirroring
  the existing `drawSkinnedMesh` DU test at `Graphics3DTests.fs:148-159`).
- Where a native `Model` is unavoidable (buffer witness), keep coverage to what the existing
  test helpers can construct; DSL compile-level coverage is provided by the samples build.

## Step 6 — Docs & changelog

- `docs/animation3d.md`: new section — `BonePose`, `computePose`, bone queries, attachment
  draws, composition order, one-evaluation-per-frame guidance, missing-bone semantics, and the
  note that skinned+instanced remains unsupported so attachments are per-instance draws. Also
  document the raylib tier change: `Animation3DState` = legacy mutating path,
  `AnimatedModel` = opt-in GPU path (no mutation, multi-pose per model, honors `pose`).
- `docs/draw-dsl.md`: document `pose` on `animatedModel*` and the new `attachedMesh`.
- `docs/graphics3d/instancing.md`: replace the bare "Skinned + instanced draws are not
  supported" note with the Step-4 design spec (mechanism, work items, open questions);
  same for the one-liner in `docs/shader-uniforms.md:208-211` (point at the design).
- `CHANGELOG.md`: entry under Unreleased (KeepAChangelog format; note the IL-signature change /
  recompile caveat exactly as PR #85 did).
- `Mibo/AGENTS.md`: update the "Skinned + instanced remains unsupported" lines (:59, :162) to
  point at the documented design; check the DSL/witness section for staleness.

## Step 7 — Validation

1. `dotnet build` in `Mibo/` — 0 warnings / 0 errors.
2. `dotnet test` — Mibo.Core.Tests, Mibo.MonoGame.Tests, Mibo.Raylib.Tests all green.
3. `dotnet fantomas .` in `Mibo/` — clean.
4. Build the full `Mibo.Samples` solution against the branch — all existing `animatedModel` call
   sites must compile unchanged (source compat via optional params).
5. Ergonomics check in one sample: wire a small bone attachment in `Platformer3D` (skeletal
   character; attach a simple prop mesh to a hand/head bone via `attachedMesh` + shared `pose`)
   on both a MonoGame and the raylib client, and confirm it tracks the animation. On the raylib
   client, use the new `AnimatedModel` record so the check also exercises the opt-in GPU path
   (verify two instances of the same model can hold different poses). Keep the diff minimal;
   it's the living example for the issue.

## Notes / explicit non-goals

- No MonoGame `AddSkinnedMesh` witness: MonoGame skinning requires `Model` + `SkinnedEffect`
  (its `PrimitiveMesh` carries no bone weights), so `Draw.skinnedMesh` stays raylib-only; the
  MonoGame explicit-palette path is `animatedModel(..., pose)`. Doc comments say so.
- No per-frame pose caching on `Animation3DState` (struct, no frame tick exists) — the caller
  owns the `BonePose` value; scratch-buffer pooling is a possible later optimization.
- raylib's mutating `Animation3DState` path is intentionally preserved (existing call sites
  unaffected, CPU-skinned vertices stay available for raycast/picking); the new `AnimatedModel`
  record is the opt-in GPU path used by attachments/queries and multi-pose draws.
- Skinned + instanced: design spec only (Step 4) — implementation is a follow-up PR.
- Legacy `AnimatedMesh.computeBoneMatrices` (both backends) untouched.
- Git: work on a feature branch in the `Mibo/` submodule; ask before any commit/push (per repo
  imperatives).
