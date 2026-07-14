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
- [x] **Iter 1 — Per-segment biome cohesion (world-X continuous field)**
  - [x] `biomeAtColumn`: continuous biome from world-tile-X (replaces chunk-level `biomeAt`)
  - [x] Resolve biome per ground slab / platform from world-X in `stamp`
  - [x] Config: `BiomeColumnScale` (~0.03, ~1 biome/chunk, smooth seams)
  - [x] Build + format
  - **Verify:** biome regions blend across chunk seams; no hard edges.
- [ ] **Iter 2 — Reachability predicate (foundation)**
  - `arcHeight(d)` + `reachable(d, r)` from physics constants (pure, unit-testable)
- [ ] **Iter 3 — Band-limited elevation + reachability-aware ground planner**
  - Per-column elevation field feeding `GroundSpec.Y`
  - Band-limit so rise-per-gap stays inside the parabola; planner clamps edge cases
- [ ] **Iter 4 — O(slabs) reachability verifier (debug/test assertion)**
- [ ] **Iter 5 — Re-tune `GroundConfig` against verifier; relief + guarantee**
- [ ] Later: revisit `Island.fs` as self-contained content boxes layered on terrain.

## Notes / known
- Minimap colors by `chunk.Biome` (origin representative); per-tile biome available for future accuracy.
- Reachability predicate deferred to Iter 2 (no dead code in Iter 1).
- Stamps are unchanged `GridSection2D -> GridSection2D` composable functions; biome is now resolved at stamp time.
