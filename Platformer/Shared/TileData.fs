/// Tile metadata registry — resolves sprite source rect + collider per Tile.
/// Sprite positions are hardcoded from the Kenney spritesheet-tiles-default.xml atlas.
/// Collider shapes are from the TSX per-tile collision objects.
module Platformer.TileData

open Platformer.Types

[<Literal>]
let ts = 64.0f

let private fullBlock: Rect = {
  X = 0.0f
  Y = 0.0f
  Width = ts
  Height = ts
}

let private cloudRect: Rect = {
  X = 0.0f
  Y = 0.0f
  Width = ts
  Height = 52.0f
}

let private cloudBgRect: Rect = {
  X = 0.0f
  Y = 8.0f
  Width = ts
  Height = 46.0f
}

let private bridgeRect: Rect = {
  X = 0.0f
  Y = 0.0f
  Width = ts
  Height = 34.0f
}

let private bridgeLogsRect: Rect = {
  X = 0.0f
  Y = 0.0f
  Width = ts
  Height = 27.0f
}

let private lavaTopLowRect: Rect = {
  X = 0.0f
  Y = 32.0f
  Width = ts
  Height = 32.0f
}

// Pixel positions for each biome's tile group, extracted from the XML atlas.
// Each group has 28 tiles laid out sequentially across 18-column rows.
// Order: block, block_bottom, block_bottom_left, block_bottom_right,
//        block_center, block_left, block_right, block_top, block_top_left, block_top_right,
//        cloud, cloud_background, cloud_left, cloud_middle, cloud_right,
//        horizontal_left, horizontal_middle, horizontal_overhang_left, horizontal_overhang_right, horizontal_right,
//        ramp_long_a, ramp_long_b, ramp_long_c, ramp_short_a, ramp_short_b,
//        vertical_bottom, vertical_middle, vertical_top

let private grassTiles: (float32 * float32)[] = [|
  (260.0f, 585.0f)
  (325.0f, 585.0f)
  (390.0f, 585.0f)
  (455.0f, 585.0f)
  (520.0f, 585.0f)
  (585.0f, 585.0f)
  (650.0f, 585.0f)
  (715.0f, 585.0f)
  (780.0f, 585.0f)
  (845.0f, 585.0f)
  // cloud row
  (910.0f, 585.0f)
  (975.0f, 585.0f)
  (1040.0f, 585.0f)
  (1105.0f, 585.0f)
  (0.0f, 650.0f)
  // horizontal row
  (65.0f, 650.0f)
  (130.0f, 650.0f)
  (195.0f, 650.0f)
  (260.0f, 650.0f)
  (325.0f, 650.0f)
  // ramp
  (390.0f, 650.0f)
  (455.0f, 650.0f)
  (520.0f, 650.0f)
  (585.0f, 650.0f)
  (650.0f, 650.0f)
  // vertical
  (715.0f, 650.0f)
  (780.0f, 650.0f)
  (845.0f, 650.0f)
|]

let private dirtTiles: (float32 * float32)[] = [|
  (780.0f, 455.0f)
  (845.0f, 455.0f)
  (910.0f, 455.0f)
  (975.0f, 455.0f)
  (1040.0f, 455.0f)
  (1105.0f, 455.0f)
  (0.0f, 520.0f)
  (65.0f, 520.0f)
  (130.0f, 520.0f)
  (195.0f, 520.0f)
  // cloud row
  (260.0f, 520.0f)
  (325.0f, 520.0f)
  (390.0f, 520.0f)
  (455.0f, 520.0f)
  (520.0f, 520.0f)
  // horizontal row
  (585.0f, 520.0f)
  (650.0f, 520.0f)
  (715.0f, 520.0f)
  (780.0f, 520.0f)
  (845.0f, 520.0f)
  // ramp
  (910.0f, 520.0f)
  (975.0f, 520.0f)
  (1040.0f, 520.0f)
  (1105.0f, 520.0f)
  (0.0f, 585.0f)
  // vertical
  (65.0f, 585.0f)
  (130.0f, 585.0f)
  (195.0f, 585.0f)
|]

let private sandTiles: (float32 * float32)[] = [|
  (390.0f, 780.0f)
  (455.0f, 780.0f)
  (520.0f, 780.0f)
  (585.0f, 780.0f)
  (650.0f, 780.0f)
  (715.0f, 780.0f)
  (780.0f, 780.0f)
  (845.0f, 780.0f)
  (910.0f, 780.0f)
  (975.0f, 780.0f)
  // cloud row
  (1040.0f, 780.0f)
  (1105.0f, 780.0f)
  (0.0f, 845.0f)
  (65.0f, 845.0f)
  (130.0f, 845.0f)
  // horizontal row
  (195.0f, 845.0f)
  (260.0f, 845.0f)
  (325.0f, 845.0f)
  (390.0f, 845.0f)
  (455.0f, 845.0f)
  // ramp
  (520.0f, 845.0f)
  (585.0f, 845.0f)
  (650.0f, 845.0f)
  (715.0f, 845.0f)
  (780.0f, 845.0f)
  // vertical
  (845.0f, 845.0f)
  (910.0f, 845.0f)
  (975.0f, 845.0f)
|]

let private snowTiles: (float32 * float32)[] = [|
  (1040.0f, 845.0f)
  (1105.0f, 845.0f)
  (0.0f, 910.0f)
  (65.0f, 910.0f)
  (130.0f, 910.0f)
  (195.0f, 910.0f)
  (260.0f, 910.0f)
  (325.0f, 910.0f)
  (390.0f, 910.0f)
  (455.0f, 910.0f)
  // cloud row
  (520.0f, 910.0f)
  (585.0f, 910.0f)
  (650.0f, 910.0f)
  (715.0f, 910.0f)
  (780.0f, 910.0f)
  // horizontal row
  (845.0f, 910.0f)
  (910.0f, 910.0f)
  (975.0f, 910.0f)
  (1040.0f, 910.0f)
  (1105.0f, 910.0f)
  // ramp
  (0.0f, 975.0f)
  (65.0f, 975.0f)
  (130.0f, 975.0f)
  (195.0f, 975.0f)
  (260.0f, 975.0f)
  // vertical
  (325.0f, 975.0f)
  (390.0f, 975.0f)
  (455.0f, 975.0f)
|]

let private stoneTiles: (float32 * float32)[] = [|
  (520.0f, 975.0f)
  (585.0f, 975.0f)
  (650.0f, 975.0f)
  (715.0f, 975.0f)
  (780.0f, 975.0f)
  (845.0f, 975.0f)
  (910.0f, 975.0f)
  (975.0f, 975.0f)
  (1040.0f, 975.0f)
  (1105.0f, 975.0f)
  // cloud row
  (0.0f, 1040.0f)
  (65.0f, 1040.0f)
  (130.0f, 1040.0f)
  (195.0f, 1040.0f)
  (260.0f, 1040.0f)
  // horizontal row
  (325.0f, 1040.0f)
  (390.0f, 1040.0f)
  (455.0f, 1040.0f)
  (520.0f, 1040.0f)
  (585.0f, 1040.0f)
  // ramp
  (650.0f, 1040.0f)
  (715.0f, 1040.0f)
  (780.0f, 1040.0f)
  (845.0f, 1040.0f)
  (910.0f, 1040.0f)
  // vertical
  (975.0f, 1040.0f)
  (1040.0f, 1040.0f)
  (1105.0f, 1040.0f)
|]

let private purpleTiles: (float32 * float32)[] = [|
  (910.0f, 650.0f)
  (975.0f, 650.0f)
  (1040.0f, 650.0f)
  (1105.0f, 650.0f)
  (0.0f, 715.0f)
  (65.0f, 715.0f)
  (130.0f, 715.0f)
  (195.0f, 715.0f)
  (260.0f, 715.0f)
  (325.0f, 715.0f)
  // cloud row
  (390.0f, 715.0f)
  (455.0f, 715.0f)
  (520.0f, 715.0f)
  (585.0f, 715.0f)
  (650.0f, 715.0f)
  // horizontal row
  (715.0f, 715.0f)
  (780.0f, 715.0f)
  (845.0f, 715.0f)
  (910.0f, 715.0f)
  (975.0f, 715.0f)
  // ramp
  (1040.0f, 715.0f)
  (1105.0f, 715.0f)
  (0.0f, 780.0f)
  (65.0f, 780.0f)
  (130.0f, 780.0f)
  // vertical
  (195.0f, 780.0f)
  (260.0f, 780.0f)
  (325.0f, 780.0f)
|]

// Tile group index order (matches the atlas layout per biome):
// 0=block 1=block_bottom 2=block_bottom_left 3=block_bottom_right
// 4=block_center 5=block_left 6=block_right 7=block_top 8=block_top_left 9=block_top_right
// 10=cloud 11=cloud_background 12=cloud_left 13=cloud_middle 14=cloud_right
// 15=horizontal_left 16=horizontal_middle 17=horizontal_overhang_left 18=horizontal_overhang_right 19=horizontal_right
// 20=ramp_long_a 21=ramp_long_b 22=ramp_long_c 23=ramp_short_a 24=ramp_short_b
// 25=vertical_bottom 26=vertical_middle 27=vertical_top

let private biomeTiles(biome: Biome) : (float32 * float32)[] =
  match biome with
  | Grass -> grassTiles
  | Dirt -> dirtTiles
  | Sand -> sandTiles
  | Snow -> snowTiles
  | Stone -> stoneTiles
  | Purple -> purpleTiles

let private sprite
  (biome: Biome)
  (idx: int)
  (collider: ColliderKind)
  (rect: Rect)
  : TileInfo =
  let x, y = biomeTiles(biome)[idx]

  {
    SpriteX = x
    SpriteY = y
    Collider = collider
    ColliderRect = rect
  }

let private spriteRaw
  (x: float32)
  (y: float32)
  (collider: ColliderKind)
  (rect: Rect)
  : TileInfo =
  {
    SpriteX = x
    SpriteY = y
    Collider = collider
    ColliderRect = rect
  }

/// Lookup sprite source rect and collider for a Tile.
let lookup(tile: Tile) : TileInfo =
  match tile with
  | Empty -> spriteRaw 0.0f 0.0f None fullBlock

  // Solid blocks
  | Tile.Block b -> sprite b 0 FullBlock fullBlock
  | Tile.BlockBottom b -> sprite b 1 FullBlock fullBlock
  | Tile.BlockBottomLeft b -> sprite b 2 FullBlock fullBlock
  | Tile.BlockBottomRight b -> sprite b 3 FullBlock fullBlock
  | Tile.BlockCenter b -> sprite b 4 None fullBlock
  | Tile.BlockLeft b -> sprite b 5 FullBlock fullBlock
  | Tile.BlockRight b -> sprite b 6 FullBlock fullBlock
  | Tile.BlockTop b -> sprite b 7 FullBlock fullBlock
  | Tile.BlockTopLeft b -> sprite b 8 FullBlock fullBlock
  | Tile.BlockTopRight b -> sprite b 9 FullBlock fullBlock

  // Horizontal
  | Tile.HorizontalLeft b -> sprite b 15 FullBlock fullBlock
  | Tile.Horizontal b -> sprite b 16 FullBlock fullBlock
  | Tile.HorizontalOverhangLeft b -> sprite b 17 FullBlock fullBlock
  | Tile.HorizontalOverhangRight b -> sprite b 18 FullBlock fullBlock
  | Tile.HorizontalRight b -> sprite b 19 FullBlock fullBlock

  // Vertical
  | Tile.VerticalBottom b -> sprite b 25 FullBlock fullBlock
  | Tile.VerticalMiddle b -> sprite b 26 FullBlock fullBlock
  | Tile.VerticalTop b -> sprite b 27 FullBlock fullBlock

  // Ramps
  | Tile.RampLongA b -> sprite b 20 FullBlock fullBlock
  | Tile.RampLongB b -> sprite b 21 FullBlock fullBlock
  | Tile.RampLongC b -> sprite b 22 FullBlock fullBlock
  | Tile.RampShortA b -> sprite b 23 FullBlock fullBlock
  | Tile.RampShortB b -> sprite b 24 FullBlock fullBlock

  // One-way platforms
  | Tile.Cloud b -> sprite b 10 OneWay cloudRect
  | Tile.CloudBackground b -> sprite b 11 OneWay cloudBgRect
  | Tile.CloudLeft b -> sprite b 12 OneWay cloudRect
  | Tile.CloudMiddle b -> sprite b 13 OneWay cloudRect
  | Tile.CloudRight b -> sprite b 14 OneWay cloudRect

  // Bridges
  | Tile.Bridge -> spriteRaw 715.0f 65.0f OneWay bridgeRect
  | Tile.BridgeLogs -> spriteRaw 780.0f 65.0f OneWay bridgeLogsRect

  // Hazards
  | Tile.Spikes -> spriteRaw 0.0f 455.0f Hazard fullBlock
  | Tile.BlockSpikes -> spriteRaw 715.0f 0.0f Hazard fullBlock
  | Tile.Lava -> spriteRaw 910.0f 325.0f Hazard fullBlock
  | Tile.LavaTop -> spriteRaw 975.0f 325.0f Hazard fullBlock
  | Tile.LavaTopLow -> spriteRaw 1040.0f 325.0f Hazard lavaTopLowRect

  // Collectibles
  | Tile.Coin -> spriteRaw 0.0f 130.0f None fullBlock
  | Tile.Flag -> spriteRaw 1105.0f 130.0f None fullBlock

// -------------------------------------------------------------
// Predicates for batch processing
// -------------------------------------------------------------

let isSolid(tile: Tile) : bool =
  match tile with
  | Empty
  | Coin
  | Flag
  | Cloud _
  | CloudLeft _
  | CloudMiddle _
  | CloudRight _
  | CloudBackground _
  | BlockCenter _
  | Bridge
  | BridgeLogs
  | Spikes
  | BlockSpikes
  | Lava
  | LavaTop
  | LavaTopLow -> false
  | _ -> true

let isOneWay(tile: Tile) : bool =
  match tile with
  | Cloud _
  | CloudLeft _
  | CloudMiddle _
  | CloudRight _
  | CloudBackground _
  | Bridge
  | BridgeLogs -> true
  | _ -> false

let isHazard(tile: Tile) : bool =
  match tile with
  | Spikes
  | BlockSpikes
  | Lava
  | LavaTop
  | LavaTopLow -> true
  | _ -> false

let isCoin(tile: Tile) : bool = tile = Coin

let isFlag(tile: Tile) : bool = tile = Flag

let isEmpty(tile: Tile) : bool = tile = Empty
