module Defli3D.Telemetry

// ── Demo instrumentation ─────────────────────────────────────────────────────
// Recompute counters. Each projection bumps its counter when it actually
// recomputes — not when it is forced and found clean. Per-key counters
// (mapA joins) count element recomputes; whole-node counters (filters,
// scalar maps) count node recomputes. The summary prints at the game-over
// transition (Application.update's edge check) so the dirty tracking is
// visible in numbers at game scale.
//
// Wired up in Defli3D (dead code in Defli): Application.update counts
// forced frames (framesTotal/framesPaused) and prints the summary once
// when GameOver flips true (gameOverPrinted is the one-shot guard);
// State.init calls reset so restarts start clean.

let mutable viewsJoin = 0
let mutable aliveFilter = 0
let mutable bossPositions = 0
let mutable effectiveDef = 0
let mutable banner = 0
let mutable gameOver = 0
let mutable homingJoin = 0
let mutable suppression = 0
let mutable rangeRing = 0
let mutable placementPreview = 0

/// Frames forced by the runner (paused frames included).
let mutable framesTotal = 0
/// Frames forced while the sim was paused — 0 recomputes expected.
let mutable framesPaused = 0
/// One-shot guard: the game-over summary prints once per state.
let mutable gameOverPrinted = false

/// Zeroes every counter and re-arms the one-shot game-over print.
/// Called by State.init (restart swaps in a fresh state).
let reset() =
  viewsJoin <- 0
  aliveFilter <- 0
  bossPositions <- 0
  effectiveDef <- 0
  banner <- 0
  gameOver <- 0
  homingJoin <- 0
  suppression <- 0
  rangeRing <- 0
  placementPreview <- 0
  framesTotal <- 0
  framesPaused <- 0
  gameOverPrinted <- false

let print (totalFrames: int) (pausedFrames: int) =
  printfn "\n═══ telemetry: %d frames forced ═══" totalFrames

  printfn
    "  Views join        recomputed %6dx  per-enemy element recomputes"
    viewsJoin

  printfn
    "  Alive filter      recomputed %6dx  — enemies spawn/die/move"
    aliveFilter

  printfn
    "  BossPositions     recomputed %6dx  — boss-only chooseA"
    bossPositions

  printfn
    "  Homing join       recomputed %6dx  per-projectile element recomputes"
    homingJoin

  printfn
    "  Suppression       recomputed %6dx  — boss aura spatial join"
    suppression

  printfn
    "  EffectiveDef      recomputed %6dx  — tower upgrades only"
    effectiveDef

  printfn "  Banner            recomputed %6dx  — wave banner text" banner
  printfn "  GameOver          recomputed %6dx  — lives <= 0" gameOver
  printfn "  RangeRing         recomputed %6dx  — hover-only" rangeRing
  printfn "  PlacementPreview  recomputed %6dx  — hover-only" placementPreview

  printfn
    "\n═══ paused phase: %d of %d frames forced, 0 recomputes ═══"
    pausedFrames
    totalFrames

  printfn
    "   (nothing in the world depends on the time root, so a paused frame\n    forces the graph to pure version checks — the whole game settles)"
