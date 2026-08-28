module AnimatedInstancing.MonoGame.View

open System
open Microsoft.Xna.Framework
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.AssetsService
open Mibo.Layout3D
open Mibo.Animation
open AnimatedInstancing
open AnimatedInstancing.MonoGame.Types

let private groundCellMaterial =
  Material3D.colored(Microsoft.Xna.Framework.Color(110, 112, 120))

let private glassCellMaterial = {
  Material3D.colored(Microsoft.Xna.Framework.Color(130, 200, 255)) with
      Opacity = 0.4f
      Roughness = 0.1f
}

// ─────────────────────────────────────────────────────────────
// Instanced terrain probe — the crowd's floor through the grid API
// ─────────────────────────────────────────────────────────────

// Unit cube primitive, created on the first Draw (the device exists then).
let mutable private cubeMesh: PrimitiveMesh voption = ValueNone

// One context for both cell kinds: the renderer groups by key, so the Ground
// cells emit one opaque DrawInstanced (inline, casts shadows) and the
// semi-transparent Glass cells emit their own command — which defers whole to
// the sorted pass (a transparent material covers every instance of its batch).
let private terrainCtx =
  InstancedRenderContext<TerrainCell, TerrainCell>(
    getKey = id
    , getMeshesAndMaterial =
      fun cell ->
        match cubeMesh with
        | ValueSome m -> [|
            struct (m,
                    (match cell with
                     | Ground -> groundCellMaterial
                     | Glass -> glassCellMaterial))
          |]
        | ValueNone -> Array.empty
    , getTransform =
      fun worldPos _ ->
        // Unit cube scaled to one cell; -0.5 puts the ground layer's top face at
        // y = 0 (the mannequins' feet) and floats the glass layer above them.
        Matrix.CreateScale(CrowdSpec.spacing, 1.0f, CrowdSpec.spacing)
        * Matrix.CreateTranslation(worldPos.X, worldPos.Y - 0.5f, worldPos.Z)
  )

// Rebuilt only when the crowd tier changes the grid's side length.
let mutable private terrainSide = -1
let mutable private terrainGrid = Unchecked.defaultof<CellGrid3D<TerrainCell>>

// Cell layer Y of the glass cells (world Y = 3, so the cubes span 2.5..3.5).
let private glassLayer = 3

let private buildTerrain(side: int) =
  let center = float32(side - 1) * 0.5f

  let grid =
    CellGrid3D.create
      side
      (glassLayer + 1)
      side
      Numerics.Vector3.One
      (Numerics.Vector3(
        -center * CrowdSpec.spacing,
        0.0f,
        -center * CrowdSpec.spacing
      ))

  for z = 0 to side - 1 do
    for x = 0 to side - 1 do
      CellGrid3D.set x 0 z Ground grid

  // Glass cells above the first three mannequins (instances 0, 1, 2 sit at
  // columns 0..2 of row 0).
  for i = 0 to 2 do
    CellGrid3D.set (i % side) glassLayer (i / side) Glass grid

  grid

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
        CastsShadows = model.ShadowsOn
      }
    )
    .drop()

  // Instanced terrain: the floor is a cell grid rendered through the volume
  // renderer (the API voxel terrain uses), plus the glass cells above the
  // first mannequins. Same world as the crowd, so the orbiting camera carries
  // it around with them.
  match cubeMesh with
  | ValueNone ->
    let primitives =
      Primitive3D.create(MonoGameGameContext.getGraphicsDevice ctx)

    cubeMesh <- ValueSome primitives.Cube
  | ValueSome _ -> ()

  let side = CrowdSpec.gridSide crowd.Count

  if terrainSide <> side then
    terrainSide <- side
    terrainGrid <- buildTerrain side

  let extent = float32 side * CrowdSpec.spacing + 8.0f
  let half = extent * 0.5f

  let bounds = {
    Mibo.Layout3D.BoundingBox.Min = Numerics.Vector3(-half, -1.0f, -half)
    Max = Numerics.Vector3(half, 4.0f, half)
  }

  terrainCtx.ResetFrameBuffers()

  terrainCtx.RenderCellGridVolumeInstanced(buffer, bounds, terrainGrid)

  // THE probe: one pose evaluation per instance into the reused pose array,
  // then a single skinned+instanced draw call (DrawAnimatedModelInstanced).
  // Pose evaluation is parallelized — computePoseInto only reads the clip
  // data + per-instance state and reuses each pose's backing arrays (grown
  // once, then no per-frame allocation), and each iteration writes a
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
            Animation3DState.computePoseInto
              animMesh
              crowd.States[i]
              crowd.Poses[i]
      )
      |> ignore

    let am: AnimatedModel = {
      Model = model.Rig.Model
      Mesh = model.Rig.Mesh
      State = crowd.States[0]
    }

    let texture = assets.Texture "mannequin_texture"

    // Opacity probe: the whole instanced crowd shares one material whose
    // Opacity cycles through CrowdSpec.opacitySteps (key O).
    // 1.0  -> inline opaque draw, casts shadows
    // <1.0 -> deferred to the sorted transparent pass, no shadows
    // 0.0  -> nothing drawn
    let baseMaterial = Material3D.defaults |> Material3D.withAlbedoMap texture

    let material = {
      baseMaterial with
          Opacity = CrowdSpec.opacitySteps[model.OpacityIndex]
    }

    // Per-instance color probe (key C): every 3rd instance is tinted and
    // semi-transparent, so the draw must defer to the transparent pass even
    // with an opaque material. Rebuilt only when the tier changes.
    if model.UseInstanceColors then
      if model.InstanceColors.Length <> crowd.Count then
        model.InstanceColors <-
          Array.init crowd.Count (fun i ->
            if i % 3 = 0 then
              Color(255, 90, 90, 110)
            else
              Microsoft.Xna.Framework.Color.White)

      buffer
        .animatedModelInstanced(
          am,
          crowd.Transforms,
          crowd.Poses,
          material = All material,
          colors = model.InstanceColors
        )
        .drop()
    else
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

  let shadows = if model.ShadowsOn then "on" else "off"

  line
    35.0f
    $"Instances: {crowd.Count}  Tier: {crowd.TierIndex + 1}/{CrowdSpec.counts.Length}  Clips: Walking_A/Running_A/Idle_A (i mod 3)"

  line 60f $"Anim: {paused}  Shadows: {shadows}"

  let opacity = CrowdSpec.opacitySteps[model.OpacityIndex]

  line
    85.0f
    $"Opacity: {opacity} (O)  [1.0 opaque+shadows | <1 blended, no shadows | 0 hidden]"

  let colors =
    if model.UseInstanceColors then
      "on (every 3rd tinted, alpha 110)"
    else
      "off"

  line 110.0f $"Instance colors: {colors} (C)"

  line
    135.0f
    "1-4 tiers | +/- step | Space pause | S shadows | O opacity | C colors"
