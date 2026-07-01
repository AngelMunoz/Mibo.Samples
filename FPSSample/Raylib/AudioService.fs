namespace FPSSample.Raylib

open System
open System.Numerics
open System.Runtime.CompilerServices
open Raylib_cs
open Mibo.Elmish
open FPSSample
open FPSSample.Types

[<Extension>]
type private CBoolExtensions =
  [<Extension>]
  static member inline AsBool(c: CBool) : bool = CBool.op_Implicit c

/// <summary>
/// Raylib-specific audio service. Manages one-shot sounds (fire, reload, SFX
/// queue) and looping footstep instances. Raylib has no built-in 3D audio, so
/// positional sounds use manual inverse-distance attenuation + stereo pan
/// computed from the player's camera right vector.
/// </summary>
type AudioService() =
  let mutable ctx = Unchecked.defaultof<GameContext>
  let mutable initialized = false

  // Looping footstep state
  let mutable playerFootstep: Sound = Unchecked.defaultof<_>
  let mutable playerFootstepLoaded = false
  let mutable playerFootstepPlaying = false

  let mutable enemyFootstep: Sound = Unchecked.defaultof<_>
  let mutable enemyFootstepLoaded = false
  let mutable enemyFootstepPlaying = false

  // Inverse-distance attenuation: full volume at minDist, gentle fade to 0.
  // This mirrors the curve OpenAL/MonoGame use natively.
  let distVol(dist: float32) : float32 =
    let minDist = 3.0f
    let maxDist = 30.0f

    if dist <= minDist then 0.85f
    elif dist >= maxDist then 0.0f
    else 0.85f * minDist / dist

  let panFor (toEmitter: Vector3) (right: Vector3) : float32 =
    let dist = toEmitter.Length()

    if dist > 0.01f then
      Math.Clamp(Vector3.Dot(Vector3.Normalize(toEmitter), right), -1.0f, 1.0f)
    else
      0.0f

  interface IAudioService with
    member _.Init(gameCtx: GameContext) : unit =
      ctx <- gameCtx
      initialized <- true

    member _.Update(_: float32, model: GameModel) : unit =
      if not initialized then
        ()

      let assets = GameContext.getService<IAssets> ctx
      let playerPos = model.PlayerPosition
      let right = ViewMath.cameraRight model.PlayerYaw

      // ── One-shot: fire + reload ──
      if model.LastFireSound <> "" then
        Raylib.PlaySound(assets.Sound(model.LastFireSound))
        model.LastFireSound <- ""

      if model.LastReloadSound <> "" then
        Raylib.PlaySound(assets.Sound(model.LastReloadSound))
        model.LastReloadSound <- ""

      // ── Looping player footsteps ──
      if not playerFootstepLoaded then
        playerFootstep <- assets.Sound(Assets.footstepsWalking)
        playerFootstepLoaded <- true

      if model.IsPlayerWalking then
        if
          not playerFootstepPlaying
          || not(Raylib.IsSoundPlaying(playerFootstep).AsBool())
        then
          Raylib.SetSoundVolume(playerFootstep, 0.5f)
          Raylib.SetSoundPan(playerFootstep, 0.5f)
          Raylib.PlaySound(playerFootstep)
          playerFootstepPlaying <- true
      elif playerFootstepPlaying then
        Raylib.StopSound(playerFootstep)
        playerFootstepPlaying <- false

      // ── Looping enemy footsteps (nearest chasing enemy) ──
      let mutable nearestChaserPos = ValueNone
      let mutable nearestDist = Single.MaxValue

      for e in model.Enemies do
        if e.State <> EnemyState.Dead && e.IsChasing then
          let d = (e.Position - playerPos).Length()

          if d < nearestDist then
            nearestDist <- d
            nearestChaserPos <- ValueSome e.Position

      if not enemyFootstepLoaded then
        enemyFootstep <- assets.Sound(Assets.footstepsRunning)
        enemyFootstepLoaded <- true

      match nearestChaserPos with
      | ValueSome ePos ->
        let toEmitter = ePos - playerPos
        let dist = toEmitter.Length()
        let vol = distVol dist
        let pan = panFor toEmitter right

        if vol > 0.01f then
          Raylib.SetSoundVolume(enemyFootstep, vol)
          Raylib.SetSoundPan(enemyFootstep, (pan + 1.0f) * 0.5f)

          if
            not enemyFootstepPlaying
            || not(Raylib.IsSoundPlaying(enemyFootstep).AsBool())
          then
            Raylib.PlaySound(enemyFootstep)
            enemyFootstepPlaying <- true
        elif enemyFootstepPlaying then
          Raylib.StopSound(enemyFootstep)
          enemyFootstepPlaying <- false
      | ValueNone ->
        if enemyFootstepPlaying then
          Raylib.StopSound(enemyFootstep)
          enemyFootstepPlaying <- false

      // ── One-shot SFX queue (robotic, bite, laugh, injured, gasp) ──
      for i = 0 to model.SoundQueueCount - 1 do
        let evt = model.SoundQueue[i % model.SoundQueue.Length]

        let snd = assets.Sound evt.Path

        if evt.IsPositional then
          let toEmitter = evt.Position - playerPos
          let dist = toEmitter.Length()
          let vol = distVol dist
          let pan = panFor toEmitter right

          Raylib.SetSoundVolume(snd, vol)
          Raylib.SetSoundPan(snd, (pan + 1.0f) * 0.5f)
          Raylib.PlaySound(snd)
        else
          Raylib.SetSoundVolume(snd, 1.0f)
          Raylib.SetSoundPan(snd, 0.5f)
          Raylib.PlaySound(snd)

      model.SoundQueueCount <- 0
