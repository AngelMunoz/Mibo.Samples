module Platformer3D.MonoGame.Program

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Animation
open Mibo.Input
open Platformer3D.Constants
open Platformer3D.Types
open Platformer3D.Physics
open Platformer3D.WorldGen
open Platformer3D.MonoGame.Types
open Platformer3D.MonoGame.Systems

// Path to the raw .glb (copied to the output dir via the fsproj <Content> entry).
let private rawModelPath =
  System.IO.Path.Combine(
    AppContext.BaseDirectory,
    "animations",
    "character-oobi.glb"
  )

let loadInitialChunks(model: Model) =
  let spawnPos = spawnPosition

  let numericsSpawn =
    System.Numerics.Vector3(spawnPos.X, spawnPos.Y, spawnPos.Z)

  loadChunks numericsSpawn model.Chunks.Chunks model.Chunks.Seed

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

  let model = Model()
  model.InputMap <- inputMap
  model.Chunks <- Chunks.init(Random.Shared.Next())
  loadInitialChunks model

  let gd = MonoGameGameContext.getGraphicsDevice ctx
  model.GraphicsDevice <- gd

  let particleTex = new Texture2D(gd, 1, 1, false, SurfaceFormat.Color)
  particleTex.SetData([| Color.White |])
  model.ParticleTexture <- particleTex

  let assets = GameContext.getService<IAssets> ctx
  model.JumpSound <- assets.Sound "sfx_jump"
  model.DiagFont <- assets.Font "diagnostics"

  let playerModel =
    assets.Model(AssetPaths.modelPath KenneyModels.characterOobi)

  let animatedMesh = assets.AnimatedMesh rawModelPath
  let clips = assets.ModelAnimations rawModelPath

  model.PlayerAnim <-
    AnimatedModel.create playerModel animatedMesh clips "idle" 60.0f

  let target =
    spawnPosition + System.Numerics.Vector3(0.0f, playerHeight * 0.5f, 0.0f)

  model.Physics.CameraTarget <- target

  model.Physics.CameraPosition <-
    computeCameraPosition
      target
      model.Physics.CameraYaw
      model.Physics.CameraPitch

  struct (model, Cmd.none)

let subscribe (ctx: GameContext) (model: Model) =
  InputMapper.subscribeStatic model.InputMap InputMapped ctx

let overlayView (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  Platformer3D.MonoGame.MinimapView.view ctx model buffer
  Platformer3D.MonoGame.DiagnosticsView.view ctx model buffer

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo MonoGame 3D Platformer"
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Tick
    |> Program.withRenderer(fun () ->
      let pipeline =
        ForwardPipeline(
          shadowBias = {
            ShadowBiasConfig.defaults with
                DirectionalBias = 0.002f
                SlopeScaleBias = 0.0008f
          },
          shadowAtlas = {
            ShadowAtlasConfig.defaults with
                Resolution = 1024 * 4
          }
        )

      Renderer3D.create pipeline Platformer3D.MonoGame.View.view)
    |> Program.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear overlayView)
    |> MonoGameProgram.ofProgram

  let game = new MiboGame<Model, Msg>(program)
  game.Content.RootDirectory <- "Content"
  game.Run()
  0
