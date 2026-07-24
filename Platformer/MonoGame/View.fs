module Platformer.MonoGame.View

open System
open Microsoft.Xna.Framework
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Elmish.Graphics2D
open Mibo.Layout
open Mibo.Animation
open Platformer.Constants
open Platformer.Types
open Platformer
open Platformer.MonoGame.Types

type Model = Types.Model

let inline isVisible2D (viewBounds: Rectangle) (itemBounds: Rectangle) =
  viewBounds.X < itemBounds.X + itemBounds.Width
  && viewBounds.X + viewBounds.Width > itemBounds.X
  && viewBounds.Y < itemBounds.Y + itemBounds.Height
  && viewBounds.Y + viewBounds.Height > itemBounds.Y

let private nearbyOccluders = ResizeArray<Occluder2D>(256)
let private nearbyTorches = ResizeArray<PointLight2D>(64)

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  // Sample the wall-clock interval since the previous Draw. This is the real
  // render rate — unlike the fixed-timestep Tick dt, it exposes frame drops.
  model.Diag.Tick()
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
      camera
      (float32 ctx.WindowWidth)
      (float32 ctx.WindowHeight)

  buffer
  |> Draw.rectGradientV
    (-1000<RenderLayer>)
    (0,
     0,
     ctx.WindowWidth,
     ctx.WindowHeight,
     MonoGameColor.toMonoGameColor skyTop,
     MonoGameColor.toMonoGameColor skyBot)
  |> Draw.beginCamera 0<RenderLayer> camera
  |> LightDraw.setAmbient
    model.Lighting
    (5<RenderLayer>,
     {
       Color = MonoGameColor.toMonoGameColor ambient
     })
  |> Draw.drop

  if sunIntensity > 0.0f then
    let sunPos = Vector2.op_Implicit sunPos

    let sunDir =
      Vector2.Normalize(Vector2(playerCenterX, groundLevel - 200.0f) - sunPos)

    buffer
    |> LightDraw.addDirectionalLight model.Lighting 6<RenderLayer> {
      Direction = sunDir
      Color = MonoGameColor.toMonoGameColor(Mibo.Color.rgb 255uy 245uy 220uy)
      Intensity = sunIntensity * 1.5f
      CastsShadows = true
    }
    |> Draw.drop

  if moonIntensity > 0.0f then
    let moonPos = Vector2.op_Implicit moonPos

    let moonDir =
      Vector2.Normalize(Vector2(playerCenterX, groundLevel - 200.0f) - moonPos)

    buffer
    |> LightDraw.addDirectionalLight model.Lighting 6<RenderLayer> {
      Direction = moonDir
      Color = MonoGameColor.toMonoGameColor(Mibo.Color.rgb 180uy 200uy 255uy)
      Intensity = moonIntensity * 0.8f
      CastsShadows = true
    }
    |> Draw.drop

  let pcx =
    int(Math.Floor(float model.Physics.Position.X / float chunkWorldSize))

  let pcy =
    int(Math.Floor(float model.Physics.Position.Y / float chunkWorldSize))

  nearbyOccluders.Clear()
  nearbyTorches.Clear()

  let maxOccluderDistSq =
    let vw = float32 ctx.WindowWidth
    let vh = float32 ctx.WindowHeight
    (vw * 1.5f) * (vw * 1.5f) + (vh * 1.5f) * (vh * 1.5f)

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
          Position = Vector2.op_Implicit t.Position
          Color = MonoGameColor.toMonoGameColor t.Color
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

  let torchSrc = AnimatedSprite.currentSource model.TorchSprite

  for i = 0 to torchCount - 1 do
    let torch = nearbyTorches[i]

    buffer
    |> LightDraw.addPointLight model.Lighting 7<RenderLayer> torch
    |> Draw.drop

    let torchDest =
      Rectangle(int torch.Position.X - 16, int torch.Position.Y - 32, 32, 32)

    buffer
    |> LightDraw.litSprite
      model.Lighting
      (SpriteState.create(model.Assets.TorchSheet.Texture, torchDest, torchSrc)
       |> SpriteState.withLayer 7<RenderLayer>)
    |> Draw.drop

  for i = 0 to ocCount - 1 do
    buffer
    |> LightDraw.addOccluder model.Lighting 8<RenderLayer> nearbyOccluders[i]
    |> Draw.drop

  buffer
  |> Draw.setSamplerState
    9<RenderLayer>
    Microsoft.Xna.Framework.Graphics.SamplerState.PointClamp
  |> Draw.drop

  for KeyValue(key, chunk) in model.Chunks.Chunks do
    let struct (cx, cy) = key

    if abs(cx - pcx) <= chunkLoadRadius && abs(cy - pcy) <= chunkLoadRadius then
      let chunkBounds = toRect chunk.Bounds

      if isVisible2D viewBounds chunkBounds then
        let terrainGrid, _ =
          LayeredGrid2D.getOrAddLayer Layer.Terrain chunk.Grids

        CellGrid2D.iterVisible
          viewBounds.X
          viewBounds.Y
          (viewBounds.X + viewBounds.Width)
          (viewBounds.Y + viewBounds.Height)
          (fun x y tile ->
            if
              tile <> Tile.Empty && tile <> Tile.Coin && tile <> Tile.Flag
            then
              let info = TileData.lookup tile
              let wx = terrainGrid.Origin.X + float32 x * tileSize
              let wy = terrainGrid.Origin.Y + float32 y * tileSize

              let dest = Rectangle(int wx, int wy, int tileSize, int tileSize)

              let srcRect =
                Rectangle(int info.SpriteX, int info.SpriteY, 64, 64)

              let sprite =
                SpriteState.create(model.Assets.TileTexture, dest, srcRect)
                |> SpriteState.withLayer 10<RenderLayer>

              buffer |> LightDraw.litSprite model.Lighting sprite |> Draw.drop)
          terrainGrid

        // Animated collectibles (litAnimatedSprite for per-frame animation)
        for coin in chunk.Coins do
          let dest =
            Rectangle(int coin.X, int coin.Y, int coin.Width, int coin.Height)

          buffer
          |> LightDraw.litAnimatedSprite
            model.Lighting
            10<RenderLayer>
            dest
            model.CoinSprite
          |> Draw.drop

        for flag in chunk.Flags do
          let dest =
            Rectangle(int flag.X, int flag.Y, int flag.Width, int flag.Height)

          buffer
          |> LightDraw.litAnimatedSprite
            model.Lighting
            10<RenderLayer>
            dest
            model.FlagSprite
          |> Draw.drop

  buffer
  |> Draw.setSamplerState
    11<RenderLayer>
    Microsoft.Xna.Framework.Graphics.SamplerState.LinearClamp
  |> Draw.drop

  let playerDrawY = int(model.Physics.Position.Y + playerHeight - 64.0f)
  let playerDest = Rectangle(int model.Physics.Position.X, playerDrawY, 64, 64)

  buffer
  |> LightDraw.litAnimatedSprite
    model.Lighting
    20<RenderLayer>
    playerDest
    model.PlayerSprite
  |> Draw.drop

  let particleCount = model.ParticleState.Count

  for i = 0 to particleCount - 1 do
    model.ParticleBuffer[i] <- toParticle model.ParticleState.Particles[i]

  buffer
  |> ParticleDraw.particles
    model.Assets.ParticleTexture
    model.ParticleBuffer
    particleCount
    3<RenderLayer>
  |> LightDraw.endLighting model.Lighting 999<RenderLayer>
  |> Draw.endCamera 1000<RenderLayer>
  |> Draw.text(
    TextState.create(
      model.Assets.Font,
      $"Day/Night Cycle | Time: {model.DayNight.Time.TimeOfDay:F1}h | Chunks: {model.Chunks.Chunks.Count} | Score: {model.Physics.Score} | WASD/Arrows: Move | Space: Jump | S/Down: Drop | R: Respawn",
      Vector2(10.0f, 10.0f)
    )
    |> TextState.withScale 1.0f
    |> TextState.withColor(Color.White |> MonoGameColor.toMonoGameColor)
    |> TextState.withLayer 1001<RenderLayer>
  )
  |> Draw.text(
    TextState.create(
      model.Assets.Font,
      $"FPS: {model.Diag.Fps} | Frame Time: {model.Diag.FrameTime * 1000.0f:F1}ms",
      Vector2(10.0f, 32.0f)
    )
    |> TextState.withScale 1.0f
    |> TextState.withColor(Color.White |> MonoGameColor.toMonoGameColor)
    |> TextState.withLayer 1001<RenderLayer>
  )
  |> MinimapView.view ctx model
