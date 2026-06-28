module MonoThreeD.Minimap

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Layout3D
open MonoThreeD.Constants
open MonoThreeD.Types

// MonoGame has no raylib-style CPU Image API (GenImageColor/ImageDrawRectangle/
// UpdateTexture/LoadTextureFromImage). We render the minimap into a CPU Color[]
// buffer and upload it to a Texture2D via SetData. The block-sampling logic is
// identical to the Raylib sample; only the pixel-target representation differs.
//
// The world sampling (playerPos/bounds/collectBlocks/generatePixels) runs in
// System.Numerics, matching the Core CellGrid3D's native type — the async path
// in Systems.fs already passes a Numerics position. XNA types appear only at the
// Color output, Texture2D, and the 2D Draw boundary.

// ── Constants ──

[<Literal>]
let minimapSize = 200.0f

[<Literal>]
let minimapMargin = 10.0f

[<Literal>]
let minimapWorldRadius = 40.0f

[<Literal>]
let sampleStep = 2

[<Literal>]
let updateInterval = 4

[<Literal>]
let private texSize = 200

type XnaColor = Microsoft.Xna.Framework.Color

// ── Helpers ──

let private blockColor (fallbackColor: XnaColor) (blockType: BlockType) =
  match blockType with
  | Ground
  | GroundSlopeXPos
  | GroundSlopeXNeg
  | GroundSlopeZPos
  | GroundSlopeZNeg -> XnaColor(76, 153, 0)
  | Platform
  | PlatformRamp -> XnaColor(100, 100, 100)
  | SnowGround
  | SnowSlopeXPos
  | SnowSlopeXNeg
  | SnowSlopeZPos
  | SnowSlopeZNeg -> XnaColor(230, 230, 230)
  | TreePine
  | TreeSnow -> XnaColor(0, 100, 0)
  | Rock -> XnaColor(128, 128, 128)
  | GrassTuft -> XnaColor(50, 120, 50)
  | Coin -> XnaColor(255, 215, 0)
  | Jewel -> XnaColor(0, 191, 255)
  | Heart -> XnaColor(255, 0, 0)
  | Star -> XnaColor(255, 255, 0)
  | Mushrooms
  | MushroomLight -> XnaColor(139, 69, 19)
  | Crate -> XnaColor(160, 82, 45)
  | Barrel -> XnaColor(139, 90, 43)
  | Flag -> XnaColor(255, 0, 0)
  | Spikes -> XnaColor(192, 192, 192)
  | Empty -> fallbackColor

// ── System ──

let private collectBlocks
  (playerPos: System.Numerics.Vector3)
  (bounds: Mibo.Layout3D.BoundingBox)
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  (blocks: Dictionary<struct (int * int), struct (float32 * BlockType)>)
  : unit =
  blocks.Clear()

  for KeyValue(struct (_cx, _cz), chunk) in chunks do
    if
      chunk.Bounds.Max.X >= bounds.Min.X
      && chunk.Bounds.Min.X <= bounds.Max.X
      && chunk.Bounds.Max.Z >= bounds.Min.Z
      && chunk.Bounds.Min.Z <= bounds.Max.Z
    then
      CellGrid3D.iterVolume
        bounds
        (fun x y z blockType ->
          if blockType <> Empty then
            let worldX =
              chunk.Grid.Origin.X + float32 x * chunk.Grid.CellSize.X

            let worldZ =
              chunk.Grid.Origin.Z + float32 z * chunk.Grid.CellSize.Z

            let worldY =
              chunk.Grid.Origin.Y + float32 y * chunk.Grid.CellSize.Y

            let qx = int(worldX) / sampleStep * sampleStep
            let qz = int(worldZ) / sampleStep * sampleStep
            let key = struct (qx, qz)

            match blocks.TryGetValue key with
            | true, struct (existingY, _) when existingY >= worldY -> ()
            | _ -> blocks[key] <- struct (worldY, blockType))
        chunk.Grid

let private generatePixels
  (playerPos: System.Numerics.Vector3)
  (timeOfDay: float32)
  (scale: float32)
  (blocks: Dictionary<struct (int * int), struct (float32 * BlockType)>)
  : XnaColor[] =
  let halfMinimap = minimapSize * 0.5f
  let bgVec = DayNight.getSkyColor timeOfDay

  let bgColor =
    XnaColor(
      byte(float32 bgVec.R * 0.3f),
      byte(float32 bgVec.G * 0.3f),
      byte(float32 bgVec.B * 0.3f),
      200uy
    )

  let pixels = Array.create (texSize * texSize) bgColor

  let pixelSize = float32 sampleStep * scale + 1.0f
  let pixelSizeI = max 1 (int pixelSize)

  let fillRect(px: int, py: int, color: XnaColor) =
    let x0 = max 0 px
    let y0 = max 0 py
    let x1 = min texSize (px + pixelSizeI)
    let y1 = min texSize (py + pixelSizeI)

    for yy = y0 to y1 - 1 do
      let row = yy * texSize

      for xx = x0 to x1 - 1 do
        pixels[row + xx] <- color

  for KeyValue(struct (wx, wz), struct (_, blockType)) in blocks do
    let relX = (float32 wx - playerPos.X) * scale
    let relZ = (float32 wz - playerPos.Z) * scale
    let pixelX = int(halfMinimap + relX)
    let pixelZ = int(halfMinimap + relZ)

    if
      pixelX >= -pixelSizeI
      && pixelX < texSize
      && pixelZ >= -pixelSizeI
      && pixelZ < texSize
    then
      let color = blockColor bgVec blockType

      if color.A > 0uy then
        fillRect(pixelX, pixelZ, color)

  pixels

let uploadTexture
  (pixels: XnaColor[])
  (model: byref<MinimapModel>)
  (gd: GraphicsDevice)
  =
  if model.TexReady then
    model.Texture.SetData(pixels)
  else
    let tex = new Texture2D(gd, texSize, texSize, false, SurfaceFormat.Color)
    tex.SetData(pixels)
    model.Texture <- tex
    model.TexReady <- true

let generateMinimapPixels
  (playerPos: System.Numerics.Vector3)
  (timeOfDay: float32)
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  : XnaColor[] =
  let scale = minimapSize / (minimapWorldRadius * 2.0f)

  // The bounds record is the Core Mibo.Layout3D.BoundingBox (System.Numerics),
  // so build its Min/Max in System.Numerics.Vector3 explicitly.
  let bounds = {
    Min =
      System.Numerics.Vector3(
        playerPos.X - minimapWorldRadius,
        -100.0f,
        playerPos.Z - minimapWorldRadius
      )
    Max =
      System.Numerics.Vector3(
        playerPos.X + minimapWorldRadius,
        100.0f,
        playerPos.Z + minimapWorldRadius
      )
  }

  let blocks = Dictionary<struct (int * int), struct (float32 * BlockType)>()
  collectBlocks playerPos bounds chunks blocks
  generatePixels playerPos timeOfDay scale blocks


// ── View ──

let private viewInner
  (ctx: GameContext)
  (minimap: MinimapModel)
  (playerPos: Microsoft.Xna.Framework.Vector3)
  (playerFacing: float32)
  (buffer: RenderBuffer2D)
  =
  let screenWidth = float32 ctx.WindowWidth
  let screenHeight = float32 ctx.WindowHeight

  let minimapX = int(screenWidth - minimapSize - minimapMargin)
  let minimapY = int(screenHeight - minimapSize - minimapMargin)
  let halfMinimap = minimapSize * 0.5f

  if minimap.TexReady then
    buffer
    |> Draw.sprite(
      SpriteState.create(
        minimap.Texture,
        Microsoft.Xna.Framework.Rectangle(
          minimapX,
          minimapY,
          int minimapSize,
          int minimapSize
        ),
        Microsoft.Xna.Framework.Rectangle(0, 0, texSize, texSize)
      )
      |> SpriteState.withLayer 100<RenderLayer>
    )
    |> Draw.drop

  let centerX = float32 minimapX + halfMinimap
  let centerY = float32 minimapY + halfMinimap
  let facingX = sin playerFacing
  let facingZ = cos playerFacing

  buffer
  |> Draw.fillCircle
    (102<RenderLayer>, XnaColor.Yellow)
    (Microsoft.Xna.Framework.Vector2(centerX, centerY), 3.0f)
  |> Draw.lineThick
    (102<RenderLayer>, XnaColor.Yellow, 2.0f)
    (Microsoft.Xna.Framework.Vector2(centerX, centerY),
     Microsoft.Xna.Framework.Vector2(
       centerX + facingX * 10.0f,
       centerY + facingZ * 10.0f
     ))
  |> Draw.rectOutline
    (103<RenderLayer>, XnaColor.White, 2.0f)
    (Microsoft.Xna.Framework.Rectangle(
      minimapX,
      minimapY,
      int minimapSize,
      int minimapSize
    ))
  |> Draw.drop

let view (ctx: GameContext) (model: GameModel) (buffer: RenderBuffer2D) =
  viewInner ctx model.Minimap model.PlayerPosition model.PlayerFacing buffer
