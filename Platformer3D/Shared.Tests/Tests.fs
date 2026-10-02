module Platformer3D.Shared.Tests.Tests

open Expecto
open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Mibo.Layout
open Platformer3D.Constants
open Platformer3D.Types
open Platformer3D.BlockData
open Platformer3D.Level
open Platformer3D.Physics
open Platformer3D.WorldGen
open Platformer3D.DayNight
open Platformer3D.Particles

/// What one X slice of a chunk holds: `ValueSome (zMin, zMax, height)`
/// when the slice is one contiguous Z run of ground at a single flat
/// height — a lane band — and `ValueNone` when the slice holds nothing
/// (a gap you jump) or broken ground (mixed heights, interrupted band).
let private laneSlice
  (chunk: Chunk)
  (x: int)
  : struct (int * int * int) voption =
  let mutable zMin = -1
  let mutable zPrev = -1
  let mutable height = 0
  let mutable broken = false

  for z = 0 to chunkDepth - 1 do
    match CellGrid2D.get x z chunk.Terrain with
    | ValueSome col when not(TerrainColumn.isPit col) ->
      if zMin < 0 then
        zMin <- z
        height <- col.Height
      elif z <> zPrev + 1 || col.Height <> height then
        broken <- true

      zPrev <- z
    | _ -> ()

  if zMin < 0 || broken then
    ValueNone
  else
    ValueSome struct (zMin, zPrev, height)

[<Tests>]
let tests =
  testList "Platformer3D.Shared" [
    // ── The lane is solid underfoot ──

    test "every slice of the course is one flat lane band or a gap" {
      // The fall-through bug this suite guards: no holes inside standing
      // ground, no height mixes inside a band. Walking every slice, the
      // populated cells must form one contiguous Z run at one height —
      // or not exist at all (a gap you jump).
      for cx in 0..2 do
        let chunk = generateChunk cx 0 42

        for x = 0 to chunkWidth - 1 do
          let mutable zMin = -1
          let mutable zMax = -1
          let mutable count = 0
          let mutable height = 0
          let mutable mixed = false

          for z = 0 to chunkDepth - 1 do
            match CellGrid2D.get x z chunk.Terrain with
            | ValueSome col when not(TerrainColumn.isPit col) ->
              if count = 0 then
                height <- col.Height
              elif col.Height <> height then
                mixed <- true

              if zMin < 0 then
                zMin <- z

              zMax <- z
              count <- count + 1
            | _ -> ()

          if count > 0 then
            Expect.equal
              (zMax - zMin + 1)
              count
              $"chunk {cx},0 slice {x}: ground is one contiguous band, no interior holes"

            Expect.isFalse
              mixed
              $"chunk {cx},0 slice {x}: one height per band, no mixes"
    }

    test "every gap the course asks you to jump is clearable" {
      // Walk the course like a runner: a run of empty slices between two
      // lane slices is a jump; its width plus the next band's rise must
      // sit inside the jump budget the generator obeys.
      let chunk = generateChunk 2 0 42
      let mutable x = 0

      while x < chunkWidth do
        match laneSlice chunk x with
        | ValueSome _ -> x <- x + 1
        | ValueNone ->
          let start = x

          while x < chunkWidth && (laneSlice chunk x).IsNone do
            x <- x + 1

          match laneSlice chunk (start - 1), laneSlice chunk x with
          | ValueSome struct (_, _, fromH), ValueSome struct (_, _, toH) ->
            let width = x - start
            let rise = toH - fromH

            Expect.isTrue
              (reachable3D (float32 width) (float32 rise))
              $"a {width}-cell gap rising {rise} must be inside the jump budget"
          | _ -> ()
    }

    test "the course continues across chunk seams" {
      // Leaving a chunk must land on the neighbor's opening band, at the
      // same height and the same Z — the run never dead-ends at a seam.
      for cx in 1..2 do
        let left = generateChunk cx 0 42
        let right = generateChunk (cx + 1) 0 42
        let exitSlice = laneSlice left (chunkWidth - 1)
        let entrySlice = laneSlice right 0

        match exitSlice, entrySlice with
        | ValueSome struct (zMinL, zMaxL, hL),
          ValueSome struct (zMinR, zMaxR, hR) ->
          Expect.equal
            (zMinL, zMaxL, hL)
            (zMinR, zMaxR, hR)
            $"seam after chunk {cx},0 must continue the lane"
        | _ -> failtest "both edges of a seam must be solid lane"
    }

    test "the course snakes and changes height" {
      // The path bends in Z across chunks and runs at more than one
      // height — a course, not a ruler.
      let centers = HashSet<int>()
      let heights = HashSet<int>()

      for cx in 0..3 do
        let chunk = generateChunk cx 0 42

        for x = 0 to chunkWidth - 1 do
          match laneSlice chunk x with
          | ValueSome struct (zMin, zMax, h) ->
            centers.Add((zMin + zMax) / 2) |> ignore
            heights.Add h |> ignore
          | ValueNone -> ()

      Expect.isGreaterThan
        centers.Count
        1
        "the lane should drift in Z across chunks"

      Expect.isGreaterThan
        heights.Count
        1
        "the course should run at more than one height"
    }

    test "you spawn on flat, safe ground" {
      let chunk = generateChunk 0 0 42

      match Flow.tryPosition "spawn" chunk.Marks with
      | ValueNone -> failtest "the spawn strip is named"
      | ValueSome rect ->
        Expect.equal 0 rect.X "the spawn strip opens its chunk"

        for x = rect.X to rect.X + rect.W - 1 do
          match laneSlice chunk x with
          | ValueSome struct (_, _, h) ->
            Expect.equal
              h
              groundY3D
              "the spawn strip is flat at the base height"
          | ValueNone -> failtest "the spawn strip must be solid"

        for x = rect.X to rect.X + rect.W - 1 do
          for z = rect.Y to rect.Y + rect.H - 1 do
            match CellGrid2D.get x z chunk.Props with
            | ValueSome { Prop = Prop.Hazard _ } ->
              failtest "no hazard may sit inside the spawn strip"
            | _ -> ()
    }

    test "every course chunk ends on its checkpoint" {
      for cx in 0..2 do
        let chunk = generateChunk cx 0 42

        Expect.isTrue
          (not(List.isEmpty(Flow.taggedRects "checkpoint" chunk.Marks)))
          $"chunk {cx},0 reports a checkpoint"

        Expect.isTrue
          (laneSlice chunk (chunkWidth - 1)).IsSome
          "the far edge must be solid lane to stand the flag on"
    }

    test "the world is one course: other chunk rows are void" {
      for cx in 0..1 do
        for cz in [ -1; 1 ] do
          let chunk = generateChunk cx cz 42
          let mutable ground = 0
          let mutable props = 0

          chunk.Terrain |> CellGrid2D.iter(fun _ _ _ -> ground <- ground + 1)

          chunk.Props |> CellGrid2D.iter(fun _ _ _ -> props <- props + 1)

          Expect.equal 0 ground $"chunk {cx},{cz} must hold no ground"
          Expect.equal 0 props $"chunk {cx},{cz} must hold no props"
    }

    test "both biome kits appear along the course" {
      let materials = HashSet<Biome3D>()

      for cx in 0..3 do
        let chunk = generateChunk cx 0 42

        chunk.Slabs
        |> CellGrid2D.iter(fun _ _ tile ->
          materials.Add tile.Material |> ignore)

      Expect.equal
        2
        materials.Count
        "the course should run through grass and snow"
    }

    // ── The set pieces exist where the landmarks say ──

    test "gap platforms are single objects spanning their gap" {
      // One platform piece = ONE tile = ONE scaled instance (never tiled
      // blocks). It anchors one cell before the gap and spans the gap plus
      // both landing cells.
      for cx in 0..3 do
        let chunk = generateChunk cx 0 42
        let gaps = Flow.taggedRects "gap" chunk.Marks
        let mutable platforms = 0

        chunk.Props
        |> CellGrid2D.iter(fun x z tile ->
          match tile.Prop with
          | Prop.Platform length ->
            platforms <- platforms + 1

            let overAGap =
              gaps
              |> List.exists(fun r ->
                x = r.X - 1 && length = r.W + 2 && z >= r.Y && z < r.Y + r.H)

            Expect.isTrue
              overAGap
              $"chunk {cx},0: platform at ({x},{z}) must anchor before a gap at full span"
          | _ -> ())

        Expect.isLessThanOrEqual
          platforms
          gaps.Length
          $"chunk {cx},0: at most one platform object per gap"
    }

    test "some gaps carry platforms somewhere along the course" {
      let mutable platforms = 0

      for cx in 0..3 do
        let chunk = generateChunk cx 0 42

        chunk.Props
        |> CellGrid2D.iter(fun _ _ tile ->
          match tile.Prop with
          | Prop.Platform _ -> platforms <- platforms + 1
          | _ -> ())

      Expect.isGreaterThan
        platforms
        0
        "some gaps should carry a platform object"
    }

    test "pickups trace the course and reward the spawn" {
      let mutable anyPickup = false

      for cx in 0..3 do
        let chunk = generateChunk cx 0 42

        chunk.Pickups
        |> CellGrid2D.iter(fun _ _ tile ->
          match tile.Prop with
          | Prop.Pickup _ -> anyPickup <- true
          | _ -> ())

      let mutable spawnGold = false
      let spawnChunk = generateChunk 0 0 42

      spawnChunk.Pickups
      |> CellGrid2D.iter(fun _ _ tile ->
        match tile.Prop with
        | Prop.Pickup PickupKind.Gold -> spawnGold <- true
        | _ -> ())

      Expect.isTrue anyPickup "coins should trace the course"
      Expect.isTrue spawnGold "the spawn ring should offer gold"
    }

    test "each platform renders as ONE slab: full coverage, exact size" {
      // The one-model-per-platform invariant: every populated cell is
      // covered by exactly one slab whose rectangle spans it, whose height
      // includes the crust cell (height + 1, matching what you stand on),
      // and whose material matches. No slab over void.
      for cx in 0..1 do
        let chunk = generateChunk cx 0 42
        let w = chunkWidth
        let d = chunkDepth

        let populated (x: int) (z: int) =
          match CellGrid2D.get x z chunk.Terrain with
          | ValueSome col when not(TerrainColumn.isPit col) ->
            ValueSome(col.Height, col.Material)
          | _ -> ValueNone

        let rects = ResizeArray<struct (int * int * SlabTile)>()

        chunk.Slabs
        |> CellGrid2D.iter(fun x z tile -> rects.Add struct (x, z, tile))

        Expect.isGreaterThan
          rects.Count
          0
          "a course chunk must have platform slabs"

        let coverage = Array.zeroCreate<int>(w * d)

        for struct (rx, rz, t) in rects do
          for zz = rz to rz + t.D - 1 do
            for xx = rx to rx + t.W - 1 do
              coverage[zz * w + xx] <- coverage[zz * w + xx] + 1

        for x = 0 to w - 1 do
          for z = 0 to d - 1 do
            match populated x z with
            | ValueSome(h, material) ->
              Expect.equal
                coverage[z * w + x]
                1
                $"chunk {cx},0: populated cell ({x},{z}) must be covered by exactly one slab"

              let mutable slab = ValueNone

              for struct (rx, rz, t) in rects do
                if x >= rx && x < rx + t.W && z >= rz && z < rz + t.D then
                  slab <- ValueSome t

              match slab with
              | ValueSome t ->
                Expect.equal
                  t.H
                  (h + 1)
                  $"chunk {cx},0: slab height must include the crust cell at ({x},{z})"

                Expect.equal
                  t.Material
                  material
                  $"chunk {cx},0: slab material must match the column at ({x},{z})"
              | ValueNone -> failtest "covered count 1 but no slab found"
            | ValueNone ->
              Expect.equal
                coverage[z * w + x]
                0
                $"chunk {cx},0: no slab may float over void at ({x},{z})"
    }

    // ── Colliders cover what you see ──

    test "ground west of the spawn chunk resolves to physics" {
      // The world continues for x < 0; walking west must land on it, not
      // fall through. Floor-division chunk lookups resolve negative cells.
      let chunks = ConcurrentDictionary<struct (int * int), Chunk>()

      let west = generateChunk -1 0 42
      chunks[struct (-1, 0)] <- west

      let origin = west.Terrain.Origin

      west.Terrain
      |> CellGrid2D.iter(fun x z col ->
        let wx = int origin.X + x
        let wz = int origin.Y + z

        Expect.equal
          (columnAt chunks wx wz)
          (ValueSome col)
          $"column ({wx},{wz}) must resolve into chunk (-1,0)")
    }

    test "resolveCollision grounds the player on x<0 ground" {
      let chunks = ConcurrentDictionary<struct (int * int), Chunk>()

      let west = generateChunk -1 0 42
      chunks[struct (-1, 0)] <- west
      chunks[struct (0, 0)] <- generateChunk 0 0 42

      // Stand on the first populated column of the west chunk.
      let origin = west.Terrain.Origin
      let mutable spot = ValueNone

      west.Terrain
      |> CellGrid2D.iter(fun x z col ->
        match spot with
        | ValueNone when not(TerrainColumn.isPit col) && col.Height > 0 ->
          spot <-
            ValueSome(
              Vector3(
                origin.X + float32 x * cellSize + cellSize * 0.5f,
                float32(TerrainColumn.surfaceCells col) * cellSize + 0.5f,
                origin.Y + float32 z * cellSize + cellSize * 0.5f
              )
            )
        | _ -> ())

      match spot with
      | ValueNone -> failtest "the west chunk should hold solid ground"
      | ValueSome start ->
        // The frame that crosses the surface: feet move from half a cell
        // above it down onto it.
        let landed = Vector3(start.X, start.Y - 0.5f, start.Z)

        let struct (pos, _, grounded, _) =
          resolveCollision start landed Vector3.Zero chunks

        Expect.isTrue grounded "the player must ground west of the spawn chunk"

        Expect.isTrue
          (pos.Y > 0.0f && pos.Y <= start.Y)
          "feet rest on the surface, not the void"
    }

    test "a plain block's collider covers its whole cell" {
      // The seam-sealing property: standing anywhere on a block's top,
      // including its edges, is ground. Corner-anchored AABBs broke this.
      let info = capInfo Grass Block
      let struct (ew, _, ed) = capExtents Block

      Expect.isTrue
        (info.CenterOffsetX - ew * 0.5f <= 0.0f)
        "collider reaches the cell's west edge"

      Expect.isTrue
        (info.CenterOffsetX + ew * 0.5f >= 1.0f)
        "collider reaches the cell's east edge"

      Expect.isTrue
        (info.CenterOffsetZ - ed * 0.5f <= 0.0f)
        "collider reaches the cell's north edge"

      Expect.isTrue
        (info.CenterOffsetZ + ed * 0.5f >= 1.0f)
        "collider reaches the cell's south edge"
    }

    test "a 2x2 cap's collider spans both of its cells" {
      let info = capInfo Grass Large
      let struct (ew, _, ed) = capExtents Large

      Expect.isTrue
        (info.CenterOffsetX - ew * 0.5f <= 0.0f)
        "collider reaches the footprint's near edge"

      Expect.isTrue
        (info.CenterOffsetX + ew * 0.5f >= 2.0f * cellSize)
        "collider reaches the footprint's far edge"
    }

    test "a gap platform's collider spans its whole length" {
      // A 3-cell platform anchored at a cell must be solid from its anchor
      // corner across all three cells — one object, one box, no seams.
      let info = propInfo(Prop.Platform 3)
      let struct (ew, _, ed) = propExtents(Prop.Platform 3)

      Expect.isTrue
        (info.CenterOffsetX - ew * 0.5f <= 0.0f)
        "collider reaches the platform's near edge"

      Expect.isTrue
        (info.CenterOffsetX + ew * 0.5f >= 3.0f * cellSize)
        "collider reaches the platform's far edge"
    }

    // ── The course holds together for any seed ──

    test "every chunk ends on solid checkpoint lane, for any seed" {
      // The program seeds the world randomly per launch; the segment
      // builder must guarantee the exit runway for ALL of them, not just
      // the seed the tests happen to like.
      for seed in 0..19 do
        for cx in 0..2 do
          let chunk = generateChunk cx 0 seed

          Expect.isTrue
            (laneSlice chunk (chunkWidth - 1)).IsSome
            $"seed {seed}, chunk {cx},0: the far edge must be solid lane"

          Expect.isTrue
            (not(List.isEmpty(Flow.taggedRects "checkpoint" chunk.Marks)))
            $"seed {seed}, chunk {cx},0: a checkpoint must exist"
    }

    // ── WorldGen plumbing ──

    test "WorldGen.Chunks.init creates empty state" {
      let model = Chunks.init 42
      Expect.equal 0 model.Chunks.Count "no chunks at init"
      Expect.equal 42 model.Seed "seed stored"
    }

    // ── DayNight / Particles behavior ──

    test "DayNight.getSkyColor is dark at midnight" {
      let c = getSkyColor 0.0f

      Expect.isTrue
        (c.R < 30uy && c.G < 30uy && c.B < 40uy)
        "midnight should be dark"
    }

    test "DayNight.getSkyColor is blue at noon" {
      let c = getSkyColor 12.0f
      Expect.isTrue (c.B > 100uy) "noon should be blueish"
    }

    test "Particles.spawnConfetti adds particles" {
      let model = init()
      let model' = update (SpawnConfetti(Vector3.Zero)) model
      Expect.isTrue (model'.Count > 0) "confetti should spawn particles"
    }

    test "Particles.Tick fades particles" {
      let model = update (SpawnConfetti(Vector3.Zero)) (init())
      let countBefore = model.Count

      for _ in 0..100 do
        update (Tick 0.1f) model |> ignore

      Expect.isTrue
        (model.Count < countBefore)
        "particles should fade over time"
    }
  ]
