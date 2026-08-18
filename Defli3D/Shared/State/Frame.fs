namespace Defli3D.State

open System.Collections.Generic
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Defli3D
open Defli3D.State.Systems
open Defli3D.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// Frame — the Force phase. Everything the renderer needs, resolved
// and packed once per Step into the RenderFrame struct; the
// renderer reads the struct — O(1), no graph access at draw time.
// ─────────────────────────────────────────────────────────────

module Frame =

  /// Everything the renderer needs, resolved and packed once per Step.
  /// The dictionaries are transient views — valid until the next
  /// Step's writes — so the renderer must read the frame immediately
  /// after Step, before the state is stepped again.
  [<Struct>]
  type RenderFrame = {
    /// Alive enemies (the Alive projection). Draw-side.
    Alive: IReadOnlyDictionary<int<EnemyId>, EnemyView>
    /// Enemy defs (names, archetypes, hull models). Draw-side.
    Defs: IReadOnlyDictionary<int<EnemyId>, EnemyDef>
    /// Tower statics (cells, defs) and levels. Draw-side.
    TowerStatics: IReadOnlyDictionary<int<TowerId>, TowerStatic>
    TowerLevels: IReadOnlyDictionary<int<TowerId>, int>
    /// The sim's current aim point per tower (TowerAim projection) —
    /// the rotating chassis views track it (decks, keep-b, guns).
    TowerAim: IReadOnlyDictionary<int<TowerId>, Vector2 voption>
    /// In-flight projectiles (the Homing projection). Draw-side.
    Projectiles: IReadOnlyDictionary<int<ProjectileId>, HomingView>
    /// Live ground zones (slow + DoT fields). Draw-side.
    Zones: IReadOnlyDictionary<int<ZoneId>, ZoneRow>
    /// HUD scalars.
    Gold: int
    Lives: int
    Banner: string
    GameOver: bool
    /// Sim narrative.
    WaveNumber: int
    WaveActive: bool
    SpawnQueueLength: int
    TowerCount: int
    EnemyCount: int
    ProjectileCount: int
    /// UI state written by the host (hover cell / selected tower) —
    /// the frame carries their current values so draw reads the
    /// struct.
    HoverCell: struct (int * int) voption
    SelectedTower: TowerDef
    /// Hover overlays — the projections the draw side consumes.
    PlacementPreview: PlacementStatus
    RangeRing: TowerDef voption
    /// Vfx pools + the view-memoized texture handles (non-adaptive
    /// state; draw reads pool particles).
    Vfx: Vfx.VfxModel
    /// The map — static world data (terrain/path/decorations/
    /// waypoints).
    Map: MapModel
    /// The camera — a backend-neutral orbit-camera snapshot at force
    /// time; the frontend builds its native camera at the edge.
    Camera: CameraState
    /// The sim's clock root, read at force time — written by
    /// Application.update every step (paused included), so the draw
    /// side (hover bob, idle spins) rides the sim's clock, not a
    /// backend-specific one.
    Time: GameTime
  }

  /// Forcing the frame: resolve every output projection once, pack the
  /// struct. After this, drawing is plain struct reads — O(1), no
  /// graph access. `force` is a pure State → RenderFrame mapping: it
  /// follows the state it is handed at force time, and the count nodes
  /// live on the State record (created at init), so a restart (in-place
  /// reset) keeps the force working with zero per-step allocation. The
  /// clock is part of the state (the Clock root) — the force needs
  /// nothing else.
  let inline force
    ([<InlineIfLambda>] getState: unit -> State)
    : unit -> RenderFrame =
    fun () ->
      let state = getState()

      {
        Alive = state.Enemies.Alive |> AMap.getValue
        Defs = state.Enemies.Defs |> AMap.getValue
        TowerStatics = state.Towers.Statics |> AMap.getValue
        TowerLevels = state.Towers.Levels |> AMap.getValue
        TowerAim = state.Projections.TowerAim |> AMap.getValue
        Projectiles = state.Projections.Homing |> AMap.getValue
        Zones = state.Zones.Rows |> AMap.getValue
        Gold = state.Economy.Gold |> AVal.getValue
        Lives = state.Economy.Lives |> AVal.getValue
        Banner = state.Waves.Banner |> AVal.getValue
        GameOver = state.Economy.GameOver |> AVal.getValue
        WaveNumber = state.Waves.WaveNumber |> AVal.getValue
        WaveActive = state.Waves.WaveActive |> AVal.getValue
        SpawnQueueLength = state.Spawning.Queue.Count
        TowerCount = state.TowerCount |> AVal.getValue
        EnemyCount = state.AliveCount |> AVal.getValue
        ProjectileCount = state.ProjectileCount |> AVal.getValue
        HoverCell = state.HoverCell |> AVal.getValue
        SelectedTower = state.SelectedTower |> AVal.getValue
        PlacementPreview = state.Projections.PlacementPreview |> AVal.getValue
        RangeRing = state.Projections.RangeRing |> AVal.getValue
        Vfx = state.Vfx
        Map = state.Map
        Camera = state.Camera.State
        Time = state.Clock |> AVal.getValue
      }
