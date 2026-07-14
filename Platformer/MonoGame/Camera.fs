module Platformer.MonoGame.Camera

open System
open Microsoft.Xna.Framework
open Mibo.Elmish

/// Read-only view of the state the camera needs to frame the scene.
/// The router (Systems.fs) builds this from sub-system models — the camera
/// never reaches into another sub-system's model directly.
[<Struct>]
type CameraQuery = { PlayerPosition: Vector2 }

/// Deadzone half-extents (world units). The camera holds still while the
/// framed point stays within this box around its current center; it only
/// follows (by the overshoot amount) once an edge is crossed.
[<Literal>]
let deadzoneHalfWidth = 150.0f

[<Literal>]
let deadzoneHalfHeight = 80.0f

/// Follow rate (1/s) for dt-aware smoothing. Higher = snappier re-centering.
/// ~6.0 matches the prior 0.1-per-frame feel at 60 fps.
[<Literal>]
let followRate = 6.0f

/// World point the camera should center on, derived from the query.
/// Keeps the framing target within sane world bounds on the Y axis.
let target(query: CameraQuery) : Vector2 =
  let clampedY = MathF.Max(-500.0f, MathF.Min(query.PlayerPosition.Y, 2000.0f))
  Vector2(query.PlayerPosition.X, clampedY)

/// Deadzone-adjusted point the camera should move toward. While the framed
/// point is inside the deadzone box around the current center, this returns
/// the current center (camera holds still); once it overshoots an edge, it
/// tracks by the overshoot amount so the player stays at the edge.
let desiredTarget (current: Vector2) (framed: Vector2) : Vector2 =
  let dx = framed.X - current.X
  let dy = framed.Y - current.Y

  let tx =
    if dx > deadzoneHalfWidth then
      current.X + (dx - deadzoneHalfWidth)
    elif dx < -deadzoneHalfWidth then
      current.X + (dx + deadzoneHalfWidth)
    else
      current.X

  let ty =
    if dy > deadzoneHalfHeight then
      current.Y + (dy - deadzoneHalfHeight)
    elif dy < -deadzoneHalfHeight then
      current.Y + (dy + deadzoneHalfHeight)
    else
      current.Y

  Vector2(tx, ty)

/// Smoothly re-center the camera toward the deadzone-adjusted target.
let update (dt: float32) (query: CameraQuery) (camera: Camera2D) : Camera2D =
  let goal = desiredTarget camera.Position (target query)

  // dt-aware exponential smoothing: same feel regardless of frame rate.
  let factor = 1.0f - MathF.Exp(-followRate * dt)
  Camera2D.smoothFollow camera goal factor
