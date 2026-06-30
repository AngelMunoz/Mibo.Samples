module FPSSample.Tests.CombatTests

open Expecto
open System.Numerics
open Mibo.Layout3D
open FPSSample
open FPSSample.Types
open FPSSample.Combat

[<Tests>]
let tests =
  testList "Combat" [

    testList "lookDirection" [
      testCase "yaw=0 pitch=0 looks toward -Z"
      <| fun _ ->
        let dir = lookDirection 0.0f 0.0f
        Expect.floatClose Accuracy.veryHigh (float dir.Z) -1.0 "Z ~ -1"
        Expect.floatClose Accuracy.veryHigh (float dir.X) 0.0 "X ~ 0"
        Expect.floatClose Accuracy.veryHigh (float dir.Y) 0.0 "Y ~ 0"

      testCase "positive pitch looks up"
      <| fun _ ->
        let dir = lookDirection 0.0f 0.5f
        Expect.isGreaterThan dir.Y 0.0f "Y > 0 when looking up"
    ]

    testList "rayVsAABB" [
      testCase "ray pointing at box hits"
      <| fun _ ->
        let bounds: BoundingBox = {
          Min = Vector3(0.0f, 0.0f, -5.0f)
          Max = Vector3(2.0f, 2.0f, -3.0f)
        }

        let origin = Vector3(1.0f, 1.0f, 0.0f)
        let dir = Vector3(0.0f, 0.0f, -1.0f)

        match rayVsAABB origin dir bounds with
        | ValueSome t ->
          Expect.floatClose Accuracy.low (float t) 3.0 "Hit distance ~3"
        | ValueNone -> failwith "Expected hit"

      testCase "ray pointing away from box misses"
      <| fun _ ->
        let bounds: BoundingBox = {
          Min = Vector3(0.0f, 0.0f, -5.0f)
          Max = Vector3(2.0f, 2.0f, -3.0f)
        }

        let origin = Vector3(1.0f, 1.0f, 0.0f)
        let dir = Vector3(0.0f, 0.0f, 1.0f)
        let result = rayVsAABB origin dir bounds
        Expect.isNone (result |> ValueOption.toOption) "Should miss"

      testCase "parallel ray outside box misses"
      <| fun _ ->
        let bounds: BoundingBox = {
          Min = Vector3(0.0f, 0.0f, 0.0f)
          Max = Vector3(2.0f, 2.0f, 2.0f)
        }

        let origin = Vector3(10.0f, 1.0f, 1.0f)
        let dir = Vector3(0.0f, 0.0f, -1.0f)
        let result = rayVsAABB origin dir bounds
        Expect.isNone (result |> ValueOption.toOption) "Should miss"
    ]

    testList "rayVsSphere" [
      testCase "ray through sphere center hits"
      <| fun _ ->
        let origin = Vector3(0.0f, 0.0f, 5.0f)
        let dir = Vector3(0.0f, 0.0f, -1.0f)
        let center = Vector3(0.0f, 0.0f, 0.0f)

        match rayVsSphere origin dir center 1.0f with
        | ValueSome t ->
          Expect.floatClose Accuracy.low (float t) 4.0 "Hit at t~4"
        | ValueNone -> failwith "Expected hit"

      testCase "ray missing sphere returns none"
      <| fun _ ->
        let origin = Vector3(10.0f, 10.0f, 5.0f)
        let dir = Vector3(0.0f, 0.0f, -1.0f)
        let center = Vector3(0.0f, 0.0f, 0.0f)
        let result = rayVsSphere origin dir center 1.0f
        Expect.isNone (result |> ValueOption.toOption) "Should miss"
    ]

    testList "handleShoot" [
      testCase "shooting with no ammo does nothing"
      <| fun _ ->
        let model = GameModel()
        model.Ammo <- 0
        model.Enemies <- [||]
        model.Colliders <- [||]
        let scoreBefore = model.Score

        handleShoot model

        Expect.equal model.Score scoreBefore "Score unchanged"
        Expect.equal model.Ammo 0 "Ammo unchanged"
        Expect.equal model.FireCooldown 0.0f "Cooldown not set"

      testCase "shooting during cooldown does nothing"
      <| fun _ ->
        let model = GameModel()
        model.Ammo <- 10
        model.FireCooldown <- 0.1f
        model.Enemies <- [||]
        model.Colliders <- [||]

        handleShoot model

        Expect.equal model.Ammo 10 "Ammo unchanged during cooldown"

      testCase "shooting consumes ammo and sets cooldown"
      <| fun _ ->
        let model = GameModel()
        model.Ammo <- 10
        model.Enemies <- [||]
        model.Colliders <- [||]

        handleShoot model

        Expect.equal model.Ammo 9 "Ammo consumed"
        Expect.isGreaterThan model.FireCooldown 0.0f "Cooldown set"
        Expect.isTrue model.MuzzleFlash.Active "Muzzle flash active"

      testCase "shooting enemy in line of sight deals damage"
      <| fun _ ->
        let model = GameModel()
        model.Ammo <- 10
        model.Colliders <- [||]
        model.PlayerPosition <- Vector3(0.0f, 0.0f, 0.0f)
        model.PlayerYaw <- 0.0f
        model.PlayerPitch <- 0.0f

        let enemy = Enemy.create(Vector3(0.0f, 0.0f, -5.0f))
        model.Enemies <- [| enemy |]

        handleShoot model

        Expect.isLessThan
          model.Enemies[0].Health
          Constants.EnemyMaxHealth
          "Enemy took damage"

      testCase "wall occludes enemy behind it"
      <| fun _ ->
        let model = GameModel()
        model.Ammo <- 10
        model.PlayerPosition <- Vector3(0.0f, 0.0f, 0.0f)
        model.PlayerYaw <- 0.0f
        model.PlayerPitch <- 0.0f

        // Wall between player and enemy
        model.Colliders <- [|
          {
            Min = Vector3(-1.0f, -1.0f, -3.0f)
            Max = Vector3(1.0f, 1.0f, -2.0f)
          }
        |]

        let enemy = Enemy.create(Vector3(0.0f, 0.0f, -5.0f))
        model.Enemies <- [| enemy |]

        handleShoot model

        Expect.equal
          model.Enemies[0].Health
          Constants.EnemyMaxHealth
          "Enemy not damaged behind wall"

      testCase "killing enemy adds score and marks dead"
      <| fun _ ->
        let model = GameModel()
        model.Ammo <- 10
        model.Colliders <- [||]
        model.PlayerPosition <- Vector3(0.0f, 0.0f, 0.0f)
        model.PlayerYaw <- 0.0f
        model.PlayerPitch <- 0.0f

        let mutable enemy = Enemy.create(Vector3(0.0f, 0.0f, -5.0f))
        enemy.Health <- 10.0f // will die in one shot
        model.Enemies <- [| enemy |]

        handleShoot model

        Expect.equal model.Enemies[0].State EnemyState.Dead "Enemy is dead"
        Expect.equal model.Enemies[0].Health 0.0f "Health is 0"
        Expect.equal model.Score 100 "Score increased by 100"
    ]

    testList "reload" [
      testCase "startReload sets reloading state"
      <| fun _ ->
        let model = GameModel()
        model.Ammo <- 5
        startReload model
        Expect.isTrue model.IsReloading "Is reloading"
        Expect.isGreaterThan model.ReloadTimer 0.0f "Timer set"

      testCase "startReload does nothing when full"
      <| fun _ ->
        let model = GameModel()
        model.Ammo <- Constants.MaxAmmo
        startReload model
        Expect.isFalse model.IsReloading "Not reloading when full"

      testCase "updateReload completes after timer"
      <| fun _ ->
        let model = GameModel()
        model.IsReloading <- true
        model.ReloadTimer <- 0.1f
        updateReload 0.2f model
        Expect.isFalse model.IsReloading "Reload finished"
        Expect.equal model.Ammo Constants.MaxAmmo "Ammo refilled"
    ]
  ]
