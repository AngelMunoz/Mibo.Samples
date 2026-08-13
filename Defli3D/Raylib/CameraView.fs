namespace Defli3D.Raylib

open System
open System.Numerics
open Raylib_cs
open Defli3D.State.Systems.Camera

// ─────────────────────────────────────────────────────────────
// CameraView — the raylib EDGE of the neutral orbit camera: builds
// the native Camera3D from the sim's CameraState each frame
// ("convert at edges" — see Shared/State/Systems/Camera.fs).
// Picking stays in Shared (Camera.pickGroundCell unprojects through
// the same FovY/viewport the drawn camera uses), so this file is
// ONLY the render-edge conversion.
//
// The world conventions are fixed by the sim (the views MUST match):
//   * +X east, +Z south, +Y up; the sim's Vector2 positions are
//     XZ-plane points (x → x, y → z).
//   * The eye comes from Camera.eyePosition (target + orbit offset
//     + the deterministic shake); the look target is the XZ target
//     on the ground plane.
//   * raylib's Camera3D.FovY is in DEGREES; the sim's Camera.FovY
//     is radians. Raylib derives the aspect and the near/far planes
//     internally (near 0.01 vs the sim's 0.1 — irrelevant for the
//     y=0 plane ray hit), so the viewport arguments are accepted
//     for signature parity with the sim's projectionMatrix but are
//     not needed to build the camera.
// ─────────────────────────────────────────────────────────────

module CameraView =

  /// The native raylib camera: clamped to the world (a copy — the
  /// sim already clamps every tick), shake applied to the eye via
  /// Camera.eyePosition, looking at the ground-plane target.
  let inline toRaylib
    (viewportW: float32, viewportH: float32)
    (state: CameraState)
    : Camera3D =
    let clamped = Camera.clampToWorld state

    Camera3D(
      Camera.eyePosition clamped,
      Vector3(clamped.Target.X, 0f, clamped.Target.Y),
      Vector3.UnitY,
      Camera.FovY * 180f / MathF.PI,
      CameraProjection.Perspective
    )
