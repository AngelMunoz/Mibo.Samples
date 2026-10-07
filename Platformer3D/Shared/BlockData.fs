/// Block metadata registry — resolves model name, extents, vertical offset,
/// rotation, and category per cap shape and prop. Mirrors the 2D TileData.fs
/// pattern: a single source of truth computed on demand (never stored per
/// cell).
///
/// Extents are raw mesh-local model units (BoneProbe reads vertices with the
/// same Assimp flag set as Mibo.MonoGame/Assets.fs — no PreTRANSFORMVertices —
/// so they equal the model's size in model units). New shapes (hexagon,
/// edge, corner, overhang, wide spikes, coins, key, flowers, stones) were
/// measured with BoneProbe for this table. Kenney block meshes are
/// bottom-anchored (min Y = 0) and centered on their origin in XZ.
module Platformer3D.BlockData

open System
open Platformer3D.Constants
open Platformer3D.Types

/// Semantic category — what the asset IS in the level.
[<Struct>]
type BlockCategory =
  | Empty
  | Solid
  | Hazard
  | Collectible
  | Decoration

/// Resolved per-block data, computed on demand via `capInfo` / `propInfo`.
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
  /// Horizontal centering offset on X (world units). Kenney block meshes
  /// are centered on their origin, but blocks are placed at the cell
  /// corner. A 2-cell-wide mesh must be translated +0.5 along each
  /// multi-cell axis to center on its footprint; 1-cell meshes need none.
  CenterOffsetX: float32
  /// Horizontal centering offset on Z (world units). See CenterOffsetX.
  CenterOffsetZ: float32
}

// -------------------------------------------------------------
// Biome model-name resolvers — grass/snow share footprints per shape,
// differing only by model. Each returns an interned KenneyModels constant.
// -------------------------------------------------------------

let capModelOf =
  function
  | Grass ->
    function
    | Block -> KenneyModels.blockGrass
    | Low -> KenneyModels.blockGrassLow
    | Narrow -> KenneyModels.blockGrassNarrow
    | Hexagon -> KenneyModels.blockGrassHexagon
    | Edge _ -> KenneyModels.blockGrassEdge
    | Corner _ -> KenneyModels.blockGrassCorner
    | Large -> KenneyModels.blockGrassLarge
    | Tall -> KenneyModels.blockGrassTall
    | Long -> KenneyModels.blockGrassLong
    | Slope _ -> KenneyModels.blockGrassSlope
    | Overhang -> KenneyModels.blockGrassOverhang
  | Snow ->
    function
    | Block -> KenneyModels.blockSnow
    | Low -> KenneyModels.blockSnowLow
    | Narrow -> KenneyModels.blockSnowNarrow
    | Hexagon -> KenneyModels.blockSnowHexagon
    | Edge _ -> KenneyModels.blockSnowEdge
    | Corner _ -> KenneyModels.blockSnowCorner
    | Large -> KenneyModels.blockSnowLarge
    | Tall -> KenneyModels.blockSnowTall
    | Long -> KenneyModels.blockSnowLong
    | Slope _ -> KenneyModels.blockSnowSlope
    | Overhang -> KenneyModels.blockSnowOverhang

/// The unit-block model a platform slab renders with (crust top + dirt
/// body — scaled, it reads as one clean island).
let inline massModel(material: Biome3D) : string =
  match material with
  | Grass -> KenneyModels.blockGrass
  | Snow -> KenneyModels.blockSnow

/// The unit block's native XZ footprint in cells (BoneProbe: the mesh spans
/// ±0.541, i.e. 1.082 cells wide). Platform slabs divide by this so a
/// scaled instance lands exactly on its W×D cell rectangle.
let blockFootprint = 1.082f

/// Y rotation (degrees) per slope direction; edge and corner caps share the
/// same convention (the shape's raised lip faces `dir`).
let slopeRotationY =
  function
  | XPos -> 0.0f
  | XNeg -> 180.0f
  | ZPos -> 90.0f
  | ZNeg -> -90.0f

let inline solid
  (name: string)
  (w: float32)
  (h: float32)
  (d: float32)
  (cx: float32)
  (cz: float32)
  (rotation: float32)
  : BlockInfo =
  {
    ModelName = name
    ExtentW = w
    ExtentH = h
    ExtentD = d
    VerticalOffset = 0.0f
    RotationY = rotation
    Category = Solid
    CenterOffsetX = cx
    CenterOffsetZ = cz
  }

let inline decoration
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
    CenterOffsetX = cellSize * 0.5f
    CenterOffsetZ = cellSize * 0.5f
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
    CenterOffsetX = cellSize * 0.5f
    CenterOffsetZ = cellSize * 0.5f
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

// Centering: every Kenney mesh in this kit is CENTERED on its local XZ
// origin and bottom-anchored at local Y = 0 (BoneProbe-verified: e.g.
// block-grass spans -0.541..+0.541, block-grass-large ±1.041, platform
// ±0.500). The grid places things at cell CORNERS, so `CenterOffset`
// translates the mesh's footprint CENTER over the anchor:
//   1-cell footprint  -> cellSize / 2 (the cell's center)
//   2-cell footprint  -> cellSize     (the 2x1 / 2x2 footprint's center)
// Colliders must then be center-based: min = corner + offset - extent/2.
let private halfCell = cellSize * 0.5f

/// Lookup the render/collision data for a terrain cap. Extents are from the
/// BoneProbe dimensions report (grass/snow identical per shape; the two
/// biomes were both measured for hexagon and the rim shapes).
let capInfo (material: Biome3D) (shape: CapShape) : BlockInfo =
  match shape with
  | Block ->
    solid
      (capModelOf material Block)
      1.082f
      1.000f
      1.082f
      halfCell
      halfCell
      0.0f
  | Low ->
    solid (capModelOf material Low) 1.082f 0.500f 1.082f halfCell halfCell 0.0f
  | Narrow ->
    solid
      (capModelOf material Narrow)
      0.782f
      1.000f
      0.782f
      halfCell
      halfCell
      0.0f
  | Hexagon ->
    solid
      (capModelOf material Hexagon)
      1.100f
      1.000f
      1.270f
      halfCell
      halfCell
      0.0f
  | Edge dir ->
    solid
      (capModelOf material shape)
      1.082f
      1.000f
      1.082f
      halfCell
      halfCell
      (slopeRotationY dir)
  | Corner dir ->
    solid
      (capModelOf material shape)
      1.054f
      1.000f
      1.054f
      halfCell
      halfCell
      (slopeRotationY dir)
  | Large ->
    solid
      (capModelOf material Large)
      2.082f
      1.000f
      2.082f
      cellSize
      cellSize
      0.0f
  | Tall ->
    solid (capModelOf material Tall) 2.082f 2.000f 2.082f cellSize cellSize 0.0f
  | Long ->
    solid (capModelOf material Long) 2.082f 1.000f 1.082f cellSize halfCell 0.0f
  | Slope dir ->
    solid
      (capModelOf material shape)
      2.082f
      0.759f
      2.011f
      cellSize
      cellSize
      (slopeRotationY dir)
  | Overhang ->
    solid
      (capModelOf material Overhang)
      2.082f
      1.000f
      2.082f
      cellSize
      cellSize
      0.0f

/// Lookup the render/collision data for a prop at its tile's cell.
/// (Patterns qualify `Prop.*` — `Hazard`/`Decoration` also name
/// BlockCategory cases, and the shorter name would resolve wrong.)
let propInfo(prop: Prop) : BlockInfo =
  match prop with
  | Prop.Platform length ->
      // One platform OBJECT, `length` cells long: the platform mesh is a
      // 1x1-footprint slab (BoneProbe: 1.0 x 0.195 x 1.0, centered, bottom-
      // anchored), so the single instance scales `length`-wide and centers
      // over anchor + length/2. One tile, one instance, no tiling.
      {
        ModelName = KenneyModels.platform
        ExtentW = float32 length
        ExtentH = 0.195f
        ExtentD = 1.000f
        VerticalOffset = cellSize * 0.5f
        RotationY = 0.0f
        Category = Solid
        CenterOffsetX = float32 length * 0.5f
        CenterOffsetZ = halfCell
      }
  | Prop.Hazard kind ->
    let info =
      match kind with
      | Spikes ->
        solid
          KenneyModels.spikeBlock
          0.900f
          0.900f
          0.900f
          halfCell
          halfCell
          0.0f
      | SpikesWide ->
        solid
          KenneyModels.spikeBlockWide
          2.400f
          0.900f
          0.900f
          cellSize
          halfCell
          0.0f

    { info with Category = Hazard }
  | Prop.Pickup kind ->
    let name =
      match kind with
      | Gold -> KenneyModels.coinGold
      | Silver -> KenneyModels.coinSilver
      | Bronze -> KenneyModels.coinBronze
      | Jewel -> KenneyModels.jewel
      | Heart -> KenneyModels.heart
      | Star -> KenneyModels.star
      | Key -> KenneyModels.key

    let struct (w, h, d) =
      match kind with
      | Key -> struct (0.382f, 0.217f, 0.068f)
      | _ -> struct (0.400f, 0.400f, 0.175f)

    collectible name w h d
  | Prop.Decoration kind ->
    match kind with
    | TreePine -> decoration KenneyModels.treePine 0.948f 1.997f 0.948f 0.0f
    | TreeSnow -> decoration KenneyModels.treeSnow 1.089f 1.931f 1.109f 0.0f
    | Rock -> decoration KenneyModels.rocks 0.653f 0.400f 0.662f 0.0f
    | Stones -> decoration KenneyModels.stones 0.710f 0.050f 0.790f 0.0f
    | GrassTuft -> decoration KenneyModels.grass 0.519f 0.314f 0.544f 0.0f
    | Flowers -> decoration KenneyModels.flowers 0.777f 0.137f 0.786f 0.0f
    | FlowersTall ->
      decoration KenneyModels.flowersTall 0.546f 0.462f 0.618f 0.0f
    | Mushrooms -> decoration KenneyModels.mushrooms 0.522f 0.289f 0.512f 0.0f
    | GlowMushroom ->
      decoration KenneyModels.mushrooms 0.522f 0.289f 0.512f 0.0f
    | Crate -> decoration KenneyModels.crate 0.500f 0.500f 0.500f 0.0f
    | Barrel -> decoration KenneyModels.barrel 0.518f 0.476f 0.518f 0.0f
    | Flag ->
      decoration KenneyModels.flag 0.423f 0.900f 0.112f (cellSize * 0.5f)

/// Bare logical model name for a cap (backend composes basePath + extension).
let inline capModel (material: Biome3D) (shape: CapShape) : string =
  (capInfo material shape).ModelName

/// True when a prop collides as a solid: platforms and hazards always;
/// decorations only the chunky ones (trees, rocks, crates, barrels).
let inline isSolidProp(prop: Prop) : bool =
  match prop with
  | Prop.Platform _ -> true
  | Prop.Hazard _ -> true
  | Prop.Pickup _ -> false
  | Prop.Decoration kind ->
    match kind with
    | TreePine
    | TreeSnow
    | Rock
    | Crate
    | Barrel -> true
    | _ -> false

/// True for props collected on overlap.
let inline isPickupProp(prop: Prop) : bool =
  match prop with
  | Prop.Pickup _ -> true
  | _ -> false

// -------------------------------------------------------------
// Collider extents (world units)
// -------------------------------------------------------------

let inline snapIfNearCell(v: float32) =
  if v >= 0.9f * cellSize && v <= 1.2f * cellSize then
    cellSize
  else
    v

/// Collider AABB dimensions (width, height, depth) for a cap at a single
/// grid cell. Multi-cell caps extend beyond their anchor cell; Physics scans
/// the neighborhood to catch them. Slopes use full cellSize height for body
/// collision — the walkable surface is analytical (`slopeSurfaceY`).
/// Fast-path: the common terrain caps return cellSize constants directly.
let capExtents(shape: CapShape) : struct (float32 * float32 * float32) =
  match shape with
  | Block
  | Edge _
  | Corner _
  | Slope _ -> cellSize, cellSize, cellSize
  | _ ->
    let info = capInfo Grass shape

    snapIfNearCell info.ExtentW,
    snapIfNearCell info.ExtentH,
    snapIfNearCell info.ExtentD

/// Collider AABB dimensions for a prop.
let inline propExtents(prop: Prop) : struct (float32 * float32 * float32) =
  let info = propInfo prop

  snapIfNearCell info.ExtentW,
  snapIfNearCell info.ExtentH,
  snapIfNearCell info.ExtentD

/// The (X, Z) centering offset a physics collider adds to its corner origin
/// so its AABB tracks the rendered mesh. One source of truth with the render
/// transform — keeping them in lockstep is what lets the player stand
/// correctly on a re-centered multi-cell block.
let inline capCenterOffset
  (material: Biome3D)
  (shape: CapShape)
  : struct (float32 * float32) =
  let info = capInfo material shape
  info.CenterOffsetX, info.CenterOffsetZ

let inline propCenterOffset(prop: Prop) : struct (float32 * float32) =
  let info = propInfo prop
  info.CenterOffsetX, info.CenterOffsetZ

/// Analytical surface height for a slope cap at the given player XZ
/// position. Returns ValueNone if the position is outside the slope's
/// footprint or the shape is not a slope.
///
/// The slope model rises ExtentH (0.759) over its run length (ExtentW
/// ≈2.082). For XPos/XNeg the run is along world X; for ZPos/ZNeg along
/// world Z. `cellWorldY` is the cap's base height (column.Height * cellSize).
let slopeSurfaceY
  (shape: CapShape)
  (cellWorldX: float32)
  (cellWorldY: float32)
  (cellWorldZ: float32)
  (px: float32)
  (pz: float32)
  : float32 voption =
  match shape with
  | Slope dir ->
    let info = capInfo Grass shape
    let run = info.ExtentW
    let rise = info.ExtentH
    let width = info.ExtentD

    // The rendered slope mesh is CENTERED on its footprint (BoneProbe:
    // spans ±run/2 and ±width/2 around the footprint center), so the
    // analytical surface starts half a run/width back from the center
    // offset — otherwise the ramp lifts the player from the wrong spot.
    let originX = cellWorldX + info.CenterOffsetX - run * 0.5f
    let originZ = cellWorldZ + info.CenterOffsetZ - width * 0.5f

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
