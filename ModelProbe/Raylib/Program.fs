module ModelProbe.Raylib.Program

open System
open System.IO
open System.Numerics

open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Input
open Raylib_cs

// ─────────────────────────────────────────────────────────────
// ModelProbe (raylib) — same orbit, same scene, same split-screen probe
// semantics as the MonoGame version.
//
// The sample toggles between:
//   * zone view: three zones in one frame, including a shadowed back section
//   * split-screen: left outdoor view with sun/shadows, right indoor view
//     with dim ambient light and no shadow-casting sun.
//
// Controls:
//   Arrows  = orbit
//   W/S     = zoom
//   A/D     = pan left/right
//   PageUp/Down = pan up/down
//   0-3     = view presets
//   4       = toggle split-screen
// ─────────────────────────────────────────────────────────────

// ── Assets ──

let private blockNames = [|
  "block-grass"
  "block-grass-corner"
  "block-grass-curve"
  "block-grass-hexagon"
  "block-grass-large"
|]

let private modelPath name =
  Path.Combine(
    AppContext.BaseDirectory,
    "assets",
    "kenney_platformer-kit",
    "Models",
    $"{name}.glb"
  )

// ── Input ──

[<Struct>]
type GameAction =
  | OrbitLeft
  | OrbitRight
  | OrbitUp
  | OrbitDown
  | ZoomIn
  | ZoomOut
  | PanLeft
  | PanRight
  | PanUp
  | PanDown
  | ViewOverview
  | ViewZone1
  | ViewZone2
  | ViewZone3
  | ToggleSplitScreen

let inputMap =
  InputMap.empty
  |> InputMap.key OrbitLeft KeyCode.Left
  |> InputMap.key OrbitRight KeyCode.Right
  |> InputMap.key OrbitUp KeyCode.Up
  |> InputMap.key OrbitDown KeyCode.Down
  |> InputMap.key ZoomIn KeyCode.W
  |> InputMap.key ZoomOut KeyCode.S
  |> InputMap.key PanLeft KeyCode.A
  |> InputMap.key PanRight KeyCode.D
  |> InputMap.key PanUp KeyCode.PageUp
  |> InputMap.key PanDown KeyCode.PageDown
  |> InputMap.key ViewOverview KeyCode.D0
  |> InputMap.key ViewZone1 KeyCode.D1
  |> InputMap.key ViewZone2 KeyCode.D2
  |> InputMap.key ViewZone3 KeyCode.D3
  |> InputMap.key ToggleSplitScreen KeyCode.D4

// ── Types ──

type BlockEntry = { Model: Model }

type ProbeModel = {
  Blocks: BlockEntry[]
  Floor: Model
  TransparentCube: Mesh
  CamTarget: Vector3
  CamYaw: float32
  CamPitch: float32
  CamDistance: float32
  SplitScreen: bool
  Input: ActionState<GameAction>
}

[<Struct>]
type Msg =
  | Tick of tick: GameTime
  | InputChanged of inputs: ActionState<GameAction>

// ── Lights (identical to the MonoGame probe) ──

let private warmAmbient: AmbientLight3D = {
  Color = Mibo.Color.rgb 255uy 220uy 180uy
  Intensity = 0.4f
}

let private indoorAmbient: AmbientLight3D = {
  Color = Mibo.Color.rgb 90uy 110uy 200uy
  Intensity = 0.25f
}

let private sunLight =
  DirectionalLight3D.create(Vector3(0.6f, -1.0f, 0.35f))
  |> DirectionalLight3D.withColor(Mibo.Color.rgb 255uy 240uy 210uy)
  |> DirectionalLight3D.withIntensity 1.0f
  |> DirectionalLight3D.withCastsShadows true

let private redPoint =
  PointLight3D.create(Vector3(0.0f, 3.0f, 14.0f), 18.0f)
  |> PointLight3D.withColor Mibo.Color.Red
  |> PointLight3D.withIntensity 3.0f

// ── Layout (identical to the MonoGame probe's zone-3 scene) ──

/// Row of the 5 blocks, in front.
let private zone1Pos(i: int) =
  Vector3(-8.0f + float32 i * 4.0f, 0.0f, -10.0f)

/// Each block type gets a row of 5 instances behind zone 1.
let private zone2RowZ(i: int) = -6.0f + float32 i * 2.0f

/// Instanced rows on the floor, at the back.
let private zone3RowZ(i: int) = 12.0f + float32 i * 1.5f

/// X offsets for the 5 instances inside an instanced row.
let private instanceX = [| -4.0f; -2.0f; 0.0f; 2.0f; 4.0f |]

let private orbitPosition
  (target: Vector3)
  (yaw: float32)
  (pitch: float32)
  (distance: float32)
  =
  target
  + Vector3(
      MathF.Cos(pitch) * MathF.Sin(yaw),
      MathF.Sin(pitch),
      MathF.Cos(pitch) * MathF.Cos(yaw)
    )
    * distance

let private orbitCamera(model: ProbeModel) : Camera3D =
  Camera3D.create
    (orbitPosition model.CamTarget model.CamYaw model.CamPitch model.CamDistance)
    model.CamTarget
    55.0f

// ── Init ──

let private loadBlock(name: string) : BlockEntry = {
  Model = Raylib.LoadModel(modelPath name)
}

let init(_ctx: GameContext) : struct (ProbeModel * Cmd<Msg>) =
  let model = {
    Blocks = [| for name in blockNames -> loadBlock name |]
    Floor = Raylib.LoadModelFromMesh(Raylib.GenMeshCube(26.0f, 1.0f, 14.0f))
    TransparentCube = Raylib.GenMeshCube(1.0f, 1.0f, 1.0f)
    CamTarget = Vector3(0.0f, 0.0f, 4.0f)
    CamYaw = 0.0f
    CamPitch = 0.65f
    CamDistance = 33.0f
    SplitScreen = false
    Input = ActionState.empty
  }

  model, Cmd.none

// ─────────────────────────────────────────────────────────────
// Update — orbit camera only, the scene itself is static
// ─────────────────────────────────────────────────────────────

let private orbitSpeed = 1.5f // rad/s
let private zoomSpeed = 20.f // units/s
let private panSpeed = 10.f // units/s

let private clampPitch v = Math.Clamp(v, 0.05f, 1.45f)
let private clampDistance v = Math.Clamp(v, 2.f, 120.f)

let private applyPreset
  (target: Vector3)
  (pitch: float32)
  (distance: float32)
  (model: ProbeModel)
  =
  {
    model with
        CamTarget = target
        CamYaw = 0.f
        CamPitch = pitch
        CamDistance = distance
  }

let update (msg: Msg) (model: ProbeModel) : struct (ProbeModel * Cmd<Msg>) =
  match msg with
  | InputChanged input -> { model with Input = input }, Cmd.none
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    let input = model.Input

    let yaw =
      model.CamYaw
      + (if input.Held.Contains OrbitLeft then
           -orbitSpeed * dt
         else
           0.f)
      + (if input.Held.Contains OrbitRight then
           orbitSpeed * dt
         else
           0.f)

    let pitch =
      model.CamPitch
      + (if input.Held.Contains OrbitUp then orbitSpeed * dt else 0.f)
      + (if input.Held.Contains OrbitDown then
           -orbitSpeed * dt
         else
           0.f)
      |> clampPitch

    let distance =
      model.CamDistance
      + (if input.Held.Contains ZoomIn then -zoomSpeed * dt else 0.f)
      + (if input.Held.Contains ZoomOut then zoomSpeed * dt else 0.f)
      |> clampDistance

    let right = Vector3(MathF.Cos model.CamYaw, 0.f, -MathF.Sin model.CamYaw)

    let target =
      model.CamTarget
      + (if input.Held.Contains PanLeft then
           -right * panSpeed * dt
         else
           Vector3.Zero)
      + (if input.Held.Contains PanRight then
           right * panSpeed * dt
         else
           Vector3.Zero)
      + (if input.Held.Contains PanUp then
           Vector3(0.f, panSpeed * dt, 0.f)
         else
           Vector3.Zero)
      + (if input.Held.Contains PanDown then
           Vector3(0.f, -panSpeed * dt, 0.f)
         else
           Vector3.Zero)

    let moved = {
      model with
          CamYaw = yaw
          CamPitch = pitch
          CamDistance = distance
          CamTarget = target
    }

    let model =
      if input.Started.Contains ViewZone1 then
        applyPreset (Vector3(0.f, 0.5f, -10.f)) 0.35f 14.f moved
      elif input.Started.Contains ViewZone2 then
        applyPreset (Vector3(0.f, 0.5f, -2.f)) 0.4f 15.f moved
      elif input.Started.Contains ViewZone3 then
        applyPreset (Vector3(0.f, 0.5f, 14.f)) 0.4f 18.f moved
      elif input.Started.Contains ViewOverview then
        applyPreset (Vector3(0.f, 0.f, 4.f)) 0.65f 33.f moved
      else
        moved

    let model =
      if input.Started.Contains ToggleSplitScreen then
        {
          model with
              SplitScreen = not model.SplitScreen
        }
      else
        model

    model, Cmd.none

// ── View ──

let private drawFloorScene (model: ProbeModel) (buffer: RenderBuffer3D) =
  // Floor: made semi-transparent (Opacity < 1) so it routes through PR #99's
  // deferred, far-to-near sorted alpha-blend pass with depth writes off. Because
  // the depth pass is binary, it is also excluded from shadow + scene-depth
  // collection — eyeball that the slab casts no shadow under the sun.
  let floorMaterial = {
    Material3D.colored(Color(110, 112, 120, 255)) with
        Opacity = 0.6f
  }

  buffer
    .modelWith(
      model.Floor,
      Raymath.MatrixTranslate(0.0f, -0.5f, 14.0f),
      floorMaterial
    )
    .drop()

  for i = 0 to model.Blocks.Length - 1 do
    let p = zone1Pos i + Vector3(0.0f, 0.0f, 20.0f)

    buffer
      .model(model.Blocks[i].Model, Raymath.MatrixTranslate(p.X, p.Y, p.Z))
      .drop()

  for i = 0 to model.Blocks.Length - 1 do
    let z = zone3RowZ i

    for x in instanceX do
      buffer
        .model(model.Blocks[i].Model, Raymath.MatrixTranslate(x, 0.0f, z))
        .drop()

  // Transparency probe (PR #99): a stack of three semi-transparent cubes at
  // differing depths. Each has a distinct opacity so the far-to-near sort is
  // visible: the nearest cube blends over the farther ones, and all blend over
  // the transparent floor. They cast no shadows and write no depth.
  let probeMat opacity = {
    Material3D.colored(Color(80, 180, 240, 255)) with
        Opacity = opacity
  }

  let probeTransform(x: float32, y: float32, z: float32) =
    Raymath.MatrixMultiply(
      Raymath.MatrixScale(2.0f, 2.0f, 2.0f),
      Raymath.MatrixTranslate(x, y, z)
    )

  buffer
    .mesh(
      model.TransparentCube,
      probeTransform(-3.0f, 1.0f, 16.0f),
      probeMat 0.3f
    )
    .drop()

  buffer
    .mesh(
      model.TransparentCube,
      probeTransform(0.0f, 1.5f, 14.0f),
      probeMat 0.5f
    )
    .drop()

  buffer
    .mesh(
      model.TransparentCube,
      probeTransform(3.0f, 2.0f, 12.0f),
      probeMat 0.8f
    )
    .drop()

  buffer

let private splitView (model: ProbeModel) (buffer: RenderBuffer3D) =
  let camera = orbitCamera model

  (buffer
    .beginCameraWith(Camera3D.splitScreenLeft camera (Color(90, 110, 150, 255)))
    .setAmbientLight(warmAmbient)
    .addDirectionalLight(sunLight)
   |> drawFloorScene model)
    .endCamera()
    .drop()

  (buffer.addPointLight redPoint).drop()

  (buffer
    .beginCameraWith(Camera3D.splitScreenRight camera (Color(8, 10, 16, 255)))
    .setAmbientLight
     indoorAmbient
   |> drawFloorScene model)
    .endCamera()
    .drop()

let private ambient: AmbientLight3D = {
  Color = Mibo.Color.White
  Intensity = 0.35f
}

let private dirLight(castsShadows: bool) : DirectionalLight3D = {
  Direction = Vector3(0.6f, -1.f, 0.35f)
  Color = Mibo.Color.White
  Intensity = 1.f
  CastsShadows = castsShadows
}

let private zonesView (model: ProbeModel) (buffer: RenderBuffer3D) =
  let camera = orbitCamera model

  let noShadow =
    buffer
      .beginCameraWith(
        Camera3D.render camera |> Camera3D.withClear(Color(30, 34, 40, 255))
      )
      .setAmbientLight(ambient)
      .addDirectionalLight(dirLight false)

  for i = 0 to model.Blocks.Length - 1 do
    let p = zone1Pos i

    noShadow
      .model(model.Blocks[i].Model, Raymath.MatrixTranslate(p.X, p.Y, p.Z))
      .drop()

  for i = 0 to model.Blocks.Length - 1 do
    let z = zone2RowZ i

    for x in instanceX do
      noShadow
        .model(model.Blocks[i].Model, Raymath.MatrixTranslate(x, 0.0f, z))
        .drop()

  noShadow.endCamera().drop()

  let shadowed =
    buffer
      .beginCamera(camera)
      .setAmbientLight(ambient)
      .addDirectionalLight(dirLight true)

  shadowed
    .model(model.Floor, Raymath.MatrixTranslate(0.0f, -0.5f, 14.0f))
    .drop()

  for i = 0 to model.Blocks.Length - 1 do
    let p = zone1Pos i + Vector3(0.0f, 0.0f, 20.0f)

    shadowed
      .model(model.Blocks[i].Model, Raymath.MatrixTranslate(p.X, p.Y, p.Z))
      .drop()

  for i = 0 to model.Blocks.Length - 1 do
    let z = zone3RowZ i

    for x in instanceX do
      shadowed
        .model(model.Blocks[i].Model, Raymath.MatrixTranslate(x, 0.0f, z))
        .drop()

  shadowed.endCamera().drop()

let view (_ctx: GameContext) (model: ProbeModel) (buffer: RenderBuffer3D) =
  if model.SplitScreen then
    splitView model buffer
  else
    zonesView model buffer

[<EntryPoint>]
let main _ =
  Raylib.SetTraceLogLevel(TraceLogLevel.Warning)

  let program =
    Program.mkProgram init update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "ModelProbe (raylib) — split-screen lights"
    })
    |> Program.withInput
    |> Program.withSubscription(fun ctx _model ->
      InputMapper.subscribeStatic inputMap InputChanged ctx)
    |> Program.withTick Tick
    |> Program.withRenderer(fun () ->
      Renderer3D.create (ForwardPbrPipeline()) view)

  let game = new RaylibGame<ProbeModel, Msg>(program)
  game.Run()
  0
