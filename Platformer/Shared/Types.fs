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
type TileType =
  | Empty
  | Ground
  | Platform
  | Spikes
  | Coin
  | Flag

[<Struct>]
type Biome =
  | Grass
  | Stone
  | Snow
  | Sand

[<Struct>]
type TorchLight = {
  Position: Vector2
  Color: Mibo.Color
  Radius: float32
}

[<Struct>]
type Chunk = {
  Grid: CellGrid2D<TileType>
  Platforms: Rect[]
  Spikes: Rect[]
  Coins: Rect[]
  Flags: Rect[]
  Occluders: Occluder[]
  Torches: TorchLight[]
  Bounds: Rect
  Biome: Biome
}
