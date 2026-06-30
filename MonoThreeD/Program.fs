module MonoThreeD.Program

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Animation
open Mibo.Input
open Mibo.Layout3D
open MonoThreeD.Constants
open MonoThreeD.WorldGen
open MonoThreeD.Physics
open MonoThreeD.Systems
open MonoThreeD.Types

// Path to the raw .glb (copied to the output dir via the fsproj <Content> entry).
// Assimp loads animation clips + skeleton from this; the XNB Model is loaded
// separately via the content pipeline for the mesh/texture data.
let private rawModelPath =
  System.IO.Path.Combine(
    AppContext.BaseDirectory,
    "animations",
    "character-oobi.glb"
  )

let loadInitialChunks(model: GameModel) =
  let spawnPos = spawnPosition

  let numericsSpawn =
    System.Numerics.Vector3(spawnPos.X, spawnPos.Y, spawnPos.Z)

  let pcx = int(Math.Floor(float spawnPos.X / float chunkWorldWidth))
  let pcz = int(Math.Floor(float spawnPos.Z / float chunkWorldDepth))

  for x in pcx - chunkLoadRadius .. pcx + chunkLoadRadius do
    for z in pcz - chunkLoadRadius .. pcz + chunkLoadRadius do
      let key = struct (x, z)

      if not(model.Chunks.ContainsKey key) then
        model.Chunks[key] <- generateChunk x z model.Seed

let init(ctx: GameContext) =
  let inputMap: InputMap<GameAction> =
    InputMap.empty
    |> InputMap.key GameAction.MoveLeft KeyCode.A
    |> InputMap.key GameAction.MoveLeft KeyCode.Left
    |> InputMap.key GameAction.MoveRight KeyCode.D
    |> InputMap.key GameAction.MoveRight KeyCode.Right
    |> InputMap.key GameAction.MoveForward KeyCode.W
    |> InputMap.key GameAction.MoveForward KeyCode.Up
    |> InputMap.key GameAction.MoveBackward KeyCode.S
    |> InputMap.key GameAction.MoveBackward KeyCode.Down
    |> InputMap.key GameAction.Jump KeyCode.Space
    |> InputMap.key GameAction.Respawn KeyCode.R
    |> InputMap.key GameAction.RotateCameraLeft KeyCode.Q
    |> InputMap.key GameAction.RotateCameraRight KeyCode.E
    |> InputMap.key GameAction.RotateCameraUp KeyCode.PageUp
    |> InputMap.key GameAction.RotateCameraDown KeyCode.PageDown

  let model = GameModel()
  model.InputMap <- inputMap
  model.Seed <- Random.Shared.Next()
  loadInitialChunks model

  // Particle texture: a 1×1 white Texture2D (MonoGame has no raylib GenImageColor).
  let gd = MonoGameGameContext.getGraphicsDevice ctx
  let particleTex = new Texture2D(gd, 1, 1, false, SurfaceFormat.Color)
  particleTex.SetData([| Color.White |])
  model.Particles.Texture <- particleTex

  let assets = GameContext.getService<IAssets> ctx
  model.JumpSound <- assets.Sound "sfx_jump"

  // Diagnostics font (MonoGame has no default font; loaded from content pipeline).
  model.Diagnostics.Font <- assets.Font "diagnostics"

  let playerModel = assets.Model(KenneyModels.characterOobi)

  // Skeleton + clips come from the raw .glb via Assimp (the content pipeline
  // drops animation data in XNB). AnimatedModel bundles Model+Mesh+State.
  let animatedMesh = assets.AnimatedMesh rawModelPath
  let clips = assets.ModelAnimations rawModelPath

  model.PlayerAnim <-
    AnimatedModel.create playerModel animatedMesh clips "idle" 60.0f

  let target = spawnPosition + Vector3(0.0f, playerHeight * 0.5f, 0.0f)
  model.CameraTarget <- target

  model.CameraPosition <-
    computeCameraPosition target model.CameraYaw model.CameraPitch

  struct (model, Cmd.none)

let subscribe (ctx: GameContext) (model: GameModel) =
  InputMapper.subscribeStatic model.InputMap InputMapped ctx

let overlayView (ctx: GameContext) (model: GameModel) (buffer: RenderBuffer2D) =
  Minimap.view ctx model buffer
  Diagnostics.view ctx model buffer

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo MonoGame 3D Platformer (MonoThreeD)"
          TargetFPS = 120
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Tick
    |> Program.withRenderer(fun () ->
      let pipeline =
        ForwardPipeline(
          shadowBias = ShadowBiasConfig.defaults,
          shadowAtlas = {
            ShadowAtlasConfig.defaults with
                Resolution = 1024 * 4
                GridSnapSize = 16.0f
          }
        )

      Renderer3D.create pipeline View.view)
    |> Program.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear overlayView)
    |> MonoGameProgram.ofProgram

  let game = new MiboGame<GameModel, Msg>(program)
  game.Content.RootDirectory <- "Content"
  game.Run()
  0
