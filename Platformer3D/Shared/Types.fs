module Platformer3D.Types

open System.Numerics
open Mibo.Layout
open Mibo.Layout3D

/// The game's heightmap model, per Mibo's "3D as 2D plus column height":
/// the grid stores each column's footprint and the vertical extent rides in
/// the tile. No voxel axis exists anywhere in the sample.
///
///   Terrain:  one `TerrainColumn` per footprint cell — the solid ground
///             (`Height` mass cells) plus an optional surface `Cap`.
///   Props:    one `PropTile` per sparse cell — platforms, hazards,
///             decorations, with their world height `Y` in the tile.
///   Pickups:  the collectible slice of props, on its own grid so physics
///             can clear cells on pickup.
///
/// Render layers (`CapTile`, `MassTile`) are derived once per chunk at
/// generation time; simulation code reads only `Terrain`, `Props`, and
/// `Pickups`.
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

/// Terrain biome — grass/snow share identical block shapes, differing only
/// by model (confirmed via BoneProbe dimensions: footprints match per shape).
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

/// The surface shape capping a column. Single-cell shapes replace the
/// column's own cap; multi-cell shapes (`Large`, `Tall`, `Long`, `Slope`,
/// `Overhang`) also cover neighbor columns, whose caps are cleared.
[<Struct>]
type CapShape =
  | Block
  | Low
  | Narrow
  | Hexagon
  | Edge of dir: SlopeDir
  | Corner of dir: SlopeDir
  | Large
  | Tall
  | Long
  | Slope of dir: SlopeDir
  | Overhang

/// One column of terrain: `Height` mass cells of solid ground (cell y
/// `0 .. Height-1`) with an optional cap sitting at cell y `Height`.
/// A pit (carved chasm) is `{ Height = 0; Cap = None }` — no ground, no
/// collider, nothing rendered.
type TerrainColumn = {
  Material: Biome3D
  Height: int
  Cap: CapShape voption
}

module TerrainColumn =

  /// Ground under a plain Block cap.
  let ground (material: Biome3D) (height: int) : TerrainColumn = {
    Material = material
    Height = height
    Cap = ValueSome Block
  }

  /// Ground with no cap — bare soil, or a cell covered by a neighbor's
  /// multi-cell cap.
  let bare (material: Biome3D) (height: int) : TerrainColumn = {
    Material = material
    Height = height
    Cap = ValueNone
  }

  /// A carved chasm column: no ground at all.
  let pit: TerrainColumn = {
    Material = Biome3D.Grass
    Height = 0
    Cap = ValueNone
  }

  let inline isPit(col: TerrainColumn) : bool =
    col.Cap.IsNone && col.Height <= 0

  /// World cells from the chunk base to the walkable surface (ground plus
  /// the cap's cell, when one is present).
  let inline surfaceCells(col: TerrainColumn) : int =
    col.Height
    + match col.Cap with
      | ValueSome _ -> 1
      | ValueNone -> 0

[<Struct>]
type HazardKind =
  | Spikes
  | SpikesWide

[<Struct>]
type PickupKind =
  | Gold
  | Silver
  | Bronze
  | Jewel
  | Heart
  | Star
  | Key

module PickupKind =

  /// Score awarded on pickup.
  let inline score(kind: PickupKind) : int =
    match kind with
    | Bronze -> 1
    | Silver -> 2
    | Gold -> 5
    | Heart -> 5
    | Jewel -> 10
    | Star -> 15
    | Key -> 20

[<Struct>]
type DecorationKind =
  | TreePine
  | TreeSnow
  | Rock
  | Stones
  | GrassTuft
  | Flowers
  | FlowersTall
  | Mushrooms
  | GlowMushroom
  | Crate
  | Barrel
  | Flag

/// A placed prop. `Y` is the prop's world cell height above the chunk base —
/// the vertical axis as tile data, exactly the heightmap contract.
/// A `Platform` is ONE platform object `length` cells long (a single scaled
/// instance of the platform asset) — never a run of tiled blocks.
/// (Struct-DU field names are distinct per case: same-named fields must
/// share a type in a struct union.)
[<Struct>]
type Prop =
  | Platform of length: int
  | Hazard of hazard: HazardKind
  | Pickup of pickup: PickupKind
  | Decoration of decoration: DecorationKind

type PropTile = { Prop: Prop; Y: int }

/// One platform slab for the render pass: a SINGLE unit block scaled to a
/// W×H×D cell rectangle (H includes the surface crust cell), anchored at
/// its min-corner cell. Greedy rectangles merge each maximal same-height,
/// same-material region of a runway, so one platform = one model scaled to
/// the platform's size — no tiled blocks, no stacked walls.
type SlabTile = {
  Material: Biome3D
  W: int
  H: int
  D: int
}

/// A generated chunk: the simulation grids plus the derived render grids
/// and the Flow landmarks (the "spawn" zone rect and feature tags).
type Chunk = {
  /// Simulation truth — physics, minimap, and prop placement read this.
  Terrain: CellGrid2D<TerrainColumn>
  /// Derived: one scaled block instance per platform region.
  Slabs: CellGrid2D<SlabTile>
  /// Static props (platforms, hazards, decorations), sparse.
  Props: CellGrid2D<PropTile>
  /// Collectibles, sparse — physics clears cells on pickup.
  Pickups: CellGrid2D<PropTile>
  /// Landmarks reported by the terrain document (`Flow.tryPosition "spawn"`).
  Marks: Landmarks
  Bounds: BoundingBox
  OriginX: int
  OriginZ: int
}
