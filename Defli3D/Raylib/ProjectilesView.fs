namespace Defli3D.Raylib

open System
open System.Collections.Generic
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Raylib_cs
open Defli3D.State
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// ProjectilesView — one .instanced draw per ammo model from the
// frame's Homing projection snapshot. The ballistic sim integrates
// the flight: each row carries the shot's current height (Y — the
// arc's lerp + parabola) and its XZ flight direction (Dir). The
// view orients the model's +Z forward along Dir (yaw) and scales
// it by the weapon's ProjectileScale (volley arrows/bullets are
// small, piercer arrows large).
// ─────────────────────────────────────────────────────────────

module ProjectilesView =

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
      let yaw =
        if v.Dir.LengthSquared() > 1e-6f then
          MathF.Atan2(v.Dir.X, v.Dir.Y)
        else
          0f

      InstanceScratch.add
        v.Model.Name
        (Raymath.MatrixMultiply(
          Raymath.MatrixMultiply(
            Raymath.MatrixScale(v.Scale, v.Scale, v.Scale),
            Raymath.MatrixRotateY(yaw)
          ),
          Raymath.MatrixTranslate(v.Pos.X, v.Y, v.Pos.Y)
        ))

    InstanceScratch.draw buffer
