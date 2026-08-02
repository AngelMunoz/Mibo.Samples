module ModelProbe

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Input

// ─────────────────────────────────────────────────────────────
// ModelProbe — minimal PBR forward + shadow atlas probe.
//
// One scene, three zones in a single frame:
//   Zone 1 (front): 5 different kenney blocks, non-instanced (Draw3D.drawModel)
//   Zone 2 (mid):   the same 5 blocks, instanced (Draw3D.drawInstanced)
//   Zone 3 (back):  both draw styles on a floor, with a shadow-casting light
//
// Zones 1+2 render in a camera block whose directional light does NOT cast
// shadows; zone 3 renders in a second camera block (same camera, no clear)
// whose light casts shadows. This isolates plain PBR vs instanced PBR vs
// PBR + shadow atlas so backend (DX12 vs Vulkan) differences can be eyeballed.
//
// Camera: orbit — arrows rotate, W/S zoom, A/D pan left/right,
// PageUp/PageDown raise/lower the target. Presets: 0 overview, 1/2/3 zones.
// 4 toggles split-screen: two half-screen camera blocks with different light
// environments (outdoor sun + shadows left, dim indoor right) — the probe for
// per-camera-block light/shadow scoping.
// ─────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────
// Input
// ─────────────────────────────────────────────────────────────

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

// ─────────────────────────────────────────────────────────────
// Assets
// ─────────────────────────────────────────────────────────────

let private blockNames = [|
  "block-grass"
  "block-grass-corner"
  "block-grass-curve"
  "block-grass-hexagon"
  "block-grass-large"
|]

let private modelPath name = $"kenney_platformer-kit/Models/{name}"

// ─────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────

type BlockEntry = {
  Name: string
  Model: Microsoft.Xna.Framework.Graphics.Model
  Parts: struct (PrimitiveMesh * Material3D)[]
  /// Absolute transform of the first mesh's parent bone. Instanced draws grab
  /// raw vertex buffers (bone-local space), so this must be baked into each
  /// instance transform — see Platformer3D/MonoGame/View.fs.
  Bone: Matrix
}

type Model = {
  Blocks: BlockEntry[]
  Floor: PrimitiveMesh
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

// ─────────────────────────────────────────────────────────────
// Init
// ─────────────────────────────────────────────────────────────

let private blockBounds = BoundingSphere(Vector3.Zero, 2.f)

let private wrapPartAsPrimitive(part: ModelMeshPart) : PrimitiveMesh = {
  Vertices = part.VertexBuffer
  Indices = part.IndexBuffer
  PrimitiveCount = part.PrimitiveCount
  Bounds = blockBounds
}

let private loadBlock (assets: IAssets) (name: string) : BlockEntry =
  let m = assets.Model(modelPath name)

  let bone =
    if m.Bones.Count > 0 && m.Meshes.Count > 0 then
      let absolute = Array.zeroCreate<Matrix> m.Bones.Count
      m.CopyAbsoluteBoneTransformsTo absolute
      absolute[m.Meshes[0].ParentBone.Index]
    else
      Matrix.Identity

  let parts = [|
    for mesh in m.Meshes do
      for part in mesh.MeshParts do
        struct (wrapPartAsPrimitive part,
                {
                  Material3D.fromModelMeshPart part with
                      Roughness = 0.65f
                      Metallic = 0.2f
                })
  |]

  {
    Name = name
    Model = m
    Parts = parts
    Bone = bone
  }

let init(ctx: GameContext) : struct (Model * Cmd<Msg>) =
  let gd = MonoGameGameContext.getGraphicsDevice ctx
  let assets = GameContext.getService<IAssets> ctx
  let primitives = Primitive3D.create gd

  let model = {
    Blocks = [| for name in blockNames -> loadBlock assets name |]
    Floor = primitives.Cylinder
    CamTarget = Vector3(0.f, 0.f, 4.f)
    CamYaw = 0.f
    CamPitch = 0.65f
    CamDistance = 33.f
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

let private clampPitch v = MathHelper.Clamp(v, 0.05f, 1.45f)
let private clampDistance v = MathHelper.Clamp(v, 2.f, 120.f)

let private applyPreset
  (target: Vector3)
  (pitch: float32)
  (distance: float32)
  (model: Model)
  =
  {
    model with
        CamTarget = target
        CamYaw = 0.f
        CamPitch = pitch
        CamDistance = distance
  }

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
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

    // Pan the orbit target on the camera's ground-right axis (A/D) and
    // vertically (PageUp/PageDown).
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

// ─────────────────────────────────────────────────────────────
// View
// ─────────────────────────────────────────────────────────────

/// Zone 1: one position per block, a spaced-out row in front.
let private zone1Pos(i: int) =
  Vector3(-8.f + float32 i * 4.f, 0.f, -10.f)

/// Zone 2: each block type gets a row of 5 instances behind zone 1.
let private zone2RowZ(i: int) = -6.f + float32 i * 2.f

/// Zone 3: drawModel row + instanced rows on the floor, at the back.
let private zone3RowZ(i: int) = 12.f + float32 i * 1.5f

/// X offsets for the 5 instances inside an instanced row.
let private instanceX = [| -4.f; -2.f; 0.f; 2.f; 4.f |]

let private ambient: AmbientLight3D = {
  Color = Mibo.Color.White
  Intensity = 0.35f
}

let private dirLight(castsShadows: bool) : DirectionalLight3D = {
  Direction = System.Numerics.Vector3(0.6f, -1.f, 0.35f)
  Color = Mibo.Color.White
  Intensity = 1.f
  CastsShadows = castsShadows
}

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

let private orbitCamera(model: Model) : Camera3D = {
  Position =
    orbitPosition model.CamTarget model.CamYaw model.CamPitch model.CamDistance
  Target = model.CamTarget
  Up = Vector3.UnitY
  FovY = MathHelper.ToRadians(55.0f)
  NearPlane = 0.1f
  FarPlane = 200.f
  Projection = CameraProjection.Perspective
}

// ─────────────────────────────────────────────────────────────
// Split-screen probe — per-camera-block lights and shadows.
//
// One frame, two camera blocks over two half-screen viewports; same orbit
// camera, same scene (the zone-3 layout: floor + model row + instanced rows).
//
//   LEFT  "outdoor": warm ambient + shadow-casting sun. Sky-blue clear.
//   ── between blocks: a red point light — a frame default, so only camera
//      blocks after it may see it.
//   RIGHT "indoor":  dim blue ambient, no sun (no shadows). Near-black clear.
//
// Eyeball checklist (per-block scoping semantics):
//   * Sun shadows on the left half only; the right half's block has no
//     shadow-casting light, so it must not sample the left block's atlas.
//   * The red point light glows on the RIGHT half only — the left block
//     predates it, and the right block (which resets) must see it exactly once.
//   * The right half stays dim blue — the left block's warm ambient and sun
//     must not leak across (reset semantics).
// ─────────────────────────────────────────────────────────────

let private warmAmbient: AmbientLight3D = {
  Color = Mibo.Color.rgb 255uy 220uy 180uy
  Intensity = 0.4f
}

let private indoorAmbient: AmbientLight3D = {
  Color = Mibo.Color.rgb 90uy 110uy 200uy
  Intensity = 0.25f
}

let private sunLight: DirectionalLight3D = {
  (dirLight true) with
      Color = Mibo.Color.rgb 255uy 240uy 210uy
}

let private redPoint: PointLight3D =
  PointLight3D.create(System.Numerics.Vector3(0.f, 3.f, 14.f), 18.f)
  |> PointLight3D.withColor Mibo.Color.Red
  |> PointLight3D.withIntensity 3.f
  |> PointLight3D.withCastsShadows true

/// The zone-3 layout: floor slab + non-instanced model row + instanced rows.
let private drawFloorScene (model: Model) (buffer: RenderBuffer3D) =
  // Floor: unit cube primitive scaled into a slab, top face at y = 0.
  let floorTransform =
    Matrix.CreateScale(26.f, 1.f, 14.f)
    * Matrix.CreateTranslation(0.f, -0.5f, 14.f)

  let floorMaterial =
    Material3D.colored(Microsoft.Xna.Framework.Color(110, 112, 120))

  buffer.mesh(model.Floor, floorTransform, floorMaterial).drop()

  for i = 0 to model.Blocks.Length - 1 do
    let p = zone1Pos i + Vector3(0.f, 0.f, 20.f)

    buffer.model(model.Blocks[i].Model, Matrix.CreateTranslation p).drop()

  for i = 0 to model.Blocks.Length - 1 do
    let block = model.Blocks[i]
    let z = zone3RowZ i

    let transforms = [|
      for x in instanceX -> block.Bone * Matrix.CreateTranslation(x, 0.f, z)
    |]

    for struct (mesh, material) in block.Parts do
      buffer.instanced(mesh, transforms, material, transforms.Length).drop()

  buffer

let private splitView
  (ctx: GameContext)
  (model: Model)
  (buffer: RenderBuffer3D)
  =
  let camera = orbitCamera model
  let bounds = Rectangle(0, 0, ctx.WindowWidth, ctx.WindowHeight)

  // ── Left half: outdoor — warm ambient + shadow-casting sun ──
  (buffer
    .beginCameraWith(
      Camera3D.splitScreenLeft
        camera
        (Microsoft.Xna.Framework.Color(90, 110, 150))
        bounds
    )
    .setAmbientLight(warmAmbient)
    .addDirectionalLight
     sunLight
   |> drawFloorScene model)
    .endCamera()
    .drop()

  // ── Between blocks: a red point light. Emitted outside any camera block it
  // joins the frame defaults — only blocks after this point may see it. ──
  buffer.addPointLight(redPoint).drop()

  // ── Right half: indoor — dim blue ambient, no sun (no shadow-casting
  // light, so this block renders no shadow map). ──
  (buffer
    .beginCameraWith(
      Camera3D.splitScreenRight
        camera
        (Microsoft.Xna.Framework.Color(8, 10, 16))
        bounds
    )
    .setAmbientLight(indoorAmbient)
   |> drawFloorScene model)
    .endCamera()
    .drop()

let private zonesView (model: Model) (buffer: RenderBuffer3D) =
  let camera = orbitCamera model

  // ── Steps 1+2: lit (no shadows), no floor ──
  let noShadow =
    buffer
      .beginCameraWith(
        Camera3D.render camera
        |> Camera3D.withClear(Microsoft.Xna.Framework.Color(30, 34, 40))
      )
      .setAmbientLight(ambient)
      .addDirectionalLight(dirLight false)

  // Step 1 — non-instanced
  for i = 0 to model.Blocks.Length - 1 do
    let p = zone1Pos i

    noShadow.model(model.Blocks[i].Model, Matrix.CreateTranslation p).drop()

  // Step 2 — instanced, different positions
  for i = 0 to model.Blocks.Length - 1 do
    let block = model.Blocks[i]
    let z = zone2RowZ i

    let transforms = [|
      for x in instanceX -> block.Bone * Matrix.CreateTranslation(x, 0.f, z)
    |]

    for struct (mesh, material) in block.Parts do
      noShadow.instanced(mesh, transforms, material, transforms.Length).drop()

  noShadow.endCamera().drop()

  // ── Step 3: same setup + floor + shadows. Same camera, NO clear — this
  // block composites over the previous one keeping color and depth. ──
  let shadowed =
    buffer
      .beginCamera(camera)
      .setAmbientLight(ambient)
      .addDirectionalLight(dirLight true)

  // Floor: unit cube primitive scaled into a slab, top face at y = 0.
  let floorTransform =
    Matrix.CreateScale(26.f, 1.f, 14.f)
    * Matrix.CreateTranslation(0.f, -0.5f, 14.f)

  let floorMaterial =
    Material3D.colored(Microsoft.Xna.Framework.Color(110, 112, 120))

  shadowed.mesh(model.Floor, floorTransform, floorMaterial).drop()

  // Non-instanced row on the floor
  for i = 0 to model.Blocks.Length - 1 do
    let p = zone1Pos i + Vector3(0.f, 0.f, 20.f)

    shadowed.model(model.Blocks[i].Model, Matrix.CreateTranslation p).drop()

  // Instanced rows on the floor
  for i = 0 to model.Blocks.Length - 1 do
    let block = model.Blocks[i]
    let z = zone3RowZ i

    let transforms = [|
      for x in instanceX -> block.Bone * Matrix.CreateTranslation(x, 0.f, z)
    |]

    for struct (mesh, material) in block.Parts do
      shadowed.instanced(mesh, transforms, material, transforms.Length).drop()

  shadowed.endCamera().drop()

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
  if model.SplitScreen then
    splitView ctx model buffer
  else
    zonesView model buffer

// ─────────────────────────────────────────────────────────────
// Plain pipeline — TEMPORARY isolation harness.
//
// Renders DrawModel commands with raw MonoGame (Model.Draw + the baked
// BasicEffects), ignoring lights, shadows, instancing, effects — everything
// else in the buffer. No Mibo shader is ever bound. If an artifact shows up
// here as well, it comes from the MonoGame backend, not from the Mibo
// forward pipeline.
// ─────────────────────────────────────────────────────────────

type PlainPipeline() =
  interface IRenderPipeline3D with
    member _.Execute(gameCtx, _gameTime, buffer, _rtPool) =
      let gd = MonoGameGameContext.getGraphicsDevice gameCtx
      let vp = gd.Viewport

      let aspect =
        if vp.Height > 0 then
          float32 vp.Width / float32 vp.Height
        else
          1.f

      let mutable viewM = Matrix.Identity
      let mutable projM = Matrix.Identity

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        | Command3D.BeginCamera camera ->
          viewM <-
            Matrix.CreateLookAt(camera.Position, camera.Target, camera.Up)

          projM <-
            Matrix.CreatePerspectiveFieldOfView(
              camera.FovY,
              aspect,
              camera.NearPlane,
              camera.FarPlane
            )
        | Command3D.DrawModel(model, transform)
        | Command3D.DrawModelWith(model, transform, _) ->
          for mesh in model.Meshes do
            for effect in mesh.Effects do
              match effect with
              | :? BasicEffect as basic -> basic.EnableDefaultLighting()
              | _ -> ()

          model.Draw(transform, viewM, projM)
        | _ -> ()

    member _.Initialize() = ()
    member _.Shutdown() = ()

/// Zone 1 only, one camera — the minimal input for PlainPipeline.
let plainView (_ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
  let cam = buffer.beginCamera(orbitCamera model)

  for i = 0 to model.Blocks.Length - 1 do
    buffer
      .model(model.Blocks[i].Model, Matrix.CreateTranslation(zone1Pos i))
      .drop()

  cam.endCamera().drop()

/// Debugging aid: true renders zone 1 through PlainPipeline (raw MonoGame),
/// false renders the full three-zone scene through the PBR forward pipeline.
let usePlainPipeline = false

// ─────────────────────────────────────────────────────────────
// Program
// ─────────────────────────────────────────────────────────────

/// Builds the full Mibo MonoGame program with the content root configured for
/// the MonoGame content pipeline. The thin client projects (DesktopVK,
/// WindowsDX12) pass this directly to MiboGame.
let create() : MonoGameProgram<Model, Msg> =
  Program.mkProgram init update
  |> Program.withConfig(fun cfg -> {
    cfg with
        Width = 1280
        Height = 720
        Title = "ModelProbe"
  })
  |> Program.withInput
  |> Program.withSubscription(fun ctx _model ->
    InputMapper.subscribeStatic inputMap InputChanged ctx)
  |> Program.withTick Tick
  |> Program.withRenderer(fun () ->
    if usePlainPipeline then
      Renderer3D.createWith
        {
          ClearColor = ValueSome(Microsoft.Xna.Framework.Color(30, 34, 40))
        }
        (PlainPipeline())
        plainView
    else
      let pipeline =
        ForwardPipeline(
          shadowBias = ShadowBiasConfig.defaults,
          shadowAtlas = {
            ShadowAtlasConfig.defaults with
                Resolution = 1024 * 4
                GridSnapSize = 16.0f
          }
        )

      Renderer3D.create pipeline view)
  |> MonoGameProgram.ofProgram
  |> MonoGameProgram.withConfig(fun (game, _deviceManager) ->
    game.Content.RootDirectory <- "Content")
