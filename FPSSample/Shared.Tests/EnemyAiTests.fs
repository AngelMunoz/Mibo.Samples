module FPSSample.Tests.EnemyAiTests

open Expecto
open System.Numerics
open Mibo.Layout3D
open FPSSample
open FPSSample.Types
open FPSSample.EnemyAi

[<Tests>]
let tests =
  testList "EnemyAi" [

    testList "xzDistance" [
      testCase "distance between same point is 0"
      <| fun _ ->
        let p = Vector3(1.0f, 99.0f, 2.0f) // Y should be ignored

        Expect.floatClose
          Accuracy.veryHigh
          (float(xzDistance p p))
          0.0
          "Distance is 0"

      testCase "distance ignores Y"
      <| fun _ ->
        let a = Vector3(0.0f, 0.0f, 0.0f)
        let b = Vector3(3.0f, 100.0f, 4.0f)

        Expect.floatClose
          Accuracy.low
          (float(xzDistance a b))
          5.0
          "3-4-5 triangle in XZ"
    ]

    testList "xzDirection" [
      testCase "direction from origin to +X is normalized"
      <| fun _ ->
        let dir = xzDirection Vector3.Zero (Vector3(10.0f, 0.0f, 0.0f))
        Expect.floatClose Accuracy.veryHigh (float dir.X) 1.0 "X ~ 1"
        Expect.floatClose Accuracy.veryHigh (float dir.Z) 0.0 "Z ~ 0"

      testCase "direction to same point is zero"
      <| fun _ ->
        let dir = xzDirection Vector3.Zero Vector3.Zero
        Expect.equal dir Vector3.Zero "Zero direction"
    ]

    testList "Enemy states" [
      testCase "idle enemy far from player stays idle"
      <| fun _ ->
        let enemies = [| Enemy.create(Vector3(100.0f, 0.0f, 100.0f)) |]

        let damage =
          update 0.016f (Vector3(0.0f, 0.0f, 0.0f)) 100.0f enemies [||]

        Expect.equal damage 0.0f "No damage from idle enemy"
        Expect.equal enemies[0].State EnemyState.Idle "Still idle"

      testCase "enemy within activation range starts chasing"
      <| fun _ ->
        let enemies = [| Enemy.create(Vector3(10.0f, 0.0f, 0.0f)) |]

        let _ = update 0.016f (Vector3(0.0f, 0.0f, 0.0f)) 100.0f enemies [||]

        Expect.equal enemies[0].State EnemyState.Chasing "Started chasing"

      testCase "enemy within attack range attacks"
      <| fun _ ->
        let enemies = [| Enemy.create(Vector3(1.0f, 0.0f, 0.0f)) |]

        let damage =
          update 0.016f (Vector3(0.0f, 0.0f, 0.0f)) 100.0f enemies [||]

        Expect.equal enemies[0].State EnemyState.Attacking "In attack range"
        Expect.equal damage Constants.EnemyAttackDamage "Dealt attack damage"

      testCase "dead enemy respawns after timer"
      <| fun _ ->
        let mutable enemy = Enemy.create(Vector3(5.0f, 0.0f, 0.0f))
        enemy.State <- EnemyState.Dead
        enemy.RespawnTimer <- 0.1f
        let enemies = [| enemy |]

        let _ = update 0.2f (Vector3(0.0f, 0.0f, 0.0f)) 100.0f enemies [||]

        Expect.equal enemies[0].State EnemyState.Idle "Respawned as idle"

        Expect.equal
          enemies[0].Health
          Constants.EnemyMaxHealth
          "Health restored"

      testCase "enemy attack respects cooldown"
      <| fun _ ->
        let mutable enemy = Enemy.create(Vector3(1.0f, 0.0f, 0.0f))
        enemy.State <- EnemyState.Attacking
        enemy.AttackCooldown <- 0.5f // still on cooldown
        let enemies = [| enemy |]

        let damage =
          update 0.016f (Vector3(0.0f, 0.0f, 0.0f)) 100.0f enemies [||]

        Expect.equal damage 0.0f "No damage during cooldown"
    ]

    testList "Enemy wall collision" [
      testCase "enemy is pushed out of collider"
      <| fun _ ->
        let mutable enemy = Enemy.create(Vector3(1.2f, 0.0f, 0.0f))
        enemy.State <- EnemyState.Idle
        let enemies = [| enemy |]

        let colliders = [|
          {
            Min = Vector3(-1.0f, -1.0f, -1.0f)
            Max = Vector3(1.0f, 1.0f, 1.0f)
          }
        |]

        let _ =
          update 0.016f (Vector3(100.0f, 0.0f, 0.0f)) 100.0f enemies colliders

        // Enemy radius is 0.4, so it overlaps the face at X=1. Should be pushed past 1.0+radius
        Expect.isGreaterThan
          enemies[0].Position.X
          1.0f
          "Pushed past collider face"
    ]
  ]
