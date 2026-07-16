namespace Platformer3D

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Mibo
open Mibo.Layout3D
open Mibo.Elmish
open Platformer3D.Types
open Platformer3D.DayNight

module Minimap =

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

  let private blockColor (fallbackColor: Color) (blockType: BlockType) =
    match blockType with
    | Block Grass
    | LargeBlock Grass
    | TallBlock Grass
    | LongBlock Grass
    | LowBlock Grass
    | NarrowBlock Grass
    | Slope(Grass, _) -> Color.rgb 76uy 153uy 0uy
    | Block Snow
    | LargeBlock Snow
    | TallBlock Snow
    | LongBlock Snow
    | LowBlock Snow
    | NarrowBlock Snow
    | Slope(Snow, _) -> Color.rgb 230uy 230uy 230uy
    | Platform
    | PlatformRamp -> Color.rgb 100uy 100uy 100uy
    | TreePine
    | TreeSnow -> Color.rgb 0uy 100uy 0uy
    | Rock -> Color.rgb 128uy 128uy 128uy
    | GrassTuft -> Color.rgb 50uy 120uy 50uy
    | Coin -> Color.rgb 255uy 215uy 0uy
    | Jewel -> Color.rgb 0uy 191uy 255uy
    | Heart -> Color.rgb 255uy 0uy 0uy
    | Star -> Color.rgb 255uy 255uy 0uy
    | Mushrooms
    | MushroomLight -> Color.rgb 139uy 69uy 19uy
    | Crate -> Color.rgb 160uy 82uy 45uy
    | Barrel -> Color.rgb 139uy 90uy 43uy
    | Flag -> Color.rgb 255uy 0uy 0uy
    | Spikes -> Color.rgb 192uy 192uy 192uy
    | Empty -> fallbackColor

  let private collectBlocks
    (playerPos: Vector3)
    (bounds: BoundingBox)
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

  let generateMinimapData
    (playerPos: Vector3)
    (timeOfDay: float32)
    (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
    : struct (Color[] * int * int) =
    let scale = minimapSize / (minimapWorldRadius * 2.0f)

    let bounds = {
      Min =
        Vector3(
          playerPos.X - minimapWorldRadius,
          -100.0f,
          playerPos.Z - minimapWorldRadius
        )
      Max =
        Vector3(
          playerPos.X + minimapWorldRadius,
          100.0f,
          playerPos.Z + minimapWorldRadius
        )
    }

    let blocks = Dictionary<struct (int * int), struct (float32 * BlockType)>()

    collectBlocks playerPos bounds chunks blocks

    let skyColor = getSkyColor timeOfDay
    let halfMinimap = minimapSize * 0.5f

    let bgColor =
      Color.create
        (byte(float32 skyColor.R * 0.3f))
        (byte(float32 skyColor.G * 0.3f))
        (byte(float32 skyColor.B * 0.3f))
        200uy

    let pixels = Array.create (texSize * texSize) bgColor

    let pixelSize = float32 sampleStep * scale + 1.0f
    let pixelSizeI = max 1 (int pixelSize)

    let fillRect(px: int, py: int, color: Color) =
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
        let color = blockColor skyColor blockType

        if color.A > 0uy then
          fillRect(pixelX, pixelZ, color)

    struct (pixels, texSize, texSize)

// -------------------------------------------------------------
// Minimap Sub-system (backend-agnostic)
// -------------------------------------------------------------

module MinimapSystem =

  type MinimapModel() =
    member val FrameCounter = 0 with get, set
    member val LastPlayerPos = Constants.spawnPosition with get, set

  let init() = MinimapModel()

  [<Struct>]
  type MinimapMsg = MinimapReady of colors: Color[] * width: int * height: int

  let update
    (playerPos: Vector3)
    (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
    (timeOfDay: float32)
    (model: MinimapModel)
    : struct (MinimapModel * Cmd<MinimapMsg>) =
    let posDelta = playerPos - model.LastPlayerPos

    let needsUpdate =
      model.FrameCounter % Minimap.updateInterval = 0
      || posDelta.LengthSquared() > 4.0f

    model.FrameCounter <- model.FrameCounter + 1

    if needsUpdate then
      model.LastPlayerPos <- playerPos

      let cmd =
        Cmd.ofAsync
          (async {
            return Minimap.generateMinimapData playerPos timeOfDay chunks
          })
          (fun struct (colors, w, h) -> MinimapReady(colors, w, h))
          (fun _ex -> MinimapReady([| Color.Black |], 1, 1))

      struct (model, cmd)
    else
      struct (model, Cmd.none)
