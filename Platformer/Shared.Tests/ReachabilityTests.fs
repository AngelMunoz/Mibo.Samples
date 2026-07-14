module Platformer.Tests.ReachabilityTests

open Expecto
open System
open Platformer.WorldGen
open Platformer.Constants

/// Reachability predicate tests.
///
/// Values are derived from the physics constants in Platformer.Constants:
///   gravity = 2000, jumpSpeed = -1100, moveSpeed = 350, tileSize = 64.
///
///   apex distance  d*  = moveSpeed·|jumpSpeed|/gravity = 192px ≈ 3.0 tiles
///   apex height    h*  ≈ 302px ≈ 4.7 tiles
///   max level gap  dmax = 2·moveSpeed·|jumpSpeed|/gravity = 385px ≈ 6.0 tiles
///
/// The predicate models a fully-held running jump (max reach). These tests
/// pin the physics envelope so a constants change that silently breaks the
/// reachability guarantee fails CI.
[<Tests>]
let tests =
  testList "Reachability" [

    testList "arcHeightTiles" [
      testCase "launch height is zero at distance 0"
      <| fun _ ->
        Expect.floatClose
          Accuracy.high
          (float(arcHeightTiles 0.0f))
          0.0
          "h(0) ~ 0"

      testCase "arc returns to launch height at max same-level range"
      <| fun _ ->
        Expect.floatClose
          Accuracy.low
          (float(arcHeightTiles maxLevelGapTiles))
          0.0
          "h(maxLevelGap) ~ 0"

      testCase "arc is below launch past the max range"
      <| fun _ ->
        Expect.isLessThan
          (arcHeightTiles(maxLevelGapTiles + 1.0f))
          0.0f
          "h(max+1) < 0"

      testCase "apex (~3 tiles) clears 4 tiles but not 5"
      <| fun _ ->
        let h3 = arcHeightTiles 3.0f
        Expect.isGreaterThan h3 4.0f "h(3) > 4 tiles"
        Expect.isLessThan h3 5.0f "h(3) < 5 tiles"
    ]

    testList "maxLevelGapTiles" [
      testCase "is ~6 tiles for current physics"
      <| fun _ ->
        Expect.isGreaterThan maxLevelGapTiles 5.5f "max level gap > 5.5"
        Expect.isLessThan maxLevelGapTiles 6.5f "max level gap < 6.5"
    ]

    testList "reachable" [
      testCase "flat gap within budget (4 tiles) is reachable"
      <| fun _ -> Expect.isTrue (reachable 4.0f 0.0f) "reachable(4,0)"

      testCase "gap beyond max range is unreachable"
      <| fun _ -> Expect.isFalse (reachable 7.0f 0.0f) "reachable(7,0) false"

      testCase "budget corner (4 across, 3 up) is reachable"
      <| fun _ -> Expect.isTrue (reachable 4.0f 3.0f) "reachable(4,3)"

      testCase "cannot rise 5 tiles straight up"
      <| fun _ -> Expect.isFalse (reachable 0.0f 5.0f) "reachable(0,5) false"

      testCase "rise and gap share budget: 4 across + 5 up is unreachable"
      <| fun _ -> Expect.isFalse (reachable 4.0f 5.0f) "reachable(4,5) false"
    ]

    testList "config envelope" [
      testCase "JumpBudget stays inside the physics envelope"
      <| fun _ ->
        // The configured budget must remain reachable. Flat ground rises 0,
        // so the binding check is that the max configured gap is clearable.
        let budget = defaultConfig.JumpBudget

        Expect.isTrue
          (reachable (float32 budget.MaxHorizontalTiles) 0.0f)
          "configured max horizontal gap is clearable"
    ]

    testList "Ground reachability verification" [

      /// Helper: elevation closure for chunk `cx` with given amplitude.
      let makeElevation cx seed amplitude =
        let originTileX = cx * chunkCells

        fun lx ->
          elevationAtColumn
            (originTileX + lx)
            seed
            defaultConfig.ElevationScale
            amplitude

      /// Helper: surface Y of the next chunk's first slab (cross-seam target).
      let nextFirstSlabY cx seed amplitude =
        elevationAtColumn
          ((cx + 1) * chunkCells)
          seed
          defaultConfig.ElevationScale
          amplitude

      /// Helper: generate ground specs for a chunk, apply cross-seam clamp,
      /// and verify reachability.
      let verifyChunk cx seed amplitude =
        let rng =
          Random(defaultConfig.Ground.GetHashCode() ^^^ (seed * 100 + cx))

        let elev = makeElevation cx seed amplitude

        let nextY = nextFirstSlabY cx seed amplitude

        Ground.plan
          rng
          defaultConfig.Ground
          defaultConfig.JumpBudget
          chunkCells
          elev
        |> fun specs -> Ground.clampCrossSeam specs chunkCells nextY
        |> fun specs -> Ground.verifyReachability specs chunkCells nextY

      testCase "flat terrain (amplitude 0) has no violations"
      <| fun _ ->
        let violations = verifyChunk 0 42 0
        Expect.isEmpty violations "flat terrain should have no violations"

      testCase
        "spawn plateau is flat groundY for first spawnProtectedCells columns"
      <| fun _ ->
        // The player spawns at spawnX ≈ tile 3.1. Elevation must be flat
        // groundY there so the player never spawns inside terrain.
        for lx in 0 .. spawnProtectedCells - 1 do
          let y =
            elevationAtColumn
              lx
              42
              defaultConfig.ElevationScale
              defaultConfig.ElevationAmplitude

          Expect.equal y (int worldHeight) $"column {lx} should be flat groundY"

      testCase "elevation varies past the spawn plateau"
      <| fun _ ->
        let mutable anyChange = false

        for lx in spawnProtectedCells .. chunkCells - 1 do
          let y =
            elevationAtColumn
              lx
              42
              defaultConfig.ElevationScale
              defaultConfig.ElevationAmplitude

          if y <> int worldHeight then
            anyChange <- true

        Expect.isTrue anyChange "terrain should vary past the spawn plateau"

      testCase
        "default amplitude 2 has no violations across 500 chunks (after cross-seam clamp)"
      <| fun _ ->
        let mutable total = 0

        for seed in 1..50 do
          for cx in 0..9 do
            let violations =
              verifyChunk cx seed defaultConfig.ElevationAmplitude

            total <- total + violations.Length

        Expect.equal total 0 $"no violations across 500 chunks (got {total})"

      testCase
        "default amplitude 2 has no cross-seam violations across 1000 chunks"
      <| fun _ ->
        // Cross-seam edges were the weak point before clampCrossSeam.
        // This wider test confirms the clamp holds at scale.
        let mutable crossCount = 0

        for seed in 1..100 do
          for cx in 0..9 do
            let violations =
              verifyChunk cx seed defaultConfig.ElevationAmplitude

            crossCount <-
              crossCount
              + (violations |> Array.filter(fun v -> v.CrossSeam)).Length

        Expect.equal crossCount 0 $"cross-seam violations: {crossCount}"

      testCase "extreme amplitude 6 may still produce cross-seam violations"
      <| fun _ ->
        // With amplitude 6, the intra and cross reachable ranges may not
        // overlap (|prevY - nextY| can exceed the sum of both arc radii).
        // clampCrossSeam does its best, but the verifier correctly detects
        // cases it cannot fix — this is the value of having a verifier.
        let mutable foundUnfixable = false

        for seed in 1..100 do
          for cx in 0..20 do
            let violations = verifyChunk cx seed 6

            if violations |> Array.exists(fun v -> v.CrossSeam) then
              foundUnfixable <- true

        Expect.isTrue
          foundUnfixable
          "extreme amplitude should produce cross-seam violations that clamping cannot fix"
    ]
  ]
