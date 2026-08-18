namespace Defli3D.State

open System
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Input
open Defli3D
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// State — the composition root (the SPUF State). The State record
// retains every feature's roots and projections, and init wires
// them.
//
// The port replaced Defli's MVU shell (Msg + Cmd + the Immediate
// pump) with the State · Projection · Update · Force shape, split
// across three files:
//   State.fs      — the record + init + StateCell (this file)
//   Application.fs — the per-frame sim, event translation and the
//                     host-facing cold paths (Update)
//   Frame.fs      — RenderFrame + the frame force (Force)
// There is no Msg, no Cmd, no Sub — handlers write roots and run
// effects directly.
// ─────────────────────────────────────────────────────────────

/// The composition root: every feature's roots and projections are
/// retained here, as fields of this record.
type State = {
  Config: WorldConfig
  Map: MapModel
  Enemies: Enemies.EnemiesModel
  Spawning: Spawning.SpawningModel
  Waves: Waves.WavesModel
  Towers: Towers.TowersModel
  Projectiles: Projectiles.ProjectilesModel
  Zones: Zones.ZonesModel
  Vfx: Vfx.VfxModel
  Economy: Economy.EconomyModel
  Camera: Camera.CameraModel

  /// Tower kind the next placement uses — a CVal because the
  /// PlacementPreview projection joins on it (cold path writes).
  SelectedTower: cval<TowerDef>

  /// Hover cell CVal — UI state written by the host; the state
  /// projections (PlacementPreview/RangeRing) join on it.
  HoverCell: cval<struct (int * int) voption>

  /// Frozen by the host; the sim writes nothing while paused.
  Paused: cval<bool>

  /// The semantic input state — the InputMapper subscription writes it
  /// (pre-step lane), Application.update consumes its Started/Released
  /// edges, and the subscription clears them after update. Input state,
  /// not gameplay state: the mapper owns the writes, the sim owns the
  /// reads.
  Actions: cval<ActionState<GameAction>>

  /// The sim's clock root — written by Application.update every step
  /// (paused included) and packed into the frame as Time, so the draw
  /// side (hover bob, idle spins) rides the sim's clock. A root rather
  /// than a mutable field: the frame force stays a pure State →
  /// RenderFrame mapping.
  Clock: cval<GameTime>

  Projections: Projections

  /// The map's difficulty ceiling (Balance.capacityOf) — the
  /// saturation the wave scale calibrates against. A constant per
  /// map: recomputed at init, never written after. Carried on the
  /// state so views/tests read it without recomputing the scan.
  Capacity: Balance.Capacity

  /// Live enemy count — one count node, created at init and forced by
  /// Waves.tick (Defli created a fresh node per frame; one node is
  /// enough and allocates nothing per frame).
  AliveCount: aval<int>

  /// Tower count node, created at init (frame draw-side; hoisted with
  /// the state so the frame force stays allocation-free).
  TowerCount: aval<int>

  /// Projectile count node, created at init (frame draw-side; hoisted
  /// with the state so the frame force stays allocation-free).
  ProjectileCount: aval<int>

  /// World-sim diagnostics (sampled inside the per-frame sim — Kimo WorldDiag).
  Diag: WorldDiag
}

/// The holder the composition root, the frame force, and the host
/// input wiring read the state through.
[<Sealed>]
type StateCell(value: State) =
  member val Value = value with get, set

module State =

  let init(cfg: WorldConfig) : State =
    let map = MapModel.create cfg
    // The map's capacity scan (cold, once): feeds the wave scale's
    // saturation BEFORE the waves model builds its Scale projection.
    let capacity = Balance.capacityOf map
    let enemies = Enemies.Enemies.init()
    let spawning = Spawning.Spawning.init cfg.Seed
    let waves = Waves.Waves.init capacity
    let towers = Towers.Towers.init()
    let projectiles = Projectiles.Projectiles.init()
    let zones = Zones.Zones.init()
    let vfx = Vfx.Vfx.init()
    let economy = Economy.Economy.init cfg

    // The world size in UNITS (1 cell = 1 world unit) — the orbit
    // camera's bounds and the picking grid extent.
    let camera =
      Camera.Camera.init(Vector2(float32 cfg.GridCols, float32 cfg.GridRows))

    let selectedTower = CVal.create TowerDefs.sentry
    let hoverCell = CVal.create ValueNone
    let paused = CVal.create false
    let actions = CVal.create ActionState.empty

    let clock =
      CVal.create {
        TotalTime = TimeSpan.Zero
        ElapsedGameTime = TimeSpan.Zero
      }

    let projections =
      Projections(
        enemies,
        towers,
        projectiles,
        economy,
        MapModel.buildableGrid map,
        hoverCell,
        selectedTower
      )

    {
      Config = cfg
      Map = map
      Enemies = enemies
      Spawning = spawning
      Waves = waves
      Towers = towers
      Projectiles = projectiles
      Zones = zones
      Vfx = vfx
      Economy = economy
      Camera = camera
      SelectedTower = selectedTower
      HoverCell = hoverCell
      Paused = paused
      Actions = actions
      Clock = clock
      Projections = projections
      Capacity = capacity
      AliveCount = enemies.Alive |> AMap.count
      TowerCount = towers.Statics |> AMap.count
      ProjectileCount = projections.Homing |> AMap.count
      Diag = WorldDiag()
    }

  /// Reset the sim to its initial state in place. Each root keeps the
  /// same instance and only its content changes, so projections and
  /// subscriptions keep working. Actions, Clock, Map, Capacity, and
  /// the derived nodes are not touched.
  let reset(state: State) : unit =
    state.Enemies.Healths |> CMap.set Map.empty
    state.Enemies.Motions |> CMap.set Map.empty
    state.Enemies.Positions |> CMap.set Map.empty
    state.Enemies.Defs |> CMap.set Map.empty
    state.Enemies.NextId <- 0<EnemyId>
    state.Enemies.SlowTimers.Clear()
    state.Enemies.Velocities.Clear()

    state.Spawning.Queue.Clear()
    state.Spawning.Rng <- Random state.Config.Seed

    state.Waves.WaveNumber.Set 0
    state.Waves.WaveActive.Set false

    state.Towers.Statics |> CMap.set Map.empty
    state.Towers.Runtimes |> CMap.set Map.empty
    state.Towers.CellIndex |> CMap.set Map.empty
    state.Towers.Levels |> CMap.set Map.empty
    state.Towers.NextId <- 0<TowerId>

    state.Projectiles.Rows |> CMap.set Map.empty
    state.Projectiles.NextId <- 0<ProjectileId>

    state.Zones.Rows |> CMap.set Map.empty
    state.Zones.NextId <- 0<ZoneId>
    state.Zones.Scratch.Clear()

    // Retire all live particles: a pool draws only its first Count slots.
    state.Vfx.Impact.Count <- 0
    state.Vfx.Explosion.Count <- 0
    state.Vfx.DeathPoof.Count <- 0
    state.Vfx.Muzzle.Count <- 0
    state.Vfx.MuzzleDust.Count <- 0
    state.Vfx.Placement.Count <- 0
    state.Vfx.BaseHit.Count <- 0

    state.Economy.Gold.Set state.Config.StartingGold
    state.Economy.Lives.Set state.Config.StartingLives

    Camera.Camera.reset state.Camera

    state.SelectedTower.Set TowerDefs.sentry
    state.HoverCell.Set ValueNone
    state.Paused.Set false

  /// The grid's uniform cell size (world geometry — 1 cell = 1 world
  /// unit; Defli's 64 px cells ÷ 64).
  let inline cellSize(_state: State) = Vector2(1f, 1f)
