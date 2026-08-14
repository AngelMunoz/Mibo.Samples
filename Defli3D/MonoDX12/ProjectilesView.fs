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
// frame's Homing projection snapshot. The sim integrates 3D homing:
// each row carries the shell's current flight height (HomingView.Y)
// and the target hull-center Y (HomingView.TargetY). Each shell is
// oriented along the homing direction: yaw = atan2(dx, dz) aligns
// the model's +Z axis — the ammo models' long axis — with the
// target in XZ, and a view-edge pitch (about X, applied BEFORE yaw)
// tips the nose up/down toward the target's Y (sign convention in
// the loop below).
// ─────────────────────────────────────────────────────────────

module ProjectilesView =

  /// Visual scale of ammo shells (1 = model size). 0.5 keeps the
  /// shots readable but slim next to the scaled towers — tune to
  /// taste.
  let projectileScale = 0.5f

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
      let xzLen = d.Length()

      let yaw =
        if d.LengthSquared() < 1e-6f then
          0f
        else
          MathF.Atan2(d.X, d.Y)

      // Pitch sign (XNA row-vector convention):
      // Vector3.Transform(UnitZ, CreateRotationX θ) = (0, −sinθ, cosθ)
      // — a POSITIVE θ tips the +Z nose DOWN (−Y). pitch > 0 means
      // the target is ABOVE the shell, so negate: a descending shot
      // (pitch < 0) noses down at its target, an ascending one noses
      // up. Skipped (yaw-only) when the target is dead ahead in XZ.
      let pitch =
        if xzLen < 1e-6f then
          0f
        else
          MathF.Atan2(v.TargetY - v.Y, xzLen)

      InstanceScratch.add
        v.Model.Path
        (Matrix.CreateScale projectileScale
         * Matrix.CreateRotationX(-pitch)
         * Matrix.CreateRotationY yaw
         * Matrix.CreateTranslation(v.Pos.X, v.Y, v.Pos.Y))

    InstanceScratch.draw buffer
