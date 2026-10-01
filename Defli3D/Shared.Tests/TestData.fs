module Defli3D.Tests.TestData

open System
open Mibo.Adaptive
open Defli3D
open Defli3D.State
open Defli3D.State.Systems.Enemies

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

  /// Test enemy definitions — distinct values catch mix-ups. Speeds
  /// are world units per second (1 cell = 1 unit; Defli's px/s ÷ 64),
  /// distinct from the production defs (1.0/1.7/0.55/2.0/0.4).
  let grunt = {
    Key = "test_grunt"
    Archetype = EnemyArchetype.Grunt
    Hp = 30f
    Speed = 0.625f // 40 px/s ÷ 64
    Resist = 0f
    GoldReward = 2
    HullModel = Models.enemyUfoA
    WeaponModel = ValueSome Models.enemyUfoAWeapon
    Scale = 1f
  }

  let runner = {
    Key = "test_runner"
    Archetype = EnemyArchetype.Runner
    Hp = 10f
    Speed = 1.40625f // 90 px/s ÷ 64
    Resist = 0f
    GoldReward = 3
    HullModel = Models.enemyUfoB
    WeaponModel = ValueNone
    Scale = 1f
  }

  let tank = {
    Key = "test_tank"
    Archetype = EnemyArchetype.Tank
    Hp = 100f
    Speed = 0.3125f // 20 px/s ÷ 64
    Resist = 0f
    GoldReward = 5
    HullModel = Models.enemyUfoC
    WeaponModel = ValueSome Models.enemyUfoCWeapon
    Scale = 1f
  }

  /// Test flier — distinct values catch production mix-ups.
  let flier = {
    Key = "test_flier"
    Archetype = EnemyArchetype.Flier
    Hp = 15f
    Speed = 0.9375f // 60 px/s ÷ 64
    Resist = 0f
    GoldReward = 4
    HullModel = Models.enemyUfoD
    WeaponModel = ValueNone
    Scale = 1f
  }

  /// Test boss — distinct values catch production mix-ups.
  let boss = {
    Key = "test_boss"
    Archetype = EnemyArchetype.Boss
    Hp = 200f
    Speed = 0.46875f // 30 px/s ÷ 64
    Resist = 0f
    GoldReward = 20
    HullModel = Models.enemyUfoA
    WeaponModel = ValueSome Models.enemyUfoAWeapon
    Scale = 1.5f
  }

  let all = [| grunt; runner; tank; flier; boss |]

  /// Test zone tower — a ground-only slowing zone with distinct
  /// values, so zone tests read the mechanism and never the
  /// production tuning.
  let zoneTower: TowerDef = {
    Key = "test_zonetower"
    Name = "Test Zone Tower"
    Chassis = Chassis.Deck 0
    Cost = 50
    Range = 3f
    Warhead = {
      Damage = 1f
      ImpactRadius = 0.35f
      Piercing = false
      Zone =
        ValueSome {
          Radius = 1f
          Seconds = 3f
          Slow = 0.5f
          TickDamage = 0f
          TickInterval = 0.5f
          MaxStacks = 3
          Affects = TargetDomain.Ground
        }
    }
    FireRate = 2f
    RatePerLevel = 0f
    ProjectileSpeed = 7f
    ProjectileSpeedScales = false
    Volley = 1
    Spread = 0f
    Trajectory = Trajectory.Flat
    Homing = HomingPolicy.Never
    WeaponModel = ValueNone
    GunScale = 1f
    ProjectileModel = Models.ammoArrow
    ProjectileScale = 0.35f
    MuzzleDust = false
    Targets = TargetDomain.Any
    TargetPolicy = TargetPolicy.First
    UpgradeCost = 50
    MaxLevel = 1
  }

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
  let cell = StateCell(state)

  let runner =
    new AdaptiveHeadless<Frame.RenderFrame>(
      Application.program ignore cell (fun _ -> AMap.empty)
    )

  Harness(state, runner)

/// Spawns an enemy through the system's direct function (the shape
/// the sim's handlers use).
let spawnEnemy (state: State) (def: EnemyDef) =
  Enemies.spawn def state.Enemies state.Map.Path

/// A guaranteed-buildable cell on a map (scan — the seeded clutter
/// moves with the seed, placement tests must not hardcode).
let openCellOfMap(map: Defli3D.State.Systems.MapModel) : struct (int * int) =
  let terrain = Defli3D.State.Systems.MapModel.terrain map
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
/// reach walking enemies quickly.
let roadSideCell(state: State) : struct (int * int) =
  let buildable = Defli3D.State.Systems.MapModel.buildableGrid state.Map

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
    (Defli3D.State.Systems.MapModel.pathGrid state.Map)

  match best with
  | ValueSome c -> c
  | ValueNone -> failwith "no buildable road-side cell on the fixture map"

/// A guaranteed-buildable cell on the state's map.
let openCell(state: State) : struct (int * int) = openCellOfMap state.Map

/// Drives damage through the same event translation the sim's enemy
/// handler uses (kills pay gold, burst, boss split).
let damageEnemy (state: State) (eid: int<EnemyId>) (amount: float32) =
  Application.handleEnemyEvents
    state
    (Enemies.applyDamage eid amount state.Enemies)

/// Coarse step for e2e timing tests (the sim is dt-agnostic — the
/// movement/spawn math consumes dt directly).
let dt = TimeSpan.FromSeconds 0.1

/// Fine step for frame-accurate tests.
let frameDt = TimeSpan.FromSeconds(1.0 / 60.0)
