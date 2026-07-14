module Platformer.MonoGame.Program

open System
open System.Numerics
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Elmish.Graphics2D
open Mibo.Input
open Mibo.Animation
open Mibo.Layout
open Platformer.Constants
open Platformer.Types
open Platformer.MonoGame.Types
open Platformer.MonoGame.Camera
open Platformer.MonoGame.Systems
open Platformer

let loadAssets(ctx: GameContext) : SpriteAssets =
  let assets = GameContext.getService<IAssets> ctx
  let playerTex = assets.Texture "Spritesheets/Characters"
  let tileTex = assets.Texture "Spritesheets/Tiles"
  let font = assets.Font "Fonts/Monogram"
  let jumpSound = assets.Sound "Sounds/Jump"
  let coinNormalMap = assets.Texture "NormalMap"

  let gd = MonoGameGameContext.getGraphicsDevice ctx
  let particleTex = new Texture2D(gd, 1, 1)
  particleTex.SetData [| Color.White |]

  let playerSheet =
    SpriteSheet.fromFrames playerTex Vector2.Zero [|
      "idle",
      {
        Frames = [| Rectangle(645, 0, 128, 128) |]
        FrameDuration = 1.0f
        Loop = false
      }
      "walk",
      {
        Frames = [|
          Rectangle(0, 129, 128, 128)
          Rectangle(129, 129, 128, 128)
        |]
        FrameDuration = 0.1f
        Loop = true
      }
      "jump",
      {
        Frames = [| Rectangle(774, 0, 128, 128) |]
        FrameDuration = 1.0f
        Loop = false
      }
      "fall",
      {
        Frames = [| Rectangle(774, 0, 128, 128) |]
        FrameDuration = 1.0f
        Loop = false
      }
      "duck",
      {
        Frames = [| Rectangle(258, 0, 128, 128) |]
        FrameDuration = 1.0f
        Loop = false
      }
    |]

  let torchSheet =
    SpriteSheet.fromFrames tileTex (Vector2(32.0f, 32.0f)) [|
      "lit",
      {
        Frames = [| Rectangle(65, 1105, 64, 64); Rectangle(130, 1105, 64, 64) |]
        FrameDuration = 0.15f
        Loop = true
      }
    |]

  let tileEffectSheet =
    SpriteSheet.fromFrames tileTex (Vector2(32.0f, 32.0f)) [|
      for def in TileAnimations.definitions do
        def.Name,
        {
          Frames =
            def.Frames
            |> Array.map(fun fr -> Rectangle(int fr.X, int fr.Y, 64, 64))
          FrameDuration = def.FrameDuration
          Loop = def.Loop
        }
    |]

  {
    PlayerSheet = playerSheet
    TileTexture = tileTex
    TorchSheet = torchSheet
    TileEffectSheet = tileEffectSheet
    ParticleTexture = particleTex
    CoinNormalMap = coinNormalMap
    Font = font
    JumpSound = jumpSound
  }

let inputMap =
  InputMap.empty
  |> InputMap.key MoveLeft KeyCode.A
  |> InputMap.key MoveLeft KeyCode.Left
  |> InputMap.key MoveRight KeyCode.D
  |> InputMap.key MoveRight KeyCode.Right
  |> InputMap.key GameAction.Jump KeyCode.Space
  |> InputMap.key GameAction.Down KeyCode.S
  |> InputMap.key GameAction.Down KeyCode.Down
  |> InputMap.key Respawn KeyCode.R

let init(ctx: GameContext) : struct (Model * Cmd<_>) =
  let assets = loadAssets ctx
  let seed = Random.Shared.Next()
  let gd = MonoGameGameContext.getGraphicsDevice ctx

  let camera =
    Camera2D.create
      (Camera.target {
        PlayerPosition = Vector2(spawnX, groundSurface - playerHeight)
      })
      1.0f
      (Vector2(float32 viewportWidth, float32 viewportHeight))

  let model =
    Model(
      InputMap = inputMap,
      Assets = assets,
      PlayerSprite = AnimatedSprite.create assets.PlayerSheet "idle",
      TorchSprite = AnimatedSprite.create assets.TorchSheet "lit",
      CoinSprite =
        AnimatedSprite.create
          (SpriteSheet.withNormalMap assets.CoinNormalMap assets.TileEffectSheet)
          "coin_gold",
      FlagSprite = AnimatedSprite.create assets.TileEffectSheet "flag_red",
      Chunks = WorldGen.Chunks.init seed,
      Camera = camera,
      Lighting =
        new LightContext2D(gd, softness = 0.05f, maxShadowDistance = 2000.0f),
      GraphicsDevice = gd

    )

  for x in -chunkLoadRadius .. chunkLoadRadius do
    for y in -chunkLoadRadius .. chunkLoadRadius do
      if x >= 0 then
        model.Chunks.Chunks[struct (x, y)] <- WorldGen.generateChunk x y seed

  model, Cmd.none

let subscribe (ctx: GameContext) (model: Model) =
  InputMapper.subscribeStatic model.InputMap InputMapped ctx

[<EntryPoint; STAThread>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = Constants.viewportWidth
          Height = Constants.viewportHeight
          Title = "Mibo MonoGame Platformer"
          TargetFPS = 120
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Msg.Tick
    |> Program.withRenderer(fun () -> Renderer2D.create View.view)
    |> MonoGameProgram.ofProgram

  let game = new MiboGame<Model, Msg>(program)
  game.Content.RootDirectory <- "Content"
  game.Run()
  0
