namespace Platformer

open System.Numerics
open Platformer.Types

module Animation =
  [<Struct>]
  type AnimationModel = {
    State: AnimationState
    Facing: float32
  }

  let init() = { State = Idle; Facing = 1.0f }

  let update
    (velocity: Vector2)
    (isGrounded: bool)
    (isDucking: bool)
    (facing: float32)
    : AnimationModel =
    {
      State = Physics.getAnimationState velocity isGrounded isDucking
      Facing = facing
    }

// -------------------------------------------------------------
// Diagnostics Sub-system (M_U — backend-agnostic)
//
// Measures the REAL render rate via a wall-clock Stopwatch sampled once per
// drawn frame (in the View, which the host calls exactly once per Draw).
// This is distinct from the framework-rate `dt` carried by Tick: under
// MonoGame's fixed timestep Tick runs at the fixed rate and hides frame drops,
// so the on-screen counter must be wall-clock to expose real dips below the
// target FPS.
// -------------------------------------------------------------

module Diagnostics =
  type DiagnosticsModel() =
    let sw = System.Diagnostics.Stopwatch.StartNew()

    /// EMA-smoothed FPS so the counter doesn't jitter by ±1 every frame.
    member val Fps = 0 with get, set

    /// Last frame's wall-clock interval in milliseconds (0.0 until first tick).
    member val FrameTime = 0.0f with get, set

    /// Sample the wall-clock interval since the last call. Call once per Draw.
    member this.Tick() =
      let elapsed = float32 sw.Elapsed.TotalSeconds
      sw.Restart()

      this.FrameTime <- elapsed

      if elapsed > 0.0f then
        // EMA over ~the last second of frames; starts from the first sample so
        // it doesn't climb from 0 on startup.
        let instant = 1.0f / elapsed

        this.Fps <-
          if this.Fps = 0 then
            int instant
          else
            int(float32 this.Fps * 0.9f + instant * 0.1f)

  let init() = DiagnosticsModel()
