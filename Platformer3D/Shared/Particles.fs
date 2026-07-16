module Platformer3D.Particles

open System
open System.Numerics
open Mibo
open Platformer3D.Constants

// -------------------------------------------------------------
// Particles Sub-system (backend-agnostic)
// -------------------------------------------------------------

let confettiColors = [|
  Color.create 255uy 50uy 50uy 255uy
  Color.create 50uy 255uy 50uy 255uy
  Color.create 50uy 50uy 255uy 255uy
  Color.create 255uy 255uy 50uy 255uy
  Color.create 255uy 50uy 255uy 255uy
  Color.create 50uy 255uy 255uy 255uy
  Color.create 255uy 150uy 50uy 255uy
  Color.create 255uy 50uy 150uy 255uy
|]

type ParticleModel() =
  member val Positions = Array.zeroCreate<Vector3> 512 with get, set
  member val Velocities = Array.zeroCreate<Vector3> 512 with get, set
  member val Sizes = Array.zeroCreate<Vector2> 512 with get, set
  member val Colors = Array.zeroCreate<Color> 512 with get, set
  member val Count = 0 with get, set

let init() = ParticleModel()

[<Struct>]
type ParticleMsg =
  | Tick of dt: float32
  | SpawnConfetti of position: Vector3

let private spawn (pos: Vector3) (model: ParticleModel) =
  let rng = Random.Shared
  let mutable pc = model.Count
  let positions = model.Positions
  let velocities = model.Velocities
  let sizes = model.Sizes
  let colors = model.Colors

  for _ in 0..100 do
    if pc < positions.Length then
      let offset =
        Vector3(
          float32(rng.NextDouble() * 0.4 - 0.2),
          float32(rng.NextDouble() * 0.2),
          float32(rng.NextDouble() * 0.4 - 0.2)
        )

      positions[pc] <- pos + Vector3(0.0f, playerHeight * 0.5f, 0.0f) + offset

      sizes[pc] <- Vector2(0.05f, 0.05f)
      colors[pc] <- confettiColors[rng.Next confettiColors.Length]

      let angle = float32(rng.NextDouble()) * MathF.PI * 6.0f
      let speed = float32(rng.NextDouble()) * 3.0f + 5.0f

      velocities[pc] <-
        Vector3(
          MathF.Cos(angle) * speed,
          float32(rng.NextDouble()) * 3.0f + 2.0f,
          MathF.Sin(angle) * speed
        )

      pc <- pc + 1

  model.Count <- pc
  model

let update (msg: ParticleMsg) (model: ParticleModel) : ParticleModel =
  match msg with
  | SpawnConfetti pos -> spawn pos model

  | Tick dt ->
    let positions = model.Positions
    let velocities = model.Velocities
    let sizes = model.Sizes
    let colors = model.Colors
    let mutable count = model.Count

    for i = 0 to count - 1 do
      let vel = velocities[i]
      let newVel = Vector3(vel.X, vel.Y + gravity * dt * 0.6f, vel.Z)
      velocities[i] <- newVel
      positions[i] <- positions[i] + newVel * dt

    let fadeAmount = 130.0f * dt
    let mutable writeIdx = 0

    for readIdx = 0 to count - 1 do
      let c = colors[readIdx]
      let newAlpha = MathF.Max(0.0f, float32 c.A - fadeAmount)

      if newAlpha > 0.0f then
        positions[writeIdx] <- positions[readIdx]
        velocities[writeIdx] <- velocities[readIdx]
        sizes[writeIdx] <- sizes[readIdx]
        colors[writeIdx] <- { c with A = byte newAlpha }
        writeIdx <- writeIdx + 1

    model.Count <- writeIdx
    model
