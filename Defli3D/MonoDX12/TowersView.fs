namespace Defli3D.MonoGame

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Defli3D
open Defli3D.State
open Defli3D.State.Frame
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// TowersView — tower bodies and weapons from the frame's
// TowerStatics/TowerLevels snapshots. The body is the level's
// pre-cut kit STACK (TowerLayout.stackFor — bottom-a + middles +
// top-a/b/c, NO roof), composed bottom→top with a running height
// accumulator so each piece rests on the one below; the weapon
// mounts flush on the top piece (TowerLayout.weaponY).
//
// Note on aiming: the frame carries no tower runtime (TowerRuntime
// with the live Target is sim-internal, not in the RenderFrame), so
// aiming is approximated at the view edge exactly like the raylib
// backend: the weapon yaws toward the NEAREST alive enemy within
// the tower's EFFECTIVE range (effectiveDef incl. upgrades); with
// no enemy in range it keeps a slow idle spin. Documented choice —
// the projectile Homing view could drive a muzzle but not the
// turret.
// ─────────────────────────────────────────────────────────────

module TowersView =

  /// Tower bodies + weapons go through the shared InstanceScratch:
  /// reset → fill → draw per frame, zero allocation once warm.
  /// Visual scale + body stack come from TowerLayout (shared with
  /// the sim's muzzle math).
  /// Reused scratch: the frame's enemy positions (XZ), refilled once
  /// per frame so the per-tower aim scan never re-enumerates.
  let private enemyPositions = ResizeArray<System.Numerics.Vector2>()

  /// Tower bodies and weapons at their cell centers.
  let view (ctx: GameContext) (frame: RenderFrame) (buffer: RenderBuffer3D) =
    let time = Time.now()
    InstanceScratch.reset()

    // One pass over the alive view fills the aim scratch.
    enemyPositions.Clear()

    for KeyValueV(_, v) in frame.Alive do
      enemyPositions.Add v.Pos

    for KeyValueV(tid, s) in frame.TowerStatics do
      let level =
        frame.TowerLevels
        |> ReadOnlyDict.tryGetValue tid
        |> ValueOption.defaultValue 1

      let struct (cx, cy) = s.Cell
      let x = float32 cx + 0.5f
      let z = float32 cy + 0.5f
      let center = System.Numerics.Vector2(x, z)

      // Body: the level's stack, pieces bottom→top with a running
      // UNSCALED height accumulator — each piece rests on the one
      // below, base at y = 0.2 (the tile top). No rotation (the
      // pieces are radially symmetric).
      let mutable acc = 0f

      for piece in TowerLayout.stackFor s.Def level do
        let pieceY = TowerLayout.baseY + acc * TowerLayout.towerScale

        InstanceScratch.add
          piece.Path
          (Matrix.CreateScale TowerLayout.towerScale
           * Matrix.CreateTranslation(x, pieceY, z))

        acc <- acc + piece.SizeY

      // Weapon — yaw toward the nearest in-range enemy (effective
      // range incl. upgrades), idle slow spin otherwise (see the
      // module header).
      let effective = TowerDefs.effectiveDef s.Def level
      let range = float32 effective.Range
      let rangeSq = range * range

      let mutable best = System.Numerics.Vector2.Zero
      let mutable bestSq = rangeSq + 1f

      for i = 0 to enemyPositions.Count - 1 do
        let dSq =
          System.Numerics.Vector2.DistanceSquared(enemyPositions[i], center)

        if dSq <= rangeSq && dSq < bestSq then
          bestSq <- dSq
          best <- enemyPositions[i]

      let yaw =
        if bestSq <= rangeSq then
          let d = best - center
          MathF.Atan2(d.X, d.Y)
        else
          // Idle: slow sweep, per-tower phase so they don't sync.
          time * 0.6f + float32(int tid % 5) * 1.2f

      // Weapon: mounted flush on the top piece (TowerLayout.weaponY),
      // scaled with the body; yaw at its own origin, then placed.
      let weaponY = TowerLayout.weaponY s.Def level

      InstanceScratch.add
        s.Def.WeaponModel.Path
        (Matrix.CreateScale TowerLayout.towerScale
         * Matrix.CreateRotationY yaw
         * Matrix.CreateTranslation(x, weaponY, z))

    InstanceScratch.draw buffer
