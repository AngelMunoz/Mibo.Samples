namespace Defli3D.Raylib

open System
open System.Collections.Generic
open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Raylib_cs
open Defli3D.State
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// ProjectilesView — one .instanced draw per ammo model from the
// frame's Homing projection snapshot. Each shot's transform is a
// translation to (pos.X, 0.4, pos.Y) (the flight height — the sim
// integrates XZ only) with the model's +Z forward yawed toward the
// homing direction (TargetPos − Pos — the target's live position,
// or the last recorded one after it despawns).
//
// Pitch is 0 by design: the homing vector is a ground-plane Vector2
// and the flight stays level at y = 0.4, so a pitched arrow would
// point at the ground it never reaches — yaw-only, documented.
// ─────────────────────────────────────────────────────────────

module ProjectilesView =

  /// Visual scale of ammo shells (1 = model size). 0.7 keeps the
  /// shots readable but slim next to the scaled towers — tune to
  /// taste.
  let projectileScale = 0.7f

  /// Ammo shells go through the shared InstanceScratch (grouped by
  /// model name): reset → fill → draw per frame, zero allocation
  /// once warm.
  let view
    (ctx: GameContext)
    (homing: IReadOnlyDictionary<int<ProjectileId>, HomingView>)
    (buffer: RenderBuffer3D)
    =
    InstanceScratch.reset()

    for KeyValueV(_, v) in homing do
      let d = v.TargetPos - v.Pos

      let yaw =
        if d.LengthSquared() > 1e-6f then
          MathF.Atan2(d.X, d.Y)
        else
          0f

      InstanceScratch.add
        v.Model.Name
        (Raymath.MatrixMultiply(
          Raymath.MatrixMultiply(
            Raymath.MatrixScale(
              projectileScale,
              projectileScale,
              projectileScale
            ),
            Raymath.MatrixRotateY(yaw)
          ),
          Raymath.MatrixTranslate(v.Pos.X, 0.4f, v.Pos.Y)
        ))

    InstanceScratch.draw buffer
