module Defli3D.Tests.TestData

open System
open Mibo.Adaptive
open Defli3D
open Defli3D.State

// ─────────────────────────────────────────────────────────────
// Test-owned fixtures — never production data (Kimo convention:
// tests build their own `test_*` stores/configs with distinct
// values, so a mix-up fails loudly and production tuning is
// never frozen by a test).
// ─────────────────────────────────────────────────────────────

module Fixtures =

  /// Test world config — distinct from WorldConfig.defaults.
  let cfg = {
    Seed = 7
    StartingGold = 100
    StartingLives = 20
    WaveClearBonus = 10
    GridCols = 20
    GridRows = 12
    // Tests rely on the fixed road (cell (1,1) buildable, row 4 is
    // the road) — the procedural variant is covered by its own tests.
    MapVariant = MapVariant.HandAuthored
  }

  /// Test enemy definitions — distinct values catch mix-ups. Speeds
  /// are world units per second (1 cell = 1 unit; Defli's px/s ÷ 64),
  /// distinct from the production defs (1.0/1.7/0.55/2.0/0.4).
  let grunt = {
    Key = "test_grunt"
    Archetype = EnemyArchetype.Grunt
    Hp = 30
    Speed = 0.625f // 40 px/s ÷ 64
    GoldReward = 2
    HullModel = Models.enemyUfoA
    WeaponModel = ValueSome Models.enemyUfoAWeapon
    Scale = 1f
  }

  let runner = {
    Key = "test_runner"
    Archetype = EnemyArchetype.Runner
    Hp = 10
    Speed = 1.40625f // 90 px/s ÷ 64
    GoldReward = 3
    HullModel = Models.enemyUfoB
    WeaponModel = ValueNone
    Scale = 1f
  }

  let tank = {
    Key = "test_tank"
    Archetype = EnemyArchetype.Tank
    Hp = 100
    Speed = 0.3125f // 20 px/s ÷ 64
    GoldReward = 5
    HullModel = Models.enemyUfoC
    WeaponModel = ValueSome Models.enemyUfoCWeapon
    Scale = 1f
  }

  /// Test flier — distinct values catch production mix-ups.
  let flier = {
    Key = "test_flier"
    Archetype = EnemyArchetype.Flier
    Hp = 15
    Speed = 0.9375f // 60 px/s ÷ 64
    GoldReward = 4
    HullModel = Models.enemyUfoD
    WeaponModel = ValueNone
    Scale = 1f
  }

  /// Test boss — distinct values catch production mix-ups.
  let boss = {
    Key = "test_boss"
    Archetype = EnemyArchetype.Boss
    Hp = 200
    Speed = 0.46875f // 30 px/s ÷ 64
    GoldReward = 20
    HullModel = Models.enemyUfoA
    WeaponModel = ValueSome Models.enemyUfoAWeapon
    Scale = 1.5f
  }

  let all = [| grunt; runner; tank; flier; boss |]

// ─────────────────────────────────────────────────────────────
// Headless harness over AdaptiveHeadless — the MVU shell is gone:
// the state is a composition root (State · Projection · Update ·
// Force). Tests drive input through Post and step virtual time;
// assertions read outputs (roots/projections) after stepping.
// ─────────────────────────────────────────────────────────────

/// A state + runner pair. The runner forces the frame once per
/// Step; the tests read the state's projections and roots between
/// steps (same objects the frame packs).
type Harness(state: State, runner: AdaptiveHeadless<Frame.RenderFrame>) =
  member _.State = state

  /// The input channel: posts a thunk for the next step's drain —
  /// the same lane the production input subscriptions post into.
  member _.Post(thunk: unit -> unit) : unit = runner.Post thunk

  member _.Step(dt: TimeSpan) : unit = runner.Step(dt) |> ignore

  member _.StepN(n: int, dt: TimeSpan) : unit =
    for _ in 1..n do
      runner.Step(dt) |> ignore

  /// Steps until the predicate holds or the budget runs out.
  /// Returns whether the predicate held.
  member _.StepUntil(pred: State -> bool, dt: TimeSpan, maxSteps: int) : bool =
    let mutable i = 0

    while not(pred state) && i < maxSteps do
      runner.Step(dt) |> ignore
      i <- i + 1

    pred state

let mkHarness(cfg: WorldConfig) =
  let state = State.init cfg

  let runner =
    new AdaptiveHeadless<Frame.RenderFrame>(
      Application.program ignore (fun () -> state) (fun _ -> AMap.empty)
    )

  Harness(state, runner)

/// Coarse step for e2e timing tests (the sim is dt-agnostic — the
/// movement/spawn math consumes dt directly).
let dt = TimeSpan.FromSeconds 0.1

/// Fine step for frame-accurate tests.
let frameDt = TimeSpan.FromSeconds(1.0 / 60.0)
