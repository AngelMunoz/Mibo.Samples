module Platformer.MonoGame.Camera

open System
open Microsoft.Xna.Framework
open Mibo.Elmish

/// Read-only view of the state the camera needs to frame the scene.
/// The router (Systems.fs) builds this from sub-system models — the camera
/// never reaches into another sub-system's model directly.
[<Struct>]
type CameraQuery = { PlayerPosition: Vector2 }

/// Vertical bias (world units). Shifts the camera center up so the player
/// sits in the lower portion of the screen and more sky is visible above.
[<Literal>]
let verticalOffset = 180.0f

/// Smoothing factor for the follow (0–1; higher = snappier).
[<Literal>]
let followSpeed = 0.1f

/// World point the camera should center on, derived from the query.
/// Keeps the framing target within sane world bounds on the Y axis.
let target(query: CameraQuery) : Vector2 =
  let clampedY = MathF.Max(-500.0f, MathF.Min(query.PlayerPosition.Y, 2000.0f))
  Vector2(query.PlayerPosition.X, clampedY - verticalOffset)

/// Smoothly move the camera toward the framed target.
let update (query: CameraQuery) (camera: Camera2D) : Camera2D =
  Camera2D.smoothFollow camera (target query) followSpeed
