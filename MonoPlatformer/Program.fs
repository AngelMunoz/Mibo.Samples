module MonoPlatformer.Program

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Input
open Mibo.Animation
open Mibo.Layout
open MonoPlatformer.Constants
open MonoPlatformer.WorldGen
open MonoPlatformer.Types
open Mibo.Elmish.Graphics2D

let inline r (x: int) (y: int) (w: int) (h: int) = Rectangle(x, y, w, h)

let loadAssets(ctx: GameContext) : SpriteAssets =
  let assets = GameContext.getService<IAssets> ctx

  let playerTex = assets.Texture "Spritesheets/Characters"
  let tileTex = assets.Texture "Spritesheets/Tiles"
  let font = assets.Font "Fonts/Monogram"
  let jumpSound = assets.Sound "Sounds/Jump"
  let coinNormalMap = assets.Texture "NormalMap"

  let gd = MonoGameGameContext.getGraphicsDevice ctx
  let particleTex = new Texture2D(gd, 1, 1)
  particleTex.SetData([| Color.White |])

  let playerSheet =
    SpriteSheet.fromFrames playerTex Vector2.Zero [|
      struct ("idle",
              {
                Frames = [| r 645 0 128 128 |]
                FrameDuration = 1.0f
                Loop = false
              })
      struct ("walk",
              {
                Frames = [| r 0 129 128 128; r 129 129 128 128 |]
                FrameDuration = 0.1f
                Loop = true
              })
      struct ("jump",
              {
                Frames = [| r 774 0 128 128 |]
                FrameDuration = 1.0f
                Loop = false
              })
      struct ("fall",
              {
                Frames = [| r 774 0 128 128 |]
                FrameDuration = 1.0f
                Loop = false
              })
    |]

  // torch_on_a (65,1105) and torch_on_b (130,1105) — 64x64 each
  let torchSheet =
    SpriteSheet.fromFrames tileTex (Vector2(32.0f, 32.0f)) [|
      struct ("lit",
              {
                Frames = [| r 65 1105 64 64; r 130 1105 64 64 |]
                FrameDuration = 0.15f
                Loop = true
              })
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

let init(ctx: GameContext) =
  let assets = loadAssets ctx

  let inputMap =
    InputMap.empty
    |> InputMap.key GameAction.MoveLeft KeyCode.A
    |> InputMap.key GameAction.MoveLeft KeyCode.Left
    |> InputMap.key GameAction.MoveRight KeyCode.D
    |> InputMap.key GameAction.MoveRight KeyCode.Right
    |> InputMap.key GameAction.Jump KeyCode.Space
    |> InputMap.key GameAction.Respawn KeyCode.R

  let model = new Model()
  model.InputMap <- inputMap
  model.Assets <- assets
  model.PlayerSprite <- AnimatedSprite.create assets.PlayerSheet "idle"
  model.TorchSprite <- AnimatedSprite.create assets.TorchSheet "lit"

  let seed = Random().Next()
  let spawnY = groundSurface - playerHeight

  let spawnChunkX = 0
  let spawnChunkY = 0

  for x in spawnChunkX - chunkLoadRadius .. spawnChunkX + chunkLoadRadius do
    for y in spawnChunkY - chunkLoadRadius .. spawnChunkY + chunkLoadRadius do
      if x >= 0 then
        model.Chunks[struct (x, y)] <- generateChunk x y seed

  model.PlayerPosition <- Vector2(spawnX, spawnY)

  model.Camera <-
    Camera2D.create
      (Vector2(spawnX, spawnY))
      1.0f
      (Vector2(viewportWidth, viewportHeight))

  model.Seed <- seed

  let gd = MonoGameGameContext.getGraphicsDevice ctx
  model.GraphicsDevice <- gd

  model.Lighting <-
    new LightContext2D(gd, softness = 0.05f, maxShadowDistance = 2000.0f)

  struct (model, Cmd.none)

let subscribe (ctx: GameContext) (model: Model) =
  InputMapper.subscribeStatic model.InputMap InputMapped ctx

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init Systems.update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo MonoGame Platformer"
          TargetFPS = 120
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Tick
    |> Program.withRenderer(fun () -> Renderer2D.create View.view)

  let game = new MiboGame<Model, Msg>(program)
  game.Content.RootDirectory <- "Content"
  game.Run()
  0
