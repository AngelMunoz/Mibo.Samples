module Defli3D.Tests.CameraTests

open System
open System.Numerics
open Expecto
open Defli3D.State.Systems
open Defli3D.State.Systems.Camera

// Phase 4 — the Camera sub-system (the port's one big redesign vs
// Defli's 2D camera: a 3D orbit camera): the sim stores
// BACKEND-NEUTRAL camera facts (CameraState); the native camera is
// built at the view edge. All assertions read the State fields
// (Target/Yaw/Pitch/Distance/WorldSize/Shake*).

let private worldSize = Vector2(20f, 12f)
let private viewport = Vector2(1280f, 720f)
let private model() = Camera.Camera.init worldSize

/// A bare state for the pure-math tests (no model) — default yaw 0.
let private state
  (target: Vector2)
  (pitch: float32)
  (distance: float32)
  : CameraState =
  {
    Target = target
    Yaw = 0f
    Pitch = pitch
    Distance = distance
    WorldSize = worldSize
    ShakeRemaining = 0f
    ShakeStrength = 0f
  }

let tests =
  testList "Camera" [
    testCase "init centers on the world center at the default orbit" (fun () ->
      let m = model()
      Expect.equal m.State.Target (worldSize / 2f) "target"
      Expect.equal m.State.Yaw 0f "yaw"
      Expect.equal m.State.Pitch Camera.DefaultPitch "pitch"
      Expect.equal m.State.Distance Camera.DefaultDistance "distance"
      Expect.equal m.State.WorldSize worldSize "world size"
      Expect.equal m.State.ShakeRemaining 0f "no shake")

    testCase
      "Pan moves the target opposite the drag, scaled by distance"
      (fun () ->
        let m = model()
        // Drag right 64 px at Distance 16 (unitsPerPixel = 16/1024 =
        // 1/64) → the world moves left 1 unit.
        Camera.Camera.pan 64f 0f m
        Expect.equal m.State.Target (Vector2(9f, 6f)) "pan at default distance"

        // Zoom in (Distance 8): the same drag moves the world half as far.
        Camera.Camera.zoomBy 0.5f m
        Camera.Camera.pan 64f 0f m
        Expect.equal m.State.Target (Vector2(8.5f, 6f)) "pan at half distance"

        // Pan is yaw-relative: at yaw π/2 screen-right maps to −Y (world
        // +Z) — the same drag moves the target along +Y (MathF.Cos of
        // the float π/2 is ≈ −4e-8, so compare with floatClose).
        m.State <- { m.State with Yaw = MathF.PI / 2f }
        Camera.Camera.pan 64f 0f m

        Expect.floatClose
          Accuracy.medium
          (float m.State.Target.X)
          8.5
          "pan x stays"

        Expect.floatClose
          Accuracy.medium
          (float m.State.Target.Y)
          6.5
          "pan rotated by yaw")

    testCase "ZoomBy scales and clamps the distance; pitch untouched" (fun () ->
      let m = model()
      Camera.Camera.zoomBy 2f m
      Expect.equal m.State.Distance 32f "zoomed in"
      Camera.Camera.zoomBy 2f m
      Expect.equal m.State.Distance Camera.MaxDistance "clamped at max"
      Camera.Camera.zoomBy 0.01f m
      Expect.equal m.State.Distance Camera.MinDistance "clamped at min"
      Expect.equal m.State.Pitch Camera.DefaultPitch "zoom keeps pitch")

    testCase "SetTarget writes; tick clamps the target to the world" (fun () ->
      let m = model()
      Camera.Camera.setTarget (Vector2(2f, 3f)) m
      Expect.equal m.State.Target (Vector2(2f, 3f)) "target set"
      Camera.Camera.setTarget (Vector2(50f, -10f)) m
      Camera.Camera.tick 0.01f m
      Expect.equal m.State.Target (Vector2(20f, 0f)) "clamped at the corner")

    testCase "clampToWorld clamps the target into the world bounds" (fun () ->
      let s =
        state (Vector2(10f, 6f)) Camera.DefaultPitch Camera.DefaultDistance

      Expect.equal
        (Camera.clampToWorld s).Target
        (Vector2(10f, 6f))
        "inside unchanged"

      let s2 =
        state (Vector2(-5f, 30f)) Camera.DefaultPitch Camera.DefaultDistance

      Expect.equal
        (Camera.clampToWorld s2).Target
        (Vector2(0f, 12f))
        "clamped at min"

      let s3 =
        state
          (Vector2(1000f, -1000f))
          Camera.DefaultPitch
          Camera.DefaultDistance

      Expect.equal
        (Camera.clampToWorld s3).Target
        (Vector2(20f, 0f))
        "clamped at max")

    testCase "Reset restores the world center at the default orbit" (fun () ->
      let m = model()
      Camera.Camera.pan 400f 300f m
      Camera.Camera.zoomBy 2f m
      Camera.Camera.shake 8f m
      Camera.Camera.reset m
      Expect.equal m.State.Target (worldSize / 2f) "target"
      Expect.equal m.State.Yaw 0f "yaw"
      Expect.equal m.State.Pitch Camera.DefaultPitch "pitch"
      Expect.equal m.State.Distance Camera.DefaultDistance "distance"
      Expect.equal m.State.ShakeRemaining 0f "shake cleared")

    testCase "Shake sets the timer, tick decays it, offset expires" (fun () ->
      let m = model()
      Camera.Camera.shake 0.5f m
      Expect.equal m.State.ShakeRemaining Camera.ShakeDuration "timer set"
      Expect.notEqual (Camera.shakeOffset m.State) Vector2.Zero "offset active"
      Camera.Camera.tick 0.2f m

      Expect.equal
        m.State.ShakeRemaining
        (Camera.ShakeDuration - 0.2f)
        "decayed"

      Camera.Camera.tick 1f m
      Expect.equal m.State.ShakeRemaining 0f "expired"

      Expect.equal
        (Camera.shakeOffset m.State)
        Vector2.Zero
        "offset zero when expired")

    testCase "shakeOffset is deterministic per state" (fun () ->
      // Pure function of the state (fixed-frequency sinusoids — no
      // RNG): identical states always produce identical offsets.
      let a = model()
      let b = model()
      Camera.Camera.shake 0.5f a
      Camera.Camera.shake 0.5f b

      Expect.equal
        (Camera.shakeOffset a.State)
        (Camera.shakeOffset b.State)
        "same state, same offset"

      Camera.Camera.tick 0.1f a
      Camera.Camera.tick 0.1f b

      Expect.equal
        (Camera.shakeOffset a.State)
        (Camera.shakeOffset b.State)
        "deterministic mid-shake")

    testCase "eyePosition orbits at the fixed distance" (fun () ->
      let s =
        state (Vector2(10f, 6f)) Camera.DefaultPitch Camera.DefaultDistance

      let eye = Camera.eyePosition s
      let target3 = Vector3(10f, 0f, 6f)

      Expect.floatClose
        Accuracy.medium
        (float(Vector3.Distance(eye, target3)))
        16.0
        "distance preserved"

      Expect.floatClose
        Accuracy.medium
        (float eye.Y)
        (float(16f * MathF.Sin Camera.DefaultPitch))
        "elevation"

      // Yaw π/2: the eye swings to +X — same distance, same height.
      let s2 = { s with Yaw = MathF.PI / 2f }
      let eye2 = Camera.eyePosition s2

      Expect.floatClose
        Accuracy.medium
        (float(Vector3.Distance(eye2, target3)))
        16.0
        "yawed distance"

      Expect.floatClose
        Accuracy.medium
        (float eye2.X)
        (float(10f + 16f * MathF.Cos Camera.DefaultPitch))
        "yawed x"

      Expect.floatClose Accuracy.medium (float eye2.Z) 6.0 "yawed z")

    testCase
      "center-of-viewport pick ≈ the look target (target cell)"
      (fun () ->
        // Aim at the CENTER of cell (10,6): an integer corner (10,6)
        // sits on the shared edge of four cells and the float32
        // unproject error (~1e-4) decides which one floor picks — the
        // cell-center aim is the gameplay-hover case.
        let s =
          state
            (Vector2(10.5f, 6.5f))
            Camera.DefaultPitch
            Camera.DefaultDistance

        // The screen center ray passes through the look-at point on the
        // ground. The unprojection is float32 (matrix inverse + far-plane
        // divide), so the hit is exact to ~1e-4 world units — Accuracy.low.
        match
          Camera.pickGroundPoint viewport.X viewport.Y (viewport / 2f) s
        with
        | ValueSome p ->
          Expect.floatClose Accuracy.low (float p.X) 10.5 "hit x"
          Expect.floatClose Accuracy.low (float p.Y) 6.5 "hit y"
        | ValueNone -> failtest "center pick must hit the ground"

        match
          Camera.pickGroundCell viewport.X viewport.Y (viewport / 2f) s
        with
        | ValueSome struct (10, 6) -> ()
        | other -> failtestf "expected (10,6), got %A" other)

    testCase "pickGroundCell out-of-bounds → ValueNone" (fun () ->
      // Camera near the world origin corner looking along +Z (yaw π):
      // the screen CENTER ray hits the look target (0.5, 0.5) — cell
      // (0,0) in bounds; the lower part of the screen shows the ground
      // BEHIND the origin, whose cells are off-grid.
      let s = {
        (state (Vector2(0.5f, 0.5f)) Camera.DefaultPitch Camera.DefaultDistance) with
            Yaw = MathF.PI
      }

      match Camera.pickGroundCell viewport.X viewport.Y (viewport / 2f) s with
      | ValueSome struct (0, 0) -> ()
      | other -> failtestf "expected (0,0), got %A" other

      Expect.isTrue
        (Camera.pickGroundCell
          viewport.X
          viewport.Y
          (Vector2(viewport.X / 2f, 700f))
          s)
          .IsNone
        "past the near edge")

    testCase
      "screenRay: the constructed ray hits the known world point"
      (fun () ->
        let s =
          state (Vector2(10f, 6f)) Camera.DefaultPitch Camera.DefaultDistance

        let struct (origin, dir) =
          Camera.screenRay viewport.X viewport.Y (viewport / 2f) s

        // The center ray is the eye → look-target line: its intersection
        // with the ground plane is exactly the target (float32 unproject,
        // so Accuracy.low — see the pick test above).
        let t = -origin.Y / dir.Y
        let hit = origin + dir * t
        Expect.floatClose Accuracy.low (float hit.X) 10.0 "ground hit x"
        Expect.floatClose Accuracy.low (float hit.Z) 6.0 "ground hit z")
  ]
