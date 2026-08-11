module AnimatedInstancing.MonoGame.Systems

open System
open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Animation
open AnimatedInstancing
open AnimatedInstancing.MonoGame.Types

type Model = Types.Model
type Msg = Types.Msg

// -------------------------------------------------------------
// Crowd sub-system — owns tier, playback states, transforms, poses
// -------------------------------------------------------------

module Crowd =
  [<Struct>]
  type CrowdMsg =
    | Tick of dt: float32
    | SetTier of tierIndex: int
    | StepTier of delta: int
    | TogglePause

  /// Slow auto-orbit (radians/second) — the camera is part of the scene, not
  /// user-controlled, so it lives with the crowd.
  let private orbitSpeed = 0.15f

  let private buildState (rig: Rig) (i: int) : Animation3DState =
    let state =
      Animation3DState.create rig.Clips (CrowdSpec.clipNameFor i) CrowdSpec.fps

    {
      state with
          CurrentFrame = CrowdSpec.frameOffsetFor i
    }

  let private buildTransforms(count: int) : Matrix[] =
    let side = CrowdSpec.gridSide count

    Array.init count (fun i ->
      let struct (x, z) = CrowdSpec.gridPosition side i
      let yaw = CrowdSpec.yawDegreesFor i * MathF.PI / 180.0f
      Matrix.CreateRotationY yaw * Matrix.CreateTranslation(x, 0.0f, z))

  let init (rig: Rig) (tierIndex: int) : CrowdModel =
    let count = CrowdSpec.counts[tierIndex]

    {
      TierIndex = tierIndex
      Count = count
      Paused = false
      CameraAngle = 0.0f
      States = Array.init count (buildState rig)
      Transforms = buildTransforms count
      Poses = Array.init count (fun _ -> BonePose.empty)
    }

  let private clampTier i =
    max 0 (min (CrowdSpec.counts.Length - 1) i)

  /// Rebuild states/transforms/poses for a new tier, deterministically —
  /// same instances keep index, clip, yaw, and frame offset. Orbit angle and
  /// pause carry over.
  let private rebuild (rig: Rig) (tierIndex: int) (crowd: CrowdModel) =
    let fresh = init rig (clampTier tierIndex)

    {
      fresh with
          CameraAngle = crowd.CameraAngle
          Paused = crowd.Paused
    }

  let update (rig: Rig) (msg: CrowdMsg) (crowd: CrowdModel) : CrowdModel =
    match msg with
    | Tick dt ->
      if not crowd.Paused then
        for i = 0 to crowd.Count - 1 do
          crowd.States[i] <- Animation3DState.update dt crowd.States[i]

      {
        crowd with
            CameraAngle = crowd.CameraAngle + orbitSpeed * dt
      }
    | SetTier tierIndex -> rebuild rig tierIndex crowd
    | StepTier delta -> rebuild rig (crowd.TierIndex + delta) crowd
    | TogglePause -> { crowd with Paused = not crowd.Paused }

// -------------------------------------------------------------
// Root update — pure router: translates discrete input actions into
// Crowd messages and forwards Tick. No game logic here.
// -------------------------------------------------------------

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | InputMapped actions ->
    model.Actions <- actions

    let started = actions.Started

    let crowd =
      if started.Contains GameAction.Tier1 then
        Crowd.update model.Rig (Crowd.SetTier 0) model.Crowd
      elif started.Contains GameAction.Tier2 then
        Crowd.update model.Rig (Crowd.SetTier 1) model.Crowd
      elif started.Contains GameAction.Tier3 then
        Crowd.update model.Rig (Crowd.SetTier 2) model.Crowd
      elif started.Contains GameAction.Tier4 then
        Crowd.update model.Rig (Crowd.SetTier 3) model.Crowd
      elif started.Contains GameAction.TierUp then
        Crowd.update model.Rig (Crowd.StepTier 1) model.Crowd
      elif started.Contains GameAction.TierDown then
        Crowd.update model.Rig (Crowd.StepTier -1) model.Crowd
      else
        model.Crowd

    let crowd =
      if started.Contains GameAction.TogglePause then
        Crowd.update model.Rig Crowd.TogglePause crowd
      else
        crowd

    if started.Contains GameAction.ToggleShadows then
      model.ShadowsOn <- not model.ShadowsOn

    model.Crowd <- crowd
    model, Cmd.none

  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    model.Crowd <- Crowd.update model.Rig (Crowd.Tick dt) model.Crowd
    model, Cmd.none
