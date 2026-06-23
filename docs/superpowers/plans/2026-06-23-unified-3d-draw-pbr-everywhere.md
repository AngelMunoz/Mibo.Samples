# Unified 3D Draw API — PBR Everywhere — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the 3D draw DSL to two mental models (`drawModel` / `drawAnimatedModel` / `drawInstanced` / `drawPrimitive`) and route every model and primitive draw through the custom PBR effect so imported models get PBR + point/spot lights + shadows automatically.

**Architecture:** Approach A — keep the internal `Command3D` DU as the zero-cost carrier, re-type its draw cases, and rewrite the forward-pipeline dispatch so model draws auto-bind the PBR effect. The linchpin is a new `Material3D.fromModelMeshPart` helper that reads a part's baked native effect (DiffuseColor/Texture/Alpha) into a `Material3D`, mirroring the 2D `litAnimatedSprite` extraction pattern. A new `AnimatedModel` value type + module mirrors 2D's `AnimatedSprite`. The `BasicEffect`/`SkinnedEffect` native-lit path is removed from model dispatch entirely (it survives only for billboards/lines and the `DrawMeshEffect` escape hatch).

**Tech Stack:** F# / MonoGame 3.8.4.1 (WindowsDX) / AssimpNetter 6.0.4 / custom Cook-Torrance HLSL (`ForwardPbr.fx`, `DepthShadow.fx`). Framework lives in the `Mibo/` submodule on branch `feat/monogame3d`; spike sample is `MonoThreeD/` in the samples repo.

---

## Canonical Reference (read before any shader/code doubt)

**`Mibo.Raylib` is the source of truth.** We are writing our own MonoGame implementation, but when unsure about a shader formula, lighting term, shadow convention, or animation/data shape, pull the reference from `Mibo/src/Mibo.Raylib/`:

- PBR GLSL + shadow sampling: `Mibo/src/Mibo.Raylib/Graphics3D/Pipelines/Shaders.fs`
- Forward-pipeline dispatch (prescan → shadow → forward): `Mibo/src/Mibo.Raylib/Graphics3D/Pipelines/ForwardPbrPipeline.fs`
- 3D animation (clips/state/skinning): `Mibo/src/Mibo.Raylib/Animation3D.fs`
- Material extraction from a native material: `Mibo/src/Mibo.Raylib/Graphics3D/Material3D.fs:109` (`fromRaylibMaterial`)

Adapt to MonoGame per the port plan §6 conventions: plain `float4x4` (no `row_major`), `mul(position, matrix)` (vector LEFT), right-handed math (re-derive, don't copy raylib's left-handed cross-product signs blindly), OpenGL capped at SM3.0 (`[loop]` + `if (i >= Count) break;`, no `dFdx`/`textureSize`). Do not re-derive from first principles when raylib already has the working reference.

---

## Verification Strategy (NO tests)

There is no MonoGame test project, and the user has explicitly deferred tests until after the API is working. **Do not write tests in this plan.** Each task verifies by:

1. `dotnet build Mibo.MonoGame` (and the sample) compiles.
2. `dotnet fantomas .` is clean (run from `Mibo/`).
3. **Visual verification** for rendering tasks: run the `MonoThreeD` spike, confirm the described visual result (textured + lit cube, textured + lit model, etc.). Take/keep a screenshot for the PR body.

A task is not done until its build + fantomas + (for rendering tasks) visual check pass.

---

## Repository / Branch Guardrails (from port plan §2)

- Framework work happens **inside `Mibo/`** on a topic branch off `feat/monogame3d`. The submodule is **already** on `feat/monogame3d` with uncommitted edits — verify with `cd Mibo && git branch --show-current` before starting.
- Sample work (`MonoThreeD/`) happens at the **samples repo root** on a topic branch off `feat/monogame3d`.
- **Imperatives:** NEVER push without permission (ask the user). NEVER force push (tell the user to instead). Run `dotnet fantomas .` before staging. NEVER use `Option.get`/`ValueOption.get`. `gh` PRs use a markdown body file.
- **Changelog guardrail (critical):** `Mibo/CHANGELOG.md` follows KeepAChangelog. **Read it before editing.** Match the existing entry style exactly. Never reflow/merge/reorder existing entries. New entries go under `## [Unreleased]` in the right section (`### Changed` for breaking retypes; `### Removed` for dropped cases/helpers). Edit the changelog **only in the task that actually lands the change**, not speculatively.
- **One phase = one PR.** After each phase: build, fantomas, visual check, ask the user before pushing, open PR with `--base feat/monogame3d --body-file <pr-body.md>`.

---

## Phase Ordering (do not skip ahead)

- **Phase 0 — Stabilize PBR foundation.** The current spike is broken (black cube, flat-white models). Fix the PBR shader/pipeline regressions so Phase 1 builds on a working PBR effect. *No API change.*
- **Phase 1 — `Material3D.fromModelMeshPart`.** The bridge helper. Pure logic (effect inspection), no rendering.
- **Phase 2 — Route models through PBR.** Rewrite `handleDrawModel`/`handleDrawSkinnedMesh` to bind PBR via the helper. Models now lit/textured/shadowed. *This fixes the flat-white-model bug.*
- **Phase 3 — `AnimatedModel` value + module.** Mirror `AnimatedSprite`. Pure logic.
- **Phase 4 — Unified command DU + DSL.** Re-type `Command3D` draw cases, rewrite DSL to the four entries, migrate the spike.
- **Phase 5 — Changelog + cleanup.** One careful changelog edit; remove dead code.

---

## Phase 0 — Stabilize the PBR foundation

> **No API change.** Goal: the existing spike renders correctly so Phase 1+ builds on truth, not a broken foundation. Verify against §8 of the spec: textured+lit cube, textured+lit model, point/spot lights landing on both, shadows, all sampler slots 0–5 set.
>
> The working copy of `Mibo/` already has uncommitted edits to `ForwardPbr.fx` (`.dx`/`.ogl` recompiled) + `ForwardPipeline.fs` + `Animation3D.fs` + `Assets.fs` + `Mibo.MonoGame.fsproj`. **Do not `git stash` or discard these** — they include the B12 skinning + per-light shadow-index work. Build on top.

### Task 0.1: Reproduce the broken baseline

**Files:** none (diagnostic only)

- [ ] **Step 1:** Confirm the submodule is on the right branch and dirty edits are present.
  Run: `cd Mibo && git branch --show-current && git status --short`
  Expected: `feat/monogame3d`, and modified `ForwardPbr.fx`/`ForwardPbr.dx.mgfx`/`ForwardPbr.ogl.mgfx`/`ForwardPipeline.fs`/`Animation3D.fs`/`Assets.fs`/`Mibo.MonoGame.fsproj`.

- [ ] **Step 2:** Build the framework.
  Run: `cd Mibo && dotnet build src/Mibo.MonoGame/Mibo.MonoGame.fsproj`
  Expected: Build succeeded (if it fails, capture the error — that's a Phase 0 fix).

- [ ] **Step 3:** Build + run the spike.
  Run: `cd .. && dotnet build MonoThreeD/MonoThreeD.fsproj && dotnet run --project MonoThreeD/MonoThreeD.fsproj`
  Expected: window opens titled "Mibo MonoGame 3D (MonoThreeD)". **Expected (broken) visuals:** black cube at (-2,0,0), flat-white skinned + flat-white static models near origin, CornflowerBlue background. Confirm these are the symptoms, then close the window. This is the baseline you're fixing.

### Task 0.2: Fix sampler states for all PBR texture slots (likely black-cube cause)

**Files:** Modify `Mibo/src/Mibo.MonoGame/Graphics3D/Pipelines/ForwardPipeline.fs` (the `Execute` member, near line 1914 — the device-defaults block).

The PS (`ForwardPbr.fx:308-361`) reads `texture0..texture4` (albedo/roughness/normal/metallic/emission) and the shadow pass binds `shadowAtlas` to slot 5. But `Execute` sets only `gd.SamplerStates[0]`. Missing samplers default to `PointClamp`/wrap-mismatch and can sample as black.

- [ ] **Step 1:** In `Execute`, after the existing `gd.SamplerStates[0] <- SamplerState.LinearWrap` (around ForwardPipeline.fs:1914), set a sensible sampler for slots 1–4 as well.

```fsharp
      gd.SamplerStates[0] <- SamplerState.LinearWrap
      // PBR material maps (albedo s0, roughness s1, normal s2, metallic s3, emission s4)
      // and the shadow atlas (s5) all need explicit sampler states — the PS reads all of them.
      gd.SamplerStates[1] <- SamplerState.LinearWrap
      gd.SamplerStates[2] <- SamplerState.LinearWrap
      gd.SamplerStates[3] <- SamplerState.LinearWrap
      gd.SamplerStates[4] <- SamplerState.LinearWrap
      // s5 is set per-shadow-pass to PointClamp; set a safe default here.
      gd.SamplerStates[5] <- SamplerState.PointClamp
```

- [ ] **Step 2:** `cd Mibo && dotnet build src/Mibo.MonoGame/Mibo.MonoGame.fsproj`
  Expected: build succeeds.

- [ ] **Step 3:** Run the spike (`dotnet run --project ../MonoThreeD/MonoThreeD.fsproj`).
  Expected: the **cube** is now textured + lit (Kenney colormap visible, shaded by the directional + ambient light) instead of black. If still black, proceed to Task 0.3; if fixed, skip 0.3.

- [ ] **Step 4:** `cd Mibo && dotnet fantomas .`

### Task 0.3: Verify the PBR cube texture bind path

**Files:** Read-only diagnostic; modify only if a real bug is found — `ForwardPipeline.fs` `handleDrawMeshPBR` (~line 912) and `bindPbrTextures` (~line 607).

If Task 0.2 did not fix the cube, the texture is not bound at draw time. `bindPbrTextures` sets `gd.Textures[0..4]`; confirm the albedo map is actually `ValueSome` in the spike's `cubeMat` (it is — `MonoThreeD/Program.fs:94`). The likely remaining cause: `uploadPbrMaterial` uploads `tiling` but the spike sets no tiling (defaults to `Vector2.One`), which is fine. Confirm by reading; if `bindPbrTextures` runs before each draw (it's inside the `MaterialKey`-gated block at ~941), verify it is reached even on the first draw of the frame (`pbrHasLastMaterial` starts `false`, so it is).

- [ ] **Step 1:** Read `ForwardPipeline.fs` lines 607-619 (`bindPbrTextures`) and 920-949 (`handleDrawMeshPBR` PBR branch). Confirm `bindPbrTextures` is called on first draw.
- [ ] **Step 2:** If the bind is correct but the cube is still black, suspect the `albedoColor` upload — confirm `uploadPbrMaterial` sets `AlbedoColor` to the material's color (white for `defaults`). The PS multiplies `tex2D(texture0,uv) * albedoColor`; white × texture = texture. If `albedoColor` were black, output is black.
- [ ] **Step 3:** Apply the minimal fix whatever the root cause, rebuild, re-run, confirm cube is textured+lit. Then `dotnet fantomas .`.

> **Note:** The flat-white *models* are NOT a Phase 0 fix — they're flat-white because the model path never reaches PBR (that's the §4.1 trap, fixed structurally in Phase 2). Phase 0 is about the *PBR effect itself* working on the cube. Leave models flat-white for now.

- [ ] **Step 4:** Commit Phase 0 fixes.
```bash
cd Mibo
git add src/Mibo.MonoGame/Graphics3D/Pipelines/ForwardPipeline.fs
git commit -m "fix(monogame3d): set sampler states for all PBR texture slots (0-5)

The PBR PS reads texture0..4 (albedo/roughness/normal/metallic/emission)
and the shadow atlas on slot 5, but Execute set only slot 0's sampler.
Missing slots defaulted to a sampler that sampled the albedo map as black."
```

- [ ] **Step 5:** Ask the user before pushing. Do not push unilaterally.

---

## Phase 1 — `Material3D.fromModelMeshPart` (the bridge)

> Pure logic — reads a `ModelMeshPart`'s baked native effect into a `Material3D`. No rendering changes yet; Phase 2 consumes it.

### Task 1.1: Add `fromModelMeshPart` + `fromEffect` to `Material3D`

**Files:** Modify `Mibo/src/Mibo.MonoGame/Graphics3D/Material3D.fs` (append to the existing `module Material3D`, after `withMetallicMap` at ~line 101).

Reference the canonical shape: `Mibo.Raylib/.../Material3D.fs:109` `fromRaylibMaterial` reads albedo color/map, normal/roughness/metallic/emission maps + scalars. For MonoGame we read from the part's `BasicEffect`/`SkinnedEffect`: DiffuseColor (→ AlbedoColor), Texture (→ AlbedoMap), Alpha (→ Opacity), TextureEnabled. Per spec §10: **albedo color + albedo map + opacity only for now** — default roughness/metallic to sensible non-metal values, no map extraction.

- [ ] **Step 1:** Add a private color-conversion helper + the two functions at the end of `module Material3D`:

```fsharp
  // ───────────────────────────────────────────────────────────────────
  // fromModelMeshPart / fromEffect — read a part's baked native effect
  // into a Material3D. This is the bridge that lets the PBR pipeline
  // preserve a model's authored look when it swaps out the native effect.
  // Mirrors the canonical Mibo.Raylib Material3D.fromRaylibMaterial shape,
  // reduced to what MonoGame's stock BasicEffect/SkinnedEffect expose:
  // DiffuseColor, Texture, Alpha. Map extraction (normal/roughness/metallic)
  // is deferred until the content-pipeline rework (spec §10).
  // ───────────────────────────────────────────────────────────────────

  let private xnaColorToMibo(c: Microsoft.Xna.Framework.Color) : Color = c

  let private vec3ToColor(v: Microsoft.Xna.Framework.Vector3) : Color =
    Color(
      byte(min 255.f (max 0.f (v.X * 255.f))),
      byte(min 255.f (max 0.f (v.Y * 255.f))),
      byte(min 255.f (max 0.f (v.Z * 255.f)))
    )

  /// <summary>
  /// Reads material params from a native <see cref="T:Microsoft.Xna.Framework.Graphics.Effect"/>
  /// that implements lighting (<c>BasicEffect</c>/<c>SkinnedEffect</c> via
  /// <see cref="T:Microsoft.Xna.Framework.Graphics.IEffectMatrices"/> + diffuse/texture/alpha).
  /// Returns <c>defaults</c> (opaque white, mid-roughness, non-metal) when the effect exposes
  /// no recognizable material fields. Per §10: albedo color + albedo map + opacity only.
  /// </summary>
  let fromEffect(effect: Effect) : Material3D =
    match box effect with
    | :? IEffectMatrices & ( _: BasicEffect | _: SkinnedEffect ) as _ ->
      // Downcast to read diffuse/texture/alpha. BasicEffect and SkinnedEffect
      // both expose DiffuseColor/Texture/Alpha; use a type-test ladder.
      match box effect with
      | :? BasicEffect as be ->
        let albedoMap =
          if be.TextureEnabled && not(isNull be.Texture)
          then ValueSome be.Texture
          else ValueNone

        { defaults with
            AlbedoColor = vec3ToColor be.DiffuseColor
            AlbedoMap = albedoMap
            Opacity = be.Alpha }
      | :? SkinnedEffect as se ->
        let albedoMap =
          if not(isNull se.Texture) then ValueSome se.Texture else ValueNone

        { defaults with
            AlbedoColor = vec3ToColor se.DiffuseColor
            AlbedoMap = albedoMap
            Opacity = se.Alpha }
      | _ -> defaults
    | _ -> defaults

  /// <summary>
  /// Reads material params from a <see cref="T:Microsoft.Xna.Framework.Graphics.ModelMeshPart"/>'s
  /// baked native effect (the content-pipeline material). Convenience over <c>fromEffect</c>.
  /// </summary>
  let fromModelMeshPart(part: ModelMeshPart) : Material3D =
    fromEffect part.Effect
```

- [ ] **Step 2:** `cd Mibo && dotnet build src/Mibo.MonoGame/Mibo.MonoGame.fsproj`
  Expected: build succeeds. **If the `:&` pattern guard doesn't compile** (F# intersection-and patterns on type tests can be finicky), simplify the outer match to a plain `match box effect with | :? BasicEffect ... | :? SkinnedEffect ... | _ -> defaults` and drop the `IEffectMatrices` guard (it was a belt-and-suspenders check; the inner ladder already distinguishes the two). Keep it compiling.

- [ ] **Step 3:** `dotnet fantomas .`

- [ ] **Step 4:** Commit.
```bash
git add src/Mibo.MonoGame/Graphics3D/Material3D.fs
git commit -m "feat(monogame3d): Material3D.fromModelMeshPart/fromEffect

Reads a ModelMeshPart's baked BasicEffect/SkinnedEffect
(DiffuseColor/Texture/Alpha) into a Material3D. The bridge that lets
the PBR pipeline preserve a model's authored look when it swaps out
the native effect. Mirrors the canonical Raylib fromRaylibMaterial
shape, reduced to albedo+opacity for now (spec §10)."
```

---

## Phase 2 — Route models through PBR (fixes flat-white models)

> Rewrites `handleDrawModel` and `handleDrawSkinnedMesh` so model parts bind the PBR effect via `Material3D.fromModelMeshPart`, instead of their native `BasicEffect`/`SkinnedEffect`. After this phase: textured models, lit by directional + point + spot, shadowed.

### Task 2.1: Add a shared "draw a part through PBR" helper

**Files:** Modify `ForwardPipeline.fs`. Add a private `member` near the existing `handleDrawMeshPBR` (~line 912).

This factors out the PBR bind+draw logic so both the primitive path and the model path share it. It's the body of `handleDrawMeshPBR`'s PBR branch (~920-948), generalized to take a `ModelMeshPart` (drawn via `drawPart`) OR a `PrimitiveMesh` (drawn via `mesh.Draw`).

- [ ] **Step 1:** Add a discriminated geometry input type + a shared helper member:

```fsharp
  /// Geometry to draw through PBR: either a native ModelMeshPart (drawn via drawPart)
  /// or an effectless PrimitiveMesh (drawn via mesh.Draw). Used by drawModel/drawPrimitive.
  [<Struct>]
  type private PbrGeometry =
    | Part of part: ModelMeshPart
    | Primitive of mesh: PrimitiveMesh

  /// Draws one piece of geometry through the PBR Standard technique. Shared by
  /// handleDrawModel (per-part) and handleDrawPrimitive (per-primitive). The caller
  /// must have set `matModel`/`viewProj`/`normalMatrix`/`cameraPos` and uploaded
  /// lights/shadow state; this sets material + textures + issues the draw.
  member private _.drawPbrGeometry
    (gd: GraphicsDevice, p: inref<PbrEffectParams>, geom: PbrGeometry, material: Material3D) =
    // Material uniform short-circuit (MaterialKey).
    let key = materialKey &material

    if not pbrHasLastMaterial || key <> pbrLastKey then
      uploadPbrMaterial(&p, &material)
      bindPbrTextures(gd, &material)
      pbrLastKey <- key
      pbrHasLastMaterial <- true

    match geom with
    | Part part -> drawPart(gd, part)
    | Primitive mesh -> mesh.Draw(gd, pbrEffect |> ValueOption.defaultValue null |> unbox)
```

> **Note:** `drawPart` applies `part.Effect.CurrentTechnique.Passes` — but we want it to apply the **PBR** effect's passes, not the part's own effect. So we must swap the part's effect to the PBR effect around the draw (same pattern the shadow pass uses at ForwardPipeline.fs:1788-1794). Adjust the `Part` branch:

```fsharp
    | Part part ->
      let pbrEff =
        match pbrEffect with
        | ValueSome e -> e
        | ValueNone -> () // unreachable: ensurePbrEffect was the gate
      let saved = part.Effect
      part.Effect <- pbrEff

      try
        drawPart(gd, part)
      finally
        part.Effect <- saved
```

(Fix the `ValueNone` branch — it's a logic gap. Since `drawPbrGeometry` is only called after `ensurePbrEffect` returned true, gate the whole helper on the effect being present, or pass the effect in explicitly. Simplest: make `drawPbrGeometry` take `effect: Effect` as an explicit arg instead of reading the field.)

- [ ] **Step 2:** Refine the signature so it takes the resolved PBR effect explicitly (avoids the ValueNone gap):

```fsharp
  member private _.drawPbrGeometry
    (gd: GraphicsDevice, effect: Effect, p: inref<PbrEffectParams>, geom: PbrGeometry, material: Material3D) =
    effect.CurrentTechnique <- effect.Techniques["Standard"]

    let key = materialKey &material

    if not pbrHasLastMaterial || key <> pbrLastKey then
      uploadPbrMaterial(&p, &material)
      bindPbrTextures(gd, &material)
      pbrLastKey <- key
      pbrHasLastMaterial <- true

    match geom with
    | Part part ->
      let saved = part.Effect
      part.Effect <- effect

      try
        drawPart(gd, part)
      finally
        part.Effect <- saved
    | Primitive mesh -> mesh.Draw(gd, effect)
```

- [ ] **Step 3:** `dotnet build src/Mibo.MonoGame/Mibo.MonoGame.fsproj` — expect build success (the helper is unused yet; that's fine).

### Task 2.2: Rewrite `handleDrawModel` to route through PBR

**Files:** Modify `ForwardPipeline.fs` `handleDrawModel` (~line 791-818). Replace the native-effect bind with a PBR bind per part.

- [ ] **Step 1:** Replace the body of `handleDrawModel`. Keep the bone-composition loop (CopyAbsoluteBoneTransformsTo) but bind PBR per part:

```fsharp
  member private _.handleDrawModel
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      model: Model,
      transform: Matrix
    ) =
    if this.ensurePbrEffect gd then
      match pbrEffect, pbrParams with
      | ValueSome e, ValueSome p ->
        let boneCount = model.Bones.Count

        if boneTransforms.Length < boneCount then
          boneTransforms <- Array.zeroCreate<Matrix> boneCount

        model.CopyAbsoluteBoneTransformsTo(boneTransforms)

        for mesh in model.Meshes do
          let world = boneTransforms[mesh.ParentBone.Index] * transform

          // normalMatrix = transpose(inverse(world)) (RH; §6.2)
          let mutable t = world
          let mutable inv = Matrix.Identity
          Matrix.Invert(&t, &inv) |> ignore
          let normalMatrix = Matrix.Transpose inv

          setMatrix p.MatModel world
          setMatrix p.ViewProj (state.View * state.Projection)
          setMatrix p.NormalMatrix normalMatrix
          setVec3 p.CameraPos state.CurrentCamera.Position
          uploadPbrLights(&p, lights, pointShadowSlots, spotShadowSlots)

          for part in mesh.MeshParts do
            let mat = Material3D.fromModelMeshPart part
            this.drawPbrGeometry(gd, e, &p, PbrGeometry.Part part, mat)
      | _ -> () // unreachable (ensurePbrEffect set both)
```

- [ ] **Step 2:** Build + run the spike.
  Run: `dotnet build src/Mibo.MonoGame/Mibo.MonoGame.fsproj && dotnet run --project ../../MonoThreeD/MonoThreeD.fsproj`
  Expected: the **static model** at offset (2,0,0) (drawn via `drawModel` in Program.fs:90) now shows **its texture + is lit** instead of flat-white. If it's textured+lit, Phase 2.2 works.

- [ ] **Step 3:** `dotnet fantomas .`

### Task 2.3: Rewrite `handleDrawSkinnedMesh` to route through PBR Skinned

**Files:** Modify `ForwardPipeline.fs` `handleDrawSkinnedMesh` (~line 839-867). The skinned model (drawn via `drawSkinnedMesh` in Program.fs:84-86) currently binds native SkinnedEffect → flat-white.

- [ ] **Step 1:** Replace the body to bind the PBR `Skinned` technique + upload bones:

```fsharp
  member private _.handleDrawSkinnedMesh
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      part: ModelMeshPart,
      transform: Matrix,
      bones: Matrix[]
    ) =
    match part.Effect with
    | :? SkinnedEffect ->
      // Skinned part: bind PBR Skinned technique, upload bone palette, draw.
      if this.ensurePbrEffect gd then
        match pbrEffect, pbrParams with
        | ValueSome e, ValueSome p ->
          e.CurrentTechnique <- e.Techniques["Skinned"]

          let mutable t = transform
          let mutable inv = Matrix.Identity
          Matrix.Invert(&t, &inv) |> ignore
          let normalMatrix = Matrix.Transpose inv

          setMatrix p.MatModel transform
          setMatrix p.ViewProj (state.View * state.Projection)
          setMatrix p.NormalMatrix normalMatrix
          setVec3 p.CameraPos state.CurrentCamera.Position

          // Bone palette (tail zero-filled to identity).
          let boneCount = min bones.Length bonePaletteScratch.Length

          for i = 0 to boneCount - 1 do
            bonePaletteScratch[i] <- bones[i]

          for i = boneCount to bonePaletteScratch.Length - 1 do
            bonePaletteScratch[i] <- Matrix.Identity

          setMatrixArray p.Bones bonePaletteScratch

          let mat = Material3D.fromModelMeshPart part
          // Material short-circuit applies even for skinned (one material per part).
          let key = materialKey &mat

          if not pbrHasLastMaterial || key <> pbrLastKey then
            uploadPbrMaterial(&p, &mat)
            bindPbrTextures(gd, &mat)
            pbrLastKey <- key
            pbrHasLastMaterial <- true

          uploadPbrLights(&p, lights, pointShadowSlots, spotShadowSlots)

          let saved = part.Effect
          part.Effect <- e

          try
            drawPart(gd, part)
          finally
            part.Effect <- saved
        | _ -> ()
      else
        () // PBR unavailable — no fallback here (native path removed by design).
    | _ ->
      // Non-skinned part: draw as a static PBR part, bones ignored.
      this.handleDrawModel(gd, &state, ???, transform) // see note
```

> **Note:** the `handleDrawModel` fallback for a non-skinned part is awkward because `handleDrawModel` takes a whole `Model`, not a single part. **Don't** call `handleDrawModel` here. Instead, inline a single-part PBR draw via `drawPbrGeometry(PbrGeometry.Part part)` after the same matrix/light setup. Replace the `| _ ->` branch with:

```fsharp
    | _ ->
      // Non-skinned part in a DrawSkinnedMesh command: draw it static through PBR.
      if this.ensurePbrEffect gd then
        match pbrEffect, pbrParams with
        | ValueSome e, ValueSome p ->
          e.CurrentTechnique <- e.Techniques["Standard"]

          let mutable t = transform
          let mutable inv = Matrix.Identity
          Matrix.Invert(&t, &inv) |> ignore

          setMatrix p.MatModel transform
          setMatrix p.ViewProj (state.View * state.Projection)
          setMatrix p.NormalMatrix(Matrix.Transpose inv)
          setVec3 p.CameraPos state.CurrentCamera.Position
          uploadPbrLights(&p, lights, pointShadowSlots, spotShadowSlots)

          let mat = Material3D.fromModelMeshPart part
          this.drawPbrGeometry(gd, e, &p, PbrGeometry.Part part, mat)
        | _ -> ()
```

- [ ] **Step 2:** Build + run the spike.
  Expected: the **skinned character model** at origin (drawn via `drawSkinnedMesh` in Program.fs:84-86) now shows **its texture + is lit + animates** (the walk loop plays) instead of flat-white. Point/spot/directional lights and shadows apply.

- [ ] **Step 3:** `dotnet fantomas .`

- [ ] **Step 4:** Commit Phase 2.
```bash
git add src/Mibo.MonoGame/Graphics3D/Pipelines/ForwardPipeline.fs
git commit -m "fix(monogame3d): route models through PBR (fixes flat-white models)

handleDrawModel/handleDrawSkinnedMesh now bind the custom PBR effect
via Material3D.fromModelMeshPart instead of the part's native
BasicEffect/SkinnedEffect. Imported models now get PBR + point/spot
lights + shadows (the §4.1 native/PBR split is gone from model dispatch).
Skinned parts use the PBR Skinned technique with bone-palette upload."
```

- [ ] **Step 5:** Ask the user before pushing.

---

## Phase 3 — `AnimatedModel` value + module (mirror `AnimatedSprite`)

> Pure logic. The 2D `AnimatedSprite` (`Mibo.MonoGame/Animation.fs:73`) is the template: a struct value holding shared data ref + live state, with a module of pure update functions. 3D already has `Animation3DState`/`AnimatedMesh`/`Animation3DClips`; we bundle them into one DSL-consumable value.

### Task 3.1: Add the `AnimatedModel` type + module

**Files:** Modify `Mibo/src/Mibo.MonoGame/Animation3D.fs`. Append the new type + module after the existing `AnimatedMesh` module (end of file, after ~line 907).

The shared data (`Model`, `AnimatedMesh`, `Animation3DClips`) + live state (`Animation3DState`) are bundled. Update functions delegate to `Animation3DState.*` (already implemented) and return a new `AnimatedModel`. **No bone computation here** — bones are computed at draw time (Phase 4 DSL wrapper), matching how 2D keeps `update` allocation-free.

- [ ] **Step 1:** Append to `Animation3D.fs`:

```fsharp
// ─────────────────────────────────────────────────────────────────────────────
// AnimatedModel — runtime state for a single animated 3D entity.
//
// Mirrors Mibo.MonoGame's 2D AnimatedSprite (Animation.fs:73): a struct value
// holding a reference to shared immutable data (the Model + skeleton + clip set)
// and the live playback state. Store one per entity in your Elmish model.
// Update functions are pure (return a new AnimatedModel); bone computation is
// deferred to draw time (Draw3D.drawAnimatedModel), so update stays allocation-free
// in the common case.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Runtime state for a single animated 3D entity. The 3D analog of the 2D
/// <c>AnimatedSprite</c>. Holds the model to draw, the shared skeleton data
/// (ValueNone if the model has no bones), and the live animation playback state.
/// </summary>
/// <remarks>
/// Store one per entity in your Elmish model. Use the <c>AnimatedModel</c> module
/// (<c>create</c>/<c>update</c>/<c>play</c>/...) to advance state, and
/// <c>Draw3D.drawAnimatedModel</c> to draw — the DSL computes the bone palette
/// from the state so you never handle a <c>Matrix[]</c> directly.
/// </remarks>
[<Struct>]
type AnimatedModel = {
  /// <summary>The MonoGame model to draw (meshes/textures from the content pipeline).</summary>
  Model: Microsoft.Xna.Framework.Graphics.Model

  /// <summary>
  /// Shared skeleton data (bone names, parents, inverse-bind). <c>ValueNone</c> if the
  /// model has no bones — <c>drawAnimatedModel</c> then falls back to a static draw.
  /// </summary>
  Mesh: AnimatedMesh voption

  /// <summary>Live playback state (current clip, frame, blend, speed, loop).</summary>
  State: Animation3DState
}

/// <summary>Pure update functions for <see cref="T:Mibo.Animation.AnimatedModel"/>.</summary>
/// <remarks>
/// Mirrors the 2D <c>AnimatedSprite</c> module. Each function returns a new
/// <c>AnimatedModel</c>. Playback delegates to <see cref="T:Mibo.Animation.Animation3DState"/>;
/// bone computation happens at draw time, not here.
/// </remarks>
module AnimatedModel =

  /// <summary>Create an animated model starting on the named clip.</summary>
  /// <param name="model">The MonoGame model (meshes/textures).</param>
  /// <param name="mesh">Shared skeleton data (ValueNone for a boneless model).</param>
  /// <param name="clips">Shared animation clip set (from <c>IAssets.ModelAnimations</c>).</param>
  /// <param name="clipName">The animation to start on (falls back to clip 0 if absent).</param>
  /// <param name="fps">Playback speed in frames per second.</param>
  let create
    (model: Microsoft.Xna.Framework.Graphics.Model)
    (mesh: AnimatedMesh voption)
    (clips: Animation3DClips)
    (clipName: string)
    (fps: float32)
    : AnimatedModel =
    {
      Model = model
      Mesh = mesh
      State = Animation3DState.create clips clipName fps
    }

  /// <summary>Advance playback by delta seconds. Pure; returns a new state.</summary>
  let update (deltaSeconds: float32) (am: AnimatedModel) : AnimatedModel =
    { am with State = Animation3DState.update deltaSeconds am.State }

  /// <summary>Play an animation by name. Resets the frame if switching clips.</summary>
  let play (clipName: string) (am: AnimatedModel) : AnimatedModel =
    { am with State = Animation3DState.play clipName am.State }

  /// <summary>Play by clip index (zero string allocation).</summary>
  let playByIndex (clipIndex: int) (am: AnimatedModel) : AnimatedModel =
    { am with State = Animation3DState.playByIndex clipIndex am.State }

  /// <summary>Play only if not already playing it.</summary>
  let playIfNot (clipName: string) (am: AnimatedModel) : AnimatedModel =
    { am with State = Animation3DState.playIfNot clipName am.State }

  /// <summary>Start blending toward a target animation.</summary>
  let blendTo (clipName: string) (duration: float32) (am: AnimatedModel) : AnimatedModel =
    { am with State = Animation3DState.blendTo clipName duration am.State }

  /// <summary>Is the current animation finished? (always false for looping).</summary>
  let inline isFinished(am: AnimatedModel) = Animation3DState.isFinished am.State

  /// <summary>Is currently playing the specified animation?</summary>
  let isPlaying (clipName: string) (am: AnimatedModel) =
    Animation3DState.isPlaying clipName am.State

  /// <summary>Force restart the current animation.</summary>
  let restart(am: AnimatedModel) : AnimatedModel =
    { am with State = Animation3DState.restart am.State }

  /// <summary>Total duration of the current clip in seconds at the current speed.</summary>
  let inline duration(am: AnimatedModel) = Animation3DState.duration am.State

  /// <summary>Name of the current clip.</summary>
  let currentClipName(am: AnimatedModel) : string =
    Animation3DState.currentClipName am.State

  /// <summary>Set the playback speed multiplier.</summary>
  let inline withSpeed (speed: float32) (am: AnimatedModel) : AnimatedModel =
    { am with State = Animation3DState.withSpeed speed am.State }

  /// <summary>Set whether the current clip loops.</summary>
  let inline withLoop (loop: bool) (am: AnimatedModel) : AnimatedModel =
    { am with State = Animation3DState.withLoop loop am.State }
```

- [ ] **Step 2:** `dotnet build src/Mibo.MonoGame/Mibo.MonoGame.fsproj`
  Expected: build succeeds.

- [ ] **Step 3:** `dotnet fantomas .`

- [ ] **Step 4:** Commit.
```bash
git add src/Mibo.MonoGame/Animation3D.fs
git commit -m "feat(monogame3d): AnimatedModel value + module (mirrors AnimatedSprite)

Bundles Model + AnimatedMesh + Animation3DState into one DSL-consumable
value, mirroring the 2D AnimatedSprite pattern. Pure update functions
delegate to Animation3DState; bone computation is deferred to draw time
so update stays allocation-free. Consumed by Draw3D.drawAnimatedModel
in Phase 4."
```

---

## Phase 4 — Unified command DU + DSL + spike migration

> The breaking change. Re-type the `Command3D` draw cases, collapse the `Draw3D` DSL to four entries (+ escape hatch), and migrate `MonoThreeD/Program.fs`.

### Task 4.1: Re-type the `Command3D` draw cases

**Files:** Modify `Mibo/src/Mibo.MonoGame/Graphics3D/Command3D.fs` (the DU at ~line 17-60 and the `Command3D` factory module ~70-167).

New draw cases per spec §6:
- `DrawModel(Model, Matrix)` — keep.
- `DrawAnimatedModel(Model, Matrix, Matrix[])` — **new**, replaces `DrawSkinnedMesh` (bones precomputed by the DSL).
- `DrawInstanced(PrimitiveMesh, Matrix[], Material3D, int)` — rename of `DrawMeshInstanced`.
- `DrawPrimitive(PrimitiveMesh, Matrix, Material3D)` — rename of `DrawMeshPBR`.
- `DrawMeshEffect(ModelMeshPart, Matrix, Effect)` — keep (escape hatch).
- **Remove:** `DrawMesh`, `DrawSkinnedMesh`, `DrawMeshPBR`, `DrawMeshInstanced`.
- Billboards/lines/camera/lights/shadow/immediate: unchanged.

- [ ] **Step 1:** Edit the DU. Replace the draw-case block (Command3D.fs:19-49) with:

```fsharp
[<RequireQualifiedAccess; Struct>]
type Command3D =
  | DrawModel of model: Model * transform: Matrix
  | DrawAnimatedModel of model: Model * transform: Matrix * bones: Matrix[]
  | DrawInstanced of
    mesh: PrimitiveMesh *
    transforms: Matrix[] *
    material: Material3D *
    instanceCount: int
  | DrawPrimitive of mesh: PrimitiveMesh * transform: Matrix * material: Material3D
  | DrawMeshEffect of meshPart: ModelMeshPart * transform: Matrix * effect: Effect
  | DrawBillboard of
    texture: Texture2D *
    position: Vector3 *
    size: Vector2 *
    color: Color
  | DrawLine3D of start: Vector3 * finish: Vector3 * color: Color
  | DrawBillboardBatch of
    textures: Texture2D[] *
    positions: Vector3[] *
    sizes: Vector2[] *
    colors: Color[] *
    count: int
  | BeginCamera of camera: Camera3D
  | BeginCameraConfig of config: Camera3DConfig
  | EndCamera
  | SetShadowOrigin of origin: Vector3
  | SetAmbientLight of aLight: AmbientLight3D
  | AddDirectionalLight of dLight: DirectionalLight3D
  | AddPointLight of pLight: PointLight3D
  | AddSpotLight of sLight: SpotLight3D
  | EnableShadows
  | DisableShadows
  | DrawImmediate of action: (unit -> unit)
```

- [ ] **Step 2:** Replace the factory module's draw helpers (Command3D.fs:72-130) with:

```fsharp
  let inline drawModel (model: Model) (transform: Matrix) =
    Command3D.DrawModel(model, transform)

  let inline drawAnimatedModel
    (model: Model)
    (transform: Matrix)
    (bones: Matrix[])
    =
    Command3D.DrawAnimatedModel(model, transform, bones)

  let inline drawMeshEffect
    (meshPart: ModelMeshPart)
    (transform: Matrix)
    (effect: Effect)
    =
    Command3D.DrawMeshEffect(meshPart, transform, effect)

  let inline drawBillboard
    (texture: Texture2D)
    (position: Vector3)
    (size: Vector2)
    (color: Color)
    =
    Command3D.DrawBillboard(texture, position, size, color)

  let inline drawLine3D (start: Vector3) (finish: Vector3) (color: Color) =
    Command3D.DrawLine3D(start, finish, color)

  /// <summary>Draws an effectless PrimitiveMesh with a PBR material (single).</summary>
  let inline drawPrimitive
    (mesh: PrimitiveMesh)
    (transform: Matrix)
    (material: Material3D)
    =
    Command3D.DrawPrimitive(mesh, transform, material)

  /// <summary>Draws an effectless PrimitiveMesh instanced (static bulk).</summary>
  let inline drawInstanced
    (mesh: PrimitiveMesh)
    (transforms: Matrix[])
    (material: Material3D)
    (instanceCount: int)
    =
    Command3D.DrawInstanced(mesh, transforms, material, instanceCount)

  let inline drawBillboardBatch
    (textures: Texture2D[])
    (positions: Vector3[])
    (sizes: Vector2[])
    (colors: Color[])
    (count: int)
    =
    Command3D.DrawBillboardBatch(textures, positions, sizes, colors, count)
```

(Remove `drawMesh`, `drawSkinnedMesh`, `drawMeshPBR`, `drawMeshInstanced`. Keep all camera/light/shadow/immediate factories unchanged.)

- [ ] **Step 3:** `dotnet build src/Mibo.MonoGame/Mibo.MonoGame.fsproj`
  Expected: **compile errors** in `ForwardPipeline.fs` (dispatch + shadow collection reference removed cases) and `Layout3D/Renderer3D.fs` (references `Command3D.drawMeshInstanced`). That's expected — fixed in Tasks 4.2-4.4. Do not commit yet.

### Task 4.2: Rewrite the forward-pass dispatch to the new cases

**Files:** Modify `ForwardPipeline.fs` `Execute` forward-pass match (~line 1995-2056).

- [ ] **Step 1:** Update the draw-command matches:

```fsharp
        | Command3D.DrawModel(model, transform) ->
          if state.HasCamera then
            this.handleDrawModel(gd, &state, model, transform)

        | Command3D.DrawAnimatedModel(model, transform, bones) ->
          if state.HasCamera then
            this.handleDrawAnimatedModel(gd, &state, model, transform, bones)

        | Command3D.DrawPrimitive(mesh, transform, material) ->
          if state.HasCamera then
            this.handleDrawPrimitive(gd, &state, mesh, transform, material)

        | Command3D.DrawInstanced(mesh, transforms, material, instanceCount) ->
          if state.HasCamera then
            this.handleDrawInstanced(gd, &state, mesh, transforms, material, instanceCount)

        | Command3D.DrawMeshEffect(part, transform, effect) ->
          if state.HasCamera then
            this.handleDrawMeshEffect(gd, &state, part, transform, effect)
```

(Remove the `DrawMesh`/`DrawSkinnedMesh`/`DrawMeshPBR`/`DrawMeshInstanced` matches.)

### Task 4.3: Rename handler methods to match new cases

**Files:** Modify `ForwardPipeline.fs`.

- [ ] **Step 1:** Rename `handleDrawMeshPBR` → `handleDrawPrimitive` (body unchanged — it's already the primitive PBR path; the BasicEffect fallback stays).
- [ ] **Step 2:** Rename `handleDrawMeshInstanced` → `handleDrawInstanced` (body unchanged).
- [ ] **Step 3:** Rename `handleDrawSkinnedMesh` → `handleDrawAnimatedModel` with the signature `(gd, &state, model: Model, transform, bones)` — but its body already draws a single `ModelMeshPart`, not a whole model. **Rename the param `part`→`model` is wrong.** Keep the existing single-part logic but the command now carries a `Model`. Resolution: iterate the model's meshes/parts like `handleDrawModel`, applying the Skinned technique to parts whose effect is `SkinnedEffect` and Standard to the rest. (See Task 4.3b.)

### Task 4.3b: `handleDrawAnimatedModel` — iterate model parts with Skinned technique

**Files:** Modify `ForwardPipeline.fs`.

- [ ] **Step 1:** Replace the renamed `handleDrawAnimatedModel` with a model-iterating version. It's `handleDrawModel`'s body, but parts with a `SkinnedEffect` use the `Skinned` technique + bone upload:

```fsharp
  member private _.handleDrawAnimatedModel
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      model: Model,
      transform: Matrix,
      bones: Matrix[]
    ) =
    if this.ensurePbrEffect gd then
      match pbrEffect, pbrParams with
      | ValueSome e, ValueSome p ->
        let boneCount = model.Bones.Count

        if boneTransforms.Length < boneCount then
          boneTransforms <- Array.zeroCreate<Matrix> boneCount

        model.CopyAbsoluteBoneTransformsTo(boneTransforms)

        // Bone palette (tail zero-filled to identity) — uploaded once per draw.
        let palCount = min bones.Length bonePaletteScratch.Length

        for i = 0 to palCount - 1 do
          bonePaletteScratch[i] <- bones[i]

        for i = palCount to bonePaletteScratch.Length - 1 do
          bonePaletteScratch[i] <- Matrix.Identity

        for mesh in model.Meshes do
          let world = boneTransforms[mesh.ParentBone.Index] * transform

          let mutable t = world
          let mutable inv = Matrix.Identity
          Matrix.Invert(&t, &inv) |> ignore

          setMatrix p.MatModel world
          setMatrix p.ViewProj (state.View * state.Projection)
          setMatrix p.NormalMatrix(Matrix.Transpose inv)
          setVec3 p.CameraPos state.CurrentCamera.Position
          uploadPbrLights(&p, lights, pointShadowSlots, spotShadowSlots)

          for part in mesh.MeshParts do
            match part.Effect with
            | :? SkinnedEffect ->
              e.CurrentTechnique <- e.Techniques["Skinned"]
              setMatrixArray p.Bones bonePaletteScratch
            | _ ->
              e.CurrentTechnique <- e.Techniques["Standard"]

            let mat = Material3D.fromModelMeshPart part

            let key = materialKey &mat

            if not pbrHasLastMaterial || key <> pbrLastKey then
              uploadPbrMaterial(&p, &mat)
              bindPbrTextures(gd, &mat)
              pbrLastKey <- key
              pbrHasLastMaterial <- true

            let saved = part.Effect
            part.Effect <- e

            try
              drawPart(gd, part)
            finally
              part.Effect <- saved
      | _ -> ()
```

### Task 4.4: Fix the shadow-pass caster collection + Layout3D renderer

**Files:** Modify `ForwardPipeline.fs` shadow-pass collection (~line 1658-1693) and `Layout3D/Renderer3D.fs:78`.

- [ ] **Step 1:** In `runShadowPass`, the caster-collection match references `DrawMeshPBR` (→ `DrawPrimitive`) and `DrawSkinnedMesh` (→ `DrawAnimatedModel`). Update both:

```fsharp
              | Command3D.DrawPrimitive(mesh, transform, _) when castEnabled ->
                // ... unchanged body ...
              | Command3D.DrawAnimatedModel(model, transform, bones) when castEnabled ->
                // was DrawSkinnedMesh(part,...); now a whole model.
                // Collect each skinned part of the model as a ShadowSkinnedDraw.
                for mesh in model.Meshes do
                  for part in mesh.MeshParts do
                    match part.Effect with
                    | :? SkinnedEffect ->
                      if skinnedCount >= shadowSkinnedDraws.Length then
                        Array.Resize(&shadowSkinnedDraws, shadowSkinnedDraws.Length * 2)
                      shadowSkinnedDraws[skinnedCount] <- {
                        Part = part
                        Transform = transform
                        Bones = bones
                      }
                      skinnedCount <- skinnedCount + 1
                    | _ -> ()
```

> **Note on shadow skinned draw:** `ShadowSkinnedDraw` currently holds a single `Part`; the per-part collection loop above preserves that. No struct change needed.

- [ ] **Step 2:** In `Layout3D/Renderer3D.fs:78`, change `Command3D.drawMeshInstanced` → `Command3D.drawInstanced`:

```fsharp
          buffer.Add(Command3D.drawInstanced mesh snapshot material count)
```

- [ ] **Step 3:** `cd Mibo && dotnet build src/Mibo.MonoGame/Mibo.MonoGame.fsproj`
  Expected: build succeeds (all compile errors resolved).

- [ ] **Step 4:** `dotnet fantomas .`

### Task 4.5: Rewrite the `Draw3D` DSL to the four entries

**Files:** Modify `Mibo/src/Mibo.MonoGame/Graphics3D/Draw3D.fs`.

- [ ] **Step 1:** Replace the geometry helpers (Draw3D.fs:34-130) with:

```fsharp
  /// <summary>Draws a static model. Auto-PBR + lights + shadows; the model's
  /// baked native effect is read via Material3D.fromModelMeshPart.</summary>
  let inline drawModel (model: Model) (transform: Matrix) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.drawModel model transform)
    buffer

  /// <summary>Draws an animated model. Mirrors 2D litAnimatedSprite: takes the
  /// runtime state value + transform; derives bones internally; auto-PBR + lights + shadows.</summary>
  let inline drawAnimatedModel
    (am: AnimatedModel)
    (transform: Matrix)
    (buffer: RenderBuffer3D)
    =
    let bones =
      match am.Mesh with
      | ValueSome mesh -> Animation3DState.computeBonePalette mesh am.State
      | ValueNone -> [||]

    buffer.Add(Command3D.drawAnimatedModel am.Model transform bones)
    buffer

  /// <summary>Draws an effectless PrimitiveMesh with a PBR material (single).</summary>
  let inline drawPrimitive
    (mesh: PrimitiveMesh)
    (transform: Matrix)
    (material: Material3D)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawPrimitive mesh transform material)
    buffer

  /// <summary>Draws static instanced bulk (terrain/props). Auto-PBR + lights + shadows.</summary>
  let inline drawInstanced
    (mesh: PrimitiveMesh)
    (transforms: Matrix[])
    (material: Material3D)
    (instanceCount: int)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawInstanced mesh transforms material instanceCount)
    buffer

  /// <summary>
  /// Draws a mesh part with a user-supplied Effect (escape hatch). The pipeline
  /// sets World/View/Projection; the caller owns lighting/material params.
  /// </summary>
  let inline drawMeshEffect
    (meshPart: ModelMeshPart)
    (transform: Matrix)
    (effect: Effect)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawMeshEffect meshPart transform effect)
    buffer
```

(Remove `drawMesh`, `drawSkinnedMesh`, `drawMeshPBR`, `drawMeshInstanced`. Keep `drawBillboard`/`drawBillboardBatch`/`drawLine3D`/camera/light/shadow/`drawImmediate`/`drop` unchanged.)

- [ ] **Step 2:** Add `open Mibo.Animation` at the top of `Draw3D.fs` (needed for `AnimatedModel`/`computeBonePalette`).

- [ ] **Step 3:** `dotnet build src/Mibo.MonoGame/Mibo.MonoGame.fsproj` — expect success.

### Task 4.6: Migrate the `MonoThreeD` spike

**Files:** Modify `MonoThreeD/Program.fs`.

- [ ] **Step 1:** Update `Model` type — replace `AnimatedMesh voption` + `Animation3DState` with a single `AnimatedModel`:

```fsharp
open Mibo.Animation

type Model = {
  PlayerModel: XnaModel
  AnimatedPlayer: AnimatedModel
  ColormapTexture: Texture2D
  Cube: PrimitiveMesh
}
```

- [ ] **Step 2:** In `init`, build the `AnimatedModel`:

```fsharp
    let animatedMesh = assets.AnimatedMesh rawModelPath
    let clips = assets.ModelAnimations rawModelPath
    let animatedPlayer =
      AnimatedModel.create playerModel animatedMesh clips "walk" 60.0f
```

- [ ] **Step 3:** In `view`, replace the per-part `drawSkinnedMesh` loop (Program.fs:84-86) + the static `drawModel` (line 90) with the unified calls. The animated character uses `drawAnimatedModel`; keep the static reference model as `drawModel`:

```fsharp
  // Bone palette computed inside the DSL wrapper (no Matrix[] in user code).
  buffer |> Draw3D.drawAnimatedModel model.AnimatedPlayer Matrix.Identity |> Draw3D.drop

  // Reference: the same model drawn static, offset to the side.
  buffer
  |> Draw3D.drawModel model.PlayerModel (Matrix.CreateTranslation(2.0f, 0.0f, 0.0f))
  |> Draw3D.drop

  // Reference: a PBR cube with the colormap texture.
  let cubeMat = Material3D.defaults |> Material3D.withAlbedoMap model.ColormapTexture

  buffer
  |> Draw3D.drawPrimitive model.Cube (Matrix.CreateTranslation(-2.0f, 0.0f, 0.0f)) cubeMat
  |> Draw3D.drop
```

- [ ] **Step 4:** In `update`, replace the `AnimState` update with `AnimatedModel.update`:

```fsharp
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    let m = { model with AnimatedPlayer = model.AnimatedPlayer |> AnimatedModel.update dt }
    struct (m, Cmd.none)
```

- [ ] **Step 5:** Build + run.
  Run: `dotnet build ../MonoThreeD/MonoThreeD.fsproj && dotnet run --project ../MonoThreeD/MonoThreeD.fsproj`
  Expected: cube textured+lit, animated character textured+lit+walking, static reference model textured+lit. All three respond to the directional + ambient light.

- [ ] **Step 6:** `cd .. && dotnet fantomas .` (samples repo)

- [ ] **Step 7:** Commit framework + sample.
```bash
# In Mibo/
cd Mibo
git add src/Mibo.MonoGame/Graphics3D/Command3D.fs src/Mibo.MonoGame/Graphics3D/Draw3D.fs src/Mibo.MonoGame/Graphics3D/Pipelines/ForwardPipeline.fs src/Mibo.MonoGame/Layout3D/Renderer3D.fs
git commit -m "feat(monogame3d)!: unify 3D draw DSL to PBR-everywhere

BREAKING: Command3D draw cases retyped to DrawModel/DrawAnimatedModel/
DrawInstanced/DrawPrimitive (+ DrawMeshEffect escape hatch). Removed
DrawMesh/DrawSkinnedMesh/DrawMeshPBR/DrawMeshInstanced. Draw3D DSL
collapses to drawModel/drawAnimatedModel/drawInstanced/drawPrimitive;
drawAnimatedModel mirrors 2D litAnimatedSprite (takes an AnimatedModel
value, computes bones internally). All model/primitive draws route
through the custom PBR effect."
# In samples repo
cd ..
git add MonoThreeD/Program.fs
git commit -m "feat(monogame3d): migrate MonoThreeD to unified 3D DSL

Uses drawAnimatedModel/drawModel/drawPrimitive. AnimatedModel value
replaces the bare Animation3DState + AnimatedMesh pair."
```

- [ ] **Step 8:** Ask the user before pushing either repo.

---

## Phase 5 — Changelog + dead-code cleanup

### Task 5.1: Update the changelog (carefully)

**Files:** Modify `Mibo/CHANGELOG.md` — the `## [Unreleased]` section.

- [ ] **Step 1:** **Read `Mibo/CHANGELOG.md` fully first.** Note the exact heading style and existing entries under `### Added`/`### Changed`/`### Removed`.

- [ ] **Step 2:** Under `### Changed`, add (matching the existing verbose, backtick-quoted style):

```markdown
- **Breaking:** `Mibo.MonoGame.Graphics3D.Command3D` draw cases retyped for the unified PBR-everywhere model: `DrawModel(Model, Matrix)`, `DrawAnimatedModel(Model, Matrix, Matrix[])`, `DrawInstanced(PrimitiveMesh, Matrix[], Material3D, int)`, `DrawPrimitive(PrimitiveMesh, Matrix, Material3D)`, plus the `DrawMeshEffect(ModelMeshPart, Matrix, Effect)` escape hatch. Removed `DrawMesh`/`DrawSkinnedMesh`/`DrawMeshPBR`/`DrawMeshInstanced`. `Draw3D` DSL collapses to `drawModel`/`drawAnimatedModel`/`drawInstanced`/`drawPrimitive`; `drawAnimatedModel` mirrors the 2D `litAnimatedSprite` pattern (takes an `AnimatedModel` value, computes bones internally). All model and primitive draws now route through the custom PBR effect, so imported models get PBR + point/spot lights + shadows automatically — the §4.1 native-effect/PBR split is gone from model dispatch.
- `Mibo.MonoGame.Graphics3D.ForwardPipeline`: `handleDrawModel`/`handleDrawAnimatedModel` bind the PBR `Standard`/`Skinned` techniques via the new `Material3D.fromModelMeshPart` (reads the part's baked `BasicEffect`/`SkinnedEffect` DiffuseColor/Texture/Alpha), instead of the part's native effect. Fixes flat-white imported models and lets point/spot lights reach them.
- `Mibo.MonoGame.Graphics3D.ForwardPipeline`: sampler states are now set for all PBR texture slots (0–5), not just slot 0. The PBR pixel shader reads `texture0..texture4` (albedo/roughness/normal/metallic/emission) plus the shadow atlas on slot 5; missing slots previously sampled the albedo map as black.
```

- [ ] **Step 3:** Under `### Added`, add:

```markdown
- `Mibo.MonoGame.Graphics3D.Material3D`: `fromModelMeshPart`/`fromEffect` — read a `ModelMeshPart`'s baked native effect (`BasicEffect`/`SkinnedEffect`: DiffuseColor/Texture/Alpha) into a `Material3D`. The bridge that lets the PBR pipeline preserve a model's authored look when it swaps out the native effect. Mirrors the canonical `Mibo.Raylib` `Material3D.fromRaylibMaterial` shape, reduced to albedo+opacity for now.
- `Mibo.Animation.AnimatedModel` (`Mibo.MonoGame`): runtime-state value for a single animated 3D entity, the 3D analog of the 2D `AnimatedSprite`. Bundles a `Model` + `AnimatedMesh voption` + `Animation3DState` and exposes pure update functions (`create`/`update`/`play`/`playByIndex`/`playIfNot`/`blendTo`/`isFinished`/`isPlaying`/`restart`/`duration`/`currentClipName`/`withSpeed`/`withLoop`). Bone computation is deferred to draw time (`Draw3D.drawAnimatedModel`), so `update` stays allocation-free.
```

- [ ] **Step 4:** Under `### Removed`, add:

```markdown
- `Mibo.MonoGame.Graphics3D.Command3D`: `DrawMesh`/`DrawSkinnedMesh`/`DrawMeshPBR`/`DrawMeshInstanced` cases and their `Draw3D` helpers (`drawMesh`/`drawSkinnedMesh`/`drawMeshPBR`/`drawMeshInstanced`), replaced by the unified `DrawModel`/`DrawAnimatedModel`/`DrawInstanced`/`DrawPrimitive` set. See `### Changed`.
```

- [ ] **Step 5:** `dotnet build` (sanity — CHANGELOG isn't compiled, but confirm nothing else moved).

- [ ] **Step 6:** Commit.
```bash
cd Mibo
git add CHANGELOG.md
git commit -m "docs(changelog): unified 3D draw / PBR-everywhere entries"
```

### Task 5.2: Final sweep — no `Option.get`/`ValueOption.get`, fantomas, build

- [ ] **Step 1:** Grep for forbidden `.get` calls in the files this plan touched:
  Run: search `Option.get|ValueOption.get` in `Mibo/src/Mibo.MonoGame/{Graphics3D,Animation3D.fs}` — expected none introduced by this work.
- [ ] **Step 2:** `cd Mibo && dotnet fantomas .` — clean.
- [ ] **Step 3:** `dotnet build src/Mibo.MonoGame/Mibo.MonoGame.fsproj` — succeeds.
- [ ] **Step 4:** Run the spike one more time — all visuals confirmed (cube textured+lit, animated character textured+lit+walking, static model textured+lit).
- [ ] **Step 5:** Ask the user about pushing / opening PRs. Do not push unilaterally.

---

## Self-Review Notes

**Spec coverage:**
- §3 (PBR everywhere + `fromModelMeshPart` bridge) → Tasks 1.1, 2.2, 2.3. ✓
- §4 DSL (`drawModel`/`drawAnimatedModel`/`drawInstanced`/`drawPrimitive`) → Task 4.5. ✓
- §5 `AnimatedModel` value + module → Task 3.1. ✓
- §6 command DU retyped → Task 4.1. ✓
- §7 pipeline changes (`handleDrawModel`/`handleDrawAnimatedModel` PBR) → Tasks 2.2, 2.3, 4.3b. ✓
- §8 PBR foundation acceptance bar → Phase 0. ✓
- §9 clean break + changelog guardrail → Task 5.1 + repo guardrails. ✓

**Known sharp edges (flagged, not bugs):**
- Task 2.1's first draft had a `ValueNone` gap; Task 2.1 Step 2 fixes it by passing the effect explicitly.
- Task 4.3 renamed a single-part handler to a whole-model handler; Task 4.3b rewrites it correctly.
- Shadow caster collection for `DrawAnimatedModel` iterates model parts (Task 4.4) — preserves the single-part `ShadowSkinnedDraw` struct.

**Type consistency:** `AnimatedModel` fields (`Model`/`Mesh`/`State`) match between Task 3.1 (definition), Task 4.5 (DSL reads `am.Mesh`/`am.State`/`am.Model`), and Task 4.6 (spike migration). `Command3D` case names match between Task 4.1 (DU), 4.2 (dispatch), 4.4 (shadow collection), and `Layout3D` rename. `drawPbrGeometry` signature matches between Task 2.1 (def) and 2.2/2.3 (callers).
