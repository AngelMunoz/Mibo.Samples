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
// frame's Homing projection snapshot. The ballistic sim integrates
// the flight: each row carries the shot's current height (Y — the
// arc's lerp + parabola) and its XZ flight direction (Dir). The
// view orients the model's +Z forward along Dir (yaw) and scales it
// by the weapon's ProjectileScale (volley arrows/bullets are small,
// piercer arrows large).
// ─────────────────────────────────────────────────────────────

/// The projectiles presenter: owns its instance groups — constructed
/// once in Program.fs, no module-level mutable state.
[<Sealed>]
type ProjectilesView() =

  let groups = InstanceGroups()

  /// Ammo shells, grouped by model path: one instanced draw per ammo
  /// model, zero allocation once warm.
  member _.View
    (
      ctx: GameContext,
      homing: IReadOnlyDictionary<int<ProjectileId>, HomingView>,
      buffer: RenderBuffer3D
    ) =
    groups.Clear()

    for KeyValueV(_, v) in homing do
      let yaw =
        if v.Dir.LengthSquared() < 1e-6f then
          0f
        else
          MathF.Atan2(v.Dir.X, v.Dir.Y)

      groups.Add(
        v.Model.Path,
        Matrix.CreateScale v.Scale
        * Matrix.CreateRotationY yaw
        * Matrix.CreateTranslation(v.Pos.X, v.Y, v.Pos.Y)
      )

    groups.Draw buffer
