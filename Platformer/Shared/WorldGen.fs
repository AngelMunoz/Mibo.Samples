module Platformer.WorldGen

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Mibo.Layout
open Platformer.Constants
open Platformer.Types
open Mibo.Elmish


let chunkSeed (cx: int) (cy: int) (worldSeed: int) =
  cx * 73856093 ^^^ cy * 19349663 ^^^ worldSeed

// -------------------------------------------------------------
// Occluder Generation
// -------------------------------------------------------------

[<Flags>]
type Edge =
  | None = 0
  | Top = 1
  | Bottom = 2
  | Left = 4
  | Right = 8
  | All = 15

let generateOccluders
  (isSolid: 'T -> bool)
  (edges: Edge)
  (grid: CellGrid2D<'T>)
  : Occluder[] =
  let occluders = ResizeArray<Occluder>()
  let cellW = grid.CellSize.X
  let cellH = grid.CellSize.Y

  for y in 0 .. grid.Height - 1 do
    for x in 0 .. grid.Width - 1 do
      match CellGrid2D.get x y grid with
      | ValueNone -> ()
      | ValueSome tile ->
        if isSolid tile then
          let wx = grid.Origin.X + float32 x * cellW
          let wy = grid.Origin.Y + float32 y * cellH

          if edges &&& Edge.Bottom = Edge.Bottom then
            match CellGrid2D.get x (y + 1) grid with
            | ValueNone ->
              occluders.Add {
                P1 = Vector2(wx, wy + cellH)
                P2 = Vector2(wx + cellW, wy + cellH)
              }
            | ValueSome n when not(isSolid n) ->
              occluders.Add {
                P1 = Vector2(wx, wy + cellH)
                P2 = Vector2(wx + cellW, wy + cellH)
              }
            | _ -> ()

          if edges &&& Edge.Top = Edge.Top then
            match CellGrid2D.get x (y - 1) grid with
            | ValueNone ->
              occluders.Add {
                P1 = Vector2(wx, wy)
                P2 = Vector2(wx + cellW, wy)
              }
            | ValueSome n when not(isSolid n) ->
              occluders.Add {
                P1 = Vector2(wx, wy)
                P2 = Vector2(wx + cellW, wy)
              }
            | _ -> ()

          if edges &&& Edge.Left = Edge.Left then
            match CellGrid2D.get (x - 1) y grid with
            | ValueNone ->
              occluders.Add {
                P1 = Vector2(wx, wy)
                P2 = Vector2(wx, wy + cellH)
              }
            | ValueSome n when not(isSolid n) ->
              occluders.Add {
                P1 = Vector2(wx, wy)
                P2 = Vector2(wx, wy + cellH)
              }
            | _ -> ()

          if edges &&& Edge.Right = Edge.Right then
            match CellGrid2D.get (x + 1) y grid with
            | ValueNone ->
              occluders.Add {
                P1 = Vector2(wx + cellW, wy)
                P2 = Vector2(wx + cellW, wy + cellH)
              }
            | ValueSome n when not(isSolid n) ->
              occluders.Add {
                P1 = Vector2(wx + cellW, wy)
                P2 = Vector2(wx + cellW, wy + cellH)
              }
            | _ -> ()

  occluders.ToArray()

// -------------------------------------------------------------
// Tile Extraction
// -------------------------------------------------------------

let private collectTiles grid predicate : Rect[] =
  let result = ResizeArray<Rect>()
  let cellW = grid.CellSize.X
  let cellH = grid.CellSize.Y

  for y in 0 .. grid.Height - 1 do
    for x in 0 .. grid.Width - 1 do
      match CellGrid2D.get x y grid with
      | ValueSome tile when predicate tile ->
        result.Add {
          X = grid.Origin.X + float32 x * cellW
          Y = grid.Origin.Y + float32 y * cellH
          Width = cellW
          Height = cellH
        }
      | _ -> ()

  result.ToArray()

let private extractPlatforms(grid: CellGrid2D<TileType>) : Rect[] =
  let platforms = ResizeArray<Rect>()
  let cellW = grid.CellSize.X
  let cellH = grid.CellSize.Y

  for y in 0 .. grid.Height - 1 do
    let mutable x = 0

    while x < grid.Width do
      match CellGrid2D.get x y grid with
      | ValueSome Ground
      | ValueSome Platform ->
        let startX = x
        let mutable runLength = 1
        let mutable more = true

        while more && x + runLength < grid.Width do
          match CellGrid2D.get (x + runLength) y grid with
          | ValueSome Ground
          | ValueSome Platform -> runLength <- runLength + 1
          | _ -> more <- false

        let wx = grid.Origin.X + float32 startX * cellW
        let wy = grid.Origin.Y + float32 y * cellH

        platforms.Add {
          X = wx
          Y = wy
          Width = float32 runLength * cellW
          Height = cellH
        }

        x <- x + runLength
      | _ -> x <- x + 1

  platforms.ToArray()

let private extractTorches
  (grid: CellGrid2D<TileType>)
  (rng: Random)
  : TorchLight[] =
  let torches = ResizeArray<TorchLight>()
  let cellW = grid.CellSize.X

  for y in 0 .. grid.Height - 1 do
    let mutable x = 0

    while x < grid.Width do
      match CellGrid2D.get x y grid with
      | ValueSome Ground
      | ValueSome Platform ->
        match CellGrid2D.get x (y - 1) grid with
        | ValueNone ->
          if rng.NextDouble() > 0.92 then
            let wx = grid.Origin.X + float32 x * cellW + cellW * 0.5f
            let wy = grid.Origin.Y + float32 y * grid.CellSize.Y - 10.0f

            torches.Add {
              Position = Vector2(wx, wy)
              Color = Mibo.Color.rgb 255uy 160uy 60uy
              Radius = 100.0f + float32(rng.Next(-20, 20))
            }
        | _ -> ()

        x <- x + 1
      | _ -> x <- x + 1

  torches.ToArray()

// -------------------------------------------------------------
// Chunk Generation
// -------------------------------------------------------------

let generateChunk (cx: int) (cy: int) (worldSeed: int) : Chunk =
  let rng = Random(chunkSeed cx cy worldSeed)
  let origin = Vector2(float32 cx * chunkWorldSize, float32 cy * chunkWorldSize)

  let grid =
    CellGrid2D.create chunkCells chunkCells (Vector2(tileSize, tileSize)) origin

  let groundY = int worldHeight

  let biome =
    match (abs cx + abs cy) % 4 with
    | 0 -> Grass
    | 1 -> Stone
    | 2 -> Snow
    | _ -> Sand

  if cy = 0 then
    let archetype = rng.Next(100)

    if archetype < 40 then
      Layout.run
        (fun section ->
          section
          |> Layout.section 0 groundY (fun groundSection ->
            groundSection |> Platformer.platform chunkCells Ground |> ignore
            let pitCount = rng.Next(1, 4)

            for _ in 1..pitCount do
              let px = rng.Next(spawnProtectedCells, chunkCells - 5)
              let pw = rng.Next(2, 5)

              groundSection
              |> Layout.section px 0 (Platformer.pit pw 1)
              |> ignore

            groundSection)
          |> ignore

          let platCount = rng.Next(1, 4)

          for _ in 1..platCount do
            let px = rng.Next(0, chunkCells - 8)
            let py = rng.Next(groundY - 3, groundY - 1)
            let pw = rng.Next(3, 8)

            section
            |> Layout.section px py (Platformer.platform pw Platform)
            |> ignore

            for cx in 0 .. pw - 1 do
              if rng.Next(4) = 0 then
                Layout.set (px + cx) (py - 1) Coin section |> ignore

          section)
        grid
      |> ignore

    elif archetype < 60 then
      Layout.run
        (fun section ->
          section
          |> Layout.section 0 groundY (fun groundSection ->
            groundSection |> Platformer.platform chunkCells Ground |> ignore
            let pitCount = rng.Next(0, 2)

            for _ in 1..pitCount do
              let px = rng.Next(spawnProtectedCells, chunkCells - 5)
              let pw = rng.Next(2, 4)

              groundSection
              |> Layout.section px 0 (Platformer.pit pw 1)
              |> ignore

            groundSection)
          |> ignore

          let sx = rng.Next(4, chunkCells - 10)

          let stairDir =
            if rng.Next(2) = 0 then
              Platformer.UpRight
            else
              Platformer.UpLeft

          section
          |> Layout.section
            sx
            (groundY - 6)
            (Platformer.stairs 5 Platform stairDir)
          |> ignore

          let platCount = rng.Next(1, 3)

          for _ in 1..platCount do
            let px = rng.Next(0, chunkCells - 6)
            let py = rng.Next(groundY - 4, groundY - 2)
            let pw = rng.Next(3, 6)

            section
            |> Layout.section px py (Platformer.platform pw Platform)
            |> ignore

          section)
        grid
      |> ignore

    elif archetype < 85 then
      Layout.run
        (fun section ->
          section
          |> Layout.section 0 groundY (fun groundSection ->
            groundSection |> Platformer.platform chunkCells Ground |> ignore
            let pitCount = rng.Next(0, 3)

            for _ in 1..pitCount do
              let px = rng.Next(spawnProtectedCells, chunkCells - 4)
              let pw = rng.Next(2, 4)

              groundSection
              |> Layout.section px 0 (Platformer.pit pw 1)
              |> ignore

            groundSection)
          |> ignore

          for row in 1..3 do
            let platCount = rng.Next(2, 5)

            for _ in 1..platCount do
              let px = rng.Next(0, chunkCells - 6)
              let py = groundY - 1 - row * 2 + rng.Next(0, 2)
              let pw = rng.Next(2, 6)

              if py >= 0 then
                section
                |> Layout.section px py (Platformer.platform pw Platform)
                |> ignore

          section)
        grid
      |> ignore

    elif archetype < 95 then
      Layout.run
        (fun section ->
          section
          |> Layout.section 0 groundY (fun groundSection ->
            groundSection |> Platformer.platform chunkCells Ground |> ignore
            let spikeRow = groundY - 1

            for x in 0 .. chunkCells - 1 do
              if rng.Next(8) = 0 then
                Layout.set x spikeRow Spikes groundSection |> ignore

            groundSection)
          |> ignore

          let platCount = rng.Next(1, 3)

          for _ in 1..platCount do
            let px = rng.Next(0, chunkCells - 6)
            let py = rng.Next(groundY - 3, groundY - 1)
            let pw = rng.Next(3, 6)

            section
            |> Layout.section px py (Platformer.platform pw Platform)
            |> ignore

            for cx in 0 .. pw - 1 do
              if rng.Next(4) = 0 then
                Layout.set (px + cx) (py - 1) Coin section |> ignore

          section)
        grid
      |> ignore

    else
      Layout.run
        (fun section ->
          section
          |> Layout.section 0 groundY (fun groundSection ->
            groundSection |> Platformer.platform chunkCells Ground |> ignore
            let pitCount = rng.Next(1, 3)

            for _ in 1..pitCount do
              let px = rng.Next(spawnProtectedCells, chunkCells - 5)
              let pw = rng.Next(2, 4)

              groundSection
              |> Layout.section px 0 (Platformer.pit pw 1)
              |> ignore

            groundSection)
          |> ignore

          let platCount = rng.Next(3, 7)

          for _ in 1..platCount do
            let px = rng.Next(0, chunkCells - 6)
            let py = rng.Next(groundY - 4, groundY - 1)
            let pw = rng.Next(3, 6)

            section
            |> Layout.section px py (Platformer.platform pw Platform)
            |> ignore

            for cx in 0 .. pw - 1 do
              if rng.Next(2) = 0 then
                Layout.set (px + cx) (py - 1) Coin section |> ignore

          section)
        grid
      |> ignore

    let flagX = rng.Next(2, chunkCells - 2)

    Layout.run (fun s -> s |> Layout.set flagX (groundY - 1) Flag) grid
    |> ignore

  elif cy < 0 then
    let archetype = rng.Next(100)

    if archetype >= 30 && archetype < 80 then
      for _ in 1 .. rng.Next(1, 4) do
        let px = rng.Next(0, chunkCells - 8)
        let py = rng.Next(chunkCells - 8, chunkCells - 2)
        let pw = rng.Next(3, 8)

        Layout.run
          (fun s -> s |> Layout.section px py (Platformer.platform pw Platform))
          grid
        |> ignore

        Layout.run
          (fun s -> s |> Layout.set (px + rng.Next(0, pw)) (py - 1) Coin)
          grid
        |> ignore
    elif archetype >= 80 then
      for _ in 1 .. rng.Next(1, 3) do
        let px = rng.Next(2, chunkCells - 4)
        let ph = rng.Next(2, 5)
        let py = chunkCells - ph - 1

        for dy in 0 .. ph - 1 do
          if py + dy < chunkCells then
            Layout.run (fun s -> s |> Layout.set px (py + dy) Platform) grid
            |> ignore

        if py - 1 >= 0 then
          Layout.run
            (fun s ->
              s
              |> Layout.section
                (px - 1)
                (py - 1)
                (Platformer.platform (rng.Next(3, 6)) Platform))
            grid
          |> ignore

  else
    let archetype = rng.Next(100)

    if archetype < 70 then
      Layout.run
        (fun section ->
          section |> Platformer.platform chunkCells Ground |> ignore

          for _ in 1 .. rng.Next(2, 5) do
            let gx = rng.Next(1, chunkCells - 6)
            let gy = rng.Next(1, chunkCells - 4)
            let gw = rng.Next(3, 6)
            let gh = rng.Next(2, 4)
            section |> Layout.section gx gy (Platformer.gap gw gh) |> ignore

          for _ in 1 .. rng.Next(1, 4) do
            let cx = rng.Next(1, chunkCells - 2)
            let cy = rng.Next(1, chunkCells - 2)

            match CellGrid2D.get cx cy grid with
            | ValueSome Ground ->
              match CellGrid2D.get cx (cy - 1) grid with
              | ValueNone ->
                Layout.run (fun s -> s |> Layout.set cx (cy - 1) Coin) grid
                |> ignore
              | _ -> ()
            | _ -> ()

          section)
        grid
      |> ignore
    else
      Layout.run
        (fun section ->
          section |> Platformer.platform chunkCells Ground |> ignore

          for _ in 1 .. rng.Next(3, 7) do
            let px = rng.Next(0, chunkCells - 6)
            let py = rng.Next(2, chunkCells - 3)
            let pw = rng.Next(3, 7)

            section
            |> Layout.section px py (Platformer.platform pw Platform)
            |> ignore

          section)
        grid
      |> ignore

  {
    Grid = grid
    Platforms = extractPlatforms grid
    Spikes = collectTiles grid (fun t -> t = Spikes)
    Coins = collectTiles grid (fun t -> t = Coin)
    Flags = collectTiles grid (fun t -> t = Flag)
    Occluders =
      generateOccluders
        (fun t -> t = Platform)
        (Edge.Bottom ||| Edge.Left ||| Edge.Right)
        grid
    Torches = extractTorches grid rng
    Bounds = {
      X = origin.X
      Y = origin.Y
      Width = chunkWorldSize
      Height = chunkWorldSize
    }
    Biome = biome
  }

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
