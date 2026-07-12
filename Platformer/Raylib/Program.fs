module Platformer.Raylib.Program

open System
open System.Numerics
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Elmish.Graphics2D
open Mibo.Input
open Mibo.Animation
open Mibo.Layout
open Platformer.Constants
open Platformer.Types
open Platformer.WorldGen
open Platformer.Raylib.Types
open Platformer.Raylib.Systems
open Platformer

let loadAssets(ctx: GameContext) : SpriteAssets =
  let assets = GameContext.getService<IAssets> ctx

  let playerTex =
    assets.Texture
      "assets/kenney_platformer/Spritesheets/spritesheet-characters-default.png"

  let tileTex =
    assets.Texture
      "assets/kenney_platformer/Spritesheets/spritesheet-tiles-default.png"

  let font = assets.Font "assets/Fonts/monogram.ttf"
  let jumpSound = assets.Sound "assets/sfx_jump.ogg"
  let coinNormalMap = assets.Texture "assets/NormalMap.png"

  let particleImg =
    Raylib.GenImageColor(1, 1, Raylib_cs.Color(255uy, 255uy, 255uy, 255uy))

  let particleTex = Raylib.LoadTextureFromImage particleImg
  Raylib.UnloadImage particleImg

  let playerSheet =
    SpriteSheet.fromFrames playerTex Vector2.Zero [|
      "idle",
      {
        Frames = [| Rectangle(645f, 0f, 128f, 128f) |]
        FrameDuration = 1.0f
        Loop = false
      }
      "walk",
      {
        Frames = [|
          Rectangle(0f, 129f, 128f, 128f)
          Rectangle(129f, 129f, 128f, 128f)
        |]
        FrameDuration = 0.1f
        Loop = true
      }
      "jump",
      {
        Frames = [| Rectangle(774f, 0f, 128f, 128f) |]
        FrameDuration = 1.0f
        Loop = false
      }
      "fall",
      {
        Frames = [| Rectangle(774f, 0f, 128f, 128f) |]
        FrameDuration = 1.0f
        Loop = false
      }
    |]

  let torchSheet =
    SpriteSheet.fromFrames tileTex (Vector2(32.0f, 32.0f)) [|
      "lit",
      {
        Frames = [|
          Rectangle(65f, 1105f, 64f, 64f)
          Rectangle(130f, 1105f, 64f, 64f)
        |]
        FrameDuration = 0.15f
        Loop = true
      }
    |]

  {
    PlayerSheet = playerSheet
    TileTexture = tileTex
    TorchSheet = torchSheet
    ParticleTexture = particleTex
    CoinNormalMap = coinNormalMap
    Font = font
    JumpSound = jumpSound
  }

let inputMap =
  InputMap.empty
  |> InputMap.key GameAction.MoveLeft KeyCode.A
  |> InputMap.key GameAction.MoveLeft KeyCode.Left
  |> InputMap.key GameAction.MoveRight KeyCode.D
  |> InputMap.key GameAction.MoveRight KeyCode.Right
  |> InputMap.key GameAction.Jump KeyCode.Space
  |> InputMap.key GameAction.Respawn KeyCode.R

let init(ctx: GameContext) : struct (Model * Cmd<_>) =
  let assets = loadAssets ctx
  let seed = Random.Shared.Next()

  let camera =
    Camera2D.create
      (Vector2(spawnX, groundSurface - playerHeight))
      1.0f
      (Vector2(float32 viewportWidth, float32 viewportHeight))

  let model =
    Model(
      InputMap = inputMap,
      Assets = assets,
      PlayerSprite = AnimatedSprite.create assets.PlayerSheet "idle",
      TorchSprite = AnimatedSprite.create assets.TorchSheet "lit",
      Chunks = Chunks.init seed,
      Camera = camera,
      Lighting =
        new LightContext2D(softness = 0.05f, maxShadowDistance = 2000.0f)
    )

  // Pre-load spawn chunks
  for x in -chunkLoadRadius .. chunkLoadRadius do
    for y in -chunkLoadRadius .. chunkLoadRadius do
      if x >= 0 then
        model.Chunks.Chunks[struct (x, y)] <- generateChunk x y seed

  model, Cmd.none

let subscribe (ctx: GameContext) (model: Model) =
  InputMapper.subscribeStatic model.InputMap InputMapped ctx

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withAssetsBasePath AppContext.BaseDirectory
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = Constants.viewportWidth
          Height = Constants.viewportHeight
          Title = "Mibo Raylib Platformer"
          TargetFPS = 120
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Msg.Tick
    |> Program.withRenderer(fun () -> Renderer2D.create View.view)

  let game = new RaylibGame<Model, Msg>(program)
  game.Run()
  0
