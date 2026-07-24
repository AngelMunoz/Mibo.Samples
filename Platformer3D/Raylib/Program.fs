module Platformer3D.Raylib.Program

open System
open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Elmish.Graphics2D
open Mibo.Animation
open Mibo.Input
open Raylib_cs
open Platformer3D.Constants
open Platformer3D.Types
open Platformer3D.Physics
open Platformer3D.WorldGen
open Platformer3D.Raylib.Types
open Platformer3D.Raylib.Systems

let loadInitialChunks(model: Model) =
  loadChunks spawnPosition model.Chunks.Chunks model.Chunks.Seed

let init(ctx: GameContext) =
  let inputMap: InputMap<GameAction> =
    InputMap.empty
    |> InputMap.key MoveLeft KeyCode.A
    |> InputMap.key MoveLeft KeyCode.Left
    |> InputMap.key MoveRight KeyCode.D
    |> InputMap.key MoveRight KeyCode.Right
    |> InputMap.key MoveForward KeyCode.W
    |> InputMap.key MoveForward KeyCode.Up
    |> InputMap.key MoveBackward KeyCode.S
    |> InputMap.key MoveBackward KeyCode.Down
    |> InputMap.key Jump KeyCode.Space
    |> InputMap.key Respawn KeyCode.R
    |> InputMap.key RotateCameraLeft KeyCode.Q
    |> InputMap.key RotateCameraRight KeyCode.E
    |> InputMap.key RotateCameraUp KeyCode.PageUp
    |> InputMap.key RotateCameraDown KeyCode.PageDown

  let model = Model()
  model.InputMap <- inputMap
  model.Chunks <- Chunks.init(Random.Shared.Next())
  loadInitialChunks model

  let particleImg =
    Raylib.GenImageColor(1, 1, Color(255uy, 255uy, 255uy, 255uy))

  model.ParticleTexture <- Raylib.LoadTextureFromImage(particleImg)
  Raylib.UnloadImage(particleImg)

  let assets = GameContext.getService<IAssets> ctx
  model.JumpSound <- assets.Sound("assets/sfx_jump.ogg")

  let playerModel =
    assets.Model(AssetPaths.modelPath KenneyModels.characterOobi)

  model.PlayerModel <- playerModel

  let animClips =
    assets.ModelAnimations(AssetPaths.modelPath KenneyModels.characterOobi)

  let clips = Animation3DClips.fromModelAnimations animClips
  model.PlayerAnimClips <- clips
  model.PlayerAnim <- Animation3DState.create playerModel clips "idle" 60.0f

  let target = spawnPosition + Vector3(0.0f, playerHeight * 0.5f, 0.0f)
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
  Platformer3D.Raylib.MinimapView.view ctx model buffer
  Platformer3D.Raylib.DiagnosticsView.view ctx model buffer

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withAssetsBasePath AppContext.BaseDirectory
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo 3D Platformer"
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Tick
    |> Program.withRenderer(fun () ->
      let pipeline =
        ForwardPbrPipeline(
          shadowBiasConfig = {
            ShadowBiasConfig.defaults with
                DirectionalBias = 0.002f
                SlopeScaleBias = 0.0008f
          },
          shadowAtlasConfig = {
            ShadowAtlasConfig.defaults with
                Resolution = 1024 * 4
          }

        )

      Renderer3D.create pipeline Platformer3D.Raylib.View.view)
    |> Program.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear overlayView)

  let game = new RaylibGame<Model, Msg>(program)
  game.Run()
  0
