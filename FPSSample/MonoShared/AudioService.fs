namespace FPSSample.MonoShared

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Audio
open Mibo.Elmish
open FPSSample
open FPSSample.Types

/// <summary>
/// MonoGame-specific audio service. Manages one-shot sounds (fire, reload, SFX
/// queue) and looping footstep instances. Uses MonoGame's native 3D audio via
/// <c>SoundEffectInstance.Apply3D(listener, emitter)</c> for positional sounds.
/// </summary>
type AudioService() =
  let mutable ctx = Unchecked.defaultof<GameContext>
  let mutable initialized = false

  // Looping footstep instances (long-lived, reused every frame).
  let mutable playerFootstep: SoundEffectInstance = null
  let mutable enemyFootstep: SoundEffectInstance = null

  interface IAudioService with
    member _.Init(gameCtx: GameContext) : unit =
      ctx <- gameCtx
      initialized <- true

    member _.Update(_: float32, model: GameModel) : unit =
      if not initialized then
        ()

      let assets = GameContext.getService<IAssets> ctx

      let mgPath(path: string) =
        path
          .Replace("assets/", "")
          .Replace(".glb", "")
          .Replace(".fbx", "")
          .Replace(".mp3", "")
          .Replace(".wav", "")

      let playerPos = model.PlayerPosition
      let pos = Vector3(playerPos.X, playerPos.Y, playerPos.Z)

      let forwardNumerics =
        ViewMath.cameraForward model.PlayerYaw model.PlayerPitch

      let forward =
        Vector3(forwardNumerics.X, forwardNumerics.Y, forwardNumerics.Z)

      let listener =
        AudioListener(
          Position = pos,
          Forward = forward,
          Velocity = model.PlayerVelocity
        )

      // ── One-shot: fire + reload (non-positional) ──
      if model.LastFireSound <> "" then
        assets.Sound(mgPath model.LastFireSound).Play() |> ignore
        model.LastFireSound <- ""

      if model.LastReloadSound <> "" then
        assets.Sound(mgPath model.LastReloadSound).Play() |> ignore
        model.LastReloadSound <- ""

      // ── Looping player footsteps ──
      if isNull playerFootstep then
        let snd = assets.Sound(mgPath Assets.footstepsWalking)
        playerFootstep <- snd.CreateInstance()
        playerFootstep.IsLooped <- true

      if model.IsPlayerWalking then
        if playerFootstep.State = SoundState.Stopped then
          playerFootstep.Volume <- 0.4f
          playerFootstep.Pan <- 0.0f
          playerFootstep.Play()
      else if playerFootstep.State = SoundState.Playing then
        playerFootstep.Stop()

      // ── Looping enemy footsteps (nearest chasing enemy) ──
      let mutable nearestChaserPos = ValueNone
      let mutable nearestDist = Single.MaxValue

      for e in model.Enemies do
        if e.State <> EnemyState.Dead && e.IsChasing then
          let d =
            Vector3.Distance(
              Vector3(e.Position.X, e.Position.Y, e.Position.Z),
              pos
            )

          if d < nearestDist then
            nearestDist <- d
            nearestChaserPos <- ValueSome e

      if isNull enemyFootstep then
        let snd = assets.Sound(mgPath Assets.footstepsRunning)
        enemyFootstep <- snd.CreateInstance()
        enemyFootstep.IsLooped <- true

      match nearestChaserPos with
      | ValueSome enemy ->
        let ePos = Vector3.op_Implicit enemy.Position

        let emitter = AudioEmitter(Position = ePos, Velocity = enemy.Velocity)

        enemyFootstep.Apply3D(listener, emitter)

        if enemyFootstep.State = SoundState.Stopped then
          enemyFootstep.Play()
      | ValueNone ->
        if
          not(isNull enemyFootstep) && enemyFootstep.State = SoundState.Playing
        then
          enemyFootstep.Stop()

      // ── One-shot SFX queue (robotic, bite, laugh, injured, gasp) ──
      for i = 0 to model.SoundQueueCount - 1 do
        let evt = model.SoundQueue[i % model.SoundQueue.Length]

        let snd = assets.Sound(mgPath evt.Path)

        if evt.IsPositional then
          let ePos = Vector3.op_Implicit evt.Position

          let emitter = AudioEmitter(Position = ePos)

          let instance = snd.CreateInstance()
          instance.Apply3D(listener, emitter)
          instance.Play()
        else
          snd.Play() |> ignore

      model.SoundQueueCount <- 0
