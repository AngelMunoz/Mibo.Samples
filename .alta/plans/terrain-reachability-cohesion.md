# Terrain Reachability & Cohesion (Platformer WorldGen)

Goal: make terrain generation cross-chunk *coherent* and *provably reachable*,
iteratively, with manual verification each step. Preserve the streaming
architecture (independent parallel chunk generation).

## Why unconstrained height failed
Old ground reachability held ONLY because height was constant (`groundY = 10`),
so a gap-only check was sufficient. Variable height makes reachability a **joint
(gap, rise) constraint** = the player's jump parabola. A gap within budget can
still be unreachable if the far slab is too high — that is the failure mode.

## Iterations
- [x] **Iter 1 — Per-segment biome cohesion (world-X continuous field)** ✅ committed e0ba715
  - [x] `biomeAtColumn`: continuous biome from world-tile-X (replaces chunk-level `biomeAt`)
  - [x] Resolve biome per ground slab / platform from world-X in `stamp`
  - [x] Config: `BiomeColumnScale` (~0.03, ~1 biome/chunk, smooth seams)
  - [x] Build + format
  - **Verify:** biome regions blend across chunk seams; no hard edges.
- [x] **Iter 2 — Reachability predicate (foundation)** ✅
  - [x] `arcHeightTiles(d)` + `reachable(d, r)` + `maxLevelGapTiles` from physics constants (pure)
  - [x] `Platformer/Shared.Tests` project (Expecto, mirrors FPSSample) — 11 tests, all pass
  - [x] Registered in `.slnx`; full solution builds
  - **Verify:** `dotnet test Platformer/Shared.Tests`
- [x] **Iter 2b — One-way platform colliders + drop-through** ✅ committed 20958eb
  - [x] Split collider data: `Chunk.Platforms` (solids) / `Chunk.OneWayPlatforms` (one-way)
  - [x] Physics: one-way = land-from-above only; `GameAction.Down` drops through
  - [x] Duck sprite (`character_beige_duck`) + `AnimationState.Duck` when grounded+Down
  - **Verify:** jump through clouds from below; land on top; hold Down to drop; duck sprite shows
- [ ] **Iter 3 — Band-limited elevation + reachability-aware ground planner** ✅ (uncommitted, awaits verify)
  - [x] `elevationAtColumn(worldX, seed, scale, amplitude)` — continuous per-column surface Y
  - [x] `Ground.plan` signature: `surfaceY: int` → `elevationAt: int -> int` (per-slab Y)
  - [x] Reachability clamp: `clampReachable(gap, prevY, targetY)` clamps to `prevY ± arcHeightTiles(gap)`
  - [x] Config: `ElevationScale = 0.04f`, `ElevationAmplitude = 2`
  - **Verify:** run client; terrain has gentle hills; everything reachable
- [ ] **Iter 4 — O(slabs) reachability verifier (debug/test assertion)**
- [ ] **Iter 5 — Re-tune `GroundConfig` against verifier; relief + guarantee**
- [ ] Later: revisit `Island.fs` as self-contained content boxes layered on terrain.

## Notes / known
- Minimap colors by `chunk.Biome` (origin representative); per-tile biome available for future accuracy.
- Reachability predicate deferred to Iter 2 (no dead code in Iter 1).
- Stamps are unchanged `GridSection2D -> GridSection2D` composable functions; biome is now resolved at stamp time.
