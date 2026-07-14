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
// -------------------------------------------------------------

module Diagnostics =
  [<Struct>]
  type DiagnosticsModel = { Fps: int; FrameTime: float32 }

  let init() = { Fps = 0; FrameTime = 0.0f }

  let update(dt: float32) : DiagnosticsModel = {
    Fps = int(1.0f / max dt 0.0001f)
    FrameTime = dt
  }
