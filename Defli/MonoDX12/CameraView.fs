namespace Defli.MonoGame

open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Defli.World.Systems.Camera

// ─────────────────────────────────────────────────────────────
// CameraView — the MonoGame EDGE of the neutral camera: builds the
// native Camera2D from the sim's CameraState each frame ("convert at
// edges"). The clamp and the deterministic shake are applied on the
// copy; picking (screenToWorld) composes the same clamp so the
// cursor maps to the cells the player actually sees.
// ─────────────────────────────────────────────────────────────

module CameraView =

  /// The camera as recorded into the buffer: clamped to the world,
  /// with the deterministic shake applied to the target. The screen
  /// offset is the viewport center (a render-time fact the sim never
  /// stores).
  let inline toMono (state: CameraState) (viewport: Vector2) : Camera2D =
    let clamped = Camera.clampToWorld state viewport
    let target = clamped.Target + shakeOffset clamped

    {
      Position = Xna.v2 target
      Zoom = state.Zoom
      Rotation = state.Rotation
      Origin = Xna.v2(viewport / 2f)
    }

  /// Records the camera into the buffer (the world-space block).
  let inline beginFrame
    (state: CameraState)
    (viewport: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.beginCamera(toMono state viewport).drop()

  /// Screen → world through the DRAWN camera (clamped, shake-free —
  /// picking must match the cells the player sees, not the shaken
  /// ones).
  let inline screenToWorld
    (state: CameraState)
    (viewport: Vector2)
    (screenPos: Vector2)
    : Vector2 =
    Camera.screenToWorld (Camera.clampToWorld state viewport) viewport screenPos

  /// The culling rect for the map passes: the clamped view inflated
  /// by 15% on each side (CellGrid2D.iterVisible culls to it).
  let inline cullingBounds
    (state: CameraState)
    (viewport: Vector2)
    : Rectangle =
    let struct (min, max) = Camera.viewBounds state viewport
    let w = max.X - min.X
    let h = max.Y - min.Y
    let marginX = w * 0.15f
    let marginY = h * 0.15f

    Rectangle(
      int(min.X - marginX),
      int(min.Y - marginY),
      int(w * 1.3f),
      int(h * 1.3f)
    )
