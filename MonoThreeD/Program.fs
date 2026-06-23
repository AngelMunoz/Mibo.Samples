module MonoThreeD.Program

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Animation

// S0 — scaffold + content pipeline + render + animate a model on screen.
//
// Renders the Kenney character model with the native ForwardPipeline and plays
// the "walk" animation clip in a loop, to verify:
//   - the content pipeline (.glb -> XNB -> Model) round-trips
//   - the 3D renderer draws skinned geometry
//   - AssimpNetter loads animation clips from the raw .glb (copied to the
//     output directory; the content pipeline does not preserve animation data)
//   - GPU skinning via Draw3D.drawSkinnedMesh + Animation3DState works
//
// Lights use radical colours (magenta directional + green ambient) so lighting
// is visually obvious. The camera uses the full window — the backend now
// recomputes the projection aspect from the active viewport.

type XnaModel = Microsoft.Xna.Framework.Graphics.Model

// Path to the raw .glb (copied to output dir via fsproj <Content>). Assimp
// loads clips + skeleton from this; the XNB Model is loaded separately via the
// content pipeline for the mesh/texture data.
let private rawModelPath =
  System.IO.Path.Combine(
    AppContext.BaseDirectory,
    "animations",
    "character-oobi.glb"
  )

type Model = {
  PlayerModel: XnaModel
  AnimatedMesh: AnimatedMesh voption
  AnimState: Animation3DState
  ColormapTexture: Texture2D
  Cube: PrimitiveMesh
}

[<Struct>]
type Msg = Tick of GameTime

let view (_ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) : unit =
  let camera: Camera3D = {
    Position = Vector3(3.0f, 3.0f, 5.0f)
    Target = Vector3.Zero
    Up = Vector3.Up
    FovY = MathHelper.ToRadians(55.0f)
    NearPlane = 0.1f
    FarPlane = 1000.0f
    Projection = CameraProjection.Perspective
  }

  let config = Camera3D.render camera |> Camera3D.withClear Color.CornflowerBlue

  // Bone palette from the current animation frame (GPU skinning).
  let bones =
    match model.AnimatedMesh with
    | ValueSome mesh -> Animation3DState.computeBonePalette mesh model.AnimState
    | ValueNone -> [||]

  buffer
  |> Draw3D.beginCameraWith config
  |> Draw3D.setAmbientLight {
    Color = Color.White
    Intensity = 1.0f
  }
  |> Draw3D.addDirectionalLight {
    Direction = Vector3.Normalize(Vector3(-0.5f, -1.0f, -0.3f))
    Color = Color.White
    Intensity = 1.0f
    CastsShadows = false
  }
  |> Draw3D.drop

  // Draw each skinned mesh part of the player model with the bone palette.
  // Per §4.1: ModelMeshPart uses its own native effect (SkinnedEffect here);
  // drawSkinnedMesh binds it + uploads the bones.
  for mesh in model.PlayerModel.Meshes do
    for part in mesh.MeshParts do
      Draw3D.drawSkinnedMesh part Matrix.Identity bones buffer |> Draw3D.drop

  // Reference: the same model drawn static via drawModel (BasicEffect path,
  // no skinning) offset to the side, so we can compare colour directly.
  buffer |> Draw3D.drawModel model.PlayerModel (Matrix.CreateTranslation(2.0f, 0.0f, 0.0f)) |> Draw3D.drop

  // Reference: a PBR cube with the colormap texture, to verify the texture
  // itself renders colour (isolates texture/UV from the model).
  let cubeMat = Material3D.defaults |> Material3D.withAlbedoMap model.ColormapTexture
  buffer |> Draw3D.drawMeshPBR model.Cube (Matrix.CreateTranslation(-2.0f, 0.0f, 0.0f)) cubeMat |> Draw3D.drop

  buffer |> Draw3D.endCamera |> Draw3D.drop

let init(ctx: GameContext) : struct (Model * Cmd<Msg>) =
  try
    let assets = GameContext.getService<IAssets> ctx
    let playerModel = assets.Model "kenney_platformer-kit/Models/character-oobi"

    for mesh in playerModel.Meshes do
      for part in mesh.MeshParts do
        match part.Effect with
        | :? SkinnedEffect as se ->
          System.IO.File.AppendAllText(
            @"E:\Mibo.Samples\diag.txt",
            $"SkinnedEffect Texture null={isNull se.Texture} Diffuse={se.DiffuseColor} Alpha={se.Alpha}\n"
          )
        | :? BasicEffect as be ->
          System.IO.File.AppendAllText(
            @"E:\Mibo.Samples\diag.txt",
            $"BasicEffect TextureEnabled={be.TextureEnabled} Texture null={isNull be.Texture} Diffuse={be.DiffuseColor}\n"
          )
        | e -> System.IO.File.AppendAllText(@"E:\Mibo.Samples\diag.txt", $"effect {e.GetType().Name}\n")

    let animatedMesh = assets.AnimatedMesh rawModelPath
    let clips = assets.ModelAnimations rawModelPath
    let animState = Animation3DState.create clips "walk" 60.0f

    let gd = MonoGameGameContext.getGraphicsDevice ctx
    let primitives = Primitive3D.create gd
    let colormapTexture = assets.Texture "kenney_platformer-kit/Models/Textures/colormap_0"

    struct ({
              PlayerModel = playerModel
              AnimatedMesh = animatedMesh
              AnimState = animState
              ColormapTexture = colormapTexture
              Cube = primitives.Cube
            },
            Cmd.none)
  with ex ->
    System.IO.File.WriteAllText(@"E:\Mibo.Samples\diag.txt", $"INIT CRASH: {ex}\n{ex.StackTrace}")
    reraise()

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    let anim = model.AnimState |> Animation3DState.update dt
    struct ({ model with AnimState = anim }, Cmd.none)

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo MonoGame 3D (MonoThreeD)"
          TargetFPS = 120
    })
    |> Program.withInput
    |> Program.withTick Tick
    |> Program.withRenderer(fun () ->
      Renderer3D.create (ForwardPipeline()) view)

  let game = new MiboGame<Model, Msg>(program)
  game.Content.RootDirectory <- "Content"
  game.Run()
  0
