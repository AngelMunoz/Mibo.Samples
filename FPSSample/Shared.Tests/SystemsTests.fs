module FPSSample.Tests.SystemsTests

open Expecto
open System
open System.Numerics
open Mibo.Elmish
open Mibo.Input
open FPSSample
open FPSSample.Types
open FPSSample.Systems

/// No-op animation service for tests (avoids raylib/asset dependencies).
type private NoopAnimationService() =
  interface IEnemyAnimationService with
    member _.Init(_, _) = ()
    member _.Update(_, _) = ()

/// No-op audio service for tests.
type private NoopAudioService() =
  interface IAudioService with
    member _.Init(_) = ()
    member _.Update(_, _) = ()

/// A test env with no-op services.
let private testEnv: Env = {
  Animation = NoopAnimationService()
  Audio = NoopAudioService()
}

/// Creates a GameModel with an empty level (no colliders) for testing.
let private createTestModel() : GameModel =
  let model = GameModel()
  model.Colliders <- [||]
  model.Level <- Level.LevelData.createDefault()
  model

/// Creates a GameTime for a tick message.
let private gameTime(seconds: float32) : GameTime = {
  ElapsedGameTime = TimeSpan.FromSeconds(float seconds)
  TotalTime = TimeSpan.Zero
}

[<Tests>]
let tests =
  testList "Systems" [

    testList "update - InputMapped" [
      testCase "InputMapped stores actions in model"
      <| fun _ ->
        let model = createTestModel()
        let actions: ActionState<GameAction> = ActionState.empty

        let struct (m, _) =
          Systems.update testEnv (Msg.InputMapped actions) model

        Expect.equal m.Actions actions "Actions stored"
    ]

    testList "update - MouseLook" [
      testCase "MouseLook adjusts yaw and pitch"
      <| fun _ ->
        let model = createTestModel()
        let initialYaw = model.PlayerYaw
        let initialPitch = model.PlayerPitch

        let struct (m, _) =
          Systems.update testEnv (Msg.MouseLook(100.0f, 50.0f)) model

        Expect.isLessThan m.PlayerYaw initialYaw "Yaw decreased"

        Expect.isLessThan
          m.PlayerPitch
          initialPitch
          "Pitch decreased (mouse down = look down)"

      testCase "MouseLook clamps pitch to max"
      <| fun _ ->
        let model = createTestModel()
        let mutable m = model

        for _ in 0..1000 do
          let struct (model, _) =
            Systems.update testEnv (Msg.MouseLook(0.0f, 1000.0f)) m

          m <- model

        Expect.isLessThanOrEqual
          m.PlayerPitch
          Constants.MaxPitch
          "Pitch clamped"
    ]

    testList "update - Tick" [
      testCase "Tick increments total time"
      <| fun _ ->
        let model = createTestModel()

        let struct (m, _) =
          Systems.update testEnv (Msg.Tick(gameTime 0.5f)) model

        Expect.floatClose
          Accuracy.veryHigh
          (float m.TotalTime)
          0.5
          "Time incremented"

      testCase "Tick decrements fire cooldown"
      <| fun _ ->
        let model = createTestModel()
        model.FireCooldown <- 0.3f

        let struct (m, _) =
          Systems.update testEnv (Msg.Tick(gameTime 0.1f)) model

        Expect.floatClose
          Accuracy.low
          (float m.FireCooldown)
          0.2
          "Cooldown decremented"

      testCase "Tick expires muzzle flash"
      <| fun _ ->
        let model = createTestModel()
        model.MuzzleFlash <- { Timer = 0.05f; Active = true }

        let struct (m, _) =
          Systems.update testEnv (Msg.Tick(gameTime 0.1f)) model

        Expect.isFalse m.MuzzleFlash.Active "Muzzle flash expired"
    ]

    testList "update - Shoot" [
      testCase "Shoot with ammo fires weapon"
      <| fun _ ->
        let model = createTestModel()
        model.Ammo <- 10
        let struct (m, _) = Systems.update testEnv Msg.Shoot model
        Expect.equal m.Ammo 9 "Ammo consumed"
        Expect.isTrue m.MuzzleFlash.Active "Muzzle flash active"
    ]

    testList "update - Reload" [
      testCase "Reload starts reload when ammo not full"
      <| fun _ ->
        let model = createTestModel()
        model.Ammo <- 5
        let struct (m, _) = Systems.update testEnv Msg.Reload model
        Expect.isTrue m.IsReloading "Is reloading"

      testCase "Reload completes after time"
      <| fun _ ->
        let model = createTestModel()
        model.Ammo <- 5
        model.IsReloading <- true
        model.ReloadTimer <- 0.1f

        let struct (m, _) =
          Systems.update testEnv (Msg.Tick(gameTime 0.2f)) model

        Expect.isFalse m.IsReloading "Reload finished"
        Expect.equal m.Ammo Constants.MaxAmmo "Ammo refilled"
    ]

    testList "update - Pickups" [
      testCase "pickup heals player when nearby"
      <| fun _ ->
        let model = createTestModel()
        model.PlayerHealth <- 50.0f
        model.PlayerPosition <- Vector3(0.0f, 0.0f, 0.0f)

        model.Pickups <- [|
          {
            Kind = Level.PickupKind.Health
            Position = Vector3(0.1f, 0.0f, 0.0f)
            IsActive = true
            RespawnTimer = 0.0f
          }
        |]

        let struct (m, _) =
          Systems.update testEnv (Msg.Tick(gameTime 0.1f)) model

        Expect.isGreaterThan m.PlayerHealth 50.0f "Player healed"
        Expect.isFalse m.Pickups[0].IsActive "Pickup consumed"

      testCase "consumed pickup respawns after time"
      <| fun _ ->
        let model = createTestModel()

        let pickup: Pickup = {
          Kind = Level.PickupKind.Ammo
          Position = Vector3(100.0f, 0.0f, 0.0f)
          IsActive = false
          RespawnTimer = 0.1f
        }

        model.Pickups <- [| pickup |]

        let struct (m, _) =
          Systems.update testEnv (Msg.Tick(gameTime 0.2f)) model

        Expect.isTrue m.Pickups[0].IsActive "Pickup respawned"
    ]
  ]
