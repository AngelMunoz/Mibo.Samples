namespace Platformer

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Mibo
open Mibo.Layout
open Platformer.Types
open Platformer.Constants
open Platformer.DayNight

module Minimap =

  [<Literal>]
  let minimapSize = 200.0f

  [<Literal>]
  let minimapMargin = 10.0f

  [<Literal>]
  let minimapWorldRadius = 400.0f

  [<Literal>]
  let updateInterval = 4

  [<Literal>]
  let texSize = 200

  let private tileColor (skyColor: Color) (biome: Biome) (tile: Tile) : Color =
    match tile with
    | Tile.Empty -> skyColor
    | Tile.Coin -> Color.rgb 255uy 215uy 0uy
    | Tile.Flag -> Color.rgb 255uy 0uy 0uy
    | Tile.Spikes
    | Tile.BlockSpikes
    | Tile.Lava
    | Tile.LavaTop
    | Tile.LavaTopLow -> Color.rgb 192uy 192uy 192uy
    | Tile.Bridge
    | Tile.BridgeLogs -> Color.rgb 139uy 90uy 43uy
    | _ ->
      // Solid or one-way tiles: color by biome
      match biome with
      | Grass -> Color.rgb 76uy 153uy 0uy
      | Dirt -> Color.rgb 139uy 90uy 43uy
      | Stone -> Color.rgb 128uy 128uy 128uy
      | Snow -> Color.rgb 230uy 230uy 230uy
      | Sand -> Color.rgb 210uy 180uy 140uy
      | Purple -> Color.rgb 160uy 100uy 200uy

  let generateMinimapData
    (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
    (timeOfDay: float32)
    (playerPos: Vector2)
    : struct (Mibo.Color[] * int * int) =
    let scale = minimapSize / (minimapWorldRadius * 2.0f)

    let blocks =
      Dictionary<struct (int * int), struct (float32 * Tile * Biome)>()

    for KeyValue(_, chunk) in chunks do
      if
        chunk.Bounds.X + chunk.Bounds.Width >= playerPos.X - minimapWorldRadius
        && chunk.Bounds.X <= playerPos.X + minimapWorldRadius
        && chunk.Bounds.Y + chunk.Bounds.Height
           >= playerPos.Y - minimapWorldRadius
        && chunk.Bounds.Y <= playerPos.Y + minimapWorldRadius
      then
        let struct (terrainGrid, _) =
          LayeredGrid2D.getOrAddLayer Layer.Terrain chunk.Grids

        let cellW = terrainGrid.CellSize.X
        let cellH = terrainGrid.CellSize.Y

        for y in 0 .. terrainGrid.Height - 1 do
          for x in 0 .. terrainGrid.Width - 1 do
            match CellGrid2D.get x y terrainGrid with
            | ValueSome tile when tile <> Tile.Empty ->
              let key =
                struct (int(terrainGrid.Origin.X + float32 x * cellW),
                        int(terrainGrid.Origin.Y + float32 y * cellH))

              if not(blocks.ContainsKey key) then
                blocks[key] <-
                  struct (terrainGrid.Origin.Y + float32 y * cellH,
                          tile,
                          chunk.Biome)
            | _ -> ()

    let skyTop, _ = getSkyColors timeOfDay
    let halfMinimap = minimapSize * 0.5f
    let pixelSizeI = max 1 (int(tileSize * scale + 1.0f))
    let count = texSize * texSize
    let colors = Array.zeroCreate<Mibo.Color> count

    for i in 0 .. count - 1 do
      colors[i] <- skyTop

    for KeyValue(struct (wx, wz), struct (_, tile, biome)) in blocks do
      let pixelX = int(halfMinimap + (float32 wx - playerPos.X) * scale)
      let pixelZ = int(halfMinimap + (float32 wz - playerPos.Y) * scale)
      let color = tileColor skyTop biome tile

      if color.A > 0uy then
        for py in pixelZ .. pixelZ + pixelSizeI - 1 do
          for px in pixelX .. pixelX + pixelSizeI - 1 do
            if px >= 0 && px < texSize && py >= 0 && py < texSize then
              colors[py * texSize + px] <- color

    struct (colors, texSize, texSize)

// -------------------------------------------------------------
// Minimap Sub-system (M_U — backend-agnostic)
// -------------------------------------------------------------

module MinimapSystem =

  open Mibo.Elmish

  [<Struct>]
  type MinimapModel = {
    FrameCounter: int
    LastPlayerPos: Vector2
  }

  let init() = {
    FrameCounter = 0
    LastPlayerPos = Vector2.Zero
  }

  [<Struct>]
  type MinimapMsg =
    | MinimapReady of colors: Mibo.Color[] * width: int * height: int

  let update
    (playerPos, chunks, timeOfDay)
    (model: MinimapModel)
    : struct (MinimapModel * Cmd<MinimapMsg>) =
    let posDelta = playerPos - model.LastPlayerPos

    let needsUpdate =
      model.FrameCounter % Minimap.updateInterval = 0
      || posDelta.LengthSquared() > 4.0f

    let model = {
      model with
          FrameCounter = model.FrameCounter + 1
    }

    if needsUpdate then
      let model = { model with LastPlayerPos = playerPos }

      let cmd =
        Cmd.ofAsync
          (async {
            return Minimap.generateMinimapData chunks timeOfDay playerPos
          })
          (fun struct (colors, w, h) -> MinimapReady(colors, w, h))
          (fun _ex -> MinimapReady([| Mibo.Color.Black |], 1, 1))

      model, cmd
    else
      model, Cmd.none
