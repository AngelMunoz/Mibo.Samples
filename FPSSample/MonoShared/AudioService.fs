namespace FPSSample.MonoShared

open System
open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Audio
open FPSSample
open FPSSample.Types

/// <summary>
/// MonoGame audio adapter over the framework's <c>Mibo.Audio.MonoGameAudio</c>
/// service. The sound bank (keys → pipeline assets) is registered by the
/// program builder (<c>MonoGameProgram.withBank</c>); this service translates
/// game intent — one-shot <c>AudioMsg</c> events and snapshot-derived footstep
/// loops — into keys and voices. Positional sounds go through the framework's
/// 3D surface (<c>Play3D</c>: distance attenuation, panning, and Doppler from
/// the listener/emitter geometry; the camera is the listener). Loop intent
/// (player/enemy footsteps) is derived from the snapshot each frame and
/// re-triggered on the clip's length — no audio flags in Elmish.
/// </summary>
type AudioService() =
  let mutable ctx = Unchecked.defaultof<GameContext>
  let mutable initialized = false

  // Footstep re-trigger countdowns (0 = fire on the next Update while the
  // intent holds). The portable IAudio contract has no looping sfx and no
  // per-key stop, so a "loop" is the clip re-triggered at its own length;
  // resetting to 0 when the intent drops keeps the next step immediate.
  let mutable playerStepTimer = 0.0f
  let mutable enemyStepTimer = 0.0f

  let audioService() : MonoGameAudio voption =
    if initialized then
      GameContext.tryGetService<MonoGameAudio> ctx
    else
      ValueNone

  // ── Play a one-shot with optional positional attenuation ──
  let playOneShot (audio: MonoGameAudio) (msg: AudioMsg) =
    match msg with
    | AudioMsg.OneShot(key, position, isPositional) ->
      if isPositional then
        // Geometry (listener vs emitter) decides attenuation and pan.
        audio.Play3D(key, Vector3.op_Implicit position)
      else
        audio.Play key

  interface IAudioService with
    member _.Init(gameCtx: GameContext) : unit =
      ctx <- gameCtx
      initialized <- true

    member _.Consume(audioMsg: AudioMsg) : unit =
      match audioService() with
      | ValueSome audio -> playOneShot audio audioMsg
      | ValueNone -> ()

    member _.Update(dt: float32, snapshot: Snapshot) : unit =
      match audioService() with
      | ValueNone -> ()
      | ValueSome audio ->
        let pos = Vector3.op_Implicit snapshot.Player.Position

        let forwardNumerics =
          ViewMath.cameraForward snapshot.Player.Yaw snapshot.Player.Pitch

        let forward =
          Vector3(forwardNumerics.X, forwardNumerics.Y, forwardNumerics.Z)

        // The camera is the listener; 3D plays re-attenuate against it.
        audio.SetListener(
          pos,
          forward,
          Vector3.UnitY,
          Vector3.op_Implicit snapshot.Player.Velocity
        )

        // ── Looping player footsteps (derived from snapshot velocity) ──
        let horizontalSpeed =
          MathF.Sqrt(
            snapshot.Player.Velocity.X * snapshot.Player.Velocity.X
            + snapshot.Player.Velocity.Z * snapshot.Player.Velocity.Z
          )

        let isWalking = snapshot.Player.IsGrounded && horizontalSpeed > 0.5f

        if isWalking then
          playerStepTimer <- playerStepTimer - dt

          if playerStepTimer <= 0.0f then
            audio.Play(Assets.Keys.footstepWalk, Voice.ofVolume 0.4f)
            playerStepTimer <- Assets.footstepWalkInterval
        else
          playerStepTimer <- 0.0f

        // ── Looping enemy footsteps (nearest chasing enemy, from snapshot) ──
        let mutable nearestChaser = ValueNone
        let mutable nearestDist = Single.MaxValue

        for e in snapshot.Enemy.Enemies do
          if e.State <> EnemyState.Dead && e.IsChasing then
            let d = Vector3.Distance(Vector3.op_Implicit e.Position, pos)

            if d < nearestDist then
              nearestDist <- d
              nearestChaser <- ValueSome e

        match nearestChaser with
        | ValueSome enemy ->
          let ePos = Vector3.op_Implicit enemy.Position

          let emitterVelocity = Vector3.op_Implicit enemy.Velocity

          // Beyond the audible range, hold for a chaser in range instead of
          // stacking silent plays.
          if nearestDist < 30.0f then
            enemyStepTimer <- enemyStepTimer - dt

            if enemyStepTimer <= 0.0f then
              audio.Play3D(Assets.Keys.footstepRun, ePos, emitterVelocity)

              enemyStepTimer <- Assets.footstepRunInterval
          else
            enemyStepTimer <- 0.0f
        | ValueNone -> enemyStepTimer <- 0.0f
