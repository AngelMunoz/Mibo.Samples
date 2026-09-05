namespace FPSSample.Raylib

open System
open System.Numerics
open Mibo.Elmish
open Mibo.Audio
open FPSSample
open FPSSample.Types

/// <summary>
/// Raylib audio adapter over the framework's <c>Mibo.Audio.IAudio</c> service.
/// The sound bank (keys → files) is registered by the program builder
/// (<c>RaylibProgram.withBank</c>); this service translates game intent —
/// one-shot <c>AudioMsg</c> events and snapshot-derived footstep loops — into
/// keys and <c>Voice</c> values. Raylib has no listener model, so positional
/// sounds use manual inverse-distance attenuation + stereo pan from the
/// player's camera right vector, folded into the per-play voice. Loop intent
/// (player/enemy footsteps) is derived from the snapshot each frame and
/// re-triggered on the clip's length — no audio flags in Elmish.
/// </summary>
type AudioService() =
  let mutable ctx = Unchecked.defaultof<GameContext>
  let mutable initialized = false

  // Cached player frame for Consume (positional one-shots). Consume may be
  // called from the Elmish message queue outside of Update; these hold the most
  // recent player position/right so positional one-shots (robotic, child laugh,
  // bite, injured) play with correct attenuation/pan.
  let mutable cachedPlayerPos = Vector3.Zero
  let mutable cachedRight = Vector3.UnitX

  // Footstep re-trigger countdowns (0 = fire on the next Update while the
  // intent holds). The portable IAudio contract has no looping sfx and no
  // per-key stop, so a "loop" is the clip re-triggered at its own length;
  // resetting to 0 when the intent drops keeps the next step immediate.
  let mutable playerStepTimer = 0.0f
  let mutable enemyStepTimer = 0.0f

  let audioService() : IAudio voption =
    if initialized then
      GameContext.tryGetService<IAudio> ctx
    else
      ValueNone

  /// Inverse-distance attenuation + camera-relative pan, folded into a Voice:
  /// full volume at minDist, gentle fade to 0 at maxDist. This mirrors the
  /// curve OpenAL/MonoGame apply natively on their listener models.
  let voiceFor(toEmitter: Vector3) : Voice =
    let dist = toEmitter.Length()
    let minDist = 3.0f
    let maxDist = 30.0f

    let volume =
      if dist <= minDist then 0.85f
      elif dist >= maxDist then 0.0f
      else 0.85f * minDist / dist

    let pan =
      if dist > 0.01f then
        Math.Clamp(
          Vector3.Dot(Vector3.Normalize(toEmitter), cachedRight),
          -1.0f,
          1.0f
        )
      else
        0.0f

    Voice.at volume pan

  // ── Play a one-shot with optional positional attenuation ──
  let playOneShot (audio: IAudio) (msg: AudioMsg) =
    match msg with
    | AudioMsg.OneShot(key, position, isPositional) ->
      if isPositional then
        audio.Play(key, voiceFor(position - cachedPlayerPos))
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
        let playerPos = snapshot.Player.Position
        let right = ViewMath.cameraRight snapshot.Player.Yaw

        // Cache for Consume (positional one-shots need this frame's player info).
        cachedPlayerPos <- playerPos
        cachedRight <- right

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
            audio.Play(Assets.Keys.footstepWalk, Voice.ofVolume 0.5f)
            playerStepTimer <- Assets.footstepWalkInterval
        else
          playerStepTimer <- 0.0f

        // ── Looping enemy footsteps (nearest chasing enemy, from snapshot) ──
        let mutable nearestChaserPos = ValueNone
        let mutable nearestDist = Single.MaxValue

        for e in snapshot.Enemy.Enemies do
          if e.State <> EnemyState.Dead && e.IsChasing then
            let d = (e.Position - playerPos).Length()

            if d < nearestDist then
              nearestDist <- d
              nearestChaserPos <- ValueSome e.Position

        match nearestChaserPos with
        | ValueSome ePos ->
          let voice = voiceFor(ePos - playerPos)

          if voice.Volume > 0.01f then
            enemyStepTimer <- enemyStepTimer - dt

            if enemyStepTimer <= 0.0f then
              audio.Play(Assets.Keys.footstepRun, voice)
              enemyStepTimer <- Assets.footstepRunInterval
          else
            enemyStepTimer <- 0.0f
        | ValueNone -> enemyStepTimer <- 0.0f
