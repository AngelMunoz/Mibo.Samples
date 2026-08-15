namespace Defli3D.MonoGame

open Microsoft.Xna.Framework
open Mibo.Elmish
open Defli3D.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// CameraView — the MonoGame EDGE of the neutral orbit camera:
// builds the native Camera3D from the sim's CameraState each frame
// ("convert at edges"). Eye/target/FOV/near/far match Shared's
// view/projection pair exactly (Camera.viewMatrix/projectionMatrix)
// so the drawn camera agrees with the hover picking (which
// unprojects through that pair). The sim-side clamp and the
// deterministic shake ride the same math (Camera.eyePosition).
// ─────────────────────────────────────────────────────────────

module CameraView =

  /// Builds the native camera from the frame's neutral snapshot:
  /// clamped to the world, with the deterministic shake applied to
  /// the eye (XZ offset — the look target stays put). The pipeline
  /// derives the aspect from the active viewport at render time, so
  /// the camera carries no viewport size.
  let inline toMono(state: CameraState) : Camera3D =
    let clamped = Camera.clampToWorld state
    let eye = Camera.eyePosition clamped

    {
      Position = Vector3(eye.X, eye.Y, eye.Z)
      Target = Vector3(clamped.Target.X, 0f, clamped.Target.Y)
      Up = Vector3.UnitY
      FovY = Camera.FovY
      NearPlane = 0.1f
      FarPlane = 1000f
      Projection = CameraProjection.Perspective
    }
