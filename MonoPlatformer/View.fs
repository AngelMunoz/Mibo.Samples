module MonoPlatformer.View

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Layout
open Mibo.Animation
open MonoPlatformer.Constants
open MonoPlatformer.DayNight
open MonoPlatformer.WorldGen
open MonoPlatformer.Minimap
open Mibo.Elmish.Graphics2D
open MonoPlatformer.Types

let inline r (x: int) (y: int) (w: int) (h: int) = Rectangle(x, y, w, h)

// Inlined — Culling module not in Mibo.MonoGame backend
let inline isVisible2D (viewBounds: Rectangle) (itemBounds: Rectangle) =
  viewBounds.X < itemBounds.X + itemBounds.Width
  && viewBounds.X + viewBounds.Width > itemBounds.X
  && viewBounds.Y < itemBounds.Y + itemBounds.Height
  && viewBounds.Y + viewBounds.Height > itemBounds.Y

// viewportBounds inlined — Camera2D.viewportBounds not in Mibo.MonoGame backend
let inline viewportBounds
  (camera: Camera2D)
  (windowW: float32)
  (windowH: float32)
  : Rectangle =
  let halfW = windowW / (2.0f * camera.Zoom)
  let halfH = windowH / (2.0f * camera.Zoom)

  Rectangle(
    int(camera.Position.X - halfW),
    int(camera.Position.Y - halfH),
    int(windowW / camera.Zoom),
    int(windowH / camera.Zoom)
  )

// Pre-allocated buffers to avoid per-frame allocation
let private nearbyOccluders = ResizeArray<Occluder2D>(256)
let private nearbyTorches = ResizeArray<TorchLight>(64)

// -------------------------------------------------------------
// Lighting & Rendering
// -------------------------------------------------------------

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  model.Lighting.Reset()

  let playerCenterX = model.PlayerPosition.X + playerWidth / 2.0f

  let camera = model.Camera

  // Sky background and day/night ambient
  let time = model.DayNightTimeOfDay

  let skyTop, skyBot = DayNight.getSkyColors time

  let viewBounds =
    viewportBounds camera (float32 ctx.WindowWidth) (float32 ctx.WindowHeight)

  buffer
  |> Draw.rectGradientV
    (-1000<RenderLayer>)
    (0, 0, ctx.WindowWidth, ctx.WindowHeight, skyTop, skyBot)
  |> Draw.beginCamera 0<RenderLayer> camera
  |> Draw.drop

  // Collect torches from nearby chunks (still needed for torch sprites)
  let pcx = int(Math.Floor(float model.PlayerPosition.X / float chunkWorldSize))
  let pcy = int(Math.Floor(float model.PlayerPosition.Y / float chunkWorldSize))

  nearbyTorches.Clear()

  let playerPos = model.PlayerPosition

  for KeyValue(key, chunk) in model.Chunks do
    let struct (cx, cy) = key

    if abs(cx - pcx) <= chunkLoadRadius && abs(cy - pcy) <= chunkLoadRadius then
      for t in chunk.Torches do
        nearbyTorches.Add t

  let torchCount = min nearbyTorches.Count maxTorchLights

  // Draw torch sprites (unlit)
  let torchSrc = AnimatedSprite.currentSource model.TorchSprite

  for i = 0 to torchCount - 1 do
    let torch = nearbyTorches[i]

    let torchDest =
      r (int torch.Position.X - 16) (int torch.Position.Y - 32) 32 32

    buffer
    |> Draw.sprite(
      SpriteState.create(model.Assets.TorchSheet.Texture, torchDest, torchSrc)
      |> SpriteState.withLayer 7<RenderLayer>
    )
    |> Draw.drop

  // Render visible tiles from nearby chunks only
  let tileSpriteSrc (biome: Biome) (tile: TileType) =
    match tile with
    | Ground ->
      match biome with
      | Grass -> r 260 585 64 64 // terrain_grass_block
      | Stone -> r 520 975 64 64 // terrain_stone_block
      | Snow -> r 1040 845 64 64 // terrain_snow_block
      | Sand -> r 390 780 64 64 // terrain_sand_block
    | Platform ->
      match biome with
      | Grass -> r 520 975 64 64 // terrain_stone_block
      | Stone -> r 780 455 64 64 // terrain_dirt_block
      | Snow -> r 520 975 64 64 // terrain_stone_block
      | Sand -> r 780 455 64 64 // terrain_dirt_block
    | Spikes -> r 715 0 64 64 // block_spikes
    | Coin -> r 0 130 64 64 // coin_gold
    | Flag -> r 780 195 64 64 // flag_red_a
    | Empty -> r 0 0 0 0

  for KeyValue(key, chunk) in model.Chunks do
    let struct (cx, cy) = key

    if abs(cx - pcx) <= chunkLoadRadius && abs(cy - pcy) <= chunkLoadRadius then
      if isVisible2D viewBounds chunk.Bounds then
        let chunkBiome = chunk.Biome

        CellGrid2D.iterVisible
          viewBounds.X
          viewBounds.Y
          (viewBounds.X + viewBounds.Width)
          (viewBounds.Y + viewBounds.Height)
          (fun x y tile ->
            if tile <> TileType.Empty then
              let wx = chunk.Grid.Origin.X + float32 x * tileSize
              let wy = chunk.Grid.Origin.Y + float32 y * tileSize
              let dest = Rectangle(int wx, int wy, int tileSize, int tileSize)

              let sprite =
                SpriteState.create(
                  model.Assets.TileTexture,
                  dest,
                  tileSpriteSrc chunkBiome tile
                )
                |> SpriteState.withLayer 10<RenderLayer>

              buffer |> Draw.sprite sprite |> Draw.drop)
          chunk.Grid

  // Player sprite (unlit)
  let playerDrawY = int(model.PlayerPosition.Y + playerHeight - 64.0f)
  let playerDest = r (int model.PlayerPosition.X) playerDrawY 64 64

  let playerSrc = AnimatedSprite.currentSource model.PlayerSprite

  let playerSrc =
    if model.PlayerSprite.FlipX then
      Rectangle(playerSrc.X, playerSrc.Y, -playerSrc.Width, playerSrc.Height)
    else
      playerSrc

  buffer
  |> Draw.sprite(
    SpriteState.create(model.Assets.PlayerSheet.Texture, playerDest, playerSrc)
    |> SpriteState.withLayer 20<RenderLayer>
  )
  |> Draw.drop

  // Particles
  buffer
  |> ParticleDraw.particles
    model.Assets.ParticleTexture
    model.Particles
    model.ParticleCount
    3<RenderLayer>

  // End camera
  |> Draw.endCamera 1000<RenderLayer>
  // UI
  |> Draw.text(
    TextState.create(
      model.Assets.Font,
      $"Day/Night Cycle | Time: {model.DayNightTimeOfDay:F1}h | Chunks: {model.Chunks.Count} | Score: {model.Score} | Pos: %.1f{model.PlayerPosition.X},%.1f{model.PlayerPosition.Y} | WASD/Arrows: Move | Space: Jump | R: Respawn",
      Vector2(10.0f, 10.0f)
    )
    |> TextState.withScale 1.0f
    |> TextState.withColor Color.White
    |> TextState.withLayer 1001<RenderLayer>
  )
  |> Draw.text(
    TextState.create(
      model.Assets.Font,
      $"FPS: {model.Diagnostics.Fps} | Frame Time: {model.Diagnostics.FrameTime * 1000.0f:F1}ms",
      Vector2(10.0f, 32.0f)
    )
    |> TextState.withScale 1.0f
    |> TextState.withColor Color.White
    |> TextState.withLayer 1001<RenderLayer>
  )
  // Minimap
  |> Minimap.view ctx model
  |> Draw.drop
