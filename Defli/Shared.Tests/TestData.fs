module Defli.Tests.TestData

open System
open Mibo.Adaptive
open Defli
open Defli.State
open Defli.State.Systems.Enemies

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
    // Tests rely on the hand-authored map's road (row 8 west of
    // the plaza); placement cells come from the scan helpers below —
    // the procedural variant is covered by its own tests.
    MapVariant = MapVariant.HandAuthored
  }

  /// Test enemy definitions — distinct values catch mix-ups.
  let grunt = {
    Key = "test_grunt"
    Archetype = EnemyArchetype.Grunt
    Hp = 30
    Speed = 40f
    GoldReward = 2
    Sprite = "tank_hull_green"
    Turret = ValueSome "tank_turret_green"
    TurretAngle = 0f
  }

  let runner = {
    Key = "test_runner"
    Archetype = EnemyArchetype.Runner
    Hp = 10
    Speed = 90f
    GoldReward = 3
    Sprite = "tank_hull_beige"
    Turret = ValueSome "turret_missiles_dual"
    TurretAngle = 90f
  }

  let tank = {
    Key = "test_tank"
    Archetype = EnemyArchetype.Tank
    Hp = 100
    Speed = 20f
    GoldReward = 5
    Sprite = "tank_hull_beige"
    Turret = ValueSome "tank_turret_beige"
    TurretAngle = 0f
  }

  /// Test flier — distinct values catch production mix-ups.
  let flier = {
    Key = "test_flier"
    Archetype = EnemyArchetype.Flier
    Hp = 15
    Speed = 60f
    GoldReward = 4
    Sprite = "plane_gray"
    Turret = ValueNone
    TurretAngle = 0f
  }

  /// Test boss — distinct values catch production mix-ups.
  let boss = {
    Key = "test_boss"
    Archetype = EnemyArchetype.Boss
    Hp = 200
    Speed = 30f
    GoldReward = 20
    Sprite = "tank_hull_beige"
    Turret = ValueSome "tank_turret_green"
    TurretAngle = 0f
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

/// Spawns an enemy through the system's direct function (the shape
/// the sim's handlers use).
let spawnEnemy (state: State) (def: EnemyDef) =
  Enemies.spawn def state.Enemies state.Map.Path

/// A guaranteed-buildable cell on a map (scan — the seeded clutter
/// moves with the seed, placement tests must not hardcode).
let openCellOfMap(map: Defli.State.Systems.MapModel) : struct (int * int) =
  let terrain = Defli.State.Systems.MapModel.terrain map
  let mutable found = ValueNone

  Mibo.Layout.CellGrid2D.iter
    (fun x y tile ->
      if tile.Buildable && found.IsNone then
        found <- ValueSome struct (x, y))
    terrain

  match found with
  | ValueSome c -> c
  | ValueNone -> failwith "no buildable cell on the fixture map"

/// A buildable cell on the state's map, orthogonally adjacent to the
/// road and nearest the spawn — for placement tests whose tower must
/// reach walking enemies quickly (enemies enter range within a second).
let roadSideCell(state: State) : struct (int * int) =
  let buildable = Defli.State.Systems.MapModel.buildableGrid state.Map

  let struct (sx, sy) = state.Map.SpawnCell
  let mutable best = ValueNone
  let mutable bestDist = Int32.MaxValue

  Mibo.Layout.CellGrid2D.iter
    (fun x y tile ->
      if tile.IsPath then
        for struct (dx, dy) in
          [| struct (0, -1); struct (0, 1); struct (-1, 0); struct (1, 0) |] do
          match Mibo.Layout.CellGrid2D.get (x + dx) (y + dy) buildable with
          | ValueSome b when b.Buildable ->
            let d = abs(x + dx - sx) + abs(y + dy - sy)

            if d < bestDist then
              best <- ValueSome struct (x + dx, y + dy)
              bestDist <- d
          | _ -> ())
    (Defli.State.Systems.MapModel.pathGrid state.Map)

  match best with
  | ValueSome c -> c
  | ValueNone -> failwith "no buildable road-side cell on the fixture map"

/// A guaranteed-buildable cell on the state's map.
let openCell(state: State) : struct (int * int) = openCellOfMap state.Map

/// Drives damage through the same event translation the sim's enemy
/// handler uses (kills pay gold, burst, boss split).
let damageEnemy (state: State) (eid: int<EnemyId>) (amount: int) =
  Application.handleEnemyEvents
    state
    (Enemies.applyDamage eid amount state.Enemies)

/// Coarse step for e2e timing tests (the sim is dt-agnostic — the
/// movement/spawn math consumes dt directly).
let dt = TimeSpan.FromSeconds 0.1

/// Fine step for frame-accurate tests.
let frameDt = TimeSpan.FromSeconds(1.0 / 60.0)
