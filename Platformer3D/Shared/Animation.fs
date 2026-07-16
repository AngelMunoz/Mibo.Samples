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
// -------------------------------------------------------------

module Diagnostics =
  type DiagnosticsModel() =
    member val Fps = 0 with get, set

  let init() = DiagnosticsModel()

  let update (dt: float32) (model: DiagnosticsModel) : DiagnosticsModel =
    if dt > 0.0f then
      let instant = 1.0f / dt
      model.Fps <- int(MathF.Round(float32 model.Fps * 0.9f + instant * 0.1f))

    model
