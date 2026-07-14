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
- [x] **Iter 1 — Per-segment biome cohesion (world-X continuous field)** ✅ e0ba715
  - `biomeAtColumn`: continuous biome from world-tile-X (replaces chunk-level `biomeAt`)
  - Resolve biome per ground slab / platform from world-X in `stamp`
  - Config: `BiomeColumnScale` (~0.03, ~1 biome/chunk, smooth seams)
- [x] **Iter 2 — Reachability predicate (foundation)** ✅ 725d637
  - `arcHeightTiles(d)` + `reachable(d, r)` + `reachableBoth` + `maxLevelGapTiles` from physics constants (pure)
  - `Platformer/Shared.Tests` project (Expecto, mirrors FPSSample) — 17 tests, all pass
  - Registered in `.slnx`; full solution builds
- [x] **Iter 2b — One-way platform colliders + drop-through + duck sprite** ✅ 20958eb
  - Split collider data: `Chunk.Platforms` (solids) / `Chunk.OneWayPlatforms` (one-way)
  - Physics: one-way = land-from-above only; `GameAction.Down` drops through
  - Duck sprite (`character_beige_duck`) + `AnimationState.Duck`
- [x] **Iter 3 — Elevation field + reachability-aware ground planner** ✅ d2aa06d
  - `elevationAtColumn(worldX, seed, scale, amplitude)` — continuous per-column surface Y
  - `Ground.plan`: `surfaceY` → `elevationAt: int -> int`; per-slab Y
  - `clampReachable`: clamps to `prevY ± arcHeightTiles(gap)` — terrain follows field where gentle, plateaus where steep
  - Platform clearance fix: per-column downward grid scan for actual ground surface (replaces flat floorY check)
  - Config: `ElevationScale = 0.04f`, `ElevationAmplitude = 2`
- [x] **Iter 4 — Reachability verifier + cross-seam clamp + bug fixes** ✅ b2a6742
  - `Ground.verifyReachability`: O(slabs) check of intra-chunk + cross-seam edges via `reachableBoth`
  - `Ground.clampCrossSeam`: adjusts last slab Y for cross-seam reachability
  - Fixed 3 latent bugs: trailing-gap growth formula, trailing-gap loop skip, `reachable(0,rise)` = false
  - 4 new tests (1500 chunks stress test at amplitude 2 = 0 violations)
- [x] **Iter 4b — Spawn plateau** ✅ 6fa70bb
  - First `spawnProtectedCells` columns pinned to flat groundY (was declared but never enforced)
  - Prevents player spawning inside raised terrain
- [x] **Iter 5 — Config tuning + elevation-aware platform attempt + revert** ✅ 91b8fec → 3bcf52d
  - Tuning sweep confirmed amplitude 2 is the ceiling for this physics model (arc peaks at ~4.7 tiles at gap 3; amplitude 3+ produces violations at wider gaps)
  - Attempted elevation-aware platform placement (platformFloorY = highest slab Y) — REVERTED: made platforms unreachable over valleys
  - Final state: `floorY = groundY`, per-column clearance scan is the only placement constraint (this is the good state)

## Current verified-safe config
```
JumpBudget: MaxVertical=3, MaxHorizontal=4
Ground: MinGap=2, MaxGap=4, MinWidth=6, MaxWidth=14, MinHeight=2, MaxHeight=4
Platform: MinClearance=3, MaxClearance=4, MinVerticalGap=3, MaxVerticalGap=4
BiomeColumnScale=0.03, ElevationScale=0.04, ElevationAmplitude=2
```
A config sweep test confirmed amplitude 2 is clean at scales 0.02–0.08. Amplitude 3+ produces cross-seam violations. MaxGap=5 produces violations (arcHeightTiles(5)≈2.66, thin margin).

## Open work
- [ ] **Elevation-aware platform placement (needs proper approach)** — the attempt to use highest slab Y as floorY made platforms unreachable over valleys. The correct approach needs a graph-based reachability model where each platform must chain from ground or a lower platform within `arcHeightTiles` of its X-span. Not a scan-and-reject — needs a placement algorithm that guarantees a reachable path.
- [ ] **Revisit `Island.fs`** as self-contained content boxes layered on proven-reachable terrain
- [ ] **Other layers** (decorations, triggers, interactables — coins/spikes/flags extraction exists in `extractAll` but placement isn't wired)

## Notes / known
- Minimap colors by `chunk.Biome` (origin representative); per-tile biome available for future accuracy.
- Reachability predicate deferred to Iter 2 (no dead code in Iter 1).
- Stamps are unchanged `GridSection2D -> GridSection2D` composable functions; biome is now resolved at stamp time.
