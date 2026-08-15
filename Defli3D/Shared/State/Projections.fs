namespace Defli3D.State

open System.Numerics
open Mibo.Adaptive
open Defli3D
open Mibo.Layout
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// State-owned CROSS-subsystem projections — joins/filters that
// touch two systems' maps. Sub-systems own projections derived
// purely from their own maps (see each system file).
//
//   Homing (#3)          — Projectiles.Rows mapped to view rows
//                          (the ballistic rework removed the live
//                          target join: dumbfire shots fly a fixed
//                          line, seeking rows chase inside the sim)
//   TowerAim             — Towers.Runtimes.Aim per tower: the sim's
//                          CURRENT target position, consumed by the
//                          rotating chassis views (decks, keep-b,
//                          gun mounts) so they track the real
//                          target, not a view-side guess
//   Suppression (#12)    — Towers.Statics × Enemies.BossPositions
//                          (the SPATIAL join: per-tower filter over
//                          boss positions — Phase 6 boss aura)
//   RangeRing (#10)      — hover cell × Towers.CellIndex/Statics
//                          (AVal.bind UI-state join)
//   PlacementPreview (#5)— hover cell × Towers.CellIndex ×
//                          Economy.Gold × SelectedTower (per-hover
//                          map3 fan-in; the full-tile filterA variant
//                          is exactly the wide fan-out the join
//                          assessment flagged — the per-hover fan-in
//                          gives the same UX with a shallow graph)
// ─────────────────────────────────────────────────────────────

[<Sealed>]
type Projections
  (
    enemies: Enemies.EnemiesModel,
    towers: Towers.TowersModel,
    projectiles: Projectiles.ProjectilesModel,
    economy: Economy.EconomyModel,
    buildable: CellGrid2D<MapTile>,
    hover: aval<struct (int * int) voption>,
    selected: aval<TowerDef>
  ) =

  /// #3 Homing — the in-flight view rows: position, flight height,
  /// flight direction (the views orient the model along it) and the
  /// downscaled model. A plain row map: the ballistic flight is
  /// self-contained in the row (dumbfire line or sim-side chase).
  member val Homing: amap<int<ProjectileId>, HomingView> =
    projectiles.Rows
    |> AMap.map(fun _ (row: ProjectileRow) ->
      Telemetry.homingJoin <- Telemetry.homingJoin + 1

      {
        Pos = row.Pos
        Y = row.Y
        Dir = row.Dir
        Model = row.Model
        Scale = row.Scale
      })

  /// TowerAim — per tower, the sim's current target position
  /// (Runtimes.Aim, written by Towers.tick). The rotating chassis
  /// views read it through the frame; ValueNone = idle (no target).
  member val TowerAim: amap<int<TowerId>, Vector2 voption> =
    towers.Runtimes |> AMap.map(fun _ (r: TowerRuntime) -> r.Aim)

  /// #12 Suppression (Phase 6) — per tower, is a live boss within
  /// BossAura.Radius of its cell? → the fire-rate factor (1 = free,
  /// Factor = suppressed). The SPATIAL-join stress case: boss
  /// positions change every frame, so every tower's filter/count node
  /// re-scans the boss map per frame — O(towers × bosses) of graph
  /// work per frame. Consumed as a DIRECT VALUE by Towers.tick (the
  /// sim update passes the transient view; nothing is written back into
  /// a changeable map).
  member val Suppression: amap<int<TowerId>, float32> =
    towers.Statics
    |> AMap.mapA(fun _ s ->
      // 1 cell = 1 world unit — the uniform cell size is (1, 1).
      let center = Cells.center s.Cell (Vector2(1f, 1f))

      enemies.BossPositions
      |> AMap.filter(fun _ bossPos ->
        Vector2.Distance(bossPos, center) <= BossAura.Radius)
      |> AMap.count
      |> AVal.map(fun n ->
        Telemetry.suppression <- Telemetry.suppression + 1
        if n > 0 then BossAura.Factor else 1f))

  /// #10 RangeRing — hovered own tower → its EFFECTIVE def (the view
  /// draws the range circle). The chain composes derived-on-derived:
  /// hover × CellIndex × (Statics × Levels) — the upgrade showcase.
  member val RangeRing: aval<TowerDef voption> =
    hover
    |> AVal.bind(fun cell ->
      match cell with
      | ValueNone -> AVal.constant ValueNone
      | ValueSome c ->
        Telemetry.rangeRing <- Telemetry.rangeRing + 1
        towers.CellIndex |> AMap.tryFind c)
    |> AVal.bind(fun tid ->
      match tid with
      | ValueNone -> AVal.constant ValueNone
      | ValueSome tid -> towers.EffectiveDef |> AMap.tryFind tid)

  /// #5 PlacementPreview — the hovered cell's build status: blocked
  /// (path/occupied/out of grid), affordable, or too expensive.
  /// map2 fan-in over Gold; re-derives only when hover or gold moves.
  member val PlacementPreview: aval<PlacementStatus> =
    hover
    |> AVal.bind(fun cell ->
      match cell with
      | ValueNone -> AVal.constant PlacementStatus.Hidden
      | ValueSome struct (x, y) ->
        // The Buildable layer row decides (road cells were stamped
        // over with the non-buildable path tile; out of grid = absent).
        let buildableOk =
          buildable |> CellGrid2D.get x y |> ValueOption.exists _.Buildable

        if not buildableOk then
          AVal.constant PlacementStatus.Blocked
        else
          let cellKey = struct (x, y)

          towers.CellIndex
          |> AMap.tryFind cellKey
          |> AVal.map3
            (fun gold def occupied ->
              Telemetry.placementPreview <- Telemetry.placementPreview + 1

              if ValueOption.isSome occupied then PlacementStatus.Blocked
              elif gold >= def.Cost then PlacementStatus.Affordable
              else PlacementStatus.TooExpensive)
            economy.Gold
            selected)
