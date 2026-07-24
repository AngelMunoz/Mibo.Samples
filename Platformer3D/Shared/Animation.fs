namespace Platformer3D

open System
open System.Numerics
open Mibo.Input
open Platformer3D.Types

module Animation =

  /// Derives the target animation clip name from physics state.
  /// Playback (Animation3DState / AnimatedModel) is backend-specific.
  let targetClip
    (isGrounded: bool)
    (actions: ActionState<GameAction>)
    : string =
    let isMoving =
      actions.Held.Contains(GameAction.MoveForward)
      || actions.Held.Contains(GameAction.MoveBackward)
      || actions.Held.Contains(GameAction.MoveLeft)
      || actions.Held.Contains(GameAction.MoveRight)

    if not isGrounded then "jump"
    elif isMoving then "walk"
    else "idle"

// -------------------------------------------------------------
// Diagnostics Sub-system (backend-agnostic)
//
// Measures the REAL render rate via a wall-clock Stopwatch sampled once per
// drawn frame (in DiagnosticsView, which the host calls exactly once per Draw).
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

      this.FrameTime <- elapsed * 1000.0f

      if elapsed > 0.0f then
        let instant = 1.0f / elapsed

        this.Fps <-
          if this.Fps = 0 then
            int instant
          else
            int(MathF.Round(float32 this.Fps * 0.9f + instant * 0.1f))

  let init() = DiagnosticsModel()
