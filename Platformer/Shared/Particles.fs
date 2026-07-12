module Platformer.Particles

open System
open System.Numerics
open Mibo
open Mibo.Elmish
open Platformer.Types
open Platformer.Constants

// -------------------------------------------------------------
// Particles Sub-system (M_U — backend-agnostic)
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

[<Struct>]
type ParticleModel = {
  Particles: Particle[]
  Velocities: Vector2[]
  Count: int
}

let init() = {
  Particles = Array.zeroCreate 512
  Velocities = Array.zeroCreate 512
  Count = 0
}

[<Struct>]
type ParticleMsg =
  | Tick of dt: float32
  | SpawnConfetti of position: Vector2

let private spawn (pos: Vector2) (model: ParticleModel) =
  let rng = Random.Shared
  let mutable pc = model.Count
  let particles = model.Particles
  let velocities = model.Velocities

  for _ in 0..19 do
    if pc < particles.Length then
      particles[pc] <- {
        Position =
          pos
          + Vector2(
            playerWidth / 2.0f + float32(rng.NextDouble() * 20.0 - 10.0),
            playerHeight * 0.3f
          )
        Size = Vector2(4.0f, 4.0f)
        Rotation = float32(rng.NextDouble() * Math.PI * 2.0)
        Color = confettiColors[rng.Next confettiColors.Length]
      }

      velocities[pc] <-
        Vector2(
          float32(rng.NextDouble() * 300.0 - 150.0),
          float32(rng.NextDouble() * -250.0 - 50.0)
        )

      pc <- pc + 1

  { model with Count = pc }

let update (msg: ParticleMsg) (model: ParticleModel) : ParticleModel =
  match msg with
  | SpawnConfetti pos -> spawn pos model

  | Tick dt ->
    let particles = model.Particles
    let velocities = model.Velocities
    let mutable count = model.Count

    for i = 0 to count - 1 do
      let vel = velocities[i]
      let newVel = Vector2(vel.X, vel.Y + gravity * dt * 0.05f)
      velocities[i] <- newVel

      particles[i] <- {
        particles[i] with
            Position = particles[i].Position + newVel * dt
      }

    // Fade + compact
    let fadeAmount = 60.0f * dt
    let mutable writeIdx = 0

    for readIdx = 0 to count - 1 do
      let p = particles[readIdx]
      let newAlpha = MathF.Max(0.0f, float32 p.Color.A - fadeAmount)

      if newAlpha > 0.0f then
        particles[writeIdx] <- {
          p with
              Color = { p.Color with A = byte newAlpha }
        }

        writeIdx <- writeIdx + 1

    { model with Count = writeIdx }
