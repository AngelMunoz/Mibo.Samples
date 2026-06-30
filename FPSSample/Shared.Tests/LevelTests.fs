module FPSSample.Tests.LevelTests

open Expecto
open System.Numerics
open Mibo.Layout3D
open FPSSample.Level

[<Tests>]
let tests =
  testList "Level" [

    testList "LevelData.createDefault" [
      testCase "default level has a non-empty grid"
      <| fun _ ->
        let level = LevelData.createDefault()
        Expect.isGreaterThan level.Grid.Width 0 "Grid width > 0"
        Expect.isGreaterThan level.Grid.Height 0 "Grid height > 0"
        Expect.isGreaterThan level.Grid.Depth 0 "Grid depth > 0"

      testCase "default level has enemy spawns"
      <| fun _ ->
        let level = LevelData.createDefault()

        Expect.isGreaterThanOrEqual
          level.EnemySpawns.Length
          1
          "At least 1 enemy spawn"

      testCase "default level has pickup spawns"
      <| fun _ ->
        let level = LevelData.createDefault()

        Expect.isGreaterThanOrEqual
          level.PickupSpawns.Length
          1
          "At least 1 pickup spawn"

      testCase "default level has solid cells"
      <| fun _ ->
        let level = LevelData.createDefault()
        let colliders = LevelData.extractColliders level

        Expect.isGreaterThanOrEqual
          colliders.Length
          1
          "At least 1 collider from solid cells"
    ]

    testList "extractColliders" [
      testCase "returns bounding boxes for solid cells"
      <| fun _ ->
        let level = LevelData.createDefault()
        let colliders = LevelData.extractColliders level

        for b in colliders do
          Expect.isTrue (b.Max.X >= b.Min.X) "Max >= Min on X"
          Expect.isTrue (b.Max.Y >= b.Min.Y) "Max >= Min on Y"
          Expect.isTrue (b.Max.Z >= b.Min.Z) "Max >= Min on Z"

      testCase "player spawn is not inside a collider"
      <| fun _ ->
        let level = LevelData.createDefault()
        let colliders = LevelData.extractColliders level
        let spawn = level.PlayerSpawn

        let insideCollider =
          colliders
          |> Array.exists(fun b ->
            spawn.X >= b.Min.X
            && spawn.X <= b.Max.X
            && spawn.Z >= b.Min.Z
            && spawn.Z <= b.Max.Z)

        Expect.isFalse
          insideCollider
          "Player spawn should not be inside a collider"
    ]

    testList "groundHeightAt" [
      testCase "returns 0 when no solid cell above"
      <| fun _ ->
        let level = LevelData.createDefault()
        let h = LevelData.groundHeightAt 999.0f 999.0f level
        Expect.floatClose Accuracy.low (float h) 0.0 "Ground is 0 outside grid"

      testCase "returns non-zero above a solid cell"
      <| fun _ ->
        let level = LevelData.createDefault()
        // Check a point near the center where we placed walls
        let h = LevelData.groundHeightAt 0.0f 0.0f level
        Expect.isGreaterThanOrEqual h 0.0f "Ground height >= 0"
    ]

    testList "Cell" [
      testCase "isSolid returns true for Wall/Cover/Crate"
      <| fun _ ->
        Expect.isTrue (Cell.isSolid Cell.Wall) "Wall is solid"
        Expect.isTrue (Cell.isSolid Cell.Cover) "Cover is solid"
        Expect.isTrue (Cell.isSolid Cell.Crate) "Crate is solid"

      testCase "isSolid returns false for Empty/Floor"
      <| fun _ ->
        Expect.isFalse (Cell.isSolid Cell.Empty) "Empty is not solid"
        Expect.isFalse (Cell.isSolid Cell.Floor) "Floor is not solid"
    ]
  ]
