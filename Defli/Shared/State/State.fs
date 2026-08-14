namespace Defli.State

open System
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Defli
open Defli.State.Systems

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
  Vfx: Vfx.VfxModel
  Economy: Economy.EconomyModel
  Camera: Camera.CameraModel

  /// The last GameTime the host handed to update — forced into the
  /// frame so the draw side (shader-driven auras) animates on the
  /// sim's clock instead of a backend-specific one. Presentation-only
  /// state; the sim itself never reads it.
  mutable LastTime: GameTime

  /// Tower kind the next placement uses — a CVal because the
  /// PlacementPreview projection joins on it (cold path writes).
  SelectedTower: cval<TowerDef>

  /// Hover cell CVal — UI state written by the host; the state
  /// projections (PlacementPreview/RangeRing) join on it.
  HoverCell: cval<struct (int * int) voption>

  /// Frozen by the host; the sim writes nothing while paused.
  Paused: cval<bool>

  Projections: Projections

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

/// The state cell — the composition root and the frame force read the
/// CURRENT state through this holder. A restart swaps the value in place;
/// the frame force re-binds to the fresh state on the next force, so
/// the runner, the window, and the subscriptions all survive the swap.
[<Sealed>]
type StateCell(value: State) =
  member val Value = value with get, set

module State =

  let init(cfg: WorldConfig) : State =
    let map = MapModel.create cfg
    let enemies = Enemies.Enemies.init()
    let spawning = Spawning.Spawning.init cfg.Seed
    let waves = Waves.Waves.init()
    let towers = Towers.Towers.init()
    let projectiles = Projectiles.Projectiles.init()
    let vfx = Vfx.Vfx.init()
    let economy = Economy.Economy.init cfg

    let camera =
      Camera.Camera.init(
        Vector2(
          float32(cfg.GridCols * Tiles.TileSize),
          float32(cfg.GridRows * Tiles.TileSize)
        )
      )

    let selectedTower = CVal.create TowerDefs.arrow
    let hoverCell = CVal.create ValueNone
    let paused = CVal.create false

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
      Vfx = vfx
      Economy = economy
      Camera = camera
      LastTime = {
        TotalTime = TimeSpan.Zero
        ElapsedGameTime = TimeSpan.Zero
      }
      SelectedTower = selectedTower
      HoverCell = hoverCell
      Paused = paused
      Projections = projections
      AliveCount = enemies.Alive |> AMap.count
      TowerCount = towers.Statics |> AMap.count
      ProjectileCount = projections.Homing |> AMap.count
      Diag = WorldDiag()
    }

  /// The grid's uniform cell size (world geometry — the sim and the
  /// frame force both use it).
  let inline cellSize(state: State) =
    Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize)
