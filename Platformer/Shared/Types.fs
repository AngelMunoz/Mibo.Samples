module Platformer.Types

open System.Numerics
open Mibo.Layout

/// Logical layer indices for the layered chunk grid.
/// Each category maps to a separate CellGrid2D inside the LayeredGrid2D,
/// so consumers only scan the layers they care about (physics reads terrain
/// + hazards; rendering walks all visible layers in z-order; collectible
/// pickup scans the collectibles layer).
///
/// Full tile categorization from spritesheet-tiles-default.xml:
///
/// Terrain — solid collision (full block, one-way, ramp). Physics walks on it.
///   terrain_* (all 6 biomes × 28 variants), bridge, bridge_logs,
///   brick_brown, brick_grey, bricks_brown, bricks_grey,
///   brick_brown_diagonal, brick_grey_diagonal, rock, conveyor
///
/// Hazards — collision that damages / kills on contact.
///   spikes, block_spikes, lava, lava_top, lava_top_low,
///   water, water_top, water_top_low, saw, bomb, bomb_active, fireball
///
/// Collectibles — no collider, picked up on overlap.
///   coin_gold, coin_gold_side, coin_bronze, coin_bronze_side,
///   coin_silver, coin_silver_side,
///   gem_blue, gem_green, gem_red, gem_yellow,
///   heart, star,
///   key_blue, key_green, key_red, key_yellow
///
/// Interactables — collision or trigger with state changes.
///   flag_blue_a/b, flag_green_a/b, flag_red_a/b, flag_yellow_a/b, flag_off,
///   door_closed, door_closed_top, door_open, door_open_top,
///   lever, lever_left, lever_right,
///   lock_blue, lock_green, lock_red, lock_yellow,
///   switch_blue/green/red/yellow (+ _pressed),
///   spring, spring_out,
///   ladder_bottom, ladder_middle, ladder_top,
///   block_coin, block_coin_active, block_strong_coin, block_strong_coin_active,
///   block_empty, block_empty_warning, block_exclamation, block_exclamation_active,
///   block_strong_empty, block_strong_empty_active,
///   block_strong_danger, block_strong_danger_active,
///   block_blue, block_green, block_red, block_yellow
///
/// Decorations — no collider, render-only.
///   torch_off, torch_on_a, torch_on_b,
///   bush, cactus, grass, grass_purple, mushroom_brown, mushroom_red,
///   hill, hill_top, hill_top_smile, snow,
///   sign, sign_exit, sign_left, sign_right,
///   fence, fence_broken, chain, rope, rop_attached,
///   window, weight
module Layer =
  [<Literal>]
  let Terrain = 0

  [<Literal>]
  let Hazards = 1

  [<Literal>]
  let Collectibles = 2

  [<Literal>]
  let Interactables = 3

  [<Literal>]
  let Decorations = 4

[<Struct>]
type Rect = {
  X: float32
  Y: float32
  Width: float32
  Height: float32
}

[<Struct>]
type Occluder = { P1: Vector2; P2: Vector2 }

[<Struct>]
type Particle = {
  Position: Vector2
  Size: Vector2
  Rotation: float32
  Color: Mibo.Color
}

// -------------------------------------------------------------
// Domain Types
// -------------------------------------------------------------

[<Struct>]
type GameAction =
  | MoveLeft
  | MoveRight
  | Jump
  | Respawn
  | Down

[<Struct>]
type AnimationState =
  | Idle
  | Walk
  | Jump
  | Fall
  | Duck

[<Struct>]
type Biome =
  | Grass
  | Dirt
  | Stone
  | Snow
  | Sand
  | Purple

// -------------------------------------------------------------
// Tile system — flat DU, biome carried as field
// Collider/sprite data resolved on demand via TileData.lookup
// -------------------------------------------------------------

/// Collider category — determines how physics treats the tile
[<Struct>]
type ColliderKind =
  | None
  | FullBlock
  | OneWay
  | Hazard

/// Per-tile resolved data (never stored in the grid — computed on demand)
[<Struct>]
type TileInfo = {
  SpriteX: float32
  SpriteY: float32
  Collider: ColliderKind
  /// Collider rect offset within the 64x64 cell (in pixels)
  ColliderRect: Rect
}

/// Flat tile type stored in the layered grid. Biome carried as a field to avoid nesting.
/// Collider/sprite data is resolved via TileData.lookup — never stored per-cell.
/// Use `tileLayer` to determine which LayeredGrid2D layer a tile belongs to.
[<Struct>]
type Tile =
  | Empty
  // Terrain layer (Layer.Terrain) — solid physics collision
  | Block of biome: Biome
  | BlockTop of biome: Biome
  | BlockBottom of biome: Biome
  | BlockTopLeft of biome: Biome
  | BlockTopRight of biome: Biome
  | BlockBottomLeft of biome: Biome
  | BlockBottomRight of biome: Biome
  | BlockLeft of biome: Biome
  | BlockRight of biome: Biome
  | BlockCenter of biome: Biome
  | Horizontal of biome: Biome
  | HorizontalLeft of biome: Biome
  | HorizontalRight of biome: Biome
  | HorizontalOverhangLeft of biome: Biome
  | HorizontalOverhangRight of biome: Biome
  | VerticalTop of biome: Biome
  | VerticalMiddle of biome: Biome
  | VerticalBottom of biome: Biome
  // Terrain layer — solid ramps (polygon collider treated as full block for now)
  | RampLongA of biome: Biome
  | RampLongB of biome: Biome
  | RampLongC of biome: Biome
  | RampShortA of biome: Biome
  | RampShortB of biome: Biome
  // Terrain layer — one-way platforms (collide from top only, partial-height rect)
  | Cloud of biome: Biome
  | CloudLeft of biome: Biome
  | CloudMiddle of biome: Biome
  | CloudRight of biome: Biome
  | CloudBackground of biome: Biome
  | Bridge
  | BridgeLogs
  // Hazards layer (Layer.Hazards) — collision that damages/kills
  | Spikes
  | BlockSpikes
  | Lava
  | LavaTop
  | LavaTopLow
  // Collectibles layer (Layer.Collectibles) — no collider, picked up on overlap
  | Coin
  // Interactables layer (Layer.Interactables) — trigger/activation
  | Flag

/// Which layered-grid layer a tile belongs to.
/// Used when stamping tiles onto the correct CellGrid2D inside a LayeredGrid2D.
let tileLayer(tile: Tile) : int =
  match tile with
  | Empty -> Layer.Terrain
  // Terrain
  | Block _
  | BlockTop _
  | BlockBottom _
  | BlockTopLeft _
  | BlockTopRight _
  | BlockBottomLeft _
  | BlockBottomRight _
  | BlockLeft _
  | BlockRight _
  | BlockCenter _
  | Horizontal _
  | HorizontalLeft _
  | HorizontalRight _
  | HorizontalOverhangLeft _
  | HorizontalOverhangRight _
  | VerticalTop _
  | VerticalMiddle _
  | VerticalBottom _
  | RampLongA _
  | RampLongB _
  | RampLongC _
  | RampShortA _
  | RampShortB _
  | Cloud _
  | CloudLeft _
  | CloudMiddle _
  | CloudRight _
  | CloudBackground _
  | Bridge
  | BridgeLogs -> Layer.Terrain
  // Hazards
  | Spikes
  | BlockSpikes
  | Lava
  | LavaTop
  | LavaTopLow -> Layer.Hazards
  // Collectibles
  | Coin -> Layer.Collectibles
  // Interactables
  | Flag -> Layer.Interactables

[<Struct>]
type TorchLight = {
  Position: Vector2
  Color: Mibo.Color
  Radius: float32
}

[<Struct>]
type Chunk = {
  Grids: LayeredGrid2D<Tile>
  Platforms: Rect[]
  OneWayPlatforms: Rect[]
  Spikes: Rect[]
  Coins: Rect[]
  Flags: Rect[]
  Occluders: Occluder[]
  Torches: TorchLight[]
  Bounds: Rect
  Biome: Biome
}
