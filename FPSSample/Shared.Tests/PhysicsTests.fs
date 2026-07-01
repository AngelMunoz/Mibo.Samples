module FPSSample.Tests.PhysicsTests

open Expecto
open System
open System.Numerics
open Mibo.Layout3D
open Mibo.Input
open FPSSample
open FPSSample.Types
open FPSSample.Physics

/// Creates a minimal GameModel with an empty level (no colliders)
/// for isolated physics testing. Physics.update operates on the PlayerModel.
let private createTestModel() : GameModel =
  let model = GameModel()
  model.Colliders <- [||]
  model.Level <- Level.LevelData.createDefault()
  model

[<Tests>]
let tests =
  testList "Physics" [

    testList "moveDirections" [
      testCase "yaw=0 gives forward = -Z, right = +X"
      <| fun _ ->
        let struct (fwd, right) = moveDirections 0.0f
        Expect.floatClose Accuracy.veryHigh (float fwd.X) 0.0 "forward.X ~ 0"
        Expect.floatClose Accuracy.veryHigh (float fwd.Y) 0.0 "forward.Y ~ 0"
        Expect.floatClose Accuracy.veryHigh (float fwd.Z) -1.0 "forward.Z ~ -1"
        Expect.floatClose Accuracy.veryHigh (float right.X) 1.0 "right.X ~ 1"
        Expect.floatClose Accuracy.veryHigh (float right.Z) 0.0 "right.Z ~ 0"

      testCase "yaw=pi/2 gives forward = -X, right = -Z"
      <| fun _ ->
        let struct (fwd, right) = moveDirections(MathF.PI / 2.0f)
        Expect.floatClose Accuracy.low (float fwd.X) -1.0 "forward.X ~ -1"
        Expect.floatClose Accuracy.low (float fwd.Z) 0.0 "forward.Z ~ 0"
    ]

    testList "resolveSphereAABB" [
      testCase "sphere outside box is pushed away"
      <| fun _ ->
        let bounds: BoundingBox = {
          Min = Vector3(0.0f, 0.0f, 0.0f)
          Max = Vector3(2.0f, 2.0f, 2.0f)
        }
        // Sphere center at (3, 1, 1) with radius 0.5 - overlaps box face at X=2
        let result = resolveSphereAABB 0.5f (Vector3(2.4f, 1.0f, 1.0f)) bounds
        Expect.isGreaterThan result.X 2.0f "Pushed out past X=2"

      testCase "sphere far from box is unchanged"
      <| fun _ ->
        let bounds: BoundingBox = {
          Min = Vector3(0.0f, 0.0f, 0.0f)
          Max = Vector3(2.0f, 2.0f, 2.0f)
        }

        let pos = Vector3(10.0f, 10.0f, 10.0f)
        let result = resolveSphereAABB 0.5f pos bounds
        Expect.equal result pos "Unchanged when far away"
    ]

    testList "Physics.update gravity" [
      testCase "player falls when not grounded"
      <| fun _ ->
        let model = createTestModel()
        model.Player.Position <- Vector3(50.0f, 10.0f, 50.0f) // above ground
        model.Player.Velocity <- Vector3(0.0f, 0.0f, 0.0f)
        model.Player.IsGrounded <- false
        // Override colliders to empty and ground to 0
        model.Colliders <- [||]

        Physics.update
          0.016f
          model.Player
          model.Level
          model.Colliders
          ActionState.empty

        Expect.isLessThan
          model.Player.Velocity.Y
          0.0f
          "Y velocity negative after gravity"

      testCase "player on ground stays grounded"
      <| fun _ ->
        let model = createTestModel()

        model.Player.Position <-
          Vector3(50.0f, Constants.PlayerEyeHeight, 50.0f)

        model.Player.Velocity <- Vector3.Zero
        model.Player.IsGrounded <- true
        model.Colliders <- [||]

        Physics.update
          0.016f
          model.Player
          model.Level
          model.Colliders
          ActionState.empty

        Expect.isTrue model.Player.IsGrounded "Still grounded"
    ]
  ]
