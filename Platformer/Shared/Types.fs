module Platformer.Types

open System.Numerics
open Mibo.Layout

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

[<Struct>]
type AnimationState =
  | Idle
  | Walk
  | Jump
  | Fall

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

/// Flat tile type stored in the grid. Biome carried as a field to avoid nesting.
/// Collider/sprite data is resolved via TileData.lookup — never stored per-cell.
[<Struct>]
type Tile =
  | Empty
  // Solid terrain (full 64x64 collider)
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
  // Solid ramps (polygon collider treated as full block for now)
  | RampLongA of biome: Biome
  | RampLongB of biome: Biome
  | RampLongC of biome: Biome
  | RampShortA of biome: Biome
  | RampShortB of biome: Biome
  // One-way platforms (collide from top only, partial-height rect)
  | Cloud of biome: Biome
  | CloudLeft of biome: Biome
  | CloudMiddle of biome: Biome
  | CloudRight of biome: Biome
  | CloudBackground of biome: Biome
  | Bridge
  | BridgeLogs
  // Hazards
  | Spikes
  | BlockSpikes
  | Lava
  | LavaTop
  | LavaTopLow
  // Collectibles / interactables (no collider for physics)
  | Coin
  | Flag

[<Struct>]
type TorchLight = {
  Position: Vector2
  Color: Mibo.Color
  Radius: float32
}

[<Struct>]
type Chunk = {
  Grid: CellGrid2D<Tile>
  Platforms: Rect[]
  Spikes: Rect[]
  Coins: Rect[]
  Flags: Rect[]
  Occluders: Occluder[]
  Torches: TorchLight[]
  Bounds: Rect
  Biome: Biome
}
