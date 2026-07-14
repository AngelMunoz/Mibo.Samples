module Platformer.Tests.ReachabilityTests

open Expecto
open Platformer.WorldGen

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
  ]
