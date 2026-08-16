namespace Defli3D.State

open Mibo.Adaptive
open Defli3D
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// Placement — the composition-root-owned build DECISIONS (cold
// path). The rules span three systems (map tile, tower occupancy,
// economy gold, level cap), so they live beside Projections at the
// root — not inside any one system. Pure functions over read-only
// inputs return an accepted plan as data; Application translates a
// plan into system handles (Towers/Economy/Vfx). The cold-path
// analog of what Projections is for the render side.
// ─────────────────────────────────────────────────────────────

module Placement =

  /// An accepted build: place this def at this cell for this cost.
  [<Struct>]
  type PlacePlan = {
    Cell: struct (int * int)
    Def: TowerDef
    Cost: int
  }

  /// An accepted upgrade: raise this tower one level for this cost.
  [<Struct>]
  type UpgradePlan = { Tower: int<TowerId>; Cost: int }

  /// Can the player place `def` at `cell`? Buildable tile, free
  /// cell, enough gold.
  let place
    (map: MapModel)
    (towers: Towers.TowersModel)
    (gold: int)
    (def: TowerDef)
    (cell: struct (int * int))
    : PlacePlan voption =
    let struct (cx, cy) = cell

    let tileOk = MapModel.isBuildable cx cy map

    let occupied = ValueOption.isSome(towers.CellIndex |> CMap.tryGetValue cell)

    if tileOk && not occupied && gold >= def.Cost then
      ValueSome {
        Cell = cell
        Def = def
        Cost = def.Cost
      }
    else
      ValueNone

  /// Can the tower under `cell` upgrade? Under the level cap and
  /// affordable. No tower under the cell rejects.
  let upgrade
    (towers: Towers.TowersModel)
    (gold: int)
    (cell: struct (int * int))
    : UpgradePlan voption =
    match towers.CellIndex |> CMap.tryGetValue cell with
    | ValueNone -> ValueNone
    | ValueSome tid ->
      let level =
        towers.Levels |> CMap.tryGetValue tid |> ValueOption.defaultValue 1

      let def =
        towers.Statics
        |> CMap.tryGetValue tid
        |> ValueOption.map(fun s -> s.Def)
        |> ValueOption.defaultValue TowerDefs.sentry

      if level >= def.MaxLevel || gold < def.UpgradeCost then
        ValueNone
      else
        ValueSome { Tower = tid; Cost = def.UpgradeCost }
