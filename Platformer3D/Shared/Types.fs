module Platformer3D.Types

open System.Collections.Generic
open System.Numerics
open Mibo.Layout3D

/// Logical layer indices for the layered chunk grid. Each category maps to a
/// separate VoxelGrid inside the LayeredMap3D, so consumers only scan the
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

/// A voxel volume the sample owns — one `'T voption` per cell, indexed
/// `x + y*Width + z*Width*Height`. Platformer3D is a genuine voxel game
/// (arbitrary blocks per voxel, not per-column heights), so it keeps its own
/// volume storage the same way it owns the layer dictionary; the retired
/// framework `CellGrid3D` was exactly this shape, semantics preserved 1:1.
[<Struct>]
type VoxelGrid<'T> = {
  Origin: Vector3
  CellSize: Vector3
  Width: int
  Height: int
  Depth: int
  Cells: 'T voption[]
}

/// Cell access, iteration, and a 6-connected flood fill over a
/// <see cref="T:Platformer3D.Types.VoxelGrid`1"/>. Out-of-range writes are
/// no-ops and out-of-range reads return <c>ValueNone</c> (same contract the
/// retired framework grid had).
module VoxelGrid =

  let inline private toIndex x y z width height =
    x + y * width + z * width * height

  let create
    width
    height
    depth
    (cellSize: Vector3)
    (origin: Vector3)
    : VoxelGrid<'T> =
    {
      Origin = origin
      CellSize = cellSize
      Width = width
      Height = height
      Depth = depth
      Cells = Array.create (width * height * depth) ValueNone
    }

  let inline set x y z (content: 'T) (grid: VoxelGrid<'T>) : unit =
    if
      x >= 0
      && x < grid.Width
      && y >= 0
      && y < grid.Height
      && z >= 0
      && z < grid.Depth
    then
      let idx = toIndex x y z grid.Width grid.Height
      grid.Cells[idx] <- ValueSome content

  let inline get x y z (grid: VoxelGrid<'T>) : 'T voption =
    if
      x >= 0
      && x < grid.Width
      && y >= 0
      && y < grid.Height
      && z >= 0
      && z < grid.Depth
    then
      let idx = toIndex x y z grid.Width grid.Height
      grid.Cells[idx]
    else
      ValueNone

  let inline clear x y z (grid: VoxelGrid<'T>) : unit =
    if
      x >= 0
      && x < grid.Width
      && y >= 0
      && y < grid.Height
      && z >= 0
      && z < grid.Depth
    then
      let idx = toIndex x y z grid.Width grid.Height
      grid.Cells[idx] <- ValueNone

  let inline getWorldPos x y z (grid: VoxelGrid<'T>) : Vector3 =
    Vector3(
      grid.Origin.X + float32 x * grid.CellSize.X,
      grid.Origin.Y + float32 y * grid.CellSize.Y,
      grid.Origin.Z + float32 z * grid.CellSize.Z
    )

  let inline iter
    ([<InlineIfLambda>] action: int -> int -> int -> 'T -> unit)
    (grid: VoxelGrid<'T>)
    : unit =
    let w = grid.Width
    let wh = w * grid.Height

    for i in 0 .. grid.Cells.Length - 1 do
      match grid.Cells[i] with
      | ValueSome content ->
        let x = i % w
        let y = (i / w) % grid.Height
        let z = i / wh
        action x y z content
      | ValueNone -> ()

  /// Visits the populated cells inside `bounds` (inclusive cell range,
  /// clamped to the grid).
  let inline iterVolume
    (bounds: BoundingBox)
    ([<InlineIfLambda>] action: int -> int -> int -> 'T -> unit)
    (grid: VoxelGrid<'T>)
    : unit =
    let startX = max 0 (int((bounds.Min.X - grid.Origin.X) / grid.CellSize.X))
    let startY = max 0 (int((bounds.Min.Y - grid.Origin.Y) / grid.CellSize.Y))
    let startZ = max 0 (int((bounds.Min.Z - grid.Origin.Z) / grid.CellSize.Z))

    let endX =
      min
        (grid.Width - 1)
        (int((bounds.Max.X - grid.Origin.X) / grid.CellSize.X))

    let endY =
      min
        (grid.Height - 1)
        (int((bounds.Max.Y - grid.Origin.Y) / grid.CellSize.Y))

    let endZ =
      min
        (grid.Depth - 1)
        (int((bounds.Max.Z - grid.Origin.Z) / grid.CellSize.Z))

    let w = grid.Width
    let wh = w * grid.Height

    for z in startZ..endZ do
      let zOffset = z * wh

      for y in startY..endY do
        let yzOffset = zOffset + y * w

        for x in startX..endX do
          let idx = yzOffset + x

          match grid.Cells[idx] with
          | ValueSome content -> action x y z content
          | ValueNone -> ()

  /// 6-connected flood fill from `(x, y, z)` while `predicate` holds;
  /// returns the visited cells.
  let floodFill
    x
    y
    z
    (predicate: int -> int -> int -> bool)
    (grid: VoxelGrid<'T>)
    : struct (int * int * int)[] =
    let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)

    if w = 0 || h = 0 || d = 0 then
      Array.empty
    elif x < 0 || x >= w || y < 0 || y >= h || z < 0 || z >= d then
      Array.empty
    elif not(predicate x y z) then
      Array.empty
    else
      let total = w * h * d
      let visited = Array.zeroCreate<bool> total
      let queue = Array.zeroCreate<struct (int * int * int)> total
      let mutable head = 0
      let mutable tail = 0
      queue[tail] <- struct (x, y, z)
      tail <- tail + 1
      visited[toIndex x y z w h] <- true

      while head < tail do
        let struct (cx, cy, cz) = queue[head]
        head <- head + 1

        if cx > 0 then
          let nx = cx - 1
          let idx = toIndex nx cy cz w h

          if not visited[idx] && predicate nx cy cz then
            visited[idx] <- true
            queue[tail] <- struct (nx, cy, cz)
            tail <- tail + 1

        if cx < w - 1 then
          let nx = cx + 1
          let idx = toIndex nx cy cz w h

          if not visited[idx] && predicate nx cy cz then
            visited[idx] <- true
            queue[tail] <- struct (nx, cy, cz)
            tail <- tail + 1

        if cy > 0 then
          let ny = cy - 1
          let idx = toIndex cx ny cz w h

          if not visited[idx] && predicate cx ny cz then
            visited[idx] <- true
            queue[tail] <- struct (cx, ny, cz)
            tail <- tail + 1

        if cy < h - 1 then
          let ny = cy + 1
          let idx = toIndex cx ny cz w h

          if not visited[idx] && predicate cx ny cz then
            visited[idx] <- true
            queue[tail] <- struct (cx, ny, cz)
            tail <- tail + 1

        if cz > 0 then
          let nz = cz - 1
          let idx = toIndex cx cy nz w h

          if not visited[idx] && predicate cx cy nz then
            visited[idx] <- true
            queue[tail] <- struct (cx, cy, nz)
            tail <- tail + 1

        if cz < d - 1 then
          let nz = cz + 1
          let idx = toIndex cx cy nz w h

          if not visited[idx] && predicate cx cy nz then
            visited[idx] <- true
            queue[tail] <- struct (cx, cy, nz)
            tail <- tail + 1

      queue[0 .. tail - 1]

/// The chunk's parallel voxel layers — a dictionary of grids the sample
/// owns. This is exactly what the retired framework `LayeredGrid3D` was:
/// dims plus a lazily filled layer dictionary.
type LayeredMap3D<'T> = {
  Width: int
  Height: int
  Depth: int
  CellSize: Vector3
  Origin: Vector3
  Layers: Dictionary<int, VoxelGrid<'T>>
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
    : struct (VoxelGrid<'T> * LayeredMap3D<'T>) =
    let mutable existing = Unchecked.defaultof<VoxelGrid<'T>>

    if m.Layers.TryGetValue(index, &existing) then
      struct (existing, m)
    else
      let grid = VoxelGrid.create m.Width m.Height m.Depth m.CellSize m.Origin
      m.Layers.Add(index, grid)
      struct (grid, m)

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
