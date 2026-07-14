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
  BiomeScale: float32
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
  BiomeScale = 0.15f
}

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

let biomeAt (cx: int) (cy: int) (seed: int) (scale: float32) : Biome =
  let n = biomeNoise (float32 cx) (float32 cy) scale seed
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
    Biome = biomeAt cx cy seed config.BiomeScale
  }

// ==============================================================
// Feature specs — pure data describing what to place
// ==============================================================

/// Ground slab specification — a sealed box with proper corners.
[<Struct>]
type GroundSpec = {
  X: int
  Y: int // top surface Y (= groundY)
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

  /// Decide ground slab placement for a chunk.
  ///
  /// Rules:
  ///   - First slab starts at x=0 (left-edge connectivity).
  ///   - Every slab (including the last) has at least MinGap before it.
  ///   - Gaps are capped to the jump budget so the player can always clear them.
  ///   - Trailing gap (last slab → chunk right edge) must also be ≤ maxGap
  ///     for cross-chunk connectivity. If it isn't, a bridge slab is placed.
  ///   - Each slab height is 1..MaxHeight (≤ 4).
  let plan
    (rng: Random)
    (config: GroundConfig)
    (budget: JumpBudget)
    : GroundSpec[] =
    let specs = ResizeArray<GroundSpec>()
    let maxGap = min config.MaxGap budget.MaxHorizontalTiles

    let mutable x = 0
    let mutable stop = false

    while not stop && specs.Count < config.MaxSlabs do
      // Gap before every slab except the first
      if specs.Count > 0 then
        x <- x + rng.Next(config.MinGap, maxGap + 1)

      let remaining = chunkCells - x

      if remaining < config.MinWidth then
        stop <- true
      else
        let w =
          rng.Next(config.MinWidth, min (config.MaxWidth + 1) (remaining + 1))

        let h = rng.Next(config.MinHeight, config.MaxHeight + 1)

        specs.Add { X = x; Y = groundY; W = w; H = h }
        x <- x + w

        // Stop early if trailing gap is within budget and we have enough slabs
        if chunkCells - x <= maxGap && specs.Count >= config.MinSlabs then
          stop <- true

    // Ensure cross-chunk connectivity: if trailing > maxGap, place bridge slabs
    while chunkCells - x > maxGap do
      let gap = rng.Next(config.MinGap, maxGap + 1)
      let bridgeX = x + gap
      let bridgeRemaining = chunkCells - bridgeX

      if bridgeRemaining < config.MinWidth then
        // Can't fit a full slab — extend the last one to close the gap
        if specs.Count > 0 then
          let i = specs.Count - 1
          let last = specs[i]

          specs[i] <- {
            last with
                W = last.W + (chunkCells - x)
          }

        x <- chunkCells
      else
        let w =
          rng.Next(
            config.MinWidth,
            min (config.MaxWidth + 1) (bridgeRemaining + 1)
          )

        let h = rng.Next(config.MinHeight, config.MaxHeight + 1)

        specs.Add {
          X = bridgeX
          Y = groundY
          W = w
          H = h
        }

        x <- bridgeX + w

    specs.ToArray()

  /// Stamp a ground spec using Stamps.ground (proper corners + sealed box).
  /// All tile-selection logic (BlockTopLeft/Right, BlockBottomLeft/Right,
  /// BlockCenter fill) is handled by Stamps — WorldGen only positions.
  let stamp (biome: Biome) (section: GridSection2D<Tile>) (spec: GroundSpec) =
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

  /// Stamp a platform spec using the appropriate Stamps function.
  /// All tile-selection logic (CloudLeft/Middle/Right, HorizontalLeft/Right,
  /// OverhangLeft/Right) is handled by Stamps — WorldGen only positions.
  let stamp (biome: Biome) (section: GridSection2D<Tile>) (spec: PlatformSpec) =
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

  /// Decide platform placement, building layer-by-layer from ground up.
  ///
  /// Each layer sits MinVerticalGap..MaxVerticalGap tiles above the previous,
  /// guaranteeing reachability from ground → layer 1 → layer 2 → …
  /// At each layer, multiple platforms may be placed as long as they have
  /// at least 1 tile X gap between them. Platforms are stamped immediately
  /// as validated, so the grid is the source of truth for occupancy.
  ///
  /// Ground must already be stamped on the grid before calling this.
  let plan
    (rng: Random)
    (config: PlatformConfig)
    (biome: Biome)
    (section: GridSection2D<Tile>)
    =
    let grid = section.BackingGrid
    let specs = ResizeArray<PlatformSpec>()
    let target = rng.Next(config.MinCount, config.MaxCount + 1)

    // First layer: MinClearance..MaxClearance tiles above ground surface
    let mutable layerY =
      groundY - rng.Next(config.MinClearance, config.MaxClearance + 1)

    while specs.Count < target && layerY >= skyCeiling do
      // Try several candidate positions at this Y level
      let maxTries = rng.Next(3, 7)

      for _ in 1..maxTries do
        if specs.Count >= target then
          ()

        let w = rng.Next(config.MinWidth, config.MaxWidth + 1)
        let x = rng.Next(0, max 1 (chunkCells - w))

        // Bounds check
        if layerY >= skyCeiling && layerY < groundY && x + w <= grid.Width then
          // Check grid cells are free using CellGrid2D.get directly
          let mutable cellsOk = true
          let mutable ci = 0

          while cellsOk && ci < w do
            match CellGrid2D.get (x + ci) layerY grid with
            | ValueNone -> ci <- ci + 1
            | ValueSome _ -> cellsOk <- false

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

          if cellsOk && spacingOk then
            let spec = {
              X = x
              Y = layerY
              W = w
              Kind = pickKind rng
            }

            specs.Add spec
            stamp biome section spec

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

        if solid || oneway then
          let info = lookup tile

          platforms.Add {
            X = wx + info.ColliderRect.X
            Y = wy + info.ColliderRect.Y
            Width = info.ColliderRect.Width
            Height = info.ColliderRect.Height
          }

          if torches.Count < maxTorchLights then
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
      // 2. Stamp ground onto grid
      Ground.plan ctx.Rng config.Ground config.JumpBudget
      |> Array.iter(Ground.stamp ctx.Biome section)

      section)
    grid
  |> ignore

  // 3. Plan + stamp platforms (reads grid for spatial validation,
  //    stamps as each platform is validated)
  let terrainGrid, _ = LayeredGrid2D.getOrAddLayer Layer.Terrain grid

  terrainGrid
  |> Layout.run(Platform.plan ctx.Rng config.Platform ctx.Biome)
  |> ignore

  let extracted = extractAll terrainGrid ctx.Rng
  let origin = grid.Origin

  {
    Grids = grid
    Platforms = extracted.Platforms
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
