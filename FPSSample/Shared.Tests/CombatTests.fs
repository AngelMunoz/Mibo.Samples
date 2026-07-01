module FPSSample.Tests.CombatTests

open Expecto
open System.Numerics
open Mibo.Layout3D
open FPSSample
open FPSSample.Types
open FPSSample.Combat

/// Creates a GameModel pre-wired with sub-models for combat tests. The weapon
/// and player sub-models are the ones mutated by handleShoot/startReload.
let private createTestModel() : GameModel =
  let model = GameModel()
  model.Colliders <- [||]
  model.Enemy.Colliders <- [||]
  model.Level <- Level.LevelData.createDefault()
  model

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
        let model = createTestModel()
        model.Weapon.Ammo <- 0
        let scoreBefore = model.Player.Score

        let events =
          handleShoot
            model.Player
            model.Weapon
            model.Enemy.Enemies
            model.Enemy.Colliders
          |> Seq.toList

        Expect.equal model.Player.Score scoreBefore "Score unchanged"
        Expect.equal model.Weapon.Ammo 0 "Ammo unchanged"
        Expect.equal model.Weapon.FireCooldown 0.0f "Cooldown not set"
        Expect.isEmpty events "No events emitted"

      testCase "shooting during cooldown does nothing"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.Ammo <- 10
        model.Weapon.FireCooldown <- 0.1f

        let events =
          handleShoot
            model.Player
            model.Weapon
            model.Enemy.Enemies
            model.Enemy.Colliders
          |> Seq.toList

        Expect.equal model.Weapon.Ammo 10 "Ammo unchanged during cooldown"
        Expect.isEmpty events "No events emitted"

      testCase "shooting consumes ammo and sets cooldown + emits Fired event"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.Ammo <- 10

        let events =
          handleShoot
            model.Player
            model.Weapon
            model.Enemy.Enemies
            model.Enemy.Colliders
          |> Seq.toList

        Expect.equal model.Weapon.Ammo 9 "Ammo consumed"
        Expect.isGreaterThan model.Weapon.FireCooldown 0.0f "Cooldown set"
        // Muzzle flash is applied by the router's EffectMsg.MuzzleFlash handler
        // (not by handleShoot directly — it emits a Fired event which the router
        // translates). The recoil kick IS applied directly here (weapon-owned).
        Expect.isGreaterThan
          model.Weapon.RecoilOffset
          0.0f
          "Recoil kick applied"

        Expect.equal events.Length 1 "One Fired event emitted"

        match events with
        | [ WeaponEvent.Fired(path, _, _) ] ->
          Expect.isTrue
            (path.Contains("762x39"))
            "Fired event carries a gun sound path"
        | _ -> failwith "Expected exactly one Fired event"

      testCase "shooting enemy in line of sight deals damage"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.Ammo <- 10
        model.Player.Position <- Vector3(0.0f, 0.0f, 0.0f)
        model.Player.Yaw <- 0.0f
        model.Player.Pitch <- 0.0f

        let enemy = Enemy.create(Vector3(0.0f, 0.0f, -5.0f))
        model.Enemy.Enemies <- [| enemy |]

        let _ =
          handleShoot
            model.Player
            model.Weapon
            model.Enemy.Enemies
            model.Enemy.Colliders

        Expect.isLessThan
          model.Enemy.Enemies[0].Health
          Constants.EnemyMaxHealth
          "Enemy took damage"

      testCase "wall occludes enemy behind it"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.Ammo <- 10
        model.Player.Position <- Vector3(0.0f, 0.0f, 0.0f)
        model.Player.Yaw <- 0.0f
        model.Player.Pitch <- 0.0f

        // Wall between player and enemy
        model.Colliders <- [|
          {
            Min = Vector3(-1.0f, -1.0f, -3.0f)
            Max = Vector3(1.0f, 1.0f, -2.0f)
          }
        |]

        model.Enemy.Colliders <- model.Colliders

        let enemy = Enemy.create(Vector3(0.0f, 0.0f, -5.0f))
        model.Enemy.Enemies <- [| enemy |]

        let _ =
          handleShoot
            model.Player
            model.Weapon
            model.Enemy.Enemies
            model.Enemy.Colliders

        Expect.equal
          model.Enemy.Enemies[0].Health
          Constants.EnemyMaxHealth
          "Enemy not damaged behind wall"

      testCase "killing enemy emits EnemyKilled event"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.Ammo <- 10
        model.Player.Position <- Vector3(0.0f, 0.0f, 0.0f)
        model.Player.Yaw <- 0.0f
        model.Player.Pitch <- 0.0f

        let mutable enemy = Enemy.create(Vector3(0.0f, 0.0f, -5.0f))
        enemy.Health <- 10.0f // will die in one shot
        model.Enemy.Enemies <- [| enemy |]

        let events =
          handleShoot
            model.Player
            model.Weapon
            model.Enemy.Enemies
            model.Enemy.Colliders
          |> Seq.toList

        Expect.equal
          model.Enemy.Enemies[0].State
          EnemyState.Dead
          "Enemy is dead"

        Expect.equal model.Enemy.Enemies[0].Health 0.0f "Health is 0"

        // Should have both Fired and EnemyKilled events.
        let hasKill =
          events
          |> List.exists (function
            | WeaponEvent.EnemyKilled _ -> true
            | _ -> false)

        Expect.isTrue hasKill "EnemyKilled event emitted"
    ]

    testList "reload" [
      testCase "startReload sets reloading state and emits ReloadStarted"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.Ammo <- 5

        let events = startReload model.Weapon |> Seq.toList

        Expect.isTrue model.Weapon.IsReloading "Is reloading"
        Expect.isGreaterThan model.Weapon.ReloadTimer 0.0f "Timer set"
        Expect.equal events.Length 1 "One ReloadStarted event emitted"

        match events with
        | [ WeaponEvent.ReloadStarted path ] ->
          Expect.isTrue (path.Contains("reload")) "Reload sound path"
        | _ -> failwith "Expected exactly one ReloadStarted event"

      testCase "startReload does nothing when full"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.Ammo <- Constants.MaxAmmo

        let events = startReload model.Weapon |> Seq.toList

        Expect.isFalse model.Weapon.IsReloading "Not reloading when full"
        Expect.isEmpty events "No events emitted"

      testCase "updateReload completes after timer"
      <| fun _ ->
        let weapon = WeaponModel()
        weapon.IsReloading <- true
        weapon.ReloadTimer <- 0.1f
        updateReload 0.2f weapon
        Expect.isFalse weapon.IsReloading "Reload finished"
        Expect.equal weapon.Ammo Constants.MaxAmmo "Ammo refilled"
    ]
  ]
