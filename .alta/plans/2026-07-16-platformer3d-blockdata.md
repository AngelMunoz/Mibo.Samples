# Platformer3D: BlockData — consolidated, baked-in per-block data

- Status: Approved (open decisions resolved — fractional extents)
- Plan file: `.alta/plans/2026-07-16-platformer3d-blockdata.md`
- Created: 2026-07-16
- Task: Create a single-source `BlockData.fs` registry of baked-in per-block data (model name, cell footprint, offset, rotation, category) for Platformer3D, consolidating the metadata currently scattered across `Types.fs`, and refactor terrain `BlockType` to carry biome as a field (grass/snow = same shape, different color) — mirroring the 2D `TileData.fs`/`Types.fs` split.
- Git: `.alta/plans/` is not ignored. Commit this plan with the related work.

## Objective
- Establish the **data layer** that the future WorldGen rework will consume. Today every block is treated as a uniform 1×1×1; baked footprints let WorldGen place multi-cell terrain (2×1×2 large, 1×2×1 tall, etc.) with conscious gaps/overlap.
- Consolidate per-block metadata (modelName, vertical offset, rotation, category, footprint) into one registry `BlockData.lookup : BlockType -> BlockInfo`, removing the scattered `BlockType` module functions from `Types.fs`.
- Be **meticulous on terrain**. Non-terrain / non-obvious assets are included lightly or treated as empty with a note.
- Express the biome concept: grass/snow are the same base shapes differing only by color/model (confirmed from the dimensions report — identical footprints per shape). Terrain block shapes carry `biome: Biome3D` as a field, like 2D `Block of biome: Biome`.

## Non-goals (explicit)
- **No WorldGen logic rework.** Only minimal compile-correct updates to construction/match sites so the code builds. The rework that actually places multi-cell terrain is a separate later plan.
- **No layered grid.** The 3D sample uses a simple `CellGrid3D`, not a `LayeredGrid2D` like 2D. Decorations/collectibles will move to separate layers in a future step — out of scope now.
- **No runtime parsing.** All values are baked literals (ints/floats/string constants). Re-run BoneProbe `dimensions` only to *author* the table; nothing parses at runtime.
- **No new dependencies, no AABB/`BoundingBox` work, no Spatial3D integration.**

## Context and evidence
- **2D reference pattern:** `Platformer/Shared/TileData.fs` = single source: `TileInfo` struct + `lookup : Tile -> TileInfo` (big match, computed on demand, never stored per-cell) + predicates (`isSolid`/`isOneWay`/...). `Platformer/Shared/Types.fs` keeps only the `Tile` union (biome carried as field: `Block of biome: Biome`) + `tileLayer`. WorldGen imports `TileData`.
- **3D current state:** `Platformer3D/Shared/Types.fs:50-139` scatters metadata as `BlockType` module functions: `modelName`, `modelVerticalOffset`, `modelRotation`, `isSolid`, `isCollectible`, `isDecoration`, `isLightSource`. `isDecoration`/`isLightSource` are **dead** (no callers — grep confirmed).
- **Consumers of BlockType (blast radius):**
  - `Platformer3D/Shared/Physics.fs:157,241` — `BlockType.isSolid`, `BlockType.isCollectible` (predicates).
  - `Platformer3D/Shared/WorldGen.fs:160,178` — `BlockType.isSolid`; plus constructs `BlockType.Ground/SnowGround/Platform/PlatformRamp/Spikes/TreePine/TreeSnow/Rock/GrassTuft/Coin/Flag/MushroomLight` (line ~76 sets `BlockType.Ground` for stairs).
  - `Platformer3D/Shared/Minimap.fs:33-61` — `blockColor` matches **every** BlockType case.
  - `Platformer3D/{MonoGame,Raylib}/View.fs` — `modelName`/`modelRotation`/`modelVerticalOffset` (predicates).
  - `Platformer3D/{MonoGame,Raylib}/Systems.fs:66` — constructs `BlockType.MushroomLight`.
  - `Platformer3D/Shared.Tests/Tests.fs` — `generateChunk` + `bt <> Empty` (no specific case construction).
- **Dimensions (from committed BoneProbe `dimensions` mode):** base `block-grass` ≈ 1.08×1×1.08; `block-grass-large` ≈ 2.08×1×2.08; `block-grass-large-tall` ≈ 2.08×2×2.08; `block-grass-long` ≈ 2.08×1×1.08; `block-grass-low` ≈ 1.08×0.5×1.08; `block-grass-narrow` ≈ 0.78×1×0.78; slopes ≈ 2×~1×2; trees ≈ 1×2×1; platforms ≈ 1×~0.2×1. **grass/snow pairs are identical** for every shape (e.g. `block-grass-large` == `block-snow-large` == 2.082×1×2.082).
- **Biome scope:** only the `block-grass*`/`block-snow*` families have biome pairs. `platform*`, `spike-block`, collectibles, decorations have NO biome variants (single model each).
- AGENTS.md: `dotnet fantomas .` before commit; never `Option.get`/`.Value`; prefer interpolated strings.

## Assumptions and open decisions
- **Cell extents rule (DECIDED):** footprint extents are kept **fractional** (`ExtentW/H/D: float32`), not rounded to integer cells. Base block ≈ 1.08 units. So low = 1.08×0.5×1.08, narrow = 0.78×1×0.78, large = 2.08×1×2.08, etc. WorldGen (later) will use the raw floats to compute exact spacing/overlap; no rounding loss.
- **Struct vs dictionary (user raised):** Recommend the 2D pattern — a `[<Struct>] BlockInfo` returned by a `lookup` match, computed on demand (never stored per-cell), so size is not a per-cell concern. Model names reference existing `KenneyModels` string constants (zero alloc). Alternative: a `Dictionary<BlockType, BlockInfo>` built once (lazy) for O(1) lookup with fewer match arms. **Default: struct-via-match** (matches 2D, zero-alloc); switch to dict only if the match gets unwieldy.
- **Overhang variants:** `block-grass-overhang-*` share footprints with their non-overhang counterparts (e.g. overhang-large == large: 2.082×1×2.082). Overhang is a visual variant. Decision: model as a separate case sharing the same footprint literal, OR defer overhang shapes entirely (note). **Default: include base shapes first; fold overhang as same-footprint cases if straightforward, else note+defer.**
- **Decorations/collectibles:** trees/rocks/grass/coins/flag/mushrooms are currently in the flat enum and placed by WorldGen. Keep them as flat (non-biome) cases, data-fy lightly from the report, add a note that they will move to a decoration layer later. Not the focus.

## Design notes

### New file: `Platformer3D/Shared/BlockData.fs`
- `Biome3D` union: `Grass | Snow` (extensible).
- `[<Struct>] BlockInfo = { ModelName: string; ExtentW: float32; ExtentH: float32; ExtentD: float32; VerticalOffset: float32; RotationY: float32; Category: BlockCategory }`. Extents are **fractional raw model units** (not rounded to cells) per the decided cell-extents rule.
- `[<Struct>] BlockCategory = Empty | Solid | OneWay | Hazard | Collectible | Decoration` (mirrors 2D `ColliderKind` intent + decoration flag).
- `lookup : BlockType -> BlockInfo` — exhaustive match. Terrain shapes match on shape then compose the biome model name via a helper (e.g. `blockModel biome = match biome with Grass -> KenneyModels.blockGrass | Snow -> KenneyModels.blockSnow`). Footprint literals are shared constants per shape (grass/snow identical).
- Predicates consolidated here: `isSolid`, `isCollectible` (drop dead `isDecoration`/`isLightSource`, OR keep `isDecoration`/`isLightSource` if WorldGen/Systems still reference — verify; grep shows only `MushroomLight` construction in Systems, no `isLightSource` call, so drop and note).
- Footprint table authored by re-running `dotnet run --project BoneProbe -- dimensions Platformer3D/assets/kenney_platformer-kit/Models` and recording raw extents. Key terrain values (ExtentW×H×D, model units):
  - Block/Corner/Edge/Hexagon: 1.08×1×1.08 · Large: 2.08×1×2.08 · LargeTall: 2.08×2×2.08 · Long: 2.08×1×1.08 · Low: 1.08×0.5×1.08 · LowLarge: 2.08×0.5×2.08 · Narrow: 0.78×1×0.78 · Curve: 2×1×1 · Slope: 2.08×~1×2 · Platform: 1×0.2×1 · PlatformRamp: 1×0.57×1 · Spikes: 0.9×0.9×0.9.
- Add to `Shared.fsproj` compile order after `Types.fs`, before `WorldGen.fs`.

### `Platformer3D/Shared/Types.fs`
- Keep: `GameAction`, `BlockType` union (refactored — see below), `Chunk`, `BoundingBox` usage.
- **Refactor terrain `BlockType` to biome-as-field** (grass/snow pairs collapse): e.g. `Block of biome: Biome3D | LargeBlock of biome | TallBlock of biome | LongBlock of biome | LowBlock of biome | NarrowBlock of biome | Corner of biome | Edge of biome | Curve of biome | Hexagon of biome | Slope of biome * SlopeDir | ...`. Non-biome flat cases stay: `Platform | PlatformRamp | PlatformOverhang | Spikes | TreePine | TreeSnow | Rock | GrassTuft | Coin | Jewel | Heart | Star | Mushrooms | Crate | Barrel | Flag | MushroomLight | Empty`.
  - `[<Struct>] SlopeDir = XPos | XNeg | ZPos | ZNeg` to fold the 4×2 slope cases into `Slope of biome * SlopeDir`.
- **Remove** the `module BlockType` (modelName/offset/rotation/isSolid/isCollectible/isDecoration/isLightSource) — moved to `BlockData`.

### Consumer updates (compile-correct only, no logic rework)
- `Physics.fs`: `BlockType.isSolid` → `BlockData.isSolid` (or `(lookup bt).Category = Solid`); same for `isCollectible`.
- `WorldGen.fs`: update `BlockType.Ground`→`Block Grass`, `BlockType.SnowGround`→`Block Snow`, slope constructions → `Slope(Grass/Snow, dir)`; predicate calls → `BlockData.*`. Logic unchanged.
- `Minimap.fs:blockColor`: update case names to biome-as-field (e.g. `Block Grass | Slope(Grass,_) -> green`; `Block Snow | Slope(Snow,_) -> white`). Colors unchanged.
- `View.fs` (×2 backends): `BlockType.modelName/rotation/offset` → `BlockData.lookup bt |> fun i -> i.ModelName` etc. (or keep thin wrapper fns in BlockData). Render logic unchanged.
- `Systems.fs` (×2): `BlockType.MushroomLight` construction — name unchanged (flat case), no change needed unless enum name moved.
- `Shared.Tests/Tests.fs`: `bt <> Empty` still valid; `Empty` stays.

### Slim-ness
- `BlockInfo` is a struct computed on demand (2D precedent). Not stored per-cell (grid stores `BlockType`). ModelName references existing `KenneyModels` constants → no alloc. If the `lookup` match grows too large with all overhang/slope variants, fall back to a lazy `Dictionary<BlockType, BlockInfo>` — note as contingency.

## Risks and challenges
- **Blast radius:** enum refactor touches Minimap (full match), WorldGen (constructions + predicates), View×2, Physics, Systems×2, tests. Mitigation: keep updates compile-correct only; preserve all behavior; run the existing test suite + both backends build.
- **Sub-cell shapes:** low (0.5Y) and narrow (0.78W) are kept as fractional extents (decided), so WorldGen (later) has full precision for overlap/gap math. No rounding loss.
- **Exhaustive match:** adding biome-as-field cases means every match must cover them or the build fails (good — compiler-enforced). Minimap `blockColor` is the largest match to update.
- **Dead code:** dropping `isDecoration`/`isLightSource` — verify no callers (grep says none for the functions; `MushroomLight` is constructed but `isLightSource` is never called). Safe to drop; note it.

## Implementation checklist
- [x] `Platformer3D/Shared/BlockData.fs`: `Biome3D`/`SlopeDir` (in Types), `BlockCategory`, `BlockInfo` (fractional `ExtentW/H/D: float32`), `lookup`, `modelName`/`modelVerticalOffset`/`modelRotation`, `isSolid`, `isCollectible`. Footprint table authored from BoneProbe `dimensions` with raw fractional extents (no rounding). Dead `isDecoration`/`isLightSource` dropped (grep-confirmed no callers).
- [x] `Platformer3D/Shared/Types.fs`: terrain `BlockType` refactored to biome-as-field (`Block`/`LargeBlock`/`TallBlock`/`LongBlock`/`LowBlock`/`NarrowBlock` of biome; `Slope` of biome*dir); `Biome3D`+`SlopeDir` defined here (before BlockData) to avoid circular deps; removed `module BlockType`. Kept `GameAction`, `Chunk`.
- [x] `Platformer3D/Shared/Shared.fsproj`: added `BlockData.fs` after `Types.fs`.
- [x] `Platformer3D/Shared/Physics.fs`: predicate calls → `BlockData.*`.
- [x] `Platformer3D/Shared/WorldGen.fs`: BlockType constructions updated (`Ground`→`Block Grass`, `SnowGround`→`Block Snow`, flat cases unqualified); predicate calls → `BlockData.*`. Logic unchanged.
- [x] `Platformer3D/Shared/Minimap.fs`: `blockColor` match updated to biome-as-field cases (colors preserved).
- [x] `Platformer3D/{MonoGame,Raylib}/View.fs`: modelName/rotation/offset via `lookup`/`modelName` (unqualified after `open Platformer3D.BlockData`). Render logic unchanged.
- [x] `Platformer3D/{MonoGame,Raylib}/Systems.fs`: no change needed (`BlockType.MushroomLight` still valid as type-qualified case).
- [x] Non-terrain (collectibles/decorations): data-fied from report; noted "moves to decoration layer later".

## Verification checklist
- [x] `dotnet build Mibo.Samples.slnx` (whole solution) succeeds.
- [x] `dotnet test Platformer3D/Shared.Tests` passes (6/6 green).
- [x] `dotnet run --project BoneProbe -- dimensions ...` values used to author footprints (fractional).
- [x] Spot-check: `lookup (Block Grass)` → `block-grass` 1.082×1×1.082 Solid; `LargeBlock Snow` → `block-snow-large` 2.082×1×2.082; `Slope(Grass, XPos)` → 2.082×0.759×2.011 rotation 0.
- [x] Minimap colors behavior preserved (grass green, snow white) after match update.
- [x] `dotnet fantomas .` passes (required before commit).
- [x] No stale `BlockType.modelName/modelVerticalOffset/modelRotation/isSolid/isCollectible/isDecoration/isLightSource/Ground/SnowGround/GroundSlope*/SnowSlope*` references remain (grep-confirmed).

## Handoff notes
- This step is **data + compile-correct refactor only**. Do NOT rework WorldGen placement logic — that is the next plan and will consume `(BlockData.lookup bt).CellsW/H/D` to place multi-cell terrain.
- Re-run BoneProbe `dimensions` to get exact extents; apply the cell-rounding rule (base ≈ 1.08). Be meticulous on terrain; for any asset whose footprint/category is unclear, set Empty-equivalent defaults and add a `// NOTE:` comment.
- Keep BlockData game-data-only (no backend types). ModelName strings come from existing `KenneyModels` constants — do not invent new model paths.
- Decorations/collectibles stay in the flat enum for now; they move to a separate layer in a future step (3D currently uses a simple grid, not layered).
- Format with `dotnet fantomas .`; commit plan + changes together; do not push.
