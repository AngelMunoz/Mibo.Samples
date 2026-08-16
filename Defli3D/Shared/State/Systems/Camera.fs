module Defli3D.State.Systems.Camera

open System
open System.Numerics

// ─────────────────────────────────────────────────────────────
// Camera sub-system — owns the single 3D orbit camera (the one big
// redesign vs Defli's 2D camera). The sim stores BACKEND-NEUTRAL
// camera facts (CameraState: XZ target / yaw / pitch / distance +
// shake timer); the native camera (raylib Camera3D, MonoGame
// View/Projection) is built from them at the view edge ("convert at
// edges" — see Mibo.Samples). No backend types here.
//
// Conventions (the views MUST match these):
//   * World axes: +X east, +Z south, +Y up. The sim's logical
//     Vector2 positions are XZ-plane points (x → x, y → z).
//   * The camera orbits the XZ target: Yaw rotates around +Y
//     (0 = looking along -Z), Pitch is the elevation above the
//     ground plane, Distance is the eye→target length.
//   * eye = target + distance·(cos pitch·sin yaw, sin pitch,
//     cos pitch·cos yaw) — pitch 0 would sit at ground level.
//   * Right-handed view/projection (System.Numerics CreateLookAt /
//     CreatePerspectiveFieldOfView) with the vertical FOV
//     Camera.FovY — pickGroundCell unprojects through exactly this
//     pair, so hover picking matches what is drawn.
//
// The window size is a RENDER-TIME fact (the sim is headless): the
// view derives viewport-dependent quantities (aspect, projection)
// from the window each frame — the sim never stores it. Screen
// picking takes the viewport size as parameters for the same reason.
//
// Pan semantics (drag): the world follows the cursor, so the target
// moves opposite the drag, oriented by the yaw (dx along the
// camera's right, dy along down-screen) and scaled by
// unitsPerPixel = Distance / PanScale. At the default distance this
// is the Defli zoom-1 feel (64 px per cell); zooming in (smaller
// distance) slows the pan, zooming out speeds it up.
//
// Shake is deterministic (fixed-frequency sinusoids — no RNG), so
// the same tick sequence always produces the same offset. It offsets
// the EYE on the XZ plane (the look target stays put — the view
// wobbles, matching the 2D camera).
// ─────────────────────────────────────────────────────────────

[<Struct>]
type CameraMsg =
  /// Screen-space drag delta (pixels) — grab semantics: the world
  /// follows the cursor, so the camera target moves opposite the
  /// drag, converted to world units with the distance-based scale
  /// (unitsPerPixel = Distance / PanScale) and oriented by the yaw.
  /// Keyboard pan mirrors a drag: Camera.tick converts the
  /// SET keyboard-pan direction (world units/s) back to
  /// the pixel convention and applies it as a Pan.
  | Pan of dx: float32 * dy: float32
  /// Keyboard pan: the CURRENT held keyboard-pan direction (the sum of
  /// the held pan actions' world-space steps). The sim SETS it every
  /// step from the mapped actions' Held set — set-state, not
  /// accumulation: synonym bindings (A and Left both map PanLeft) count
  /// once, and a missed edge can never leave a stale direction behind.
  /// Camera.tick applies the direction as a Pan each step.
  | SetKeyboardPan of dir: Vector2
  /// Multiplicative zoom step (e.g. 1.1 = zoom in, 0.9 = zoom out) —
  /// scales the orbit Distance, clamped to [MinDistance, MaxDistance].
  | ZoomBy of factor: float32
  | SetTarget of target: Vector2
  /// Kick the shake timer (amplitude in world units).
  | Shake of strength: float32
  /// Back to the world center at the default yaw/pitch/distance.
  | Reset

/// Backend-neutral orbit-camera state — everything the view needs to
/// build a backend camera at the edge. A struct copy rides the
/// RenderFrame.
[<Struct>]
type CameraState = {
  /// World position (XZ plane) the camera centers on and orbits.
  Target: Vector2
  /// Orbit yaw in radians (0 = looking along -Z, positive rotates
  /// the eye clockwise around +Y).
  Yaw: float32
  /// Elevation above the ground plane in radians, clamped to
  /// [MinPitch, MaxPitch] (0 = eye at ground level).
  Pitch: float32
  /// Eye→target distance in world units, clamped to
  /// [MinDistance, MaxDistance].
  Distance: float32
  /// World bounds (0,0 → WorldSize) — Camera.tick keeps the target
  /// inside; the view may clamp a copy the same way.
  WorldSize: Vector2
  /// Seconds of shake left (decayed by Camera.tick).
  ShakeRemaining: float32
  /// Peak shake amplitude in world units (XZ eye offset).
  ShakeStrength: float32
}

type CameraModel() =
  /// The camera facts, mutated IN PLACE by the subsystem. A
  /// `val mutable` FIELD on purpose: the frame reads a struct copy
  /// at force time (no property indirection on the hot path).
  [<DefaultValue>]
  val mutable State: CameraState

  /// The current keyboard-pan direction (set every step from the held
  /// pan actions); Camera.tick consumes it as a Pan each step.
  [<DefaultValue>]
  val mutable KeyboardPan: Vector2

module Camera =

  /// Yaw is unbounded (full orbit) — the view wraps it into a
  /// canonical range at the edge if it needs to.
  let MinYaw = -MathF.PI
  let MaxYaw = MathF.PI
  /// Elevation limits — never at ground level (0.35 ≈ 20°) and never
  /// overhead (1.45 ≈ 83°).
  let MinPitch = 0.35f
  let MaxPitch = 1.45f
  /// Zoom limits in world units (the whole 20×12 grid at the default
  /// distance 16; 4 ≈ close-up on a tower, 40 ≈ wide overview).
  let MinDistance = 4f
  let MaxDistance = 40f
  let DefaultPitch = 0.8f
  let DefaultDistance = 16f
  let ShakeDuration = 0.35f
  /// Keyboard pan speed in world units per second (Defli's 500 px/s
  /// ÷ 64) — the SET keyboard-pan direction is scaled by
  /// it in Camera.tick.
  let KeyboardPanSpeed = 8f
  /// Vertical field of view (radians) of the sim-side projection —
  /// the views must build their projection with the same FOV or
  /// pickGroundCell will disagree with what is drawn.
  let FovY = MathF.PI / 4f
  /// Pixel→world conversion constant: unitsPerPixel = Distance /
  /// PanScale. Chosen so the DEFAULT camera (distance 16) pans at
  /// the Defli zoom-1 rate (64 px per cell); the scale tracks the
  /// zoom so a drag always moves the ground under the cursor.
  let PanScale = 1024f

  let init(worldSize: Vector2) : CameraModel =
    CameraModel(
      State = {
        Target = worldSize / 2f // world center
        Yaw = 0f
        Pitch = DefaultPitch
        Distance = DefaultDistance
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
    | Pan(dx, dy) ->
      let unitsPerPixel = model.State.Distance / PanScale

      // Yaw-relative screen axes on the XZ plane: right (+screen X)
      // and down-screen (+screen Y, the ground projection of the
      // view direction).
      let right = Vector2(MathF.Cos model.State.Yaw, -MathF.Sin model.State.Yaw)
      let down = Vector2(-MathF.Sin model.State.Yaw, -MathF.Cos model.State.Yaw)

      model.State <- {
        model.State with
            Target =
              model.State.Target
              - (right * (dx * unitsPerPixel) + down * (dy * unitsPerPixel))
      }
    | SetKeyboardPan dir -> model.KeyboardPan <- dir
    | ZoomBy f ->
      model.State <- {
        model.State with
            Distance =
              Math.Clamp(model.State.Distance * f, MinDistance, MaxDistance)
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
            Yaw = 0f
            Pitch = DefaultPitch
            Distance = DefaultDistance
            ShakeRemaining = 0f
            ShakeStrength = 0f
      }

  /// The XZ target clamped into the world bounds (0,0 → WorldSize).
  /// The orbit camera never shows void: Camera.tick clamps every
  /// frame, and the view may clamp a copy the same way (the 2D
  /// version's clampToWorld, reduced to the plain XZ bounds — there
  /// is no zoom-out-beyond-world case in 3D).
  let inline clampToWorld(state: CameraState) : CameraState = {
    state with
        Target =
          Vector2(
            Math.Clamp(state.Target.X, 0f, state.WorldSize.X),
            Math.Clamp(state.Target.Y, 0f, state.WorldSize.Y)
          )
  }

  /// Hot path (per tick): apply the accumulated keyboard pan as a
  /// Pan (the accumulated direction is in world units/s — converted
  /// to the Pan message's pixel convention), decay the shake timer,
  /// and clamp the target to the world bounds.
  let tick (dt: float32) (model: CameraModel) : unit =
    if model.KeyboardPan <> Vector2.Zero then
      let pxPerUnit = PanScale / model.State.Distance

      handle
        (Pan(
          model.KeyboardPan.X * KeyboardPanSpeed * dt * pxPerUnit,
          model.KeyboardPan.Y * KeyboardPanSpeed * dt * pxPerUnit
        ))
        model

    if model.State.ShakeRemaining > 0f then
      model.State <- {
        model.State with
            ShakeRemaining = max 0f (model.State.ShakeRemaining - dt)
      }

    model.State <- clampToWorld model.State

  // ── Pure view math (backend-neutral, headless-testable) ──────
  // The backend-specific conversion (native camera structs, culling
  // rectangles) lives in the frontend view layers.

  /// Deterministic shake offset (no RNG): fixed-frequency sinusoids
  /// scaled by the remaining strength. Zero once the shake expired.
  let inline shakeOffset(state: CameraState) : Vector2 =
    if state.ShakeRemaining <= 0f then
      Vector2.Zero
    else
      let amp = state.ShakeStrength * (state.ShakeRemaining / ShakeDuration)

      Vector2(
        amp * MathF.Sin(state.ShakeRemaining * 47f),
        amp * MathF.Cos(state.ShakeRemaining * 37f)
      )

  /// The eye (camera position) in world space: the XZ target plus
  /// the orbit offset (pitch elevation, yaw azimuth) and the
  /// deterministic XZ shake offset. The view looks from here toward
  /// the ground-plane target point.
  let inline eyePosition(state: CameraState) : Vector3 =
    let shake = shakeOffset state
    let cp = MathF.Cos state.Pitch
    let sp = MathF.Sin state.Pitch

    Vector3(
      state.Target.X + state.Distance * cp * MathF.Sin state.Yaw + shake.X,
      state.Distance * sp,
      state.Target.Y + state.Distance * cp * MathF.Cos state.Yaw + shake.Y
    )

  /// The right-handed view matrix (System.Numerics convention) — the
  /// sim's half of the projection pair, exposed so the views build
  /// IDENTICAL matrices (pickGroundCell and the drawn camera agree).
  let inline viewMatrix(state: CameraState) : Matrix4x4 =
    Matrix4x4.CreateLookAt(
      eyePosition state,
      Vector3(state.Target.X, 0f, state.Target.Y),
      Vector3.UnitY
    )

  /// The perspective projection for a viewport (aspect from the
  /// window, FOV from Camera.FovY). The views must use this pair
  /// (near 0.1 / far 1000) for picking to match the drawing.
  let inline projectionMatrix
    (viewportW: float32)
    (viewportH: float32)
    : Matrix4x4 =
    let aspect = viewportW / max 1f viewportH

    Matrix4x4.CreatePerspectiveFieldOfView(FovY, aspect, 0.1f, 1000f)

  /// World → screen projection through the view/projection pair
  /// (row-vector convention): clip space, NDC, then viewport pixels
  /// (origin top-left, +Y down). ValueNone when the point is behind
  /// the camera (w ≤ 0 in clip space) or outside the NDC cube (fully
  /// off-viewport) — the HUD's world-anchored tags skip those.
  let inline worldToScreen
    (viewportW: float32)
    (viewportH: float32)
    (worldPos: Vector3)
    (state: CameraState)
    : Vector2 voption =
    let clip =
      Vector4.Transform(
        Vector4(worldPos, 1f),
        viewMatrix state * projectionMatrix viewportW viewportH
      )

    if clip.W <= 0f then
      ValueNone
    else
      let nx = clip.X / clip.W
      let ny = clip.Y / clip.W

      if nx < -1f || nx > 1f || ny < -1f || ny > 1f then
        ValueNone
      else
        ValueSome(
          Vector2((nx + 1f) * 0.5f * viewportW, (1f - ny) * 0.5f * viewportH)
        )

  /// Screen → world ray through the orbit camera: the eye (origin)
  /// and the world-space direction for the pixel (standard
  /// unproject — NDC → inverse view-projection, row-vector
  /// convention).
  let inline screenRay
    (viewportW: float32)
    (viewportH: float32)
    (screenPos: Vector2)
    (state: CameraState)
    : struct (Vector3 * Vector3) =
    let ndcX = 2f * screenPos.X / max 1f viewportW - 1f
    let ndcY = 1f - 2f * screenPos.Y / max 1f viewportH

    let mutable inv = Unchecked.defaultof<Matrix4x4>

    Matrix4x4.Invert(
      viewMatrix state * projectionMatrix viewportW viewportH,
      &inv
    )
    |> ignore

    let far = Vector4.Transform(Vector4(ndcX, ndcY, 1f, 1f), inv)
    let farPos = Vector3(far.X, far.Y, far.Z) / far.W
    let origin = eyePosition state
    let dir = Vector3.Normalize(farPos - origin)
    struct (origin, dir)

  /// The XZ world point under the cursor — the ray ∩ y=0 plane
  /// intersection (ValueNone when the ray misses the plane in front
  /// of the camera). The placement preview reads this hit point
  /// directly (cell-free).
  let inline pickGroundPoint
    (viewportW: float32)
    (viewportH: float32)
    (screenPos: Vector2)
    (state: CameraState)
    : Vector2 voption =
    let struct (origin, dir) = screenRay viewportW viewportH screenPos state

    if dir.Y > -1e-4f then
      // Parallel to (or pointing away from) the ground plane.
      ValueNone
    else
      let t = -origin.Y / dir.Y
      ValueSome(Vector2(origin.X + dir.X * t, origin.Z + dir.Z * t))

  /// The ground CELL under the cursor: floor of the XZ hit point
  /// (the containing cell — Mibo's worldToCell floors the same way,
  /// but the sim must stay backend-neutral, so the floor + bounds
  /// check live here), bounds-checked against the grid (0,0 →
  /// WorldSize). ValueNone off-grid or when the ray misses the
  /// ground plane.
  let inline pickGroundCell
    (viewportW: float32)
    (viewportH: float32)
    (screenPos: Vector2)
    (state: CameraState)
    : struct (int * int) voption =
    match pickGroundPoint viewportW viewportH screenPos state with
    | ValueNone -> ValueNone
    | ValueSome p ->
      let x = int(MathF.Floor p.X)
      let y = int(MathF.Floor p.Y)

      if
        x >= 0
        && x < int state.WorldSize.X
        && y >= 0
        && y < int state.WorldSize.Y
      then
        ValueSome(struct (x, y))
      else
        ValueNone
