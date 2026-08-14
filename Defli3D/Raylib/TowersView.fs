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
// Body: the kit's pre-cut STACK parts (TowerLayout.stackFor —
// bottom-a + middles + top-a/b/c, NO roof), one instanced draw per
// piece, laid bottom→top on the tile top (TowerLayout.baseY). The
// scale is the SHARED TowerLayout.towerScale (the sim's muzzle math
// depends on it) and the pieces are bottom-anchored (min-Y = 0), so
// a running unscaled height accumulator gives each piece's resting
// Y. The weapon mounts FLUSH on the top piece at
// TowerLayout.weaponY and rotates around its own origin.
// ─────────────────────────────────────────────────────────────

module TowersView =

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

      // Body — the kit's stack parts (TowerLayout.stackFor,
      // bottom→top, NO roof), one instanced draw per piece, each
      // scaled by the shared TowerLayout.towerScale. The pieces are
      // bottom-anchored (min-Y = 0), so a running unscaled height
      // accumulator gives every piece's resting Y on the tile top
      // (TowerLayout.baseY). No rotation — the parts are radially
      // symmetric.
      let scale = TowerLayout.towerScale

      let mutable acc = 0f

      for piece in TowerLayout.stackFor def level do
        let pieceY = TowerLayout.baseY + acc * scale
        acc <- acc + piece.SizeY

        InstanceScratch.add
          piece.Name
          (Raymath.MatrixMultiply(
            Raymath.MatrixScale(scale, scale, scale),
            Raymath.MatrixTranslate(cx, pieceY, cy)
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

      // Weapon on the tower top — mounted FLUSH on the top piece
      // (TowerLayout.weaponY = baseY + scaled stack height), scaled
      // with the body; rotate at its own origin, then place.
      let weaponInfo = def.WeaponModel
      let weaponY = TowerLayout.weaponY def level

      InstanceScratch.add
        weaponInfo.Name
        (Raymath.MatrixMultiply(
          Raymath.MatrixMultiply(
            Raymath.MatrixScale(scale, scale, scale),
            Raymath.MatrixRotateY(yaw)
          ),
          Raymath.MatrixTranslate(cx, weaponY, cy)
        ))

    InstanceScratch.draw buffer
