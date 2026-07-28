module AnimatedInstancing.MonoGame.View

open System
open Microsoft.Xna.Framework
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.AssetsService
open Mibo.Animation
open AnimatedInstancing
open AnimatedInstancing.MonoGame.Types

let private groundMaterial =
  Material3D.colored(Microsoft.Xna.Framework.Color(110, 112, 120))

// ─────────────────────────────────────────────────────────────
// 3D scene
// ─────────────────────────────────────────────────────────────

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
  let assets = GameContext.getService<IAssets> ctx

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

  let camera: Camera3D = {
    Position = position
    Target = Vector3.Zero
    Up = Vector3.UnitY
    FovY = MathHelper.ToRadians(55.0f)
    NearPlane = 0.1f
    FarPlane = 2000.0f
    Projection = CameraProjection.Perspective
  }

  buffer
    .beginCameraWith(
      Camera3D.render camera
      |> Camera3D.withClear(Microsoft.Xna.Framework.Color(30, 34, 40))
    )
    .setAmbientLight(
      {
        Color = Mibo.Color.White
        Intensity = 0.35f
      }
    )
    .addDirectionalLight(
      {
        Direction = System.Numerics.Vector3(0.6f, -1.0f, 0.35f)
        Color = Mibo.Color.White
        Intensity = 1.0f
        CastsShadows = true
      }
    )
    .drop()

  // Ground slab sized to the current tier's grid, top face at y = 0.
  let side = CrowdSpec.gridSide crowd.Count
  let extent = float32 side * CrowdSpec.spacing + 8.0f

  let groundTransform =
    Matrix.CreateScale(extent, 1.0f, extent)
    * Matrix.CreateTranslation(0.0f, -0.5f, 0.0f)

  buffer.mesh(model.GroundMesh, groundTransform, groundMaterial).drop()

  // THE probe: one pose evaluation per instance into the reused pose array,
  // then a single skinned+instanced draw call (DrawAnimatedModelInstanced).
  // Pose evaluation is parallelized — computePose only reads the clip data +
  // per-instance state and allocates each pose, and each iteration writes a
  // distinct Poses slot. Skipped while paused: the states (and therefore the
  // poses) don't change.
  match model.Rig.Mesh with
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

    let am: AnimatedModel = {
      Model = model.Rig.Model
      Mesh = model.Rig.Mesh
      State = crowd.States[0]
    }

    let texture = assets.Texture "mannequin_texture"

    let material = Material3D.defaults |> Material3D.withAlbedoMap texture

    buffer
      .animatedModelInstanced(
        am,
        crowd.Transforms,
        crowd.Poses,
        material = All material
      )
      .drop()
  | _ -> ()

  buffer.endCamera().drop()

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
    buffer
      .text(
        TextState.create(model.DiagFont, text, Vector2(10.0f, yPos))
        |> TextState.withScale 1.25f
        |> TextState.withColor Color.Yellow
        |> TextState.withLayer 0<RenderLayer>
      )
      .drop()

  line
    10.0f
    $"FPS: {model.Diag.Fps}  ({model.Diag.FrameTime:F1}ms)  Backend: MonoGame"

  line
    35.0f
    $"Instances: {crowd.Count}  Tier: {crowd.TierIndex + 1}/{CrowdSpec.counts.Length}  Clips: Walking_A/Running_A/Idle_A (i mod 3)  Anim: {paused}"

  line 60.0f "1-4 tiers | +/- step | Space pause"
