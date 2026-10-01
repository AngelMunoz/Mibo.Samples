module Platformer3D.Types

open System.Collections.Generic
open System.Numerics
open Mibo.Layout3D

/// Logical layer indices for the layered chunk grid. Each category maps to a
/// separate CellGrid3D inside the LayeredMap3D, so consumers only scan the
/// layers they care about (physics reads terrain; rendering walks all visible
/// layers). Mirrors the 2D sample's Layer module.
module Layer =
  [<Literal>]
  let Terrain = 0

  [<Literal>]
  let Hazards = 1

  [<Literal>]
  let Collectibles = 2

  [<Literal>]
  let Decorations = 3

/// The chunk's parallel voxel layers — a dictionary of grids the sample
/// owns. This is exactly what the retired framework `LayeredGrid3D` was:
/// dims plus a lazily filled layer dictionary.
type LayeredMap3D<'T> = {
  Width: int
  Height: int
  Depth: int
  CellSize: Vector3
  Origin: Vector3
  Layers: Dictionary<int, CellGrid3D<'T>>
}

module LayeredMap3D =

  let create
    width
    height
    depth
    (cellSize: Vector3)
    (origin: Vector3)
    : LayeredMap3D<'T> =
    {
      Width = width
      Height = height
      Depth = depth
      CellSize = cellSize
      Origin = origin
      Layers = Dictionary()
    }

  let getOrAddLayer
    index
    (m: LayeredMap3D<'T>)
    : struct (CellGrid3D<'T> * LayeredMap3D<'T>) =
    let mutable existing = Unchecked.defaultof<CellGrid3D<'T>>

    if m.Layers.TryGetValue(index, &existing) then
      struct (existing, m)
    else
      let grid = CellGrid3D.create m.Width m.Height m.Depth m.CellSize m.Origin
      m.Layers.Add(index, grid)
      struct (grid, m)

  /// Runs a `Layout3D` paint pipeline over one layer (the retired
  /// `LayeredLayout3D.layer`).
  let runLayer
    index
    (paint: GridSection3D<'T> -> GridSection3D<'T>)
    (m: LayeredMap3D<'T>)
    : LayeredMap3D<'T> =
    let struct (grid, m) = getOrAddLayer index m
    Layout3D.run paint grid |> ignore
    m

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
  Grids: LayeredMap3D<BlockType>
  Bounds: BoundingBox
  OriginX: int
  OriginZ: int
}
