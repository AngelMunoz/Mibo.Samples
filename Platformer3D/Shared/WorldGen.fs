module Platformer3D.WorldGen

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Mibo.Elmish
open Mibo.Layout
open Mibo.Layout3D
open Platformer3D.Constants
open Platformer3D.Types
open Platformer3D.Level

// ==============================================================
// Config
// ==============================================================

type GenConfig3D = {
  /// Course segment sizing: runways and budget-checked gaps.
  Segment: SegmentSpec
  /// Set-piece knobs: hazards, hop platforms, lane borders.
  Props: PropsSpec
  /// Pickup knobs: coin arcs and lines.
  Pickups: PickupsSpec
}

let defaultConfig3D = {
  Segment = {
    MinRun = 5
    MaxRun = 9
    MinGap = 2
    MaxGap = 5
  }
  Props = {
    HazardChance = 0.25f
    HopChance = 0.5f
    BorderSpacing = 3
  }
  Pickups = {
    LinesPerRunway = 1
    GoldChance = 0.3f
  }
}

// ==============================================================
// Deterministic schedules — heights, lane centers, biomes
// ==============================================================

let inline chunkSeed (cx: int) (cz: int) (worldSeed: int) =
  cx * 73856093 ^^^ cz * 19349663 ^^^ worldSeed

let private hash01 (x: int) (z: int) (seed: int) : float32 =
  let mutable h = x * 374761393 ^^^ z * 668265263 ^^^ seed * 1442695041
  h <- h ^^^ (h >>> 13)
  h <- h * 1274126177
  h <- h ^^^ (h >>> 16)
  abs(float32(h % 1000)) / 1000.0f

// ==============================================================
// World constants
// ==============================================================

/// Base course height, in cells. The height schedule walks one or two
/// cells around it; the spawn chunk is pinned flat at it.
let groundY3D = 2

/// The course row: the only chunk row that holds ground. Every other row
/// is void — there is exactly one path through the world.
[<Literal>]
let CourseRowZ = 0

/// The chunk's course heights: entry and exit, one or two cells around the
/// base. Deterministic in the chunk index, so chunk N's exit is always
/// chunk N+1's entry and the course chains seamlessly along X.
let entryHeightAt (worldSeed: int) (cx: int) : int =
  if cx = 0 then
    groundY3D
  else
    groundY3D + ((abs(chunkSeed cx CourseRowZ worldSeed) % 3) - 1)

/// The biome a chunk's runways are made of: two-chunk zones, alternating
/// grass and snow along the course. The seed picks which biome opens the
/// run; the zone schedule guarantees both kits appear within any four
/// consecutive chunks.
let biomeForChunk (worldSeed: int) (cx: int) : Biome3D =
  let flip = if hash01 cx 0 worldSeed < 0.5f then 0 else 1

  if (cx / 2 + flip) % 2 = 0 then
    Biome3D.Grass
  else
    Biome3D.Snow

// ==============================================================
// Chunk generation — one Flow course segment per chunk
// ==============================================================

let private cellSize2 = Vector2(cellSize, cellSize)

/// The chunk grid origin shared by every layer (grid Y is world Z).
let private chunkOrigin (cx: int) (cz: int) : Vector2 =
  Vector2(
    float32(cx * chunkWidth) * cellSize,
    float32(cz * chunkDepth) * cellSize
  )

/// Derived render layer: platform slabs. Greedy maximal rectangles merge
/// the populated cells of one height and material, and each rectangle
/// renders as ONE unit block scaled to the platform's exact size (H
/// includes the crust cell) — one platform, one model instance.
let private deriveSlabs
  (terrain: CellGrid2D<TerrainColumn>)
  (origin: Vector2)
  : CellGrid2D<SlabTile> =
  let slabs = CellGrid2D.create terrain.Width terrain.Height cellSize2 origin
  let w = terrain.Width
  let d = terrain.Height

  let heightAt (x: int) (z: int) : int =
    match CellGrid2D.get x z terrain with
    | ValueSome col when not(TerrainColumn.isPit col) -> col.Height
    | _ -> -1 // unpopulated

  let materialAt (x: int) (z: int) : Biome3D =
    match CellGrid2D.get x z terrain with
    | ValueSome col -> col.Material
    | ValueNone -> Biome3D.Grass

  let visited = Array.zeroCreate<bool>(w * d)

  for z = 0 to d - 1 do
    for x = 0 to w - 1 do
      if not visited[z * w + x] && heightAt x z >= 0 then
        let h = heightAt x z
        let material = materialAt x z

        let same (xx: int) (zz: int) =
          not visited[zz * w + xx]
          && heightAt xx zz = h
          && materialAt xx zz = material

        // Grow the rectangle along X first, then along Z while the whole
        // row matches — maximal per (height, material) region.
        let mutable x1 = x

        while x1 + 1 < w && same (x1 + 1) z do
          x1 <- x1 + 1

        let mutable z1 = z
        let mutable rowOk = true

        while z1 + 1 < d && rowOk do
          rowOk <- true

          for xx = x to x1 do
            if not(same xx (z1 + 1)) then
              rowOk <- false

          if rowOk then
            z1 <- z1 + 1

        for zz = z to z1 do
          for xx = x to x1 do
            visited[zz * w + xx] <- true

        CellGrid2D.set
          x
          z
          {
            Material = material
            W = x1 - x + 1
            H = h + 1
            D = z1 - z + 1
          }
          slabs

  slabs

/// Generate one chunk: the terrain document lays out the course segment
/// (runways, gaps, stairs, spawn, checkpoint) and reports its landmarks;
/// the prop and pickup documents read those landmarks to place the set
/// pieces. Runs on a background thread per chunk (Cmd.ofAsync).
///
/// Chunk rows other than the course row generate as empty sky — the world
/// is one path over the void, not a lattice of slabs.
let generateChunk (cx: int) (cz: int) (worldSeed: int) : Chunk =
  let config = defaultConfig3D
  let rng = Random(chunkSeed cx cz worldSeed)
  let origin = chunkOrigin cx cz

  let emptyGrid() : CellGrid2D<'a> =
    CellGrid2D.create chunkWidth chunkDepth cellSize2 origin

  if cz <> CourseRowZ then
    let chunkOriginV3 = Vector3(origin.X, 0.0f, origin.Y)

    {
      Terrain = emptyGrid()
      Slabs = emptyGrid()
      Props = emptyGrid()
      Pickups = emptyGrid()
      Marks = Level.emptyChunkMarks()
      Bounds = {
        Min = chunkOriginV3
        Max =
          chunkOriginV3
          + Vector3(
            chunkWorldWidth,
            float32 chunkHeight * cellSize,
            chunkWorldDepth
          )
      }
      OriginX = cx
      OriginZ = cz
    }
  else
    let material = biomeForChunk worldSeed cx

    let entryHeight = entryHeightAt worldSeed cx
    let exitHeight = entryHeightAt worldSeed (cx + 1)
    let entryCenter = Level.laneCenterAt worldSeed cx
    let exitCenter = Level.laneCenterAt worldSeed (cx + 1)

    let spawn = if cx = 0 then ValueSome { Length = 8 } else ValueNone

    // 1. Terrain: the course segment, one Flow.run.
    let terrainGrid = CellGrid2D.create chunkWidth chunkDepth cellSize2 origin

    let struct (terrainGrid, marks) =
      terrainGrid
      |> Flow.run(
        Level.segment
          rng
          config.Segment
          material
          entryHeight
          exitHeight
          entryCenter
          exitCenter
          spawn
      )

    // 2. Props and pickups, reading the finished terrain and its landmarks.
    let propsGrid = CellGrid2D.create chunkWidth chunkDepth cellSize2 origin

    let struct (propsGrid, _) =
      propsGrid |> Flow.run(Level.props rng terrainGrid marks config.Props)

    let pickupsGrid = CellGrid2D.create chunkWidth chunkDepth cellSize2 origin

    let struct (pickupsGrid, _) =
      pickupsGrid
      |> Flow.run(Level.pickups rng terrainGrid marks config.Pickups)

    // 3. Derived render layers + the chunk record.
    let chunkOriginV3 = Vector3(origin.X, 0.0f, origin.Y)

    {
      Terrain = terrainGrid
      Slabs = deriveSlabs terrainGrid origin
      Props = propsGrid
      Pickups = pickupsGrid
      Marks = marks
      Bounds = {
        Min = chunkOriginV3
        Max =
          chunkOriginV3
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
