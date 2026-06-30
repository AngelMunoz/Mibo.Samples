namespace FPSSample

open System
open System.Numerics
open Mibo.Layout3D

/// Level definition built on Mibo.Layout3D.CellGrid3D.
/// The grid stores voxel cells (Wall, Floor, Cover, etc.) and collision
/// AABBs are extracted from solid cells for physics and raycasting.
module Level =

  /// Voxel cell types used to build the FPS arena.
  [<Struct; RequireQualifiedAccess>]
  type Cell =
    | Empty
    | Floor
    | Wall
    | Cover
    | Crate

  module Cell =
    let isSolid =
      function
      | Cell.Empty
      | Cell.Floor -> false
      | Cell.Wall
      | Cell.Cover
      | Cell.Crate -> true

    /// Kenney model path for this cell type (used by backend views).
    let modelPath =
      function
      | Cell.Floor -> FPSSample.Assets.blockGrassLarge
      | Cell.Wall -> FPSSample.Assets.blockGrass
      | Cell.Cover -> FPSSample.Assets.blockGrass
      | Cell.Crate -> FPSSample.Assets.crate
      | Cell.Empty -> ""

  /// Logical pickup kind (health/ammo).
  [<Struct; RequireQualifiedAccess>]
  type PickupKind =
    | Health
    | Ammo

  /// A pickup spawn point.
  [<Struct>]
  type PickupSpawn = { Kind: PickupKind; Position: Vector3 }

  /// An enemy spawn point.
  [<Struct>]
  type EnemySpawn = { Position: Vector3 }

  /// Complete level definition: voxel grid + spawn data.
  type LevelData = {
    Grid: CellGrid3D<Cell>
    CellSize: float32
    PlayerSpawn: Vector3
    EnemySpawns: EnemySpawn[]
    PickupSpawns: PickupSpawn[]
  }

  module LevelData =

    /// Converts a grid cell (x,y,z) to a world-space center position.
    let inline cellCenter
      (x: int)
      (y: int)
      (z: int)
      (level: LevelData)
      : Vector3 =
      CellGrid3D.getWorldPos x y z level.Grid

    /// Converts a grid cell to a world-space bounding box.
    let inline cellBounds
      (x: int)
      (y: int)
      (z: int)
      (level: LevelData)
      : BoundingBox =
      let center = cellCenter x y z level
      let half = level.CellSize * 0.5f

      {
        BoundingBox.Min =
          Vector3(center.X - half, center.Y - half, center.Z - half)
        BoundingBox.Max =
          Vector3(center.X + half, center.Y + half, center.Z + half)
      }

    /// Extracts all solid cell bounding boxes for collision and raycasting.
    let extractColliders(level: LevelData) : BoundingBox[] =
      let result = ResizeArray<BoundingBox>(256)

      level.Grid
      |> CellGrid3D.iter(fun x y z cell ->
        if Cell.isSolid cell then
          result.Add(cellBounds x y z level))

      result.ToArray()

    /// Finds the highest walkable surface at (x,z) and returns its top Y.
    /// Considers both Floor cells (walkable surface) and solid cells (walls/cover).
    let inline groundHeightAt
      (worldX: float32)
      (worldZ: float32)
      (level: LevelData)
      : float32 =
      let g = level.Grid
      let fx = (worldX - g.Origin.X) / g.CellSize.X
      let fz = (worldZ - g.Origin.Z) / g.CellSize.Z
      let cx = int(MathF.Floor(fx))
      let cz = int(MathF.Floor(fz))

      let mutable topY = 0.0f

      for y = g.Height - 1 downto 0 do
        match CellGrid3D.get cx y cz g with
        | ValueSome cell when cell <> Cell.Empty ->
          let center = CellGrid3D.getWorldPos cx y cz g
          topY <- center.Y + g.CellSize.Y * 0.5f
        | _ -> ()

      topY

    /// Builds the default FPS arena: a bounded floor with perimeter walls
    /// (outline only, not full slices), interior cover, crates, and a ramp.
    let createDefault() : LevelData =
      let cs = 2.0f
      let half = int(Constants.FloorSize / cs / 2.0f)
      let wallH = 2
      let w = half * 2 + 1
      let h = wallH
      let d = half * 2 + 1

      let grid =
        CellGrid3D.create
          w
          h
          d
          (Vector3(cs, cs, cs))
          (Vector3(-float32 half * cs, -cs * 0.5f, -float32 half * cs))

      // Floor at y=0
      grid |> CellGrid3D.iter(fun _ _ _ _ -> ()) // no-op

      for x = 0 to w - 1 do
        for z = 0 to d - 1 do
          CellGrid3D.set x 0 z Cell.Floor grid

      // Perimeter walls (outline only - just the edge cells)
      for y = 0 to h - 1 do
        for x = 0 to w - 1 do
          CellGrid3D.set x y 0 Cell.Wall grid
          CellGrid3D.set x y (d - 1) Cell.Wall grid

        for z = 0 to d - 1 do
          CellGrid3D.set 0 y z Cell.Wall grid
          CellGrid3D.set (w - 1) y z Cell.Wall grid

      // Interior cover: central pillar
      let mid = half

      for y = 0 to 1 do
        for dx = -1 to 1 do
          for dz = -1 to 1 do
            CellGrid3D.set (mid + dx) y (mid + dz) Cell.Wall grid

      // Crates as cover
      CellGrid3D.set (mid - 6) 0 (mid + 3) Cell.Crate grid
      CellGrid3D.set (mid - 6) 1 (mid + 3) Cell.Crate grid
      CellGrid3D.set (mid + 5) 0 (mid - 4) Cell.Crate grid
      CellGrid3D.set (mid - 9) 0 (mid - 6) Cell.Crate grid
      CellGrid3D.set (mid + 8) 0 (mid + 6) Cell.Crate grid

      // A stepped ramp going up along +Z near the east side
      let rampBaseX = mid + 5
      let rampBaseZ = mid + 5
      let rampDepth = 4
      let rampRise = 1

      for rz = 0 to rampDepth - 1 do
        let yMax = (rz * rampRise) / rampDepth

        for dx = 0 to 1 do
          for fy in 0..yMax do
            CellGrid3D.set (rampBaseX + dx) fy (rampBaseZ + rz) Cell.Wall grid

      let enemySpawns = [|
        {
          Position = Vector3(5.0f, 0.0f, -15.0f)
        }
        {
          Position = Vector3(-5.0f, 0.0f, -12.0f)
        }
        {
          Position = Vector3(10.0f, 0.0f, -8.0f)
        }
        {
          Position = Vector3(-12.0f, 0.0f, 5.0f)
        }
        { Position = Vector3(8.0f, 0.0f, 5.0f) }
      |]

      let pickupSpawns = [|
        {
          Kind = PickupKind.Health
          Position = Vector3(-8.0f, 0.5f, -8.0f)
        }
        {
          Kind = PickupKind.Health
          Position = Vector3(8.0f, 0.5f, -4.0f)
        }
        {
          Kind = PickupKind.Ammo
          Position = Vector3(-4.0f, 0.5f, -10.0f)
        }
        {
          Kind = PickupKind.Ammo
          Position = Vector3(4.0f, 0.5f, 2.0f)
        }
      |]

      {
        Grid = grid
        CellSize = cs
        PlayerSpawn = Vector3(-6.0f, Constants.PlayerEyeHeight, -6.0f)
        EnemySpawns = enemySpawns
        PickupSpawns = pickupSpawns
      }
