module Platformer3D.Shared.Tests.Tests

open Expecto
open System.Numerics
open Mibo.Layout3D
open Platformer3D.Constants
open Platformer3D.Types
open Platformer3D.WorldGen
open Platformer3D.DayNight
open Platformer3D.Particles

[<Tests>]
let tests =
  testList "Platformer3D.Shared" [
    test "WorldGen.generateChunk produces non-empty grid" {
      let chunk = generateChunk 0 0 42

      let mutable count = 0

      CellGrid3D.iterVolume
        chunk.Bounds
        (fun _ _ _ bt ->
          if bt <> Empty then
            count <- count + 1)
        chunk.Grid

      Expect.isTrue (count > 0) "Chunk should have non-empty blocks"
    }

    test "WorldGen.Chunks.init creates empty state" {
      let model = Chunks.init 42
      Expect.equal 0 model.Chunks.Count "No chunks at init"
      Expect.equal 42 model.Seed "Seed stored"
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
  ]
