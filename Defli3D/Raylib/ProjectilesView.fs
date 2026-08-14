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
// frame's Homing projection snapshot. The sim integrates 3D homing:
// each row carries the shell's current flight height (HomingView.Y)
// and the target hull-center Y it homes on (HomingView.TargetY —
// EnemyLayout.impactY at fire time). Each shot is a translation to
// (pos.X, v.Y, pos.Y) with the model's +Z forward oriented along the
// homing direction: yaw = atan2(dx, dz) toward the target in XZ
// (TargetPos − Pos — the target's live position, or the last
// recorded one after it despawns), and a view-edge pitch (about X,
// applied BEFORE yaw) tipping the nose up/down toward the target's
// Y (sign convention in the loop below).
// ─────────────────────────────────────────────────────────────

module ProjectilesView =

  /// Visual scale of ammo shells (1 = model size). 0.5 keeps the
  /// shots readable but slim next to the scaled towers — tune to
  /// taste.
  let projectileScale = 0.5f

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
      let xzLen = d.Length()

      let yaw =
        if d.LengthSquared() > 1e-6f then
          MathF.Atan2(d.X, d.Y)
        else
          0f

      // Pitch sign (raylib matches XNA here): Raymath matrices upload
      // as GLSL column-major and the pipeline's shaders apply
      // mat * vec, so MatrixRotateX θ maps +Z → (0, −sinθ, cosθ) — a
      // POSITIVE θ tips the +Z nose DOWN (−Y), exactly like XNA's
      // CreateRotationX. pitch > 0 means the target is ABOVE the
      // shell, so negate: a descending shot (pitch < 0) noses down at
      // its target, an ascending one noses up. Skipped (yaw-only)
      // when the target is dead ahead in XZ.
      let pitch =
        if xzLen < 1e-6f then
          0f
        else
          MathF.Atan2(v.TargetY - v.Y, xzLen)

      InstanceScratch.add
        v.Model.Name
        (Raymath.MatrixMultiply(
          Raymath.MatrixMultiply(
            Raymath.MatrixMultiply(
              Raymath.MatrixScale(
                projectileScale,
                projectileScale,
                projectileScale
              ),
              Raymath.MatrixRotateX(-pitch)
            ),
            Raymath.MatrixRotateY(yaw)
          ),
          Raymath.MatrixTranslate(v.Pos.X, v.Y, v.Pos.Y)
        ))

    InstanceScratch.draw buffer
