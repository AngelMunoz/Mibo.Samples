module Platformer.Raylib.View

open System
open System.Numerics
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Elmish.Graphics2D
open Mibo.Layout
open Mibo.Animation
open Platformer.Constants
open Platformer.Types
open Platformer.Raylib
open Platformer.Raylib.Types

type Model = Types.Model

let private nearbyOccluders = ResizeArray<Occluder2D>(256)
let private nearbyTorches = ResizeArray<PointLight2D>(64)

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  model.Lighting.Reset()

  let playerCenterX = model.Physics.Position.X + playerWidth / 2.0f
  let camera = model.Camera

  let dayNight: Platformer.DayNight.State = {
    TimeOfDay = model.DayNight.Time.TimeOfDay
    DayDuration = model.DayNight.Time.DayDuration
  }

  let skyTop, skyBot = Platformer.DayNight.getSkyColors dayNight.TimeOfDay
  let ambient = Platformer.DayNight.getAmbientColor dayNight.TimeOfDay
  let sunIntensity = Platformer.DayNight.getSunIntensity dayNight.TimeOfDay
  let moonIntensity = Platformer.DayNight.getMoonIntensity dayNight.TimeOfDay

  let sunPos, moonPos =
    Platformer.DayNight.orbitalPositions playerCenterX dayNight

  let viewBounds =
    Camera2D.viewportBounds
      &camera
      (float32 ctx.WindowWidth)
      (float32 ctx.WindowHeight)

  buffer
  |> Draw.rectGradientV
    (-1000<RenderLayer>)
    (0,
     0,
     ctx.WindowWidth,
     ctx.WindowHeight,
     RaylibColor.toRaylibColor skyTop,
     RaylibColor.toRaylibColor skyBot)
  |> Draw.beginCamera 0<RenderLayer> camera
  |> LightDraw.setAmbient
    model.Lighting
    (5<RenderLayer>,
     {
       Color = RaylibColor.toRaylibColor ambient
     })
  |> Draw.drop

  // Sun
  if sunIntensity > 0.0f then
    let sunDir =
      Vector2.Normalize(Vector2(playerCenterX, groundLevel - 200.0f) - sunPos)

    buffer
    |> LightDraw.addDirectionalLight model.Lighting 6<RenderLayer> {
      Direction = sunDir
      Color = Color(255uy, 245uy, 220uy)
      Intensity = sunIntensity * 1.5f
      CastsShadows = true
    }
    |> Draw.drop

  // Moon
  if moonIntensity > 0.0f then
    let moonDir =
      Vector2.Normalize(Vector2(playerCenterX, groundLevel - 200.0f) - moonPos)

    buffer
    |> LightDraw.addDirectionalLight model.Lighting 6<RenderLayer> {
      Direction = moonDir
      Color = Color(180uy, 200uy, 255uy)
      Intensity = moonIntensity * 0.8f
      CastsShadows = true
    }
    |> Draw.drop

  // Collect occluders and torches
  let pcx =
    int(Math.Floor(float model.Physics.Position.X / float chunkWorldSize))

  let pcy =
    int(Math.Floor(float model.Physics.Position.Y / float chunkWorldSize))

  nearbyOccluders.Clear()
  nearbyTorches.Clear()

  let maxOccluderDistSq =
    let vw = float32 ctx.WindowWidth
    let vh = float32 ctx.WindowHeight
    vw * 1.5f * vw * 1.5f + vh * 1.5f * vh * 1.5f

  let playerPos = model.Physics.Position

  for KeyValue(key, chunk) in model.Chunks.Chunks do
    let struct (cx, cy) = key

    if abs(cx - pcx) <= chunkLoadRadius && abs(cy - pcy) <= chunkLoadRadius then
      for o in chunk.Occluders do
        let mx = (o.P1.X + o.P2.X) * 0.5f
        let my = (o.P1.Y + o.P2.Y) * 0.5f
        let dx = mx - playerPos.X
        let dy = my - playerPos.Y

        if dx * dx + dy * dy <= maxOccluderDistSq then
          nearbyOccluders.Add(toOccluder o)

      for t in chunk.Torches do
        nearbyTorches.Add {
          PointLight2D.Position = t.Position
          Color = RaylibColor.toRaylibColor t.Color
          Intensity = 1.2f
          Radius = t.Radius
          Falloff = 1.5f
          CastsShadows = false
        }

  let ocCount = min nearbyOccluders.Count maxOccluders

  if nearbyOccluders.Count > 1 then
    nearbyOccluders.Sort(fun a b ->
      let ax = (a.P1.X + a.P2.X) * 0.5f - playerPos.X
      let ay = (a.P1.Y + a.P2.Y) * 0.5f - playerPos.Y
      let bx = (b.P1.X + b.P2.X) * 0.5f - playerPos.X
      let by = (b.P1.Y + b.P2.Y) * 0.5f - playerPos.Y
      compare (ax * ax + ay * ay) (bx * bx + by * by))

  let torchCount = min nearbyTorches.Count maxTorchLights

  if nearbyTorches.Count > 1 then
    nearbyTorches.Sort(fun a b ->
      let ax = a.Position.X - playerPos.X
      let ay = a.Position.Y - playerPos.Y
      let bx = b.Position.X - playerPos.X
      let by = b.Position.Y - playerPos.Y
      compare (ax * ax + ay * ay) (bx * bx + by * by))

  // Torches
  let torchSrc = AnimatedSprite.currentSource model.TorchSprite

  for i = 0 to torchCount - 1 do
    let torch = nearbyTorches[i]

    buffer
    |> LightDraw.addPointLight model.Lighting 7<RenderLayer> torch
    |> Draw.drop

    let torchDest =
      Rectangle(torch.Position.X - 16f, torch.Position.Y - 32f, 32f, 32f)

    buffer
    |> LightDraw.litSprite
      model.Lighting
      (SpriteState.create(model.Assets.TorchSheet.Texture, torchDest, torchSrc)
       |> SpriteState.withLayer 7<RenderLayer>)
    |> Draw.drop

  // Occluders
  for i = 0 to ocCount - 1 do
    buffer
    |> LightDraw.addOccluder model.Lighting 8<RenderLayer> nearbyOccluders[i]
    |> Draw.drop

  // Tiles
  let tileSpriteSrc (biome: Biome) (tile: TileType) =
    match tile with
    | Ground ->
      match biome with
      | Grass -> Rectangle(260f, 585f, 64f, 64f)
      | Stone -> Rectangle(520f, 975f, 64f, 64f)
      | Snow -> Rectangle(1040f, 845f, 64f, 64f)
      | Sand -> Rectangle(390f, 780f, 64f, 64f)
    | Platform ->
      match biome with
      | Grass -> Rectangle(520f, 975f, 64f, 64f)
      | Stone -> Rectangle(780f, 455f, 64f, 64f)
      | Snow -> Rectangle(520f, 975f, 64f, 64f)
      | Sand -> Rectangle(780f, 455f, 64f, 64f)
    | Spikes -> Rectangle(715f, 0f, 64f, 64f)
    | Coin -> Rectangle(0f, 130f, 64f, 64f)
    | Flag -> Rectangle(780f, 195f, 64f, 64f)
    | Empty -> Rectangle(0f, 0f, 0f, 0f)

  for KeyValue(key, chunk) in model.Chunks.Chunks do
    let struct (cx, cy) = key

    if abs(cx - pcx) <= chunkLoadRadius && abs(cy - pcy) <= chunkLoadRadius then
      let chunkBounds = toRect chunk.Bounds

      if Culling.isVisible2D viewBounds chunkBounds then
        let chunkBiome = chunk.Biome

        CellGrid2D.iterVisible
          (int viewBounds.X)
          (int viewBounds.Y)
          (int(viewBounds.X + viewBounds.Width))
          (int(viewBounds.Y + viewBounds.Height))
          (fun x y tile ->
            if tile <> TileType.Empty then
              let wx = chunk.Grid.Origin.X + float32 x * tileSize
              let wy = chunk.Grid.Origin.Y + float32 y * tileSize
              let dest = Rectangle(wx, wy, tileSize, tileSize)

              let sprite =
                let s =
                  SpriteState.create(
                    model.Assets.TileTexture,
                    dest,
                    tileSpriteSrc chunkBiome tile
                  )
                  |> SpriteState.withLayer 10<RenderLayer>

                if tile = TileType.Coin then
                  s |> SpriteState.withNormalMap model.Assets.CoinNormalMap
                else
                  s

              buffer |> LightDraw.litSprite model.Lighting sprite |> Draw.drop)
          chunk.Grid

  // Player
  let playerDrawY = model.Physics.Position.Y + playerHeight - 64.0f
  let playerDest = Rectangle(model.Physics.Position.X, playerDrawY, 64f, 64f)

  buffer
  |> LightDraw.litAnimatedSprite
    model.Lighting
    20<RenderLayer>
    playerDest
    model.PlayerSprite
  |> Draw.drop

  // Particles
  let particleCount = model.ParticleState.Count

  for i = 0 to particleCount - 1 do
    model.ParticleBuffer[i] <- toParticle model.ParticleState.Particles[i]

  buffer
  |> ParticleDraw.particles
    model.Assets.ParticleTexture
    model.ParticleBuffer
    particleCount
    3<RenderLayer>

  // End lighting + camera
  |> LightDraw.endLighting model.Lighting 999<RenderLayer>
  |> Draw.endCamera 1000<RenderLayer>
  // UI
  |> Draw.text(
    TextState.create(
      model.Assets.Font,
      $"Day/Night Cycle | Time: {model.DayNight.Time.TimeOfDay:F1}h | Chunks: {model.Chunks.Chunks.Count} | Score: {model.Physics.Score} | WASD/Arrows: Move | Space: Jump | R: Respawn",
      Vector2(10.0f, 10.0f)
    )
    |> TextState.withFontSize 20.0f
    |> TextState.withSpacing 1.0f
    |> TextState.withColor Raylib_cs.Color.White
    |> TextState.withLayer 1001<RenderLayer>
  )
  |> Draw.text(
    TextState.create(
      model.Assets.Font,
      $"FPS: {model.Diag.Fps} | Frame Time: {model.Diag.FrameTime * 1000.0f:F1}ms",
      Vector2(10.0f, 32.0f)
    )
    |> TextState.withFontSize 20.0f
    |> TextState.withSpacing 1.0f
    |> TextState.withColor Raylib_cs.Color.White
    |> TextState.withLayer 1001<RenderLayer>
  )
  |> MinimapView.view ctx model
