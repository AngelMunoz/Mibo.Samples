module Platformer.WorldGen

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Mibo.Layout
open Platformer.Constants
open Platformer.Types
open Platformer.TileData
open Platformer.Stamps
open Mibo.Elmish

// ==============================================================
// Config
// ==============================================================

/// Player jump reachability budget in tile units.
/// Derived from physics constants:
///   vertical = jumpSpeed² / (2*gravity) ≈ 202px ≈ 3 tiles
///   horizontal = moveSpeed * airtime ≈ 315px ≈ 5 tiles
[<Struct>]
type JumpBudget = {
  MaxVerticalTiles: int
  MaxHorizontalTiles: int
}

[<Struct>]
type GroundConfig = {
  MinSlabs: int
  MaxSlabs: int
  MinWidth: int
  MaxWidth: int
  MinHeight: int
  MaxHeight: int
  MinGap: int
  MaxGap: int
}

[<Struct>]
type PlatformConfig = {
  MinCount: int
  MaxCount: int
  MinWidth: int
  MaxWidth: int
  MinClearance: int
  MaxClearance: int
  MinVerticalGap: int
  MaxVerticalGap: int
}

[<Struct>]
type GenConfig = {
  JumpBudget: JumpBudget
  Ground: GroundConfig
  Platform: PlatformConfig
  /// Biome noise scale sampled at world-tile-X granularity. Smaller = wider
  /// biome regions; ~0.03 yields roughly one biome per chunk with smooth
  /// cross-seam transitions.
  BiomeColumnScale: float32
  /// Elevation noise scale. Smaller = gentler, wider hills.
  ElevationScale: float32
  /// Maximum elevation offset in tiles (±amplitude from groundY).
  /// 0 = flat terrain (degenerate case). The planner clamps any rise
  /// that exceeds the jump arc, so this is aesthetic, not a safety limit.
  ElevationAmplitude: int
}

let defaultConfig = {
  JumpBudget = {
    MaxVerticalTiles = 3
    MaxHorizontalTiles = 4
  }
  Ground = {
    MinSlabs = 1
    MaxSlabs = 5
    MinWidth = 6
    MaxWidth = 14
    MinHeight = 2
    MaxHeight = 4
    MinGap = 2
    MaxGap = 4
  }
  Platform = {
    MinCount = 2
    MaxCount = 5
    MinWidth = 2
    MaxWidth = 7
    MinClearance = 3
    MaxClearance = 4
    MinVerticalGap = 3
    MaxVerticalGap = 4
  }
  BiomeColumnScale = 0.03f
  ElevationScale = 0.04f
  ElevationAmplitude = 2
}

// ==============================================================
// Reachability — physics-derived jump predicate
//
// Terrain generation must guarantee the player can always reach every
// surface by walking or jumping. That guarantee is only sound if it is
// derived from the actual jump arc rather than from independent
// "max gap" / "max height" caps — those are an over-approximation that
// broke the moment height varied (a 4-tile gap you clear flat becomes
// unreachable if the far slab is also too high, because rise and gap
// share the same jump budget).
//
// The player launches upward at `jumpSpeed` and drifts horizontally at
// `moveSpeed` (no acceleration ramp — full speed is instant in Physics).
// This models a RUNNING jump (constant horizontal drift), which is the
// trajectory used to clear gaps between surfaces. Gravity decelerates
// the ascent. A fully-held jump (no jump cut) is the player's MAXIMUM
// reach; jump cut only lowers height, so the guarantee uses the
// best-case arc.
//
//   t(d) = d / moveSpeed                  (time to cross distance d)
//   h(d) = |jumpSpeed|·t(d) − ½·gravity·t(d)²   (height above launch)
//
// h(d) peaks at d* = moveSpeed·|jumpSpeed|/gravity ≈ 192px (3 tiles),
// height ≈ 302px (4.7 tiles), and returns to 0 at the max same-level
// range d_max = 2·moveSpeed·|jumpSpeed|/gravity ≈ 385px (6 tiles).
// The JumpBudget config must stay inside this envelope to keep terrain
// reachable (it does, with a safety margin).
// ==============================================================

/// Height (in tiles) the player reaches above the launch surface at
/// horizontal distance `distanceTiles`, for a fully-held running jump.
/// Negative past the max same-level range (player has fallen below launch).
let arcHeightTiles(distanceTiles: float32) : float32 =
  let d = distanceTiles * tileSize
  let t = d / moveSpeed
  (-jumpSpeed * t - 0.5f * gravity * t * t) / tileSize

/// Maximum same-level gap (in tiles) a running jump can clear.
/// At this distance the arc has returned to launch height.
let maxLevelGapTiles: float32 =
  (2.0f * moveSpeed * (-jumpSpeed) / gravity) / tileSize

/// True when a surface `gapTiles` away horizontally and `riseTiles`
/// higher than the launch surface (negative `riseTiles` = lower) is
/// reachable by a fully-held running jump. This is the physics truth;
/// generation budgets must stay inside it to guarantee reachability.
///
/// NOTE: this models a SINGLE jump from ONE launch surface. It does NOT
/// by itself guarantee the player can get back. For any MANDATORY
/// traversal edge (a gap the player must cross) use `reachableBoth`.
///
/// All platform colliders in this game behave as solid full blocks (no
/// pass-through / semi-solid tiles — see Physics.resolvePlatformCollision,
/// which lands, blocks-from-below, and blocks-from-sides uniformly). So
/// Cloud/Ledge/Overhang are visually distinct but equally landable; the
/// asymmetry risk is purely geometric and lives in the generation planner,
/// not in the tile type.
let reachable (gapTiles: float32) (riseTiles: float32) : bool =
  arcHeightTiles gapTiles >= riseTiles

/// Bidirectional reachability between two surfaces separated by `gapTiles`
/// horizontally and `dyTiles` vertically (positive = the far surface is
/// higher). BOTH the forward jump (gap, +dy) and the return jump (gap, -dy)
/// must be clearable. Use this for any MANDATORY traversal edge: the player
/// must be able to cross a gap in both directions (e.g. to backtrack), not
/// just one way.
///
/// With the symmetric running-jump model (instant horizontal speed, equal in
/// both directions) and all-solid platforms, reachability between two points
/// depends only on |gap| and |Δheight|, so this is equivalent to
/// `reachable (gap, |dy|)`. The two explicit checks keep the intent obvious
/// and serve as the predicate the generation planner uses to validate both
/// directions of every mandatory edge.
let reachableBoth (gapTiles: float32) (dyTiles: float32) : bool =
  reachable gapTiles dyTiles && reachable gapTiles (-dyTiles)

// ==============================================================
// Biome — value-noise based coherent regions
// ==============================================================

let inline chunkSeed (cx: int) (cy: int) (worldSeed: int) =
  cx * 73856093 ^^^ cy * 19349663 ^^^ worldSeed

let private hash01 (x: int) (y: int) (seed: int) : float32 =
  let mutable h = x * 374761393 ^^^ y * 668265263 ^^^ seed * 1442695041
  h <- h ^^^ (h >>> 13)
  h <- h * 1274126177
  h <- h ^^^ (h >>> 16)
  abs(float32(h % 1000)) / 1000.0f

let inline private smoothstep(t: float32) = t * t * (3.0f - 2.0f * t)

let private biomeNoise
  (cx: float32)
  (cy: float32)
  (scale: float32)
  (seed: int)
  : float32 =
  let fx = cx * scale
  let fy = cy * scale
  let x0 = int(MathF.Floor(fx))
  let y0 = int(MathF.Floor(fy))

  let sx = smoothstep(fx - float32 x0)
  let sy = smoothstep(fy - float32 y0)

  let n00 = hash01 x0 y0 seed
  let n10 = hash01 (x0 + 1) y0 seed
  let n01 = hash01 x0 (y0 + 1) seed
  let n11 = hash01 (x0 + 1) (y0 + 1) seed

  let top = n00 + (n10 - n00) * sx
  let bot = n01 + (n11 - n01) * sx
  top + (bot - top) * sy

let private allBiomes = [| Grass; Dirt; Stone; Snow; Sand; Purple |]

/// Biome resolved at world-tile-column granularity from the continuous biome
/// noise field. Unlike a per-chunk lookup, this yields a smooth biome that
/// blends across chunk seams. Sampled per terrain segment so each slab/ledge
/// keeps one consistent biome (no mid-slab seams); transitions land at gaps.
let biomeAtColumn (worldX: int) (seed: int) (scale: float32) : Biome =
  let n = biomeNoise (float32 worldX) 0.0f scale seed
  let idx = min (allBiomes.Length - 1) (int(n * float32 allBiomes.Length))
  allBiomes[idx]

// ==============================================================
// World constants
// ==============================================================

/// Tile Y of the ground surface within every chunk.
let groundY = int worldHeight

/// Ceiling Y — nothing generates above this.
/// Platforms may occupy Y = skyCeiling..(groundY - MinClearance).
let skyCeiling = groundY - 10

// --------------------------------------------------------------
// Elevation — continuous per-column height field
//
// Same value-noise pattern as biomeAtColumn, but produces a surface-Y
// offset from groundY. Low frequency (config.ElevationScale) keeps
// rises gentle. The planner clamps any rise that exceeds the jump arc
// (see Ground.plan), so terrain is ALWAYS reachable regardless of the
// field's shape — the amplitude is aesthetic, not a safety limit.
// --------------------------------------------------------------

/// Surface tile-Y for world column `worldX`, derived from band-limited
/// noise. Returns groundY ± amplitude (lower Y = higher terrain on screen).
/// Uses a seed offset so elevation noise is independent from biome noise.
let elevationAtColumn
  (worldX: int)
  (seed: int)
  (scale: float32)
  (amplitude: int)
  : int =
  if amplitude <= 0 then
    groundY
  else
    let n = biomeNoise (float32 worldX) 0.0f scale (seed ^^^ 0x5A5A5A5A)
    let offset = int(round(n * float32(2 * amplitude + 1))) - amplitude
    groundY - offset

// ==============================================================
// Context
// ==============================================================

[<Struct>]
type GenContext = {
  CX: int
  CY: int
  Seed: int
  Rng: Random
  Biome: Biome
}

let createContext
  (config: GenConfig)
  (cx: int)
  (cy: int)
  (seed: int)
  : GenContext =
  {
    CX = cx
    CY = cy
    Seed = seed
    Rng = Random(chunkSeed cx cy seed)
    Biome = biomeAtColumn (cx * chunkCells) seed config.BiomeColumnScale
  }

// ==============================================================
// Feature specs — pure data describing what to place
// ==============================================================

/// Ground slab specification — a sealed box with proper corners.
[<Struct>]
type GroundSpec = {
  X: int
  Y: int // top surface Y (caller-supplied, e.g. groundY for chunks)
  W: int
  H: int // 1..MaxHeight
}

/// Platform kind — determines which stamp is used.
[<Struct>]
type PlatformKind =
  | Cloud // one-way floating platform (pass-through from below)
  | Ledge // solid horizontal ledge (blocks all sides)
  | Overhang // solid overhang tiles

/// Platform specification.
[<Struct>]
type PlatformSpec = {
  X: int
  Y: int
  W: int
  Kind: PlatformKind
}

// ==============================================================
// Ground primitive — procedural slab placement
//
// Owns: slab count, width, height (≤ 4), gaps, chunk-edge connectivity.
// Delegates tile selection (corners, edges, fill) to Stamps.ground.
// ==============================================================

module Ground =

  /// Decide ground slab placement within a `width`-wide region whose surface
  /// height is sampled per-column from `elevationAt` (localX → surfaceY).
  ///
  /// The caller passes the generation region explicitly — this is chunk-agnostic,
  /// so the same plan fills a chunk row (width = chunkCells) or an island floor
  /// (width = island interior).
  ///
  /// Rules:
  ///   - First slab starts at x=0 (left-edge connectivity).
  ///   - Every slab (including the last) has at least MinGap before it.
  ///   - Gaps are capped to the jump budget so the player can always clear them.
  ///   - **Reachability clamp**: each slab's surface Y is sampled from the
  ///     elevation field, then clamped so the rise from the previous slab
  ///     stays inside the jump arc (`reachable(gap, rise)`). Where the field
  ///     is too steep the terrain flattens into a reachable plateau.
  ///   - The region ends with a trailing gap, not a sealed edge: the last slab
  ///     stops short of the right edge so its corner tile lands at a genuine
  ///     segment end. The next region (starting at x=0) is reached by jumping
  ///     that trailing gap, which is kept within [MinGap, maxGap].
  ///   - Each slab height is 1..MaxHeight (≤ 4).
  let plan
    (rng: Random)
    (config: GroundConfig)
    (budget: JumpBudget)
    (width: int)
    (elevationAt: int -> int)
    : GroundSpec[] =
    let specs = ResizeArray<GroundSpec>()
    let maxGap = min config.MaxGap budget.MaxHorizontalTiles

    /// Clamp `targetY` so the rise from `prevY` across `gap` tiles stays
    /// inside the jump arc. This is the guarantee that terrain is always
    /// reachable — even if the elevation field produces steep changes.
    let clampReachable (gap: int) (prevY: int) (targetY: int) =
      let safeRise = int(floor(arcHeightTiles(float32 gap)))
      let minY = prevY - safeRise // can't be too far up (lower Y)
      let maxY = prevY + safeRise // can't be too far down (higher Y)
      max minY (min maxY targetY)

    let mutable x = 0
    let mutable prevY = elevationAt 0
    let mutable stop = false

    while not stop && specs.Count < config.MaxSlabs do
      // Gap before every slab except the first
      let gap =
        if specs.Count > 0 then
          rng.Next(config.MinGap, maxGap + 1)
        else
          0

      x <- x + gap

      let remaining = width - x

      if remaining < config.MinWidth then
        stop <- true
      else
        let w =
          rng.Next(config.MinWidth, min (config.MaxWidth + 1) (remaining + 1))

        let h = rng.Next(config.MinHeight, config.MaxHeight + 1)

        // Sample elevation, then clamp so the jump from prevY is reachable
        let targetY = elevationAt x

        let y =
          if specs.Count > 0 then
            clampReachable gap prevY targetY
          else
            targetY

        specs.Add { X = x; Y = y; W = w; H = h }
        prevY <- y
        x <- x + w

        // Stop early if trailing gap is within budget and we have enough slabs
        if width - x <= maxGap && specs.Count >= config.MinSlabs then
          stop <- true

    // Ensure the trailing gap stays within the jump budget. When there isn't
    // room for another minimum-width slab, extend the last slab just enough to
    // leave a jumpable gap at the end — instead of bridging to the right edge,
    // which would put a corner tile on the seam.
    while width - x > maxGap do
      let gap = rng.Next(config.MinGap, maxGap + 1)
      let bridgeX = x + gap
      let bridgeRemaining = width - bridgeX

      if bridgeRemaining < config.MinWidth then
        // Can't fit another slab — grow the last one so the trailing gap is
        // jumpable rather than a sealed edge.
        if specs.Count > 0 then
          let i = specs.Count - 1
          let last = specs[i]
          let trailingGap = rng.Next(config.MinGap, maxGap + 1)

          specs[i] <- {
            last with
                W = last.W + (width - x - trailingGap)
          }

        x <- width
      else
        let w =
          rng.Next(
            config.MinWidth,
            min (config.MaxWidth + 1) (bridgeRemaining + 1)
          )

        let h = rng.Next(config.MinHeight, config.MaxHeight + 1)

        // Sample elevation + clamp for the bridge slab too
        let targetY = elevationAt bridgeX
        let y = clampReachable gap prevY targetY

        specs.Add { X = bridgeX; Y = y; W = w; H = h }

        prevY <- y
        x <- bridgeX + w

    specs.ToArray()

  /// Stamp a ground spec, resolving the biome from the spec's world-X column
  /// so biome regions blend continuously across chunk seams. `originX` is the
  /// world-tile-X of column 0 of the region (chunk or island).
  /// All tile-selection logic (BlockTopLeft/Right, BlockBottomLeft/Right,
  /// BlockCenter fill) is handled by Stamps — WorldGen only positions.
  let stamp
    (biomeAt: int -> Biome)
    (originX: int)
    (section: GridSection2D<Tile>)
    (spec: GroundSpec)
    =
    let biome = biomeAt(originX + spec.X)

    section
    |> Layout.section spec.X spec.Y (Stamps.ground biome spec.W spec.H)
    |> ignore

// ==============================================================
// Platform primitive — procedural floating platform placement
//
// Owns: platform count, kind, width, Y-clearance validation.
// Uses CellGrid2D (Grid2D.fs) for spatial occupancy checks and
// Stamps for tile selection (Cloud/Ledge/Overhang edge tiles).
//
// Ground must be stamped on the grid before calling plan — the grid
// is the source of truth for multi-stamp coherency.
// ==============================================================

module Platform =

  // --- Kind selection ---

  let private pickKind(rng: Random) : PlatformKind =
    match rng.Next 3 with
    | 0 -> Cloud
    | 1 -> Ledge
    | _ -> Overhang

  // --- Stamping ---

  /// Stamp a platform spec, resolving the biome from the spec's world-X column
  /// so biome regions blend continuously across chunk seams. `originX` is the
  /// world-tile-X of column 0 of the region.
  /// All tile-selection logic (CloudLeft/Middle/Right, HorizontalLeft/Right,
  /// OverhangLeft/Right) is handled by Stamps — WorldGen only positions.
  let stamp
    (biomeAt: int -> Biome)
    (originX: int)
    (section: GridSection2D<Tile>)
    (spec: PlatformSpec)
    =
    let biome = biomeAt(originX + spec.X)

    match spec.Kind with
    | Cloud ->
      section
      |> Layout.section spec.X spec.Y (Stamps.floatingPlatform biome spec.W)
      |> ignore
    | Ledge ->
      section
      |> Layout.section spec.X spec.Y (Stamps.ledge biome spec.W)
      |> ignore
    | Overhang ->
      section
      |> Layout.section
        spec.X
        spec.Y
        (Stamps.hRow
          spec.W
          (HorizontalOverhangLeft biome)
          (Horizontal biome)
          (HorizontalOverhangRight biome)
          (Horizontal biome))
      |> ignore

  // --- Planning ---

  /// Decide platform placement, building layer-by-layer from `floorY` up.
  ///
  /// The caller passes the generation region explicitly — this is chunk-agnostic,
  /// so the same plan fills a chunk sky (floorY = groundY, ceilingY = skyCeiling)
  /// or an island interior (floorY/ceilingY = the walls' inner rows).
  ///
  /// Each layer sits MinVerticalGap..MaxVerticalGap tiles above the previous,
  /// guaranteeing reachability from floor → layer 1 → layer 2 → …
  /// At each layer, multiple platforms may be placed as long as they have
  /// at least 1 tile X gap between them. Platforms are stamped immediately
  /// as validated, so the grid is the source of truth for occupancy.
  ///
  /// Solid ground must already be stamped on the grid before calling this.
  let plan
    (rng: Random)
    (config: PlatformConfig)
    (budget: JumpBudget)
    (biomeAt: int -> Biome)
    (originX: int)
    (floorY: int)
    (ceilingY: int)
    (section: GridSection2D<Tile>)
    =
    let grid = section.BackingGrid
    let specs = ResizeArray<PlatformSpec>()
    let target = rng.Next(config.MinCount, config.MaxCount + 1)

    // First layer: MinClearance..MaxClearance tiles above the floor surface
    let mutable layerY =
      floorY - rng.Next(config.MinClearance, config.MaxClearance + 1)

    while specs.Count < target && layerY >= ceilingY do
      // Try several candidate positions at this Y level
      let maxTries = rng.Next(3, 7)

      for _ in 1..maxTries do
        if specs.Count >= target then
          ()

        let w = rng.Next(config.MinWidth, config.MaxWidth + 1)
        let x = rng.Next(0, max 1 (section.Width - w))

        // Bounds check
        if layerY >= ceilingY && layerY < floorY && x + w <= grid.Width then
          // Check grid cells are free using CellGrid2D.get directly
          let mutable cellsOk = true
          let mutable ci = 0

          while cellsOk && ci < w do
            match CellGrid2D.get (x + ci) layerY grid with
            | ValueNone -> ci <- ci + 1
            | ValueSome _ -> cellsOk <- false

          // Clearance: scan downward from each platform column to find the
          // actual ground surface. Require at least MinClearance tiles of
          // gap so platforms never sit flush against ground. With variable
          // elevation the ground surface is different per column, so the old
          // flat-floorY check would pass platforms with 0 spacing.
          let mutable clearanceOk = cellsOk
          let mutable cci = 0

          while clearanceOk && cci < w do
            let mutable groundFound = false
            let mutable sy = layerY + 1

            while not groundFound && sy <= layerY + config.MaxClearance do
              match CellGrid2D.get (x + cci) sy grid with
              | ValueSome _ -> groundFound <- true
              | ValueNone -> sy <- sy + 1

            // Ground found within MaxClearance rows below the platform
            if groundFound && (sy - layerY) < config.MinClearance then
              clearanceOk <- false

            cci <- cci + 1

          // Check vertical spacing + X non-overlap (min 1 gap) against placed specs
          let mutable spacingOk = true
          let mutable si = 0

          while spacingOk && si < specs.Count do
            let s = specs[si]
            // X ranges within 1 tile of each other (overlap + min gap buffer)
            let xTooClose = x < s.X + s.W + 1 && s.X < x + w + 1
            let yTooClose = abs(s.Y - layerY) < config.MinVerticalGap

            if xTooClose && yTooClose then
              spacingOk <- false
            else
              si <- si + 1

          if cellsOk && clearanceOk && spacingOk then
            let spec = {
              X = x
              Y = layerY
              W = w
              Kind = pickKind rng
            }

            specs.Add spec
            stamp biomeAt originX section spec

      // Step up for next layer
      layerY <-
        layerY - rng.Next(config.MinVerticalGap, config.MaxVerticalGap + 1)

    section

// ==============================================================
// Extraction — single pass over the grid
// ==============================================================

[<Struct>]
type ExtractedData = {
  Platforms: Rect[]
  OneWayPlatforms: Rect[]
  Spikes: Rect[]
  Coins: Rect[]
  Flags: Rect[]
  Occluders: Occluder[]
  Torches: TorchLight[]
}

/// Single-pass extraction: iterate the grid once and collect all colliders,
/// hazards, collectibles, occluders, and torch lights.
///
/// This scans the full chunk grid (not just camera-visible cells) because
/// Physics iterates nearby chunks and needs ALL colliders regardless of
/// camera position. Chunk generation runs async (cold path), so the full
/// scan is not a per-frame concern.
let private extractAll (grid: CellGrid2D<Tile>) (rng: Random) : ExtractedData =
  let platforms = ResizeArray<Rect>(256)
  let oneWayPlatforms = ResizeArray<Rect>(64)
  let spikes = ResizeArray<Rect>(32)
  let coins = ResizeArray<Rect>(64)
  let flags = ResizeArray<Rect>(4)
  let occluders = ResizeArray<Occluder>(maxOccluders)
  let torches = ResizeArray<TorchLight>(maxTorchLights)

  let cellW = grid.CellSize.X
  let cellH = grid.CellSize.Y

  for y in 0 .. grid.Height - 1 do
    for x in 0 .. grid.Width - 1 do
      match CellGrid2D.get x y grid with
      | ValueNone -> ()
      | ValueSome tile ->
        let wx = grid.Origin.X + float32 x * cellW
        let wy = grid.Origin.Y + float32 y * cellH
        let solid = isSolid tile
        let oneway = isOneWay tile

        if solid then
          let info = lookup tile

          platforms.Add {
            X = wx + info.ColliderRect.X
            Y = wy + info.ColliderRect.Y
            Width = info.ColliderRect.Width
            Height = info.ColliderRect.Height
          }
        elif oneway then
          let info = lookup tile

          oneWayPlatforms.Add {
            X = wx + info.ColliderRect.X
            Y = wy + info.ColliderRect.Y
            Width = info.ColliderRect.Width
            Height = info.ColliderRect.Height
          }

        if (solid || oneway) && torches.Count < maxTorchLights then
          match CellGrid2D.get x (y - 1) grid with
          | ValueNone ->
            if rng.NextDouble() > 0.92 then
              torches.Add {
                Position = Vector2(wx + cellW * 0.5f, wy - 10.0f)
                Color = Mibo.Color.rgb 255uy 160uy 60uy
                Radius = 100.0f + float32(rng.Next(-20, 20))
              }
          | _ -> ()

        if isHazard tile then
          spikes.Add {
            X = wx
            Y = wy
            Width = cellW
            Height = cellH
          }

        if isCoin tile then
          coins.Add {
            X = wx
            Y = wy
            Width = cellW
            Height = cellH
          }

        if isFlag tile then
          flags.Add {
            X = wx
            Y = wy
            Width = cellW
            Height = cellH
          }

        if oneway && occluders.Count < maxOccluders then
          let edgeExposed (nx: int) (ny: int) =
            match CellGrid2D.get nx ny grid with
            | ValueNone -> true
            | ValueSome n -> not(isOneWay n)

          if edgeExposed x (y + 1) then
            occluders.Add {
              P1 = Vector2(wx, wy + cellH)
              P2 = Vector2(wx + cellW, wy + cellH)
            }

          if occluders.Count < maxOccluders && edgeExposed (x - 1) y then
            occluders.Add {
              P1 = Vector2(wx, wy)
              P2 = Vector2(wx, wy + cellH)
            }

          if occluders.Count < maxOccluders && edgeExposed (x + 1) y then
            occluders.Add {
              P1 = Vector2(wx + cellW, wy)
              P2 = Vector2(wx + cellW, wy + cellH)
            }

  {
    Platforms = platforms.ToArray()
    OneWayPlatforms = oneWayPlatforms.ToArray()
    Spikes = spikes.ToArray()
    Coins = coins.ToArray()
    Flags = flags.ToArray()
    Occluders = occluders.ToArray()
    Torches = torches.ToArray()
  }

// ==============================================================
// Orchestrator — reads like a workflow
// ==============================================================

let generateChunk (cx: int) (cy: int) (worldSeed: int) : Chunk =
  let config = defaultConfig
  let ctx = createContext config cx cy worldSeed

  // World-tile-X of this chunk's leftmost column. Terrain biome is resolved
  // per-segment from a continuous world-X field so regions blend across seams.
  let originTileX = cx * chunkCells

  let biomeForColumn wx =
    biomeAtColumn wx worldSeed config.BiomeColumnScale

  let elevationForColumn lx =
    elevationAtColumn
      (originTileX + lx)
      worldSeed
      config.ElevationScale
      config.ElevationAmplitude

  let grid =
    LayeredGrid2D.create
      chunkCells
      chunkCells
      (Vector2(tileSize, tileSize))
      (Vector2(float32 cx * chunkWorldSize, float32 cy * chunkWorldSize))

  LayeredLayout.layer
    Layer.Terrain
    (fun section ->
      // 1. Plan ground slabs (pure data — no grid access)
      // 2. Stamp ground onto grid (biome resolved per slab from world-X)
      Ground.plan
        ctx.Rng
        config.Ground
        config.JumpBudget
        chunkCells
        elevationForColumn
      |> Array.iter(Ground.stamp biomeForColumn originTileX section)

      section)
    grid
  |> ignore

  // 3. Plan + stamp platforms (reads grid for spatial validation,
  //    stamps as each platform is validated)
  let terrainGrid, _ = LayeredGrid2D.getOrAddLayer Layer.Terrain grid

  terrainGrid
  |> Layout.run(
    Platform.plan
      ctx.Rng
      config.Platform
      config.JumpBudget
      biomeForColumn
      originTileX
      groundY
      skyCeiling
  )
  |> ignore

  let extracted = extractAll terrainGrid ctx.Rng
  let origin = grid.Origin

  {
    Grids = grid
    Platforms = extracted.Platforms
    OneWayPlatforms = extracted.OneWayPlatforms
    Spikes = extracted.Spikes
    Coins = extracted.Coins
    Flags = extracted.Flags
    Occluders = extracted.Occluders
    Torches = extracted.Torches
    Bounds = {
      X = origin.X
      Y = origin.Y
      Width = chunkWorldSize
      Height = chunkWorldSize
    }
    Biome = ctx.Biome
  }

// ==============================================================
// Chunk streaming
// ==============================================================

module Chunks =
  [<Struct>]
  type ChunkModel = {
    Chunks: ConcurrentDictionary<struct (int * int), Chunk>
    PendingChunks: HashSet<struct (int * int)>
    Seed: int
  }

  let init(seed: int) = {
    Chunks = ConcurrentDictionary()
    PendingChunks = HashSet()
    Seed = seed
  }

  [<Struct>]
  type ChunkMsg = ChunkCreated of key: struct (int * int) * chunk: Chunk

  let private keysToRemove = ResizeArray<struct (int * int)>(32)

  let inline chunkCreated key chunk model =
    model.Chunks[key] <- chunk
    model.PendingChunks.Remove(key) |> ignore
    model

  let update
    (playerPos: Vector2)
    (model: ChunkModel)
    : struct (ChunkModel * Cmd<ChunkMsg>) =
    let pcx = int(Math.Floor(float playerPos.X / float chunkWorldSize))
    let pcy = int(Math.Floor(float playerPos.Y / float chunkWorldSize))
    let toGen = ResizeArray<struct (int * int)>()

    for x in pcx - chunkLoadRadius .. pcx + chunkLoadRadius do
      for y in pcy - chunkLoadRadius .. pcy + chunkLoadRadius do
        let key = struct (x, y)

        if
          not(model.Chunks.ContainsKey key)
          && not(model.PendingChunks.Contains key)
        then
          model.PendingChunks.Add key |> ignore
          toGen.Add key

    keysToRemove.Clear()

    for KeyValue(key, _) in model.Chunks do
      let struct (cx, cy) = key

      if
        abs(cx - pcx) > chunkEvictRadius || abs(cy - pcy) > chunkEvictRadius
      then
        keysToRemove.Add key

    for k in keysToRemove do
      model.Chunks.TryRemove k |> ignore

    if toGen.Count = 0 then
      model, Cmd.none
    else
      let cmds = [|
        for struct (x, y) in toGen do
          Cmd.ofAsync
            (async { return generateChunk x y model.Seed })
            (fun chunk -> ChunkCreated(struct (x, y), chunk))
            (fun _ex ->
              ChunkCreated(struct (x, y), generateChunk x y model.Seed))
      |]

      model, Cmd.batch cmds
