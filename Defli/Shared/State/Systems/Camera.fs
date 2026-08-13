module Defli.State.Systems.Camera

open System
open System.Numerics
open Defli.State

// ─────────────────────────────────────────────────────────────
// Camera sub-system — owns the single 2D camera (Kimo analog:
// State/Systems/Camera.fs). The sim stores BACKEND-NEUTRAL camera
// facts (CameraState: target/zoom/rotation + shake timer); the
// native camera (raylib Camera2D, MonoGame Camera2D) is built from
// them at the view edge ("convert at edges" — see Mibo.Samples).
// No backend types here.
//
// The window size is a RENDER-TIME fact (the sim is headless): the
// view derives the screen offset (viewport/2) from the window each
// frame — the sim never stores it.
//
// No PrevTarget lerp: Kimo interpolates because its sim runs at a
// different rate than its draw (30 Hz sim / draw-rate renders). In
// Shape C the sim and the view share the 60 Hz frame, so there is
// nothing to interpolate.
//
// Shake is deterministic (fixed-frequency sinusoids — no RNG), so
// the same tick sequence always produces the same offset.
// ─────────────────────────────────────────────────────────────

[<Struct>]
type CameraMsg =
  /// Screen-space drag delta (pixels) — grab semantics: the world
  /// follows the cursor, so the camera target moves opposite the
  /// drag, scaled by the current zoom. Conversion happens HERE (the
  /// subsystem owns the zoom); callers send raw screen pixels.
  /// Keyboard pan mirrors a drag: Camera.tick applies the accumulated
  /// AddKeyboardPan direction with the same sign convention.
  | Pan of screenDelta: Vector2
  /// Keyboard pan: the pressed/released deltas ACCUMULATE in the model
  /// (pressed adds, released subtracts — the shell's PanDir tracking);
  /// Camera.tick applies the accumulated direction as a Pan each step.
  | AddKeyboardPan of delta: Vector2
  /// Multiplicative zoom step (e.g. 1.1 = zoom in, 0.9 = zoom out).
  | ZoomBy of factor: float32
  | SetTarget of target: Vector2
  /// Kick the shake timer (amplitude in world pixels).
  | Shake of strength: float32
  /// Back to the world center at zoom 1 (viewport offset untouched).
  | Reset

/// Backend-neutral camera state — everything the view needs to build
/// a backend camera at the edge. A struct copy rides the RenderFrame.
[<Struct>]
type CameraState = {
  /// World position the camera centers on.
  Target: Vector2
  /// Zoom factor.
  Zoom: float32
  /// Rotation in degrees (Defli never rotates — always 0).
  Rotation: float32
  /// World bounds (0,0 → WorldSize) — clampToWorld keeps the target
  /// inside the world, soft-clamping axes whose view is wider than
  /// the world (the target can slide until the world edge meets the
  /// screen edge).
  WorldSize: Vector2
  /// Seconds of shake left (decayed by Camera.tick).
  ShakeRemaining: float32
  /// Peak shake amplitude in world pixels.
  ShakeStrength: float32
}

type CameraModel() =
  /// The camera facts, mutated IN PLACE by the subsystem. A
  /// `val mutable` FIELD on purpose: the frame reads a struct copy
  /// at force time (no property indirection on the hot path).
  [<DefaultValue>]
  val mutable State: CameraState

  /// Accumulated keyboard pan direction (pressed adds, released
  /// subtracts); Camera.tick consumes it as a Pan each step.
  [<DefaultValue>]
  val mutable KeyboardPan: Vector2

module Camera =

  let MinZoom = 0.5f
  let MaxZoom = 3f
  let ShakeDuration = 0.35f
  /// Keyboard pan speed in world pixels per second (the accumulated
  /// AddKeyboardPan direction is scaled by it in Camera.tick).
  let KeyboardPanSpeed = 500f

  let init(worldSize: Vector2) : CameraModel =
    CameraModel(
      State = {
        Target = worldSize / 2f // world center
        Zoom = 1f
        Rotation = 0f
        WorldSize = worldSize
        ShakeRemaining = 0f
        ShakeStrength = 0f
      },
      KeyboardPan = Vector2.Zero
    )

  /// Cold path: apply an input intent by mutating the state in place
  /// (never re-creating it). No return.
  let handle (msg: CameraMsg) (model: CameraModel) : unit =
    match msg with
    | Pan d ->
      model.State <- {
        model.State with
            Target = model.State.Target - d / model.State.Zoom
      }
    | AddKeyboardPan d -> model.KeyboardPan <- model.KeyboardPan + d
    | ZoomBy f ->
      model.State <- {
        model.State with
            Zoom = Math.Clamp(model.State.Zoom * f, MinZoom, MaxZoom)
      }
    | SetTarget t -> model.State <- { model.State with Target = t }
    | Shake strength ->
      model.State <- {
        model.State with
            ShakeRemaining = ShakeDuration
            ShakeStrength = strength
      }
    | Reset ->
      model.State <- {
        model.State with
            Target = model.State.WorldSize / 2f
            Zoom = 1f
            ShakeRemaining = 0f
            ShakeStrength = 0f
      }

  /// Hot path (per RoomTick): apply the accumulated keyboard pan as a
  /// Pan (drag semantics — the sign convention mirrors the shell's old
  /// PanDir step, so no sign flip here), then decay the shake timer.
  let tick (dt: float32) (model: CameraModel) : unit =
    if model.KeyboardPan <> Vector2.Zero then
      handle (Pan(model.KeyboardPan * KeyboardPanSpeed * dt)) model

    if model.State.ShakeRemaining > 0f then
      model.State <- {
        model.State with
            ShakeRemaining = max 0f (model.State.ShakeRemaining - dt)
      }

  let inline clampAxis (world: float32) (view: float32) =
    if view >= world then
      // View wider than the world: soft range around the center
      // (min < max holds because view > world). At the extremes the
      // world edge aligns with the screen edge and the excess shows
      // void on the opposite side — keyboard panning never dead-ends.
      struct (world - view / 2f, view / 2f)
    else
      struct (view / 2f, world - view / 2f)
  // ── Pure view math (backend-neutral, headless-testable) ──────
  // The backend-specific conversion (native camera structs, culling
  // rectangles) lives in the frontend view layers.

  /// The clamped camera: per axis, the target is kept inside the
  /// world when the view fits; when the view is WIDER than the world
  /// the clamp is soft — the target can still slide until the world
  /// edge meets the screen edge, so keyboard panning works at every
  /// zoom. Pure — the sim stores render-independent facts; the view
  /// clamps a copy each frame.
  let inline clampToWorld
    (state: CameraState)
    (viewport: Vector2)
    : CameraState =
    let view = viewport / state.Zoom
    let struct (minX, maxX) = clampAxis state.WorldSize.X view.X
    let struct (minY, maxY) = clampAxis state.WorldSize.Y view.Y

    {
      state with
          Target =
            Vector2(
              Math.Clamp(state.Target.X, minX, maxX),
              Math.Clamp(state.Target.Y, minY, maxY)
            )
    }

  /// Screen → world through the camera (the offset is the viewport
  /// center — the view builds it from the window size). Frontends
  /// compose clampToWorld + this so picking matches what is drawn.
  let inline screenToWorld
    (state: CameraState)
    (viewport: Vector2)
    (screenPos: Vector2)
    : Vector2 =
    (screenPos - viewport / 2f) / state.Zoom + state.Target

  /// The world-space rect the camera shows — (min, max) of the view
  /// centered on the CLAMPED target (the backend-neutral equivalent
  /// of raylib's viewportBounds helper).
  let inline viewBounds
    (state: CameraState)
    (viewport: Vector2)
    : struct (Vector2 * Vector2) =
    let clamped = clampToWorld state viewport
    let half = viewport / 2f / clamped.Zoom
    struct (clamped.Target - half, clamped.Target + half)

/// Deterministic shake offset (no RNG): fixed-frequency sinusoids
/// scaled by the remaining strength. Zero once the shake expired.
let inline shakeOffset(state: CameraState) : Vector2 =
  if state.ShakeRemaining <= 0f then
    Vector2.Zero
  else
    let amp =
      state.ShakeStrength * (state.ShakeRemaining / Camera.ShakeDuration)

    Vector2(
      amp * MathF.Sin(state.ShakeRemaining * 47f),
      amp * MathF.Cos(state.ShakeRemaining * 37f)
    )
