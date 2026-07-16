module Platformer3D.Types

open System.Numerics
open Mibo.Layout3D

[<Struct>]
type GameAction =
  | MoveLeft
  | MoveRight
  | MoveForward
  | MoveBackward
  | Jump
  | Respawn
  | RotateCameraLeft
  | RotateCameraRight
  | RotateCameraUp
  | RotateCameraDown

/// Terrain biome — grass/snow share identical block shapes, differing only by
/// color/model (confirmed via BoneProbe dimensions: footprints match per shape).
/// Like the 2D sample's `Biome`, this is carried as a field on terrain block
/// cases so each shape exists once instead of once-per-color.
[<Struct>]
type Biome3D =
  | Grass
  | Snow

/// Slope facing direction. Determines the model's Y rotation (see BlockData).
[<Struct>]
type SlopeDir =
  | XPos
  | XNeg
  | ZPos
  | ZNeg

/// Block type stored in the chunk grid. Terrain shapes carry biome as a field
/// (grass/snow = same shape, different color), folding the old Ground/SnowGround
/// and the four-per-biome slope cases into a single parametric case each.
///
/// Per-block data (model name, extents, vertical offset, rotation, category) is
/// resolved on demand via BlockData.lookup — never stored per-cell, mirroring the
/// 2D TileData.fs pattern.
[<Struct>]
type BlockType =
  | Empty
  // Terrain (biome-as-field) — solid collision
  | Block of biome: Biome3D
  | LargeBlock of biome: Biome3D
  | TallBlock of biome: Biome3D
  | LongBlock of biome: Biome3D
  | LowBlock of biome: Biome3D
  | NarrowBlock of biome: Biome3D
  | Slope of biome: Biome3D * dir: SlopeDir
  // Non-terrain (flat) — platforms, hazards, decorations, collectibles
  | Platform
  | PlatformRamp
  | Spikes
  | TreePine
  | TreeSnow
  | Rock
  | GrassTuft
  | Coin
  | Jewel
  | Heart
  | Star
  | Mushrooms
  | Crate
  | Barrel
  | Flag
  | MushroomLight

[<Struct>]
type Chunk = {
  Grid: CellGrid3D<BlockType>
  Bounds: BoundingBox
  OriginX: int
  OriginZ: int
}
