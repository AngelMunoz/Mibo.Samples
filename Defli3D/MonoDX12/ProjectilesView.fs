namespace Defli3D.MonoGame

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Defli3D
open Defli3D.State
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// ProjectilesView — one instanced draw per ammo model from the
// frame's Homing projection snapshot. Each shell is oriented along
// the homing direction (yaw = atan2(dx, dz) aligns the model's +Z
// axis — the ammo models' long axis — with the target direction).
// The sim flies shells on the XZ plane, so there is no pitch; the
// shells cruise at y = 0.4 (mid-air above the tiles).
// ─────────────────────────────────────────────────────────────

module ProjectilesView =

  /// Visual scale of ammo shells (1 = model size). 0.7 keeps the
  /// shots readable but slim next to the scaled towers — tune to
  /// taste.
  let projectileScale = 0.7f

  /// Ammo shells go through the shared InstanceScratch (grouped by
  /// model path): reset → fill → draw per frame, zero allocation
  /// once warm.
  /// The shells, oriented along the homing direction.
  let view
    (ctx: GameContext)
    (homing: IReadOnlyDictionary<int<ProjectileId>, HomingView>)
    (buffer: RenderBuffer3D)
    =
    InstanceScratch.reset()

    for KeyValueV(_, v) in homing do
      let d = v.TargetPos - v.Pos

      let yaw =
        if d.LengthSquared() < 1e-6f then
          0f
        else
          MathF.Atan2(d.X, d.Y)

      InstanceScratch.add
        v.Model.Path
        (Matrix.CreateScale projectileScale
         * Matrix.CreateRotationY yaw
         * Matrix.CreateTranslation(v.Pos.X, 0.4f, v.Pos.Y))

    InstanceScratch.draw buffer
