module AnimatedInstancing.Raylib.View

open System
open System.Numerics
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open AnimatedInstancing
open AnimatedInstancing.Raylib.Types

let private groundMaterial =
  Material3D.colored(Raylib_cs.Color(110, 112, 120, 255))

// ─────────────────────────────────────────────────────────────
// 3D scene
// ─────────────────────────────────────────────────────────────

let view (_ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
  let crowd = model.Crowd
  let distance = CrowdSpec.cameraDistance crowd.Count
  let pitch = 0.6f
  let angle = crowd.CameraAngle

  let position =
    Vector3(
      MathF.Cos pitch * MathF.Sin angle,
      MathF.Sin pitch,
      MathF.Cos pitch * MathF.Cos angle
    )
    * distance

  let camera =
    Camera3D(
      position,
      Vector3.Zero,
      Vector3.UnitY,
      55.0f,
      CameraProjection.Perspective
    )

  buffer
  |> Draw3D.beginCameraWith(
    Camera3D.render camera
    |> Camera3D.withClear(Mibo.Color.op_Implicit(Mibo.Color.rgb 30uy 34uy 40uy))
  )
  |> Draw3D.setAmbientLight {
    Color = Mibo.Color.White
    Intensity = 0.35f
  }
  |> Draw3D.addDirectionalLight {
    Direction = Vector3(0.6f, -1.0f, 0.35f)
    Color = Mibo.Color.White
    Intensity = 1.0f
    CastsShadows = true
  }
  |> Draw3D.drop

  // Ground slab sized to the current tier's grid, top face at y = 0.
  let side = CrowdSpec.gridSide crowd.Count
  let extent = float32 side * CrowdSpec.spacing + 8.0f

  let groundTransform =
    Raymath.MatrixMultiply(
      Raymath.MatrixScale(extent, 1.0f, extent),
      Raymath.MatrixTranslate(0.0f, -0.5f, 0.0f)
    )

  buffer
  |> Draw3D.drawMesh model.GroundMesh groundTransform groundMaterial
  |> Draw3D.drop

  // THE probe: one pose evaluation per instance into the reused pose array,
  // then a single skinned+instanced draw call (one DrawSkinnedMeshInstanced
  // per sub-mesh). Pose arrays are allocated per pose by computePose — the
  // outer array is reused; only tier changes reallocate it. Pose evaluation
  // is parallelized (computePose only reads the clip data + per-instance
  // state; each iteration writes a distinct Poses slot) and skipped while
  // paused (the states, and therefore the poses, don't change).
  match model.AnimMesh with
  | ValueSome animMesh when crowd.Count > 0 ->
    if not crowd.Paused then
      System.Threading.Tasks.Parallel.For(
        0,
        crowd.Count,
        fun i ->
          crowd.Poses[i] <-
            Animation3DState.computePose animMesh crowd.States[i]
      )
      |> ignore

    let am = AnimatedModel.create animMesh crowd.States[0]

    buffer.animatedModelInstanced(am, crowd.Transforms, crowd.Poses) |> ignore
  | _ -> ()

  buffer |> Draw3D.endCamera |> Draw3D.drop

// ─────────────────────────────────────────────────────────────
// HUD (Renderer2D overlay)
// ─────────────────────────────────────────────────────────────

let viewHud (_ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  // Sample the wall-clock interval since the previous Draw — the real render
  // rate. See AnimatedInstancing.Diagnostics.
  model.Diag.Tick()

  let crowd = model.Crowd
  let paused = if crowd.Paused then "PAUSED" else "running"

  let inline line (yPos: float32) (text: string) =
    Draw.text
      {
        Font = model.DiagFont
        Text = text
        Position = Vector2(10.0f, yPos)
        FontSize = 20.0f
        Spacing = 1.0f
        Color = Color.Yellow
        Layer = 0<RenderLayer>
      }
      buffer
    |> Draw.drop

  line
    10.0f
    $"FPS: {model.Diag.Fps}  ({model.Diag.FrameTime:F1}ms)  Backend: raylib"

  line
    35.0f
    $"Instances: {crowd.Count}  Tier: {crowd.TierIndex + 1}/{CrowdSpec.counts.Length}  Clips: Walking_A/Running_A/Idle_A (i mod 3)  Anim: {paused}"

  line 60.0f "1-4 tiers | +/- step | Space pause"
