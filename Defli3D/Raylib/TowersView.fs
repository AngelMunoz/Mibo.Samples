namespace Defli3D.Raylib

open System
open System.Collections.Generic
open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Raylib_cs
open Defli3D.State
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// TowersView — tower bodies + weapons from the frame's
// TowerStatics/TowerLevels snapshots, all instanced (one draw per
// model, not per tower). The frame does NOT carry the tower's
// live target (only statics + levels are packed), so aiming is
// approximated at the view edge: the weapon yaws toward the NEAREST
// alive enemy within the tower's EFFECTIVE range (the sim's target
// policy picks by First/Strongest/… — nearest is a stable, cheap
// approximation that reads correctly); with no enemy in range the
// weapon idles with a slow rotation. Documented choice — the
// projectile Homing view could drive a muzzle but not the turret.
//
// Body: ONE pre-built kit model per tower, picked by def kind +
// level like the MonoGame backend — tower-round-build-A..F for
// arrow/frost, tower-square-build-A..F for cannon (1 → A … 6+ → F;
// the defs carry no body field). The build sits at the cell center
// with its base at y = 0.2 — the tile top (kit models are
// bottom-anchored, min-Y = 0) — scaled by towerScale. The weapon
// mounts at the tile top + scaled body height × 0.95 and rotates
// around its own origin.
// ─────────────────────────────────────────────────────────────

module TowersView =

  /// Visual scale of tower bodies + weapons (1 = model size on a
  /// 1-unit tile). 0.8 keeps the builds from crowding the tiles —
  /// tune to taste; the weapon mount height follows.
  let towerScale = 0.8f

  /// The body model for a tower kind + level — the level's complete
  /// build model (round family for arrow/frost, square for cannon;
  /// 1 → A … 6+ → F, mirroring the MonoGame backend).
  let private bodyModel (def: TowerDef) (level: int) : ModelInfo =
    let idx = min (max level 1) 6

    let round =
      match idx with
      | 1 -> Models.towerRoundBuildA
      | 2 -> Models.towerRoundBuildB
      | 3 -> Models.towerRoundBuildC
      | 4 -> Models.towerRoundBuildD
      | 5 -> Models.towerRoundBuildE
      | _ -> Models.towerRoundBuildF

    let square =
      match idx with
      | 1 -> Models.towerSquareBuildA
      | 2 -> Models.towerSquareBuildB
      | 3 -> Models.towerSquareBuildC
      | 4 -> Models.towerSquareBuildD
      | 5 -> Models.towerSquareBuildE
      | _ -> Models.towerSquareBuildF

    if def.Key = TowerDefs.cannon.Key then square else round

  /// The world-space height of a tower's body top above y = 0 (tile
  /// top 0.2 + the scaled body height) — the HUD's Lv-tag anchor.
  let towerTop (def: TowerDef) (level: int) : float32 =
    0.2f + (bodyModel def level).SizeY * towerScale

  /// Reused scratch: the frame's enemy positions (XZ), refilled once
  /// per frame so the per-tower aim scan never re-enumerates.
  let private enemyPositions = ResizeArray<Vector2>()

  let view
    (ctx: GameContext)
    (statics: IReadOnlyDictionary<int<TowerId>, TowerStatic>)
    (levels: IReadOnlyDictionary<int<TowerId>, int>)
    (alive: IReadOnlyDictionary<int<EnemyId>, EnemyView>)
    (buffer: RenderBuffer3D)
    =
    let time = float32(Raylib.GetTime())
    InstanceScratch.reset()

    // One pass over the alive view fills the aim scratch.
    enemyPositions.Clear()

    for KeyValueV(_, v) in alive do
      enemyPositions.Add v.Pos

    for KeyValueV(tid, s) in statics do
      let level =
        levels |> ReadOnlyDict.tryGetValue tid |> ValueOption.defaultValue 1

      let def = s.Def
      let center = Cells.center s.Cell (Vector2.One)
      let cx = center.X
      let cy = center.Y

      // Body — the level's complete build model, no rotation (the
      // build parts are radially symmetric), scaled by towerScale,
      // base at y = 0.2 (the tile top).
      let body = bodyModel def level

      InstanceScratch.add
        body.Name
        (Raymath.MatrixMultiply(
          Raymath.MatrixScale(towerScale, towerScale, towerScale),
          Raymath.MatrixTranslate(cx, 0.2f, cy)
        ))

      // Weapon — yaw toward the nearest in-range enemy (effective
      // range incl. upgrades), idle slow rotation otherwise.
      let effective = TowerDefs.effectiveDef def level
      let range = float32 effective.Range
      let rangeSq = range * range

      let mutable best = Vector2.Zero
      let mutable bestSq = rangeSq + 1f

      for i = 0 to enemyPositions.Count - 1 do
        let dSq = Vector2.DistanceSquared(enemyPositions[i], center)

        if dSq <= rangeSq && dSq < bestSq then
          bestSq <- dSq
          best <- enemyPositions[i]

      let yaw =
        if bestSq <= rangeSq then
          let d = best - center
          MathF.Atan2(d.X, d.Y)
        else
          // Idle: slow sweep, per-tower phase so they don't sync.
          time * 0.5f + float32(int(tid % 7<TowerId>))

      // Weapon on the tower top — mounted at the tile top + the
      // scaled body height × 0.95 (the kit's mount fraction), scaled
      // with the body; rotate at its own origin, then place.
      let weaponInfo = def.WeaponModel
      let weaponY = 0.2f + body.SizeY * 0.95f * towerScale

      InstanceScratch.add
        weaponInfo.Name
        (Raymath.MatrixMultiply(
          Raymath.MatrixMultiply(
            Raymath.MatrixScale(towerScale, towerScale, towerScale),
            Raymath.MatrixRotateY(yaw)
          ),
          Raymath.MatrixTranslate(cx, weaponY, cy)
        ))

    InstanceScratch.draw buffer
