namespace FPSSample

open System
open System.Numerics
open Mibo.Layout
open Mibo.Layout3D

/// Level definition on a 2D footprint grid with per-column height —
/// the heightmap approach. Each (x, z) cell carries what stands there
/// and how tall it is; collision AABBs and ground heights derive from
/// the columns, and the views draw one native-size instance per
/// height level at the retired voxel positions. The arena itself is a
/// Flow document (floor fill, wall border, crates, pillar, ramp);
/// spawn points keep the retired level's literal coordinates.
module Level =

  /// Content kinds used to build the FPS arena.
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

  /// One footprint column: the content kind and how many cells it
  /// stacks from the ground. Height 0 stands for nothing.
  [<Struct>]
  type Column = { Kind: Cell; Height: int }

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

  /// Complete level definition: footprint columns + spawn data.
  type LevelData = {
    Grid: CellGrid2D<Column>
    CellSize: float32
    /// World Y of height level 0's center — the floor top lands at
    /// world Y 0, matching the retired voxel arena.
    BaseY: float32
    PlayerSpawn: Vector3
    EnemySpawns: EnemySpawn[]
    PickupSpawns: PickupSpawn[]
  }

  module LevelData =

    /// World-space center of stack level `y` of the column at (x, z) —
    /// the same positions the retired voxel grid produced.
    let inline cellCenter
      (x: int)
      (y: int)
      (z: int)
      (level: LevelData)
      : Vector3 =
      let corner = CellGrid2D.getWorldPos x z level.Grid

      Vector3(corner.X, level.BaseY + float32 y * level.CellSize, corner.Y)

    /// Extracts one collider AABB per solid stack level — walls,
    /// cover, and crates. Floor columns stay out (standing on the
    /// floor is a ground-height query, not a collider).
    let extractColliders(level: LevelData) : BoundingBox[] =
      let result = ResizeArray<BoundingBox>(256)
      let half = level.CellSize * 0.5f

      level.Grid
      |> CellGrid2D.iter(fun x z column ->
        if Cell.isSolid column.Kind then
          for y = 0 to column.Height - 1 do
            let center = cellCenter x y z level

            result.Add(
              {
                BoundingBox.Min =
                  Vector3(center.X - half, center.Y - half, center.Z - half)
                BoundingBox.Max =
                  Vector3(center.X + half, center.Y + half, center.Z + half)
              }
            ))

      result.ToArray()

    /// The world Y of a column's top surface — the highest walkable
    /// height at (x, z). Empty columns report 0.
    let inline columnTop (x: int) (z: int) (level: LevelData) : float32 =
      match CellGrid2D.get x z level.Grid with
      | ValueSome column when column.Height > 0 ->
        level.BaseY
        + float32(column.Height - 1) * level.CellSize
        + level.CellSize * 0.5f
      | _ -> 0.0f

    /// Finds the highest walkable surface under a world position.
    let inline groundHeightAt
      (worldX: float32)
      (worldZ: float32)
      (level: LevelData)
      : float32 =
      let g = level.Grid
      let cx = int(MathF.Floor((worldX - g.Origin.X) / g.CellSize.X))
      let cz = int(MathF.Floor((worldZ - g.Origin.Y) / g.CellSize.Y))

      columnTop cx cz level

    /// Builds the default FPS arena as one Flow document: a floor
    /// canvas with a wall border and crate columns, a tagged central
    /// pillar, and the ramp footprint. Spawn points keep the retired
    /// level's literal world coordinates.
    let createDefault() : LevelData =
      let cs = 2.0f
      let half = int(Constants.FloorSize / cs / 2.0f)
      let w = half * 2 + 1
      let d = half * 2 + 1
      let mid = half

      let floorCol = { Kind = Cell.Floor; Height = 1 }
      let wallCol = { Kind = Cell.Wall; Height = 2 }

      let crateCol height = { Kind = Cell.Crate; Height = height }

      let doc =
        Flow.overlay [
          // Ground everywhere, wall columns along the rim, crates as
          // single footprint cells (a double stack plus singles).
          Flow.canvas [
            Flow.fill floorCol
            Flow.border wallCol
            Flow.cell { X = mid - 6; Y = mid + 3 } (crateCol 2)
            Flow.cell { X = mid + 5; Y = mid - 4 } (crateCol 1)
            Flow.cell { X = mid - 9; Y = mid - 6 } (crateCol 1)
            Flow.cell { X = mid + 8; Y = mid + 6 } (crateCol 1)
          ]

          // Central pillar: a tagged 3x3 box offset into place.
          Stamp.tagged [ "pillar" ] (Stamp.box 3 3 [ Flow.fill wallCol ])
          |> Stamp.offset (mid - 1) (mid - 1)

          // The ramp footprint, 2 wide and 4 deep. Height 1 across —
          // byte-identical to the retired voxel loop, whose integer
          // division painted every step at level 0.
          Stamp.tagged
            [ "ramp" ]
            (Stamp.box 2 4 [ Flow.fill { Kind = Cell.Wall; Height = 1 } ])
          |> Stamp.offset (mid + 5) (mid + 5)
        ]

      let grid =
        CellGrid2D.create
          w
          d
          (Vector2(cs, cs))
          (Vector2(-float32 half * cs, -float32 half * cs))

      let struct (grid, _) = grid |> Flow.run doc

      // Spawn points keep the retired level's exact world coordinates —
      // placement is not part of the storage migration.
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
        BaseY = -cs * 0.5f
        PlayerSpawn = Vector3(-6.0f, Constants.PlayerEyeHeight, -6.0f)
        EnemySpawns = enemySpawns
        PickupSpawns = pickupSpawns
      }
