/// Block metadata registry — resolves model name, extents, vertical offset,
/// rotation, and category per BlockType. Mirrors the 2D TileData.fs pattern: a
/// single source of truth computed on demand (never stored per-cell).
///
/// Extents are raw mesh-local model units (BoneProbe reads vertices with the
/// same Assimp flag set as Mibo.MonoGame/Assets.fs — no PreTransformVertices —
/// so they equal the model's size in model units). They are fractional, not
/// rounded to cells, so the future WorldGen can compute exact spacing/overlap.
/// Grass/snow share identical extents per shape; only the model differs.
module Platformer3D.BlockData

open System
open Platformer3D.Constants
open Platformer3D.Types

/// Semantic category for a block — what the asset IS. NOTE: this describes the
/// asset's role, not necessarily how it collides today; see isSolid/isCollectible
/// for the current physics behavior (preserved from the pre-consolidation code).
[<Struct>]
type BlockCategory =
  | Empty
  | Solid
  | Hazard
  | Collectible
  | Decoration

/// Resolved per-block data, computed on demand via `lookup`.
[<Struct>]
type BlockInfo = {
  /// Bare logical model name (backend composes basePath + extension).
  ModelName: string
  /// Mesh extent on X (model units).
  ExtentW: float32
  /// Mesh extent on Y (model units).
  ExtentH: float32
  /// Mesh extent on Z (model units).
  ExtentD: float32
  /// Vertical placement offset (model units).
  VerticalOffset: float32
  /// Y rotation in degrees.
  RotationY: float32
  Category: BlockCategory
  /// Horizontal centering offset on X (world units). Kenney block meshes are
  /// centered on their origin (symmetric ±extent/2 — verified via BoneProbe),
  /// but blocks are placed at the cell corner. A 1-cell mesh at a corner lands
  /// centered on its cell by uniformity; a 2-cell-wide mesh does not — it must
  /// be translated +0.5 along each multi-cell axis to center on its footprint.
  /// 0.0 for 1-cell-wide-in-X blocks; 0.5 (cellSize/2) for 2-cell-wide-in-X.
  CenterOffsetX: float32
  /// Horizontal centering offset on Z (world units). See CenterOffsetX.
  CenterOffsetZ: float32
}

// -------------------------------------------------------------
// Biome model-name resolvers — grass/snow share footprints, differ by model.
// Each returns an existing KenneyModels constant (alloc-free interned string).
// -------------------------------------------------------------

let private blockModel =
  function
  | Grass -> KenneyModels.blockGrass
  | Snow -> KenneyModels.blockSnow

let private largeModel =
  function
  | Grass -> KenneyModels.blockGrassLarge
  | Snow -> KenneyModels.blockSnowLarge

let private tallModel =
  function
  | Grass -> KenneyModels.blockGrassTall
  | Snow -> KenneyModels.blockSnowTall

let private longModel =
  function
  | Grass -> KenneyModels.blockGrassLong
  | Snow -> KenneyModels.blockSnowLong

let private lowModel =
  function
  | Grass -> KenneyModels.blockGrassLow
  | Snow -> KenneyModels.blockSnowLow

let private narrowModel =
  function
  | Grass -> KenneyModels.blockGrassNarrow
  | Snow -> KenneyModels.blockSnowNarrow

let private slopeModel =
  function
  | Grass -> KenneyModels.blockGrassSlope
  | Snow -> KenneyModels.blockSnowSlope

/// Y rotation (degrees) for each slope direction. Matches the pre-consolidation
/// GroundSlope*/SnowSlope* rotations exactly.
let slopeRotationY =
  function
  | XPos -> 0.0f
  | XNeg -> 180.0f
  | ZPos -> 90.0f
  | ZNeg -> -90.0f

let private solidTerrain
  (name: string)
  (w: float32)
  (h: float32)
  (d: float32)
  (cx: float32)
  (cz: float32)
  : BlockInfo =
  {
    ModelName = name
    ExtentW = w
    ExtentH = h
    ExtentD = d
    VerticalOffset = 0.0f
    RotationY = 0.0f
    Category = Solid
    CenterOffsetX = cx
    CenterOffsetZ = cz
  }

let private decoration
  (name: string)
  (w: float32)
  (h: float32)
  (d: float32)
  (offset: float32)
  : BlockInfo =
  {
    ModelName = name
    ExtentW = w
    ExtentH = h
    ExtentD = d
    VerticalOffset = offset
    RotationY = 0.0f
    Category = Decoration
    CenterOffsetX = 0.0f
    CenterOffsetZ = 0.0f
  }

let private collectible
  (name: string)
  (w: float32)
  (h: float32)
  (d: float32)
  : BlockInfo =
  {
    ModelName = name
    ExtentW = w
    ExtentH = h
    ExtentD = d
    VerticalOffset = cellSize * 0.5f
    RotationY = 0.0f
    Category = Collectible
    CenterOffsetX = 0.0f
    CenterOffsetZ = 0.0f
  }

let private emptyInfo: BlockInfo = {
  ModelName = ""
  ExtentW = 0.0f
  ExtentH = 0.0f
  ExtentD = 0.0f
  VerticalOffset = 0.0f
  RotationY = 0.0f
  Category = Empty
  CenterOffsetX = 0.0f
  CenterOffsetZ = 0.0f
}

/// Half a cell — the centering translation for a 2-cell-wide mesh to sit on its
/// footprint (meshes are centered on origin; blocks are placed at the cell corner).
let private halfCell = cellSize * 0.5f

/// Lookup resolved block data for a BlockType.
/// Terrain extents are from the BoneProbe dimensions report (grass/snow
/// identical per shape). Non-terrain values are data-fied from the same report;
/// these will move to a separate decoration/collectible layer in a future step.
let lookup(bt: BlockType) : BlockInfo =
  match bt with
  | BlockType.Empty -> emptyInfo
  // Terrain (biome-as-field) — solid collision.
  // Center offsets: 1-cell blocks need none (centered mesh at a corner lands on
  // its cell by uniformity); 2-cell-wide blocks need +0.5 along each multi-cell
  // axis to center on their footprint (meshes are centered on origin).
  | Block biome ->
    solidTerrain (blockModel biome) 1.082f 1.000f 1.082f 0.0f 0.0f
  | LargeBlock biome ->
    solidTerrain (largeModel biome) 2.082f 1.000f 2.082f halfCell halfCell
  | TallBlock biome ->
    solidTerrain (tallModel biome) 2.082f 2.000f 2.082f halfCell halfCell
  | LongBlock biome ->
    solidTerrain (longModel biome) 2.082f 1.000f 1.082f halfCell 0.0f
  | LowBlock biome ->
    solidTerrain (lowModel biome) 1.082f 0.500f 1.082f 0.0f 0.0f
  | NarrowBlock biome ->
    solidTerrain (narrowModel biome) 0.782f 1.000f 0.782f 0.0f 0.0f
  | Slope(biome, dir) -> {
      ModelName = slopeModel biome
      ExtentW = 2.082f
      ExtentH = 0.759f
      ExtentD = 2.011f
      VerticalOffset = 0.0f
      RotationY = slopeRotationY dir
      Category = Solid
      CenterOffsetX = halfCell
      CenterOffsetZ = halfCell
    }
  // Non-terrain — platforms (solid-colliding thin slabs, current behavior)
  | Platform -> {
      ModelName = KenneyModels.platform
      ExtentW = 1.000f
      ExtentH = 0.195f
      ExtentD = 1.000f
      VerticalOffset = cellSize * 0.5f
      RotationY = 0.0f
      Category = Solid
      CenterOffsetX = 0.0f
      CenterOffsetZ = 0.0f
    }
  | PlatformRamp -> {
      ModelName = KenneyModels.platformRamp
      ExtentW = 1.000f
      ExtentH = 0.570f
      ExtentD = 1.027f
      VerticalOffset = cellSize * 0.5f
      RotationY = 0.0f
      Category = Solid
      CenterOffsetX = 0.0f
      CenterOffsetZ = 0.0f
    }
  // Hazard
  | Spikes -> {
      ModelName = KenneyModels.spikeBlock
      ExtentW = 0.900f
      ExtentH = 0.900f
      ExtentD = 0.900f
      VerticalOffset = 0.0f
      RotationY = 0.0f
      Category = Hazard
      CenterOffsetX = 0.0f
      CenterOffsetZ = 0.0f
    }
  // Decorations
  | TreePine -> decoration KenneyModels.treePine 0.948f 1.997f 0.948f 0.0f
  | TreeSnow -> decoration KenneyModels.treeSnow 1.089f 1.931f 1.109f 0.0f
  | Rock -> decoration KenneyModels.rocks 0.653f 0.400f 0.662f 0.0f
  | GrassTuft -> decoration KenneyModels.grass 0.519f 0.314f 0.544f 0.0f
  | Mushrooms -> decoration KenneyModels.mushrooms 0.522f 0.289f 0.512f 0.0f
  | MushroomLight -> decoration KenneyModels.mushrooms 0.522f 0.289f 0.512f 0.0f
  | Crate -> decoration KenneyModels.crate 0.500f 0.500f 0.500f 0.0f
  | Barrel -> decoration KenneyModels.barrel 0.518f 0.476f 0.518f 0.0f
  | Flag -> decoration KenneyModels.flag 0.423f 0.900f 0.112f (cellSize * 0.5f)
  // Collectibles
  | Coin -> collectible KenneyModels.coinGold 0.400f 0.400f 0.175f
  | Jewel -> collectible KenneyModels.jewel 0.333f 0.370f 0.288f
  | Heart -> collectible KenneyModels.heart 0.412f 0.384f 0.119f
  | Star -> collectible KenneyModels.star 0.365f 0.363f 0.239f

// -------------------------------------------------------------
// Accessors / predicates
// -------------------------------------------------------------

/// Bare logical model name (backend composes basePath + extension).
let modelName(bt: BlockType) : string = (lookup bt).ModelName

/// Vertical placement offset (model units).
let modelVerticalOffset(bt: BlockType) : float32 = (lookup bt).VerticalOffset

/// Y rotation in degrees.
let modelRotation(bt: BlockType) : float32 = (lookup bt).RotationY

/// Horizontal centering offset on X (world units). 0.0 for 1-cell-wide-in-X
/// blocks; cellSize/2 for 2-cell-wide-in-X. Consumed by the render transform
/// and the physics collider so both move together (see colliderCenterOffset).
let centerOffsetX(bt: BlockType) : float32 = (lookup bt).CenterOffsetX

/// Horizontal centering offset on Z (world units). See centerOffsetX.
let centerOffsetZ(bt: BlockType) : float32 = (lookup bt).CenterOffsetZ

/// The (X, Z) centering offset a physics collider must add to its corner origin
/// so its AABB tracks the rendered mesh. One source of truth shared with the
/// render transform — keeping them in lockstep is what lets the player stand
/// correctly on a re-centered multi-cell block.
let colliderCenterOffset(bt: BlockType) : struct (float32 * float32) =
  let info = lookup bt
  struct (info.CenterOffsetX, info.CenterOffsetZ)

/// True when the block collides as a solid. Preserves the pre-consolidation
/// behavior exactly (terrain + platforms + spikes + trees/rocks/crates/barrels
/// are solid; empty, collectibles, and low decorations do not collide).
let isSolid(bt: BlockType) : bool =
  match bt with
  | BlockType.Empty
  | Coin
  | Jewel
  | Heart
  | Star
  | GrassTuft
  | Mushrooms
  | MushroomLight
  | Flag -> false
  | _ -> true

/// True for collectibles picked up on overlap.
let isCollectible(bt: BlockType) : bool =
  match bt with
  | Coin
  | Jewel
  | Heart
  | Star -> true
  | _ -> false

/// Collider AABB dimensions (width, height, depth) in world units for a block at
/// a single grid cell. Multi-cell blocks extend beyond their anchor cell; Physics
/// must scan a wider neighborhood (±2 in XZ) to catch them. Slopes use full
/// cellSize height — no ramp physics this step. Blocks whose extents are within
/// [0.9, 1.2] × cellSize snap to exactly cellSize to avoid fractional overlap
/// jitter on adjacent terrain cells. Sub-cell blocks keep their real extent.
///
/// Fast-path: the most common terrain blocks (`Block _`, `Slope _`) return
/// cellSize constants directly — no `lookup` call, no match overhead in the
/// physics hot path.
let colliderExtents(bt: BlockType) : struct (float32 * float32 * float32) =
  match bt with
  | Block _
  | Slope _ -> struct (cellSize, cellSize, cellSize)
  | _ ->
    let info = lookup bt

    let snapIfNearCell(v: float32) =
      if v >= 0.9f * cellSize && v <= 1.2f * cellSize then
        cellSize
      else
        v

    struct (snapIfNearCell info.ExtentW,
            snapIfNearCell info.ExtentH,
            snapIfNearCell info.ExtentD)

/// Analytical surface height for a slope block at the given player XZ position.
/// Returns ValueNone if the position is outside the slope's footprint or the
/// block is not a slope.
///
/// The slope model rises ExtentH (0.759) over its run length (ExtentW ≈2.082).
/// For XPos/XNeg the run is along world X; for ZPos/ZNeg along world Z.
/// The perpendicular span uses ExtentD (≈2.011). The surface height varies
/// linearly from worldY at the low end to worldY + ExtentH at the high end.
let slopeSurfaceY
  (bt: BlockType)
  (cellWorldX: float32)
  (cellWorldY: float32)
  (cellWorldZ: float32)
  (px: float32)
  (pz: float32)
  : float32 voption =
  match bt with
  | Slope(_, dir) ->
    let info = lookup bt
    let run = info.ExtentW
    let rise = info.ExtentH
    let width = info.ExtentD

    // The rendered slope mesh is centered on its footprint via the center
    // offset (see lookup), so the analytical surface must use the same shifted
    // origin — otherwise the ramp lifts the player from the wrong spot.
    let originX = cellWorldX + info.CenterOffsetX
    let originZ = cellWorldZ + info.CenterOffsetZ

    // Footprint bounds and parametric t along the run axis.
    let xMin, xMax, zMin, zMax, t =
      match dir with
      | XPos ->
        originX, originX + run, originZ, originZ + width, (px - originX) / run
      | XNeg ->
        originX,
        originX + run,
        originZ,
        originZ + width,
        (originX + run - px) / run
      | ZPos ->
        originX, originX + width, originZ, originZ + run, (pz - originZ) / run
      | ZNeg ->
        originX,
        originX + width,
        originZ,
        originZ + run,
        (originZ + run - pz) / run

    if px >= xMin && px <= xMax && pz >= zMin && pz <= zMax then
      ValueSome(cellWorldY + rise * Math.Clamp(t, 0.0f, 1.0f))
    else
      ValueNone
  | _ -> ValueNone
