module Platformer3D.Shared.Tests.Tests

open Expecto
open System
open System.Numerics
open Mibo.Layout3D
open Platformer3D.Constants
open Platformer3D.Types
open Platformer3D.BlockData
open Platformer3D.WorldGen
open Platformer3D.DayNight
open Platformer3D.Particles

[<Tests>]
let tests =
  testList "Platformer3D.Shared" [
    test "WorldGen.generateChunk produces non-empty grid" {
      let chunk = generateChunk 0 0 42
      let terrainGrid, _ = LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

      let mutable count = 0

      CellGrid3D.iterVolume
        chunk.Bounds
        (fun _ _ _ bt ->
          if bt <> BlockType.Empty then
            count <- count + 1)
        terrainGrid

      Expect.isTrue (count > 0) "Chunk should have non-empty blocks"
    }

    test "WorldGen.Chunks.init creates empty state" {
      let model = Chunks.init 42
      Expect.equal 0 model.Chunks.Count "No chunks at init"
      Expect.equal 42 model.Seed "Seed stored"
    }

    // ── Reachability predicate tests ──

    test "arcHeight3D is zero at distance 0" {
      let h = arcHeight3D 0.0f
      Expect.floatClose Accuracy.high (float h) 0.0 "h(0) ~ 0"
    }

    test "arcHeight3D returns to zero at max level gap" {
      let h = arcHeight3D maxLevelGap3D
      Expect.floatClose Accuracy.low (float h) 0.0 "h(max) ~ 0"
    }

    test "arcHeight3D apex (~4.8 cells) clears 3 but not 4" {
      let h = arcHeight3D 4.8f
      Expect.isGreaterThan h 3.0f "apex > 3"
      Expect.isLessThan h 4.0f "apex < 4"
    }

    test "maxLevelGap3D is ~9.6 cells" {
      Expect.isGreaterThan maxLevelGap3D 9.0f "max gap > 9"
      Expect.isLessThan maxLevelGap3D 10.0f "max gap < 10"
    }

    test "reachable3D: flat gap within budget is reachable" {
      Expect.isTrue (reachable3D 4.0f 0.0f) "reachable(4,0)"
    }

    test "reachable3D: gap beyond max range is unreachable" {
      Expect.isFalse (reachable3D 10.0f 0.0f) "reachable(10,0) false"
    }

    test "reachable3D: budget corner (4 across, 3 up) is reachable" {
      Expect.isTrue (reachable3D 4.0f 3.0f) "reachable(4,3)"
    }

    test "reachable3D: cannot rise 5 cells straight up" {
      Expect.isFalse (reachable3D 1.0f 5.0f) "reachable(1,5) false"
    }

    test "JumpBudget3D stays inside the physics envelope" {
      let budget = defaultConfig3D.JumpBudget

      Expect.isTrue
        (reachable3D (float32 budget.MaxHorizontalCells) 0.0f)
        "max horizontal gap is clearable"
    }

    // ── Collider extent tests ──

    test "colliderExtents: Block snaps to cellSize" {
      let struct (w, h, d) = colliderExtents(Block Grass)
      Expect.equal w cellSize "Block width = cellSize"
      Expect.equal h cellSize "Block height = cellSize"
      Expect.equal d cellSize "Block depth = cellSize"
    }

    test "colliderExtents: LargeBlock uses mesh extents" {
      let struct (w, h, d) = colliderExtents(LargeBlock Snow)
      Expect.isGreaterThan w cellSize "LargeBlock width > cellSize"
      Expect.equal h cellSize "LargeBlock height = cellSize (snapped)"
      Expect.isGreaterThan d cellSize "LargeBlock depth > cellSize"
    }

    test "colliderExtents: TallBlock uses mesh height" {
      let struct (w, h, d) = colliderExtents(TallBlock Grass)
      Expect.isGreaterThan h cellSize "TallBlock height > cellSize"
    }

    test "colliderExtents: Slope uses full cellSize height" {
      let struct (_, h, _) = colliderExtents(Slope(Grass, XPos))
      Expect.equal h cellSize "Slope height = cellSize (no ramp physics)"
    }

    test "colliderExtents: LowBlock keeps real extent" {
      let struct (_, h, _) = colliderExtents(LowBlock Grass)
      Expect.isLessThan h cellSize "LowBlock height < cellSize"
    }

    // ── Terrain integrity tests ──

    test "terrain elevation varies past spawn area" {
      let mutable anyChange = false

      for lx in defaultConfig3D.Terrain.SpawnProtectedRadius .. chunkWidth - 1 do
        for lz in defaultConfig3D.Terrain.SpawnProtectedRadius .. chunkDepth - 1 do
          let worldX = lx
          let worldZ = lz

          let h =
            elevationAt
              worldX
              worldZ
              42
              defaultConfig3D.Terrain.ElevationScale
              defaultConfig3D.Terrain.ElevationAmplitude
              defaultConfig3D.Terrain.SpawnProtectedRadius

          if h <> groundY3D then
            anyChange <- true

      Expect.isTrue anyChange "terrain should vary past spawn area"
    }

    test "spawn area is flat at groundY3D" {
      let spawnRadius = defaultConfig3D.Terrain.SpawnProtectedRadius
      let radiusSq = float32(spawnRadius * spawnRadius)
      let spX = int spawnPosition.X
      let spZ = int spawnPosition.Z

      for lx in
        max 0 (spX - spawnRadius) .. min (chunkWidth - 1) (spX + spawnRadius) do
        for lz in
          max 0 (spZ - spawnRadius) .. min (chunkDepth - 1) (spZ + spawnRadius) do
          let dx = float32(lx - spX)
          let dz = float32(lz - spZ)

          if dx * dx + dz * dz <= radiusSq then
            let h =
              elevationAt
                lx
                lz
                42
                defaultConfig3D.Terrain.ElevationScale
                defaultConfig3D.Terrain.ElevationAmplitude
                spawnRadius

            Expect.equal h groundY3D $"spawn area ({lx},{lz}) should be flat"
    }

    test "biome varies across chunks" {
      let mutable hasGrass = false
      let mutable hasSnow = false

      for cx in 0..2 do
        for cz in 0..2 do
          for lx in 0 .. chunkWidth - 1 do
            let worldX = cx * chunkWidth + lx
            let worldZ = cz * chunkDepth

            let b =
              biomeAt worldX worldZ 42 defaultConfig3D.Terrain.BiomeColumnScale

            match b with
            | Grass -> hasGrass <- true
            | Snow -> hasSnow <- true

      Expect.isTrue hasGrass "should have grass biome"
      Expect.isTrue hasSnow "should have snow biome"
    }

    test "terrain surface is connected (flood fill)" {
      // Generate a chunk and verify the surface is one connected region.
      let chunk = generateChunk 0 0 42
      let terrainGrid, _ = LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

      // Find the first solid surface cell near the spawn point
      let mutable startFound = false
      let mutable startX, startY, startZ = 0, 0, 0

      for x in 0 .. chunkWidth - 1 do
        for z in 0 .. chunkDepth - 1 do
          for y in chunkHeight - 1 .. -1 .. 0 do
            if not startFound then
              match CellGrid3D.get x y z terrainGrid with
              | ValueSome bt when isSolid bt ->
                startFound <- true
                startX <- x
                startY <- y
                startZ <- z
              | _ -> ()

      Expect.isTrue startFound "should find at least one solid cell"

      // Flood fill over 6-connected solid surface cells
      let filled =
        Grid3DSpatial.floodFill
          startX
          startY
          startZ
          (fun x y z ->
            match CellGrid3D.get x y z terrainGrid with
            | ValueSome bt -> isSolid bt
            | ValueNone -> false)
          terrainGrid

      // Count total solid cells
      let mutable total = 0

      CellGrid3D.iter
        (fun _ _ _ bt ->
          if bt <> BlockType.Empty then
            total <- total + 1)
        terrainGrid

      // The flood fill should reach a significant portion of solid cells.
      // Multi-cell blocks clear 3 cells and leave 1, so not every solid cell
      // is 6-connected to every other. We expect at least 50% connectivity.
      let threshold = total / 2

      Expect.isGreaterThan
        filled.Length
        threshold
        $"flood fill ({filled.Length}) should cover more than half of solid cells ({total})"
    }

    test "terrain has full volume fill (interior cells exist)" {
      let chunk = generateChunk 0 0 42
      let terrainGrid, _ = LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

      // Find a non-spawn column and check that cells below the surface are solid.
      let mutable foundInterior = false

      for x in 8 .. chunkWidth - 1 do
        for z in 8 .. chunkDepth - 1 do
          if not foundInterior then
            // Find the surface cell
            let mutable surfaceY = -1

            for y in chunkHeight - 1 .. -1 .. 0 do
              if surfaceY < 0 then
                match CellGrid3D.get x y z terrainGrid with
                | ValueSome bt when isSolid bt -> surfaceY <- y
                | _ -> ()

            // If surface is above y=0, check interior cell exists
            if surfaceY > 1 then
              match CellGrid3D.get x 0 z terrainGrid with
              | ValueSome bt -> foundInterior <- bt <> BlockType.Empty
              | ValueNone -> ()

      Expect.isTrue
        foundInterior
        "full volume fill should have solid interior cells"
    }

    test "terrain contains block variety beyond plain Block" {
      let mutable hasVariety = false

      for cx in 0..2 do
        for cz in 0..2 do
          let chunk = generateChunk cx cz 42

          let terrainGrid, _ =
            LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

          CellGrid3D.iter
            (fun _ _ _ bt ->
              if not hasVariety then
                match bt with
                | LargeBlock _
                | TallBlock _
                | LowBlock _
                | NarrowBlock _ -> hasVariety <- true
                | _ -> ())
            terrainGrid

      Expect.isTrue
        hasVariety
        "terrain should have multi-cell or sub-cell block variety"
    }

    test "terrain contains floating platforms" {
      let mutable hasPlatform = false

      for cx in 0..2 do
        for cz in 0..2 do
          let chunk = generateChunk cx cz 42

          let terrainGrid, _ =
            LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

          CellGrid3D.iter
            (fun _ _ _ bt ->
              if bt = Platform then
                hasPlatform <- true)
            terrainGrid

      Expect.isTrue
        hasPlatform
        "terrain should contain floating Platform blocks"
    }

    test "terrain contains gaps (not all columns are solid)" {
      // With gaps carved, some columns should be empty.
      // Check a chunk far from spawn where gaps are likely.
      let chunk = generateChunk 3 3 42
      let terrainGrid, _ = LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

      let mutable emptyColumns = 0

      for x in 0 .. chunkWidth - 1 do
        for z in 0 .. chunkDepth - 1 do
          let mutable hasSolid = false

          for y in 0 .. chunkHeight - 1 do
            if not hasSolid then
              match CellGrid3D.get x y z terrainGrid with
              | ValueSome bt when isSolid bt -> hasSolid <- true
              | _ -> ()

          if not hasSolid then
            emptyColumns <- emptyColumns + 1

      // With 2-4 gaps of radius 1-2, at least some columns should be empty.
      Expect.isTrue
        (emptyColumns > 0)
        $"expected some empty columns from gaps, got {emptyColumns}"
    }

    test "DayNight.getSkyColor is dark at midnight" {
      let c = getSkyColor 0.0f

      Expect.isTrue
        (c.R < 30uy && c.G < 30uy && c.B < 40uy)
        "Midnight should be dark"
    }

    test "DayNight.getSkyColor is blue at noon" {
      let c = getSkyColor 12.0f
      Expect.isTrue (c.B > 100uy) "Noon should be blueish"
    }

    test "Particles.spawnConfetti adds particles" {
      let model = init()
      let model' = update (SpawnConfetti(Vector3.Zero)) model
      Expect.isTrue (model'.Count > 0) "Confetti should spawn particles"
    }

    test "Particles.Tick fades particles" {
      let model = update (SpawnConfetti(Vector3.Zero)) (init())
      let countBefore = model.Count

      for _ in 0..100 do
        update (Tick 0.1f) model |> ignore

      Expect.isTrue
        (model.Count < countBefore)
        "Particles should fade over time"
    }

    // ── Slope surface tests ──
    // The slope mesh is centered on its footprint (CenterOffsetX/Z = cellSize/2),
    // so the analytical surface's footprint starts at cellWorldX + cellSize/2,
    // not at the cell corner. Player positions below are footprint-relative.

    test "slopeSurfaceY: XPos at low end returns worldY" {
      // Footprint is centered: spans [cellSize/2, cellSize/2 + run] on X and
      // [cellSize/2, cellSize/2 + width] on Z. Low end of the run + an interior Z.
      let px = cellSize * 0.5f
      let pz = cellSize * 0.5f + 1.0f
      let sy = slopeSurfaceY (Slope(Grass, XPos)) 0.0f 0.0f 0.0f px pz

      match sy with
      | ValueSome h ->
        Expect.floatClose Accuracy.high (float h) 0.0 "low end = worldY"
      | ValueNone -> failtest "should return a surface height"
    }

    test "slopeSurfaceY: XPos at high end returns worldY + rise" {
      let info = lookup(Slope(Grass, XPos))
      let run = info.ExtentW
      let rise = info.ExtentH
      // High end is at footprint start + run.
      let px = cellSize * 0.5f + run
      let pz = cellSize * 0.5f + 1.0f
      let sy = slopeSurfaceY (Slope(Grass, XPos)) 0.0f 0.0f 0.0f px pz

      match sy with
      | ValueSome h ->
        Expect.floatClose
          Accuracy.low
          (float h)
          (float rise)
          "high end = worldY + rise"
      | ValueNone -> failtest "should return a surface height"
    }

    test "slopeSurfaceY: XPos at midpoint returns half rise" {
      let info = lookup(Slope(Grass, XPos))
      let run = info.ExtentW
      let rise = info.ExtentH
      // Midpoint is at footprint start + run/2.
      let px = cellSize * 0.5f + run / 2.0f
      let pz = cellSize * 0.5f + 1.0f

      let sy = slopeSurfaceY (Slope(Grass, XPos)) 0.0f 0.0f 0.0f px pz

      match sy with
      | ValueSome h ->
        Expect.floatClose
          Accuracy.low
          (float h)
          (float(rise / 2.0f))
          "mid = rise/2"
      | ValueNone -> failtest "should return a surface height"
    }

    test "slopeSurfaceY: outside footprint returns ValueNone" {
      // px well before the centered footprint (which starts at cellSize/2).
      let sy = slopeSurfaceY (Slope(Grass, XPos)) 0.0f 0.0f 0.0f -1.0f 1.0f
      Expect.isTrue sy.IsNone "should be outside footprint"
    }

    test "slopeSurfaceY: non-slope returns ValueNone" {
      let sy = slopeSurfaceY (Block Grass) 0.0f 0.0f 0.0f 0.5f 0.5f
      Expect.isTrue sy.IsNone "non-slope should return ValueNone"
    }

    test "slopeSurfaceY: XNeg reverses rise direction" {
      let info = lookup(Slope(Grass, XNeg))
      let run = info.ExtentW
      let rise = info.ExtentH
      // Footprint is centered: spans [cellSize/2, cellSize/2 + run] on X.
      let lowX = cellSize * 0.5f
      let highX = cellSize * 0.5f + run
      let pz = cellSize * 0.5f + 1.0f

      // At the footprint's low end: should be HIGH (worldY + rise)
      let syHigh = slopeSurfaceY (Slope(Grass, XNeg)) 0.0f 0.0f 0.0f lowX pz

      // At the footprint's high end: should be LOW (worldY)
      let syLow = slopeSurfaceY (Slope(Grass, XNeg)) 0.0f 0.0f 0.0f highX pz

      match syHigh, syLow with
      | ValueSome hH, ValueSome hL ->
        Expect.floatClose
          Accuracy.low
          (float hH)
          (float rise)
          "XNeg low end is high"

        Expect.floatClose Accuracy.high (float hL) 0.0 "XNeg high end is low"
      | _ -> failtest "both should return surface heights"
    }
  ]
