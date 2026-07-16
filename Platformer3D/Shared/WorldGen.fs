module Platformer3D.WorldGen

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Mibo.Layout3D
open Mibo.Elmish
open Platformer3D.Constants
open Platformer3D.Types

// ==============================================================
// Config
// ==============================================================

/// Player jump reachability budget in cell units.
/// Derived from physics constants:
///   gravity=-20, jumpSpeed=12, moveSpeed=8, cellSize=1
///   apex: h=3.6 at d=4.8, max same-level range: d=9.6
[<Struct>]
type JumpBudget3D = {
  MaxHorizontalCells: int
  MaxVerticalCells: int
}

[<Struct>]
type TerrainConfig3D = {
  BiomeColumnScale: float32
  ElevationScale: float32
  ElevationAmplitude: int
  SpawnProtectedRadius: int
  /// Probability of replacing a flat 2×2 surface region with a multi-cell
  /// block (LargeBlock or TallBlock). 0 = all individual Block cells.
  MultiCellChance: float32
  /// P(TallBlock | multi-cell hit). Remainder → LargeBlock.
  TallBlockChance: float32
  /// P(replacing a surface Block with a LowBlock). Sub-cell height variety.
  LowBlockChance: float32
  /// P(replacing a surface Block with a NarrowBlock). Sub-cell width variety.
  NarrowBlockChance: float32
}

[<Struct>]
type GapConfig3D = {
  MinCount: int
  MaxCount: int
  MinRadius: int
  MaxRadius: int
}

[<Struct>]
type PlatformConfig3D = {
  MinCount: int
  MaxCount: int
  /// Cells above the terrain surface to place the platform.
  MinHeight: int
  MaxHeight: int
}

[<Struct>]
type GenConfig3D = {
  JumpBudget: JumpBudget3D
  Terrain: TerrainConfig3D
  Gap: GapConfig3D
  Platform: PlatformConfig3D
}

let defaultConfig3D = {
  JumpBudget = {
    MaxHorizontalCells = 4
    MaxVerticalCells = 3
  }
  Terrain = {
    BiomeColumnScale = 0.03f
    ElevationScale = 0.04f
    ElevationAmplitude = 2
    SpawnProtectedRadius = 4
    MultiCellChance = 0.15f
    TallBlockChance = 0.4f
    LowBlockChance = 0.08f
    NarrowBlockChance = 0.05f
  }
  Gap = {
    MinCount = 2
    MaxCount = 4
    MinRadius = 1
    MaxRadius = 2
  }
  Platform = {
    MinCount = 4
    MaxCount = 8
    MinHeight = 2
    MaxHeight = 4
  }
}

// ==============================================================
// Reachability — physics-derived 3D jump predicate
//
// The player launches upward at jumpSpeed and drifts horizontally at
// moveSpeed. In 3D the horizontal distance is Euclidean √(dx²+dz²).
//
//   t(d) = d / moveSpeed              (time to cross distance d)
//   h(d) = jumpSpeed·t(d) + ½·gravity·t(d)²   (height above launch)
//
// h(d) peaks at d* = moveSpeed·jumpSpeed/|gravity| ≈ 4.8 cells,
// height ≈ 3.6 cells, and returns to 0 at d_max = 2·moveSpeed·jumpSpeed/|gravity| ≈ 9.6.
// ==============================================================

/// Height (in cells) the player reaches above the launch surface at
/// Euclidean horizontal distance `horizontalDist` (in cells), for a fully-held
/// running jump. Negative past the max same-level range.
let arcHeight3D(horizontalDist: float32) : float32 =
  let d = horizontalDist * cellSize
  let t = d / moveSpeed
  (jumpSpeed * t + 0.5f * gravity * t * t) / cellSize

/// Maximum same-level gap (in cells) a running jump can clear.
let maxLevelGap3D: float32 =
  (2.0f * moveSpeed * jumpSpeed / abs(gravity)) / cellSize

/// True when a surface `horizontalDist` cells away (Euclidean XZ) and `rise`
/// cells higher (negative = lower) is reachable by a fully-held running jump.
let reachable3D (horizontalDist: float32) (rise: float32) : bool =
  let effectiveGap = max horizontalDist 1.0f
  arcHeight3D effectiveGap >= rise

// ==============================================================
// Noise — 2D value noise for elevation and biome fields
// ==============================================================

let inline chunkSeed (cx: int) (cz: int) (worldSeed: int) =
  cx * 73856093 ^^^ cz * 19349663 ^^^ worldSeed

let private hash01 (x: int) (z: int) (seed: int) : float32 =
  let mutable h = x * 374761393 ^^^ z * 668265263 ^^^ seed * 1442695041
  h <- h ^^^ (h >>> 13)
  h <- h * 1274126177
  h <- h ^^^ (h >>> 16)
  abs(float32(h % 1000)) / 1000.0f

let inline private smoothstep(t: float32) = t * t * (3.0f - 2.0f * t)

/// Bilinear-interpolated 2D value noise in [0, 1].
let private valueNoise
  (worldX: float32)
  (worldZ: float32)
  (scale: float32)
  (seed: int)
  : float32 =
  let fx = worldX * scale
  let fz = worldZ * scale
  let x0 = int(MathF.Floor(fx))
  let z0 = int(MathF.Floor(fz))

  let sx = smoothstep(fx - float32 x0)
  let sz = smoothstep(fz - float32 z0)

  let n00 = hash01 x0 z0 seed
  let n10 = hash01 (x0 + 1) z0 seed
  let n01 = hash01 x0 (z0 + 1) seed
  let n11 = hash01 (x0 + 1) (z0 + 1) seed

  let top = n00 + (n10 - n00) * sx
  let bot = n01 + (n11 - n01) * sx
  top + (bot - top) * sz

// ==============================================================
// World constants
// ==============================================================

/// Base terrain surface Y. Columns are filled from y=0 to the elevation.
/// Player spawns at y=10, well above this.
let groundY3D = 2

// ==============================================================
// Elevation & biome — continuous per-column fields over world XZ
// ==============================================================

/// Surface Y for world column (worldX, worldZ), derived from band-limited
/// noise. Returns groundY3D ± amplitude. Spawn area is pinned flat so the
/// player always lands on a stable surface.
let elevationAt
  (worldX: int)
  (worldZ: int)
  (seed: int)
  (scale: float32)
  (amplitude: int)
  (spawnRadius: int)
  : int =
  if amplitude <= 0 then
    groundY3D
  else
    let dx = float32 worldX - spawnPosition.X
    let dz = float32 worldZ - spawnPosition.Z
    let distSq = dx * dx + dz * dz
    let radius = float32 spawnRadius

    if distSq <= radius * radius then
      groundY3D
    else
      let n =
        valueNoise (float32 worldX) (float32 worldZ) scale (seed ^^^ 0x5A5A5A5A)

      let offset = int(round(n * float32(2 * amplitude + 1))) - amplitude
      groundY3D + offset

let private allBiomes3D = [| Biome3D.Grass; Biome3D.Snow |]

/// Biome resolved per-column from the continuous biome noise field.
/// Sampled per-column so each cell keeps one consistent biome.
let biomeAt (worldX: int) (worldZ: int) (seed: int) (scale: float32) : Biome3D =
  let n = valueNoise (float32 worldX) (float32 worldZ) scale seed
  let idx = min (allBiomes3D.Length - 1) (int(n * float32 allBiomes3D.Length))
  allBiomes3D[idx]

// ==============================================================
// Chunk generation
// ==============================================================

let generateChunk (cx: int) (cz: int) (worldSeed: int) : Chunk =
  let config = defaultConfig3D

  let origin =
    Vector3(float32 cx * chunkWorldWidth, 0.0f, float32 cz * chunkWorldDepth)

  let originTileX = cx * chunkWidth
  let originTileZ = cz * chunkDepth

  let grids =
    LayeredGrid3D.create
      chunkWidth
      chunkHeight
      chunkDepth
      (Vector3(cellSize, cellSize, cellSize))
      origin

  let rng = Random(chunkSeed cx cz worldSeed)

  LayeredLayout3D.layer
    Layer.Terrain
    (fun section ->
      let grid = section.BackingGrid

      // ── Elevation field (W×D, no border — full fill doesn't need neighbors) ──
      let elevField = Array.create (section.Width * section.Depth) 0

      for lz in 0 .. section.Depth - 1 do
        for lx in 0 .. section.Width - 1 do
          elevField[lz * section.Width + lx] <-
            elevationAt
              (originTileX + lx)
              (originTileZ + lz)
              worldSeed
              config.Terrain.ElevationScale
              config.Terrain.ElevationAmplitude
              config.Terrain.SpawnProtectedRadius

      let elevAt (lx: int) (lz: int) = elevField[lz * section.Width + lx]

      let biomeAt' (lx: int) (lz: int) =
        biomeAt
          (originTileX + lx)
          (originTileZ + lz)
          worldSeed
          config.Terrain.BiomeColumnScale

      let inSpawn (lx: int) (lz: int) =
        let wx = float32(originTileX + lx) - spawnPosition.X
        let wz = float32(originTileZ + lz) - spawnPosition.Z
        let r = float32 config.Terrain.SpawnProtectedRadius
        wx * wx + wz * wz <= r * r

      // ── 1. Full volume fill ──
      // Solid columns from y=0 to surface. The framework shadow instancing fix
      // (ForwardPbrPipeline) renders these as single DrawMeshInstanced calls,
      // so the full volume no longer incurs per-cell shadow cost.
      for lz in 0 .. section.Depth - 1 do
        for lx in 0 .. section.Width - 1 do
          let surfaceY = min (elevAt lx lz) (section.Height - 1)
          let biome = biomeAt' lx lz

          for y in 0..surfaceY do
            setLocal lx y lz (Block biome) section

      // ── 2. Multi-cell surface variety (2×2 flat regions) ──
      // Replace flat 2×2 surface areas with LargeBlock or TallBlock.
      if config.Terrain.MultiCellChance > 0.0f then
        let mutable lz = 0

        while lz < section.Depth - 1 do
          let mutable lx = 0

          while lx < section.Width - 1 do
            let h00 = elevAt lx lz
            let h10 = elevAt (lx + 1) lz
            let h01 = elevAt lx (lz + 1)
            let h11 = elevAt (lx + 1) (lz + 1)

            if h00 = h10 && h00 = h01 && h00 = h11 then
              let b00 = biomeAt' lx lz
              let b10 = biomeAt' (lx + 1) lz
              let b01 = biomeAt' lx (lz + 1)
              let b11 = biomeAt' (lx + 1) (lz + 1)

              if b00 = b10 && b00 = b01 && b00 = b11 then
                if
                  float32(rng.NextDouble()) < config.Terrain.MultiCellChance
                then
                  let y = min h00 (section.Height - 1)

                  clearLocal lx y lz section
                  clearLocal (lx + 1) y lz section
                  clearLocal lx y (lz + 1) section
                  clearLocal (lx + 1) y (lz + 1) section

                  let blockType =
                    if
                      float32(rng.NextDouble()) < config.Terrain.TallBlockChance
                    then
                      TallBlock b00
                    else
                      LargeBlock b00

                  setLocal lx y lz blockType section
                  lx <- lx + 2
                else
                  lx <- lx + 2
              else
                lx <- lx + 2
            else
              lx <- lx + 1

          lz <- lz + 2

      // ── 3. Individual surface variety (LowBlock, NarrowBlock) ──
      // For surface cells still containing a plain Block, roll for sub-cell
      // variants. These are single-cell blocks — no multi-cell scan needed.
      if
        config.Terrain.LowBlockChance > 0.0f
        || config.Terrain.NarrowBlockChance > 0.0f
      then
        let lowThreshold = config.Terrain.LowBlockChance
        let narrowThreshold = lowThreshold + config.Terrain.NarrowBlockChance

        for lz in 0 .. section.Depth - 1 do
          for lx in 0 .. section.Width - 1 do
            let y = min (elevAt lx lz) (section.Height - 1)

            match CellGrid3D.get lx y lz grid with
            | ValueSome(Block biome) ->
              let roll = float32(rng.NextDouble())

              if roll < lowThreshold then
                setLocal lx y lz (LowBlock biome) section
              elif roll < narrowThreshold then
                setLocal lx y lz (NarrowBlock biome) section
            | _ -> ()

      // ── 4. Gaps — carve circular pits through the terrain ──
      // Removes all cells in a column within a small radius, creating pits
      // the player must jump across. Gaps are kept small (radius 1-2) so they
      // are easily jumpable (max Euclidean gap ~4 cells, well within budget).
      if config.Gap.MaxCount > 0 then
        let gapCount = rng.Next(config.Gap.MinCount, config.Gap.MaxCount + 1)

        for _ in 1..gapCount do
          let gcx =
            rng.Next(
              config.Terrain.SpawnProtectedRadius,
              section.Width - config.Terrain.SpawnProtectedRadius
            )

          let gcz =
            rng.Next(
              config.Terrain.SpawnProtectedRadius,
              section.Depth - config.Terrain.SpawnProtectedRadius
            )

          let radius = rng.Next(config.Gap.MinRadius, config.Gap.MaxRadius + 1)

          for dz in -radius .. radius do
            for dx in -radius .. radius do
              if dx * dx + dz * dz <= radius * radius then
                let gx = gcx + dx
                let gz = gcz + dz

                if
                  gx >= 0
                  && gx < section.Width
                  && gz >= 0
                  && gz < section.Depth
                  && not(inSpawn gx gz)
                then
                  let h = min (elevAt gx gz) (section.Height - 1)

                  for y in 0..h do
                    clearLocal gx y gz section

      // ── 5. Floating platforms ──
      // Place Platform blocks above the terrain surface at jumpable heights.
      // Each platform is a single Platform cell; adjacent platforms form wider
      // surfaces the player can land on. Height is 2-4 cells above the surface,
      // within the jump budget (MaxVerticalCells = 3).
      if config.Platform.MaxCount > 0 then
        let targetCount =
          rng.Next(config.Platform.MinCount, config.Platform.MaxCount + 1)

        let mutable placed = 0
        let mutable tries = 0
        let maxTries = targetCount * 4

        while placed < targetCount && tries < maxTries do
          tries <- tries + 1

          let px = rng.Next(1, section.Width - 1)
          let pz = rng.Next(1, section.Depth - 1)

          if not(inSpawn px pz) then
            let surfH = min (elevAt px pz) (section.Height - 1)

            let heightOffset =
              rng.Next(
                config.Platform.MinHeight,
                config.Platform.MaxHeight + 1
              )

            let py = min (surfH + heightOffset) (section.Height - 1)

            // Only place if the cell is empty (no overlap with terrain or existing platform).
            match CellGrid3D.get px py pz grid with
            | ValueNone ->
              setLocal px py pz Platform section
              placed <- placed + 1
            | _ -> ()

      section)
    grids
  |> ignore

  {
    Grids = grids
    Bounds = {
      Min = origin
      Max =
        origin
        + Vector3(
          chunkWorldWidth,
          float32 chunkHeight * cellSize,
          chunkWorldDepth
        )
    }
    OriginX = cx
    OriginZ = cz
  }

let loadChunks
  (playerPos: Vector3)
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  (seed: int)
  =
  let pcx = int(Math.Floor(float playerPos.X / float chunkWorldWidth))
  let pcz = int(Math.Floor(float playerPos.Z / float chunkWorldDepth))

  for x in pcx - chunkLoadRadius .. pcx + chunkLoadRadius do
    for z in pcz - chunkLoadRadius .. pcz + chunkLoadRadius do
      let key = struct (x, z)

      if not(chunks.ContainsKey(key)) then
        chunks[key] <- generateChunk x z seed

let evictDistantChunks
  (playerPos: Vector3)
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  (keysToRemove: ResizeArray<struct (int * int)>)
  =
  let pcx = int(Math.Floor(float playerPos.X / float chunkWorldWidth))
  let pcz = int(Math.Floor(float playerPos.Z / float chunkWorldDepth))
  keysToRemove.Clear()

  for KeyValue(key, _) in chunks do
    let struct (cx, cz) = key

    if abs(cx - pcx) > chunkEvictRadius || abs(cz - pcz) > chunkEvictRadius then
      keysToRemove.Add key

  for i = 0 to keysToRemove.Count - 1 do
    chunks.TryRemove(keysToRemove[i]) |> ignore

// -------------------------------------------------------------
// Chunks Sub-system (backend-agnostic)
// -------------------------------------------------------------

module Chunks =

  type ChunksModel() =
    member val Chunks =
      ConcurrentDictionary<struct (int * int), Chunk>() with get, set

    member val PendingChunks = HashSet<struct (int * int)>() with get, set
    member val KeysToRemove = ResizeArray<struct (int * int)>() with get, set
    member val Seed = 0 with get, set

  [<Struct>]
  type ChunkMsg = ChunkCreated of key: struct (int * int) * chunk: Chunk

  let init(seed: int) = ChunksModel(Seed = seed)

  let chunkCreated
    (key: struct (int * int))
    (chunk: Chunk)
    (model: ChunksModel)
    : ChunksModel =
    model.Chunks[key] <- chunk
    model.PendingChunks.Remove(key) |> ignore
    model

  let private generateChunkAsync
    (cx: int)
    (cz: int)
    (seed: int)
    : Cmd<ChunkMsg> =
    Cmd.ofAsync
      (async { return generateChunk cx cz seed })
      (fun chunk -> ChunkCreated(struct (cx, cz), chunk))
      (fun _ex -> ChunkCreated(struct (cx, cz), generateChunk cx cz seed))

  // Reused every tick — avoids allocating a fresh ResizeArray per update.
  let private keysToGenerate = ResizeArray<struct (int * int)>()

  let update
    (playerPos: Vector3)
    (model: ChunksModel)
    : struct (ChunksModel * Cmd<ChunkMsg>) =
    let pcx = int(Math.Floor(float playerPos.X / float chunkWorldWidth))
    let pcz = int(Math.Floor(float playerPos.Z / float chunkWorldDepth))
    keysToGenerate.Clear()

    for x in pcx - chunkLoadRadius .. pcx + chunkLoadRadius do
      for z in pcz - chunkLoadRadius .. pcz + chunkLoadRadius do
        let key = struct (x, z)

        if
          not(model.Chunks.ContainsKey(key))
          && not(model.PendingChunks.Contains(key))
        then
          model.PendingChunks.Add(key) |> ignore
          keysToGenerate.Add(key)

    evictDistantChunks playerPos model.Chunks model.KeysToRemove

    if keysToGenerate.Count = 0 then
      struct (model, Cmd.none)
    else
      let cmd =
        Cmd.batch [|
          for struct (x, z) in keysToGenerate do
            generateChunkAsync x z model.Seed
        |]

      struct (model, cmd)
