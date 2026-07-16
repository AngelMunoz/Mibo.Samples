# BoneProbe: model dimensions + animatable batch report

- Status: Approved
- Plan file: `.alta/plans/2026-07-16-boneprobe-model-dimensions.md`
- Created: 2026-07-16
- Task: Add a game-agnostic batch mode to BoneProbe that scans a folder of `.glb` files and reports each model's raw dimensions (vertex extents) and whether it is animatable.
- Git: `.alta/plans/` is not ignored (git check-ignore returned nothing for it). Commit this plan with the related implementation work.

## Objective
- Give BoneProbe the missing capability it needs to support asset evaluation: **model dimensions** and **animatable** flag, over a whole folder, in one run.
- Report raw model-unit extents only. **No** AABB framing, **no** node-transform traversal, **no** cell-footprint derivation, **no** Platformer3D coupling. The user derives cells by dividing the reported size by the known cell unit (e.g. a "large" block reporting 256 vs a 1x1 block reporting 64 ⇒ 4 cells).
- Console output only. No committed report files, no generated F# tables.

## Non-goals (explicit)
- No WorldGen rework here — that is a separate later step.
- No AABB/`BoundingBox` work, no `Mibo.Layout3D`/`Spatial3D` integration.
- No Platformer3D `BlockType` awareness, no cell-size math inside the tool.
- No node-transform accumulation. Raw mesh-vertex extents only (confirmed from code — see Context).

## Context and evidence
- `BoneProbe/Program.fs` dispatches two modes (`raw`, `palette`); both are single-file and print to console.
- `BoneProbe/Scene.fs:5-13` defines `postProcessFlags` and `tryLoad`. Its flag set is:
  `FindDegenerates | FindInvalidData | FlipUVs | FlipWindingOrder | JoinIdenticalVertices | ImproveCacheLocality | OptimizeMeshes | Triangulate`.
- **`Mesh.Vertices` are mesh-local — confirmed from code, not assumed.**
  `Mibo.MonoGame/Assets.fs:142-155` (`loadScene`) imports scenes with the **identical** flag set as `BoneProbe/Scene.fs`. Neither includes `PreTransformVertices`, which is the flag Assimp uses to bake node transforms into vertices. So the vertices BoneProbe reads are the same mesh-local vertices Mibo renders.
- **Animation pipeline confirms the animatable signal.** `Mibo.MonoGame/Animation3D.fs:294` `Animation3DClips.fromScene scene` builds clips from the scene; `AnimatedMesh.fromScene` (line 714) walks the node tree for bone bind poses but never repositions mesh vertices. So "animatable" = `scene.HasAnimations` / `scene.AnimationCount` matches how clips are loaded elsewhere.
- **Rendering confirms vertices stay local.** `Platformer3D/MonoGame/View.fs:78-93` builds each instance's world matrix from the grid `worldPos` + `modelRotation` + `modelVerticalOffset` via `Matrix.CreateTranslation`/rotation; mesh vertices are never pre-transformed. Raw mesh-vertex extents therefore equal the model's size in model units.
- `BoneProbe/RawAssimp.fs` already reads `scene.AnimationCount`/`scene.HasAnimations`, `m.VertexCount`, and iterates meshes/bones. `Mesh.Vertices` (Assimp `Vector3D` X/Y/Z) is available but unused for extents.
- `BoneProbe/BoneProbe.fsproj` compiles `Scene.fs`, `RawAssimp.fs`, `Palette.fs`, `Program.fs` (net8.0, `AssimpNetter` 6.0.4). A new source file must be added to this `<Compile>` order before `Program.fs`.
- Target assets: `Platformer3D/assets/kenney_platformer-kit/Models/*.glb` (~130 files).
- AGENTS.md: run `dotnet fantomas .` before committing; never use `Option.get`/`.Value`.

## Assumptions and open decisions
- Confirmed: vertices are mesh-local (see Context), so raw per-axis vertex extents across all meshes = model size in model units. No node-transform traversal needed.
- "Animatable" = scene embeds ≥1 animation (`scene.HasAnimations`), matching `Animation3DClips.fromScene`.
- **Performance is a feature**: scan all files in parallel (not one-by-one), collect results, then print the report once. ~130 files should be processed concurrently.
- Name: new mode is `dimensions`, accepts a **file or directory** path; on a directory it scans `*.glb` concurrently.

## Design notes
- New module `BoneProbe/Dimensions.fs` with a `probe(options: Options)` entrypoint, mirroring `RawAssimp.probe`/`Palette.probe` shape so dispatch stays uniform.
  - If path is a directory: enumerate `*.glb` (sorted for stable output). If a single file: process just that file. If path missing: print usage error, return 1.
  - **Parallel scan (perf is a feature):** process all `.glb` files concurrently via `System.Threading.Tasks.Parallel.ForEach` (or `Parallel.For` over the sorted array). Each task does `Scene.tryLoad` + extent fold + animatable check independently, producing a result record into a thread-safe collection (or into a pre-sized array indexed by position). Assimp `AssimpContext` is per-call (`use importer = new AssimpContext()` inside `tryLoad`), so no shared importer — safe to parallelize.
  - Per-file result: `name`, per-axis min/max + extent (raw model units), `meshCount`, `hasAnimations` + `animationCount`, and a `loaded: bool` flag (false if `tryLoad` returned `ValueNone`).
  - After the parallel pass completes, print the report **once**: a header, one sorted line per model (`name, sizeX sizeY sizeZ, animCount, meshes`), and a footer with totals (files, animatable, failed). Sorting the final array after the parallel pass keeps output deterministic.
  - **Dimensions**: iterate `scene.Meshes`, for each mesh iterate `mesh.Vertices`, track per-axis min/max across all meshes (raw model units). Extent per axis = max − min. Skip meshes with no vertices (guard against the degenerate case).
  - **Animatable**: `scene.HasAnimations` with `scene.AnimationCount`.
- `Scene.fs`: add `Dimensions` to the `Mode` union.
- `Program.fs`: add the `dimensions :: path :: rest` parse case (after `palette`) and dispatch to `BoneProbe.Dimensions.probe`. Existing `raw`/`palette` paths unchanged.
- Keep all output `printfn` with interpolated strings (matches existing BoneProbe style). Pattern-match `tryLoad`/`TryGetValue`, never `Option.get`.

## Risks and challenges
- Assimp `AssimpContext` is not guaranteed thread-safe across calls on a shared instance; mitigation is that `Scene.tryLoad` constructs a fresh `AssimpContext` per call (`use importer = new AssimpContext()`), so each parallel task owns its own — no shared native importer. Verify the build's run doesn't fault; if it does, fall back to processing files serially (still one pass, results printed once).
- A corrupt/unloadable `.glb` must not abort the batch — handled per-file with a `loaded=false` result and a footer count.
- `Mesh.Vertices` may be empty for some meshes — skip empty meshes in the min/max fold.

## Implementation checklist
- [x] `BoneProbe/Scene.fs`: add `Dimensions` case to the `Mode` union.
- [x] `BoneProbe/Dimensions.fs`: new module — define a result record (`name`, per-axis extent, `meshCount`, `animationCount`, `loaded`); a `probe(options)` that resolves file-or-dir, enumerates `*.glb` sorted, runs `Array.Parallel.map scanOne` over them (each scan: `Scene.tryLoad` → fold per-axis min/max over all meshes' `Vertices`, read `scene.HasAnimations`/`AnimationCount`), then after the pass sorts + prints header + per-model line + footer (total/animatable/failed); returns 0 on success, 1 if no files/missing path.
- [x] `BoneProbe/BoneProbe.fsproj`: add `<Compile Include="Dimensions.fs" />` between `Palette.fs` and `Program.fs`.
- [x] `BoneProbe/Program.fs`: add `dimensions :: path :: rest` parse case; dispatch to `BoneProbe.Dimensions.probe`; update `printUsage` text.

## Verification checklist
- [x] `dotnet build BoneProbe` (or `dotnet build`) succeeds.
- [x] `dotnet run --project BoneProbe -- dimensions Platformer3D/assets/kenney_platformer-kit/Models` prints a per-model table (sizeX/Y/Z, animCount, meshes) for all 153 `.glb`; processes in parallel (`Array.Parallel.map`); `block-grass-large.glb` (2.08×1×2.08) shows a larger size than base `block-grass.glb` (1.08×1×1.08); characters show animCount = 25.
- [x] Single-file form works: `dotnet run --project BoneProbe -- dimensions .../tree-pine.glb`.
- [x] Existing modes unaffected: `dotnet run --project BoneProbe -- raw <some.glb> -v summary` still works.
- [x] `dotnet fantomas .` passes (required before commit).

## Handoff notes
- Default agent: keep BoneProbe game-agnostic — no Platformer3D constants/types in the new module. Report raw model-unit extents only; the cell footprint is the user's derivation.
- Run with the Models folder path above to produce the report the user will read.
- Do NOT touch WorldGen, Types.fs, or Constants.fs in this step. The later WorldGen rework is a separate plan.
- Commit the plan file together with the BoneProbe changes; format with `dotnet fantomas .` first.
