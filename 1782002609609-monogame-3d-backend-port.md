# Plan: MonoGame 3D Backend + `MonoTreeD` Sample

Port `Mibo.Raylib`'s 3D capabilities to `Mibo.MonoGame` (faithful, feature-for-feature), then port `ThreeDSample` to a new `MonoTreeD` sample (1:1 / near-1:1).

> **Scope note (read first):** This adds 3D support to the **MonoGame backend** (`Mibo.MonoGame`). The backend works with **native MonoGame types** (`Microsoft.Xna.Framework.Graphics.Model`, `Effect`, `Texture2D`, `VertexBuffer`, `RenderTarget2D`, `BoundingFrustum`, etc.) and loads assets via the **MonoGame content pipeline** (`ContentManager` / `OpenAssetImporter`).

---

## 1. Goal & Non-Goals

**Goal**

- `Mibo.MonoGame` gains a `Graphics3D/` module (plus `Layout3D/`, `Animation3D`, `Camera3D`, `Culling`) that mirrors `Mibo.Raylib` feature-for-feature.
- A new `MonoTreeD` sample in `Mibo.Samples` reproduces `ThreeDSample`'s gameplay and visuals on MonoGame.

**Non-Goals**

- Not rewriting `Mibo.Core`. Core's `CellGrid3D` (`Mibo.Layout3D`) is already backend-neutral and reused.
- Not supporting Vulkan/Metal/Web (OpenGL is the floor; DX is also produced by the shader build).
- Not porting raylib-internal quirks that have no MonoGame analog.

---

## 2. Repository / Branch Strategy

There are **two separate git repositories**. Understand which one you are in:

| Repo                      | Directory                         | Remote                                       | Default branch | Holds                                                                                       |
| ------------------------- | --------------------------------- | -------------------------------------------- | -------------- | ------------------------------------------------------------------------------------------- |
| Framework (**submodule**) | `Mibo/`                           | `https://github.com/AngelMunoz/Mibo.git`     | `main`         | `Mibo.Core`, `Mibo.Raylib`, `Mibo.MonoGame`                                                 |
| Samples (**main repo**)   | `.` (repo root `E:\Mibo.Samples`) | `git@github.com:AngelMunoz/Mibo.Samples.git` | `master`       | `ThreeDSample/`, `MonoPlatformer/`, the new `MonoTreeD/`, and the `Mibo/` submodule pointer |

`Mibo/` is a git **submodule** of the samples repo. The samples repo stores a _pinned commit pointer_ to the framework; it does not own the framework's history.

### 2.1 Verified facts the agent must respect

- Samples default branch is **`master`** (not `main`).
- Framework default branch is **`main`**.
- `.gitmodules` declares `branch = main`, but main is not the target branch for updates. **Do NOT run `git submodule update --remote`** (it would chase `main`). Manage the pointer manually as below.
- Precedent: 2D work used framework branch `feat/monogame2d` (merged to `main`) and samples branch `feat/monogame-platformer`. We mirror that for 3D.

### 2.2 Branch creation (do once, up front, in BOTH repos)

```bash
# ── Framework submodule (inside Mibo/) ──
cd Mibo
git checkout main && git pull origin main
git checkout -b feat/monogame3d
# (do NOT push until the user approves; see Imperatives)

# ── Samples main repo (repo root) ──
cd ..                 # back to E:\Mibo.Samples
git checkout master && git pull origin master
git checkout -b feat/monogame3d
```

Both `feat/monogame3d` branches are created **from the respective default branch** (`main` / `master`). Everything afterwards builds on these.

### 2.3 The two target branches (the only bases for any PR in this effort)

- **Framework `feat/monogame3d`** ← all backend (B\*) PRs and any backend bug-fix PRs land here.
- **Samples `feat/monogame3d`** ← all sample (S\*) PRs land here.

**No PR in this effort targets `main` or `master`.** `gh pr create --base feat/monogame3d ...` always.

### 2.4 The submodule pin-update loop (after every framework merge)

Because the samples repo pins a specific framework commit, **each time a framework PR (B\*) merges, update the pin in samples `feat/monogame3d`:**

```bash
# 1. Framework side (inside Mibo/) — fast-forward to the merged branch
cd Mibo
git checkout feat/monogame3d
git pull origin feat/monogame3d     # now at the latest merged B* commit

# 2. Samples side (repo root) — record the new submodule pointer
cd ..
git checkout feat/monogame3d
git add Mibo                         # stages the updated submodule commit pointer
git commit -m "chore: bump Mibo submodule to feat/monogame3d (<B-phase>)"
```

This "pin bump" commit is its own small commit on samples `feat/monogame3d` (or folded into the next sample PR that needs it). The samples repo MUST always point at a framework commit that is **on `feat/monogame3d`** (never a detached/topic-only commit).

### 2.5 Working on a backend phase (B\*)

- All edits happen **inside `Mibo/`** on a topic branch off `feat/monogame3d`:
  ```bash
  cd Mibo
  git checkout feat/monogame3d && git pull origin feat/monogame3d
  git checkout -b monogame3d/<phase>   # e.g. monogame3d/b1-types
  # ... edit, build, fantomas, commit ...
  gh pr create --base feat/monogame3d --title "..." --body-file <pr-body.md>
  ```
- After the B\* PR merges into framework `feat/monogame3d`: run §2.4 (pin bump) so the samples repo can see it.

### 2.6 Working on a sample phase (S\*)

- All edits happen **at the repo root** on a topic branch off samples `feat/monogame3d`, and the `Mibo/` submodule is checked out **on framework `feat/monogame3d`**:
  ```bash
  cd ..   # repo root
  git checkout feat/monogame3d && git pull origin feat/monogame3d
  cd Mibo && git checkout feat/monogame3d && git pull origin feat/monogame3d && cd ..
  git checkout -b monogame3d/<sample-phase>   # e.g. monogame3d/s2-static-view
  # ... edit (MonoTreeD/...), build, fantomas, commit ...
  # If the sample phase also needs the latest backend, ensure the pin bump (§2.4) is present first.
  gh pr create --base feat/monogame3d --title "..." --body-file <pr-body.md>
  ```

### 2.7 Backend bug-fixes found during sample work

If a sample phase uncovers a backend bug:

1. Fix it **inside `Mibo/`** on a new topic branch off framework `feat/monogame3d`; open a PR **base = framework `feat/monogame3d`**.
2. Do NOT block the sample PR on it if possible; once the fix merges, run §2.4 (pin bump), then the sample continues from the new pointer.

### 2.8 Guardrails

- Never push without explicit user approval (Imperative #1). Never force-push — tell the user instead (Imperative #2).
- Never leave the submodule on a detached HEAD for editing work; always `git checkout feat/monogame3d` first.
- Each phase = one small, reviewable PR that **compiles and (where rendering) produces visible output**.
- PR body = a markdown file (`gh ... --body-file`), per Imperative #4.

---

## 3. Imperatives (from `AGENTS.md` — follow on every PR)

1. NEVER push without permission. NEVER force push (tell the user to force-push instead).
2. Run `dotnet fantomas .` before staging. Format all F# files.
3. NEVER use `Option.get` / `ValueOption.get`. Pattern-match or use safe helpers (`Option.defaultValue`, `Array.choose`, etc.).
4. `gh` PRs use a **markdown file** as the PR body, not inline escaped markdown strings.
5. KeepAChangelog format; add entries under `## [Unreleased]` in `Mibo/CHANGELOG.md`.
6. Public API: well-documented XML comments, ergonomic, elmish-friendly, zero-cost (structs/ArrayPool/no hot-path allocations).
7. Do not answer review comments uner the user's name, tell the user what they need to know and do not use the gh command to reply.

---

## 4. Native-First Rule (apply on EVERY phase)

`Mibo.Raylib` is the **canonical feature reference**, NOT a type-for-type copy target. Before porting any raylib file, run this checklist:

| raylib feature                                     | MonoGame native equivalent                                                                                                   | Action                                                                                                                  |
| -------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| `Model` / `Mesh` / `Material`                      | `Microsoft.Xna.Framework.Graphics.Model` / `ModelMesh` / `ModelMeshPart` / `Effect`                                          | **Use native.** Do not invent parallel types.                                                                           |
| Lighting on models                                 | `BasicEffect`, `SkinnedEffect`, etc. (no native PBR)                                                                         | Use native effects as the **working floor**; author custom HLSL only where native lacks the feature (PBR, shadow maps). |
| Instancing (`in mat4 instanceTransform`)           | `GraphicsDevice.DrawInstancedPrimitives` + instance vertex streams (HiDef — `MiboGame` already sets `GraphicsProfile.HiDef`) | **Use native.** If the spike shows a parity gap, note it and DEFER to a late phase.                                     |
| Culling (`Frustum`/`BoundingSphere`/`BoundingBox`) | `Microsoft.Xna.Framework.BoundingFrustum` / `BoundingSphere` / `BoundingBox`                                                 | **Use native.** Port only the thin `Culling` helper facade, not the raylib `Frustum` class.                             |
| `Texture2D` / `RenderTexture2D`                    | `Texture2D` / `RenderTarget2D`                                                                                               | **Use native.**                                                                                                         |
| PBR shading                                        | none native                                                                                                                  | **Custom HLSL** (OpenGL floor + DX).                                                                                    |
| Shadow maps                                        | none native                                                                                                                  | **Custom HLSL.**                                                                                                        |
| Skeletal **skinning shader**                       | `SkinnedEffect` (4-bone skinning)                                                                                            | Investigate in the **animation spike**.                                                                                 |
| Skeletal **animation clips/keyframes**             | none native (raylib `LoadModelAnimations`)                                                                                   | **Animation spike** (may delay sample).                                                                                 |
| Runtime `.glb` loading (`LoadModel`)               | content pipeline `OpenAssetImporter` → XNB via `ContentManager`                                                              | **Use content pipeline.** `Assets.fs` already exposes `assets.Model`.                                                   |

> If you find yourself creating a type that duplicates a native one, STOP and use native. Add a Mibo-level struct **only** when it is pure ergonomics with no native equivalent (e.g. `Camera3D` config, light/material PBR param carriers, command DUs).

---

## 5. System.Numerics ↔ XNA Boundary (`Conversions`)

Core (`CellGrid3D`, etc.) uses `System.Numerics.*`; MonoGame uses `Microsoft.Xna.Framework.*`. Add an **internal** bridge module that wraps MonoGame's existing extension/implicit methods (do not hand-roll matrix math):

```fsharp
// Mibo.MonoGame/Graphics3D/Conversions.fs  (internal)
module internal Mibo.Elmish.Graphics3D.Conversions =
  let inline toNumericsMatrix (m: Microsoft.Xna.Framework.Matrix) : System.Numerics.Matrix4x4 = m.ToNumerics()
  let inline fromNumericsMatrix (m: System.Numerics.Matrix4x4) : Microsoft.Xna.Framework.Matrix = Microsoft.Xna.Framework.Matrix.op_Implicit(m)
  // Vector2/Vector3/Vector4/Quaternion/Color as needed, preferring MonoGame's built-in
  // conversion extension methods where they exist; add explicit ctor-based ones only if missing.
```

- Prefer `m.ToNumerics()` / `Matrix.op_Implicit(numerics)` (MonoGame ships these).
- Convert **at the Core↔backend boundary only** (e.g., reading `CellGrid3D.Origin` (Numerics) into XNA draw code). Keep internals in XNA types end-to-end.

---

## 6. Shader & Matrix Conventions (follow the repo's working precedent, not raylib)

Do NOT reason from first principles or copy raylib's GLSL/matrix quirks. The **authoritative precedent is the existing Mibo.MonoGame 2D shader `Shaders/LitSprite.fx`** and the note in `Graphics2D/Renderer2D.fs:1111-1135`. Mirror it exactly.

### 6.1 Matrix upload convention (from `LitSprite.fx` + `Renderer2D.fs:1111`)
- Declare matrices as **plain** `float4x4 MatrixTransform;` — NO `row_major`/`column_major` qualifier.
- Upload MonoGame `Matrix` **directly** via `effect.Parameters["Name"].SetValue(matrix)` — **no manual transpose, no special handling**; this works identically on the DX and OpenGL MGFX backends.
- In HLSL, write the **vector on the LEFT**: `output.Position = mul(input.Position, MatrixTransform);` (this is the row-vector convention matching MonoGame's row-major `Matrix` memory layout). **Do NOT write `mul(matrix, vector)`.**
- F# side composes with `*` in **application order** (`A * B` applies A first): the world→clip chain is `world * view * projection`, and the combined transform passed to the shader is `view * projection` (see `Renderer2D.fs`: `let matrixTransform = view * projection`). World→clip = `world` applied to the combined view-projection.
- The `BeginMode3D` / `rlMatrixToFloatV` / "capture VP inside BeginMode3D" rules in `Mibo/AGENTS.md` are **raylib-internal and DO NOT APPLY** to MonoGame. View-projection for MonoGame (including shadow maps) is just `Matrix.CreateLookAt(...) * Matrix.CreatePerspectiveFieldOfView(...)` (or `CreateOrthographic*`), composed in application order. No special capture.

### 6.2 MonoGame Matrix is right-handed (raylib is left-handed)
MonoGame docs: *"Represents the right-handed 4x4 floating point matrix."* raylib is left-handed. Consequence: **do not blindly copy raylib GLSL fragment math that embeds left-handed assumptions** — cross products, TBN tangent/bitangent derivation, light-direction signs, view/projection construction. MonoGame is internally self-consistent in RH (`CreateLookAt`, `CreatePerspective*` all RH), so build the pipeline in RH throughout and re-derive any copied raylib math (esp. normal-map TBN in the PBR shader, B9) in the right-handed convention. Flag any sign-flip in a code comment.

### 6.3 OpenGL is capped at shader model 3.0 (HARD constraint)
`LitSprite.fx` opens with this profile split — **copy it verbatim into every new 3D `.fx`**:
```hlsl
#if OPENGL
  #define VS_SHADERMODEL vs_3_0
  #define PS_SHADERMODEL ps_3_0
  #define MAX_OCCLUDERS 32   // smaller constant-register budget on OGL
#else
  #define VS_SHADERMODEL vs_5_0
  #define PS_SHADERMODEL ps_5_0
  #define MAX_OCCLUDERS 128
#endif
```
Implications (the agent MUST respect these when porting `Mibo.Raylib/.../Shaders.fs` GLSL `#version 330` → HLSL):
- **OpenGL MGFX = SM3.0 only.** No `dFdx`/`dFdy` (raylib's PBR shadow uses these for slope-scale bias — see B10), limited/no dynamic array indexing in fragment shaders, tight constant-register limits. **Render-target flips and half-texel offsets also differ between DX/OGL.**
- This is why the 2D shader uses `[loop]` + `if (i >= Count) break;` instead of raylib's `for (i < count)` over fixed-size arrays — mirror this pattern. Light/occluder/shadow arrays may need **separate, smaller MAX_* constants on OGL vs DX**.
- **Any raylib PBR/shadow technique that relies on SM4+ features** (`dFdx`/`dFdy`, textureSize-in-shader, dynamic indexing, large uniform arrays) **cannot be ported as-is to the OpenGL build.** Find an SM3.0-compatible alternative (e.g., manual bias instead of derivative-based slope bias; fixed texel size from a uniform instead of `textureSize`). If a feature is genuinely impossible on SM3.0, **flag it** (it may have to be DX-only or deferred) — do not silently drop it.
- Validate each shader on **both** `/Profile:OpenGL` and `/Profile:DirectX_11` builds (the `script.fsx` already compiles both). Visual smoke-test on the OpenGL (WindowsGL) path is the floor.

### 6.4 Build the shaders with the existing toolchain (precedent: `Shaders/script.fsx`)
Add each new `.fx` to `ShaderList` in `Mibo.MonoGame/Shaders/script.fsx`; it compiles `.dx.mgfx` + `.ogl.mgfx` via `mgfxc` and you embed both as `<EmbeddedResource>`. `ShaderLoader`/`ShaderLoader3D` loads the right one per backend (`PlatformInfo.GraphicsBackend`).

---

## 7. Backend Phases → `feat/monogame3d` (Mibo framework repo)

Target dir: `Mibo/src/Mibo.MonoGame/Graphics3D/` (+ `Layout3D/`, and root-level `Camera3D.fs`, `Culling.fs`, `Animation3D.fs`). Update `Mibo.MonoGame.fsproj` `<Compile>` order incrementally. Each phase compiles + (from B2) renders.

### B1 — Scaffolding + core 3D data types + Conversions

- Create `Graphics3D/` directory.
- **`Graphics3D/Conversions.fs`** (internal) — see §5.
- **`Graphics3D/Light3D.fs`** — port canonical `Mibo.Raylib/Graphics3D/Light3D.fs`. Struct light types use `Microsoft.Xna.Framework.Color`/`Vector3` and `float32`. Keep the `AmbientLight3D`/`DirectionalLight3D`/`PointLight3D`/`SpotLight3D` records + builder modules verbatim (pure data, no native conflict).
- **`Graphics3D/Material3D.fs`** — _reduced role_: native `Effect` is the real material. Provide a **minimal PBR-param struct** used **only by the custom-PBR path** (B9); include `defaults`/`colored`/`unlit`/`withAlbedoMap`/`withNormalMap`. Provide `fromEffect`/`fromModelMeshPart` helpers that read native `BasicEffect` (DiffuseColor, Texture, etc.) into the struct for the native path. **Native-first:** for normal rendering, bind the model's own `Effect`; the PBR struct is a fallback/upgrade carrier.
- Add fsproj `<Compile>` entries (after `Assets.fs`/`Runtime.fs`).
- **Validation:** `dotnet build Mibo.MonoGame`; `dotnet fantomas .`; no new raylib reference.

### B2 — Command buffer + Draw3D DSL + pipeline interface + renderer shell (no-op pipeline)

Port canonical: `RenderBuffer3D.fs` (ArrayPool), `Command3D.fs` (closed DU + factory), `Draw3D.fs` (pipe DSL), `RenderPipeline3D.fs` (`IRenderPipeline3D`), `Renderer3D.fs` (`Renderer3D<'Model>` + `Renderer3DConfig`).

- **`Graphics3D/Command3D.fs`** — port the `Command3D` DU. Fields use **native types**: `ModelMesh`/`ModelMeshPart` (not raylib `Mesh`/`Model`), `Effect` or `Material3D`, `Texture2D`, `Matrix`, `Vector3`/`Vector2`, `Color`. Keep all command variants for parity: DrawMesh/DrawModel/DrawBillboard/DrawLine3D/DrawSkinnedMesh/DrawMeshInstanced/DrawBillboardBatch/BeginCamera/BeginCameraConfig/EndCamera/SetShadowOrigin/SetAmbientLight/AddDirectionalLight/AddPointLight/AddSpotLight/EnableShadows/DisableShadows/DrawImmediate. (Skinned/Instanced/Billboard dispatch wired in their phases; DU present now.)
- **`Graphics3D/RenderBuffer3D.fs`** — copy `RenderBuffer3D` (ArrayPool<Command3D>) nearly verbatim.
- **`Graphics3D/Draw3D.fs`** — copy the DSL wrappers, retyping to native.
- **`Graphics3D/RenderPipeline3D.fs`** — copy `IRenderPipeline3D` interface (`Initialize`/`Execute`/`Shutdown`).
- **`Graphics3D/RenderTargetPool3D.fs`** — port `RenderTargetPool3D` but use native `RenderTarget2D` keyed by (w,h); dispose via `Dispose()`.
- **`Graphics3D/Renderer3D.fs`** — port `Renderer3D<'Model>` + `Renderer3DConfig` (`ClearColor`, no post-process initially). Acquire `GraphicsDevice` via `MonoGameGameContext.getGraphicsDevice`. Ship a **`NoopPipeline`** (implements `IRenderPipeline3D`, does nothing) so a renderer can be plugged into `MiboGame` and just clears the screen.
- **Validation:** build; a tiny scratch test program (or sample S0 step) that adds a `Renderer3D` renderer with the no-op pipeline and shows a cleared window.

### B3 — `Camera3D`

Port canonical `Mibo.Raylib/Camera.fs` (the 3D portions: `Camera`, `Ray`, `Camera3DConfig`, `module Camera3D`).

- Use XNA `Matrix` (`CreateLookAt`, `CreatePerspectiveFieldOfView`, `CreateOrthographic`). `lookAt`/`orbit`/`screenPointToRay` (`Ray` struct) + `render`/`withViewport`/`withClear`/`withPostProcess`/`withoutPostProcess`/split-screen/overlay builders.
- Keep `Camera3DConfig.PostProcessPasses: int[] voption` for parity (consumed in polish phase).
- **Validation:** build; unit-ish sanity (lookAt produces invertible VP).

### B4 — Culling (native-first)

- **Use native** `Microsoft.Xna.Framework.BoundingFrustum` (constructed from `View * Projection`), `BoundingSphere`, `BoundingBox`.
- **`Culling.fs`** — thin facade `module Culling` with `isVisible`/`isGenericVisible` delegating to native `ContainmentType` checks (`!= Disjoint`). Port `Culling.isVisible2D` only if 3D code needs it (the sample uses 2D rects for the minimap — that stays in the 2D renderer). Do **not** port the raylib `Frustum` class.
- **Validation:** build; frustum culls a sphere behind the camera.

### B5 — Forward pipeline: dispatch + structure, native-effect working lighting

Port canonical `Mibo.Raylib/Graphics3D/Pipelines/ForwardPbrPipeline.fs` _dispatch/structure_, **but bind native MonoGame effects first** (not PBR yet).

- **`Graphics3D/Pipelines/ForwardPipeline.fs`** implementing `IRenderPipeline3D`:
  - Pre-scan buffer for active camera(s), ambient, directional, point, spot lights, shadow origin.
  - Execute loop pattern-matches `Command3D` (mirrors raylib Step 1–5 structure: prescan → shadow pass (stubbed here) → forward pass → post-process).
  - **Drawing**: DrawMesh/DrawModel bind each `ModelMeshPart.Effect` (native `BasicEffect` etc.): set `World`/`View`/`Projection`, lights, fog off; `mesh.Draw()` or manual `DrawIndexedPrimitives`. DrawLine3D + DrawBillboard stubbed to a simple fallback (full impl in B7/B8). DrawImmediate inline.
  - **Lighting**: ambient + 1 directional + N point + M spot translated to `BasicEffect`'s `AmbientLightColor`/`DirectionalLight0..2`/`EnableDefaultLighting` where applicable; document the limitations (Blinn-Phong, no PBR, limited light count) — these are the _native floor_, upgraded in B9.
- **`Graphics3D/Pipelines/PostProcess3D.fs`** — port canonical (post-process chain scaffold); may be empty pass-through now.
- **Validation:** render a lit `Model` + primitives lit by a directional light (BasicEffect path). Visible on screen.

### B6 — Primitives

Port canonical `Mibo.Raylib/Graphics3D/Primitive3D.fs`.

- **`Graphics3D/Primitive3D.fs`** — generate native meshes once at init: `cube`/`sphere`/`cylinder`/`plane`/`torus`/`cone` as small wrappers holding a `VertexBuffer`+`IndexBuffer` (or a tiny `PrimitiveMesh` type with a `Draw(gd, effect)` helper). There is no native generator, so write minimal vertex builders (this is the MonoGame analog of `GenMeshCube`, not redundant type bloat).
- **Validation:** draw a lit cube/sphere via the pipeline.

### B7 — Native instancing + `Layout3D` cell-grid renderer

- **Native-first:** implement hardware instancing via `GraphicsDevice.DrawInstancedPrimitives` with an instance vertex stream (per-instance world matrix). Provide a `drawMeshInstanced(mesh, transforms[], effect, count)` handler in the pipeline (mirrors `Command3D.DrawMeshInstanced`).
- **Spike check (in-phase):** confirm `DrawInstancedPrimitives` on HiDef covers the sample's needs (thousands of block instances). If parity is impossible on native, **note it and DEFER instancing to a late phase** (after B13) — do not block.
- **`Layout3D/Renderer3D.fs`** — port canonical `Mibo.Raylib/Layout3D/Renderer3D.fs` (`CellGridRenderer3D` + `InstancedRenderContext<_,_>`) reusing Core's `CellGrid3D` (`Mibo.Layout3D`). Convert Numerics↔XNA at the boundary via `Conversions`.
- **Validation:** render a `CellGrid3D` of blocks instanced.

### B8 — Billboards + lines (full)

- **`drawBillboard`/`drawBillboardBatch`** — camera-facing quads (the sample's particles). Use a small vertex batch with the active camera basis; native `DrawUserPrimitives`/`DrawPrimitives`. `drawLine3D` — 1px line via a 2-vertex primitive or `PrimitiveBatch`-style. Wire into pipeline dispatch.
- **Validation:** draw N billboards + a line.

### B9 — Custom PBR HLSL pipeline (no native PBR exists)

MonoGame ships **no PBR effect**, so faithful parity needs custom HLSL. Translate canonical `Mibo.Raylib/Graphics3D/Pipelines/Shaders.fs` GLSL → HLSL `.fx`.

- **`Shaders/ForwardPbr.fx`** (+ normal-map variant) + **`Shaders/DepthShadow.fx`**: translate the Cook-Torrance PBR fragment (D_GGX, Smith geometry, Schlick Fresnel), ambient/directional/point/spot, emission, opacity, tiling, normal-map TBN. Vertex shaders: standard + instanced variant (from B7) + skinned variant (forward-declared; wired in B12).
- **Read §6 first and obey it.** Specifically: (a) matrix upload via plain `float4x4` + `SetValue` directly + `mul(position, matrix)` (vector LEFT); (b) re-derive normal-map TBN / cross products in **right-handed** convention, don't copy raylib's left-handed GLSL math; (c) the **OpenGL SM3.0 cap (§6.3)** — raylib's `Shaders.fs` is GLSL `#version 330` and uses SM4+ features that have **no SM3.0 equivalent** (notably `dFdx`/`dFdy` for slope-scale shadow bias, and `textureSize`). For each such feature, provide an SM3.0-compatible fallback (e.g., uniform-passed texel size + manual bias instead of derivative-based) or, if truly impossible on OGL, flag it for the DX-only/defer decision. Copy the `#if OPENGL` profile split from `LitSprite.fx` into every new `.fx`.
- Build via the **existing pattern**: extend `Mibo.MonoGame/Shaders/script.fsx` (`ShaderList`) to compile each `.fx` with `/Profile:OpenGL` **and** `/Profile:DirectX_11` → embed `.dx.mgfx`/`.ogl.mgfx` as `<EmbeddedResource>` in the fsproj.
- **`Graphics3D/ShaderLoader3D.fs`** — reuse the `ShaderLoader` pattern (`ShaderLoader.fs`) to load embedded `.mgfx` by backend.
- Upgrade `ForwardPipeline` to bind the PBR `Effect` when a `Material3D`/PBR path is requested (material cache keyed by map IDs + scalars, mirroring raylib `MaterialKey`). Keep native `Effect` path available.
- **`PostProcess.fs`**: extend the post-process pass selection used by `Camera3DConfig.PostProcessPasses`.
- **Validation:** same scene as B5 now lit with PBR (compare look to raylib `ThreeDSample`).

### B10 — Directional shadow atlas

Port canonical `Mibo.Raylib/Graphics3D/Pipelines/ShadowAtlas.fs` + the shadow pass in `ForwardPbrPipeline`.

- Shadow pass renders casters (`CastsShadows`) to a depth `RenderTarget2D` (atlas region for the directional light) using `DepthShadow.fx`. Forward pass samples it (PCF 3×3, matching raylib) in `ForwardPbr.fx`.
- Implement `SetShadowOrigin`, `EnableShadows`/`DisableShadows`, per-light bias (use `DirectionalLight3D`/global bias config like the sample's `shadowBiasConfig`).
- **Validation:** directional light casts a shadow; toggle works.

### B11 — Point + spot shadows

Extend the atlas + pipeline so point/spot lights that set `CastsShadows` render and sample correctly (mirrors raylib `computePointShadow`/`computeSpotShadow`). Wire `PointLight3D.ShadowBias`/`SpotLight3D.ShadowBias`.

- **Validation:** a point/spot light casts a shadow.

### B12 — Animation module (SPIKE-GATED, LAST backend phase)

> **⚠️ SPIKE REQUIRED BEFORE IMPLEMENTATION.** raylib gives runtime skeletal animation for free (`LoadModelAnimations`, `ModelAnimation`, `UpdateModelAnimation[Ex]`, `Model.Skeleton`). MonoGame's native `Model` does **not** carry animation clips/keyframes out of the box. The implementing agent MUST first spike and report:
>
> 1. Does the **content pipeline** (`OpenAssetImporter`/Assimp) expose animation **clips + keyframes + bone hierarchy + inverse-bind matrices** for the sample's models? (Inspect a processed model's `Tag`/`SkinningData`; check MGCB output.)
> 2. Can native **`SkinnedEffect`** (4-bone skinning) drive the sample's meshes, or do they need >4 influences / a custom skinning HLSL?
> 3. Is runtime clip extraction feasible, or is a **custom content processor/reader** required?
>
> **A concrete solution must come from this spike.** It may **delay the sample's animated-character feature (S4)**. Do NOT port `Animation3D` until the spike resolves the data source.

Once resolved, port canonical `Mibo.Raylib/Animation3D.fs`:

- `Animation3DClips` / `Animation3DState` (struct, with `play`/`playByIndex`/`blendTo`/`update`/`applyToModel`/`isFinished`/`isPlaying`/`duration`).
- `AnimatedMesh` + `computeBoneMatrices` (GPU skinning path) and a skinned HLSL variant in `ForwardPbr.fx`/`DepthShadow.fx` (forward-declared in B9).
- Adapt `applyToModel` to MonoGame's native skinning model (matrix palette upload) per spike result. Add `assets.ModelAnimations` to `Assets.fs`/`IAssets` **only** if the spike produces a real loader (else leave absent and document).
- **Validation:** an animated model plays + blends clips.

### B13 — Polish

- Post-process pass selection end-to-end; optional debug overlay (raylib pipeline has one); fill XML docs on all public API; verify no `Option.get`/`ValueOption.get`; `dotnet fantomas .`; update `Mibo/CHANGELOG.md` (`### Added` MonoGame 3D backend) + README. Backend is feature-complete vs raylib.

---

## 8. Sample Phases → `Mibo.Samples` (submodule at `feat/monogame3d`)

New project `MonoTreeD/` mirroring `ThreeDSample/` file layout. MonoGame app (WindowsDX, `MiboGame`), content pipeline (`Content/Content.mgcb`) processing the Kenney **`.glb` assets via `OpenAssetImporter`** (the GLB mention is sample-asset-only).

### S0 — Scaffold + content pipeline + trivial renderer

- `MonoTreeD.fsproj` (WindowsDX net10), copy of `ThreeDSample/Program.fs` skeleton using `MiboGame` + `Program` builders; window + a `Renderer3D` with `NoopPipeline` (then swap to `ForwardPipeline` in S2).
- MGCB: reference Kenney glb models via `OpenAssetImporter`; verify a model compiles to XNB and `assets.Model` loads it.
- **Validation:** window opens; a model loads.

### S1 — Game logic (no rendering)

Port `Types.fs`, `Constants.fs`, `DayNight.fs`, `WorldGen.fs`, `Physics.fs`, `Systems.fs` from `ThreeDSample/`. Convert raylib types → XNA (`Color`, `Vector3`/`Vector2`) and keep Core `CellGrid3D` (`Mibo.Layout3D`) worldgen. Reuse `MonoPlatformer`'s `System` pipeline + `MiboGame` patterns. **Skip** animation/player-model init in `Program.fs` for now (stub a static reference).

- **Validation:** builds; runs (blank gameplay).

### S2 — Static 3D view

Port `View.fs` (3D portion) using the MonoGame `Draw3D` DSL: `Camera3D`, ambient + directional light (day/night), directional shadows, **instanced chunks** (`CellGridRenderer3D`), static player model (`drawModel`), 2D overlay (minimap/diagnostics reuse the MonoGame 2D renderer). Particle billboards stubbed.

- **Validation:** 3D world renders with shadows, day/night sky; minimap/HUD overlay.

### S3 — Particles + mushroom point lights

Port particle system (`drawBillboardBatch`) + glowing-mushroom point lights (`addPointLight`). Jump/jump-confetti particles. (No character animation yet.)

- **Validation:** matches `ThreeDSample` minus animated character.

### S4 — Animation + final parity (GATED on B12 spike outcome)

Once B12 resolves: port the player `Animation3DState` (idle/walk/jump + blend), `applyToModel`, GPU skinning. Wire character model + clips via the spike-approved loader. **This phase may be delayed** depending on B12. If B12 proves clips can't come through cleanly, document the gap and ship S0–S3 as the deliverable.

- **Validation:** character animates; full visual parity with `ThreeDSample`.

### S5 — Polish & parity verification

- Side-by-side compare `MonoTreeD` vs `ThreeDSample` (lighting, shadows, animation, minimap, day/night). Fix deltas. `fantomas`; sample README.

---

## 9. Risks & Open Sub-Decisions (agent must resolve/check)

1. **Animation data source (B12/S4)** — highest risk. raylib's free skeletal animation has no native MonoGame equivalent. **Spike first**; outcome may change API surface (`assets.ModelAnimations`) and delay S4.
2. **MonoGame has no native PBR** — confirmed (stock effects only). Custom HLSL (B9) is mandatory for parity; not optional.
3. **Native instancing parity (B7)** — likely fine via `DrawInstancedPrimitives` (HiDef), but verify scale (thousands of instances) in-phase; defer if it can't deliver.
4. **OpenGL SM3.0 shader cap (§6.3) — concrete blocker for faithful PBR/shadow parity.** raylib's `Shaders.fs` is GLSL `#version 330` and uses `dFdx`/`dFdy` (slope-scale shadow bias) and `textureSize` (PCF texel step), neither of which exists in MonoGame's OpenGL SM3.0 profile. Each must get an SM3.0 fallback (uniform texel size + manual bias) or become DX-only. Validate every shader on both profiles; this is the single most likely source of "feature can't reach parity on the OpenGL floor."
5. **Matrix convention / RT flip / half-texel / handedness** — follow §6 (repo precedent: `mul(position, matrix)`, plain `float4x4`, no transpose; re-derive math RH not LH). Validate per-shader, especially shadow + post-process under OpenGL. Document decisions.
6. **`Material3D` scope** — keep minimal (PBR-param carrier + `fromEffect`); prefer binding native `Effect`. Confirm importer exposes PBR maps; if not, author/fallback materials.
7. **System.Numerics↔XNA** — only at the Core boundary via `Conversions` (§5); keep internals XNA.
8. **Content pipeline for glTF** — `OpenAssetImporter` (Assimp) at 3.8.4.1 supports it; verify in S0 that the specific Kenney models round-trip (materials, transforms).

## 10. Execution Notes for the Implementing Agent

- Work phase-by-phase; do NOT skip ahead. Each backend phase merges to `feat/monogame3d` before the next starts.
- After each phase, run: `dotnet build`, `dotnet fantomas .`, and (rendering phases) a visual smoke test. Report back for review.
- Re-check §4 (native-first) and §3 (imperatives) before opening any PR.
- Canonical references live in `Mibo/src/Mibo.Raylib/` (`Graphics3D/`, `Layout3D/`, `Animation3D.fs`, `Camera.fs`, `Culling.fs`). Mirror structure under `Mibo/src/Mibo.MonoGame/`.
- 2D backend patterns to copy for shader/build/loader: `Mibo/src/Mibo.MonoGame/Graphics2D/{ShaderLoader,Renderer2D,RenderBuffer2D}.fs` and `Mibo/src/Mibo.MonoGame/Shaders/script.fsx`.
