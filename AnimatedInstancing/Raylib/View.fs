module AnimatedInstancing.Raylib.View

open System
open System.Numerics
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Layout
open Mibo.Layout3D
open Mibo.Animation
open AnimatedInstancing
open AnimatedInstancing.Raylib.Types

let private groundCellMaterial =
  Material3D.colored(Raylib_cs.Color(110, 112, 120, 255))

let private glassCellMaterial = {
  Material3D.colored(Raylib_cs.Color(130, 200, 255, 255)) with
      Opacity = 0.4f
      Roughness = 0.1f
}

// ─────────────────────────────────────────────────────────────
// Instanced terrain probe — the crowd's floor through the grid API
// ─────────────────────────────────────────────────────────────

// Unit cube mesh, created on the first Draw (the GL context exists then).
let mutable private cubeMesh: Raylib_cs.Mesh voption = ValueNone

// World Y of the glass cells (the cubes span 2.5..3.5).
let private glassLayer = 3

// One context for both cell kinds: the renderer groups by key, so the Ground
// cells emit one opaque DrawMeshInstanced (inline, casts shadows) and the
// semi-transparent Glass cells emit their own command — which defers whole to
// the sorted pass (a transparent material covers every instance of its batch).
// The vertical position rides in the cell: ground cubes sit at the
// mannequins' feet, glass cubes float at the glass layer's world Y.
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
      fun basePos cell ->
        // Unit cube scaled to one cell; ground tops land at y = 0 (the
        // mannequins' feet) and the glass floats at layer 3 (spanning
        // 2.5..3.5). The vertical is data in the cell kind.
        let y =
          match cell with
          | Ground -> -0.5f
          | Glass -> float32 glassLayer - 0.5f

        Raymath.MatrixMultiply(
          Raymath.MatrixScale(CrowdSpec.spacing, 1.0f, CrowdSpec.spacing),
          Raymath.MatrixTranslate(basePos.X, y, basePos.Z)
        )
  )

// Rebuilt only when the crowd tier changes the grid's side length.
let mutable private terrainSide = -1
let mutable private terrainGrid = Unchecked.defaultof<CellGrid2D<TerrainCell>>

let private buildTerrain(side: int) =
  let center = float32(side - 1) * 0.5f

  // Cell size stays 1: the transform scales a unit cube by the crowd
  // spacing, exactly as the retired voxel grid did.
  let grid =
    CellGrid2D.create
      side
      side
      (Vector2(1f, 1f))
      (Vector2(-center * CrowdSpec.spacing, -center * CrowdSpec.spacing))

  for z = 0 to side - 1 do
    for x = 0 to side - 1 do
      CellGrid2D.set x z Ground grid

  // Glass cells above the first three mannequins (instances 0, 1, 2 sit at
  // columns 0..2 of row 0).
  for i = 0 to 2 do
    CellGrid2D.set (i % side) (i / side) Glass grid

  grid

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
    CastsShadows = model.ShadowsOn
  }
  |> Draw3D.drop

  // Instanced terrain: the floor is a cell grid rendered through the volume
  // renderer (the API voxel terrain uses), plus the glass cells above the
  // first mannequins. Same world as the crowd, so the orbiting camera carries
  // it around with them.
  match cubeMesh with
  | ValueNone ->
    let mutable m = Raylib.GenMeshCube(1.0f, 1.0f, 1.0f)
    Raylib.UploadMesh(&m, false)
    cubeMesh <- ValueSome m
  | ValueSome _ -> ()

  let side = CrowdSpec.gridSide crowd.Count

  if terrainSide <> side then
    terrainSide <- side
    terrainGrid <- buildTerrain side

  let extent = float32 side * CrowdSpec.spacing + 8.0f
  let half = extent * 0.5f

  terrainCtx.ResetFrameBuffers()

  // The old volume's XZ extent becomes the window, in world units; the
  // vertical (ground vs glass) is data in the cells now.
  terrainCtx.RenderWindowInstanced(
    buffer,
    int -half,
    int -half,
    int half,
    int half,
    terrainGrid
  )

  // THE probe: one pose evaluation per instance into the reused pose array,
  // then a single skinned+instanced draw call (one DrawSkinnedMeshInstanced
  // per sub-mesh). computePoseInto grows each pose's backing arrays once and
  // reuses them thereafter — no per-frame allocation; only tier changes
  // reallocate the outer array. Pose evaluation is parallelized (each
  // iteration writes a distinct Poses slot) and skipped while paused (the
  // states, and therefore the poses, don't change).
  match model.AnimMesh with
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

    let am = AnimatedModel.create animMesh crowd.States[0]

    // Opacity probe: the whole instanced crowd shares one material whose
    // Opacity cycles through CrowdSpec.opacitySteps (key O).
    // 1.0  -> inline opaque draw, casts shadows
    // <1.0 -> deferred to the sorted transparent pass, no shadows
    // 0.0  -> nothing drawn
    let crowdMaterial = {
      model.CrowdMaterial with
          Opacity = CrowdSpec.opacitySteps[model.OpacityIndex]
    }

    buffer.animatedModelInstanced(
      am,
      crowd.Transforms,
      crowd.Poses,
      material = MaterialOverride.All crowdMaterial
    )
    |> ignore
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

  let shadows = if model.ShadowsOn then "on" else "off"

  line
    35.0f
    $"Instances: {crowd.Count}  Tier: {crowd.TierIndex + 1}/{CrowdSpec.counts.Length}  Clips: Walking_A/Running_A/Idle_A (i mod 3)"

  line 60f $"Anim: {paused}  Shadows: {shadows}"

  let opacity = CrowdSpec.opacitySteps[model.OpacityIndex]

  line
    85.0f
    $"Opacity: {opacity} (O)  [1.0 opaque+shadows | <1 blended, no shadows | 0 hidden]"

  line 110.0f "1-4 tiers | +/- step | Space pause | S shadows | O opacity"
