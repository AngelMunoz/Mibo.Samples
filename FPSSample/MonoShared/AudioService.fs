namespace FPSSample.MonoShared

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Audio
open Mibo.Elmish
open FPSSample
open FPSSample.Types

/// <summary>
/// MonoGame-specific audio service. Manages one-shot sounds (fire, reload, SFX)
/// via <c>Consume</c> and looping footstep instances via <c>Update</c>. Uses
/// MonoGame's native 3D audio via <c>SoundEffectInstance.Apply3D(listener, emitter)</c>
/// for positional sounds. Loop intent (player/enemy footsteps) is derived from
/// the snapshot each frame and applied idempotently against
/// <c>SoundEffectInstance.State</c> — no audio flags in Elmish.
/// </summary>
type AudioService() =
  let mutable ctx = Unchecked.defaultof<GameContext>
  let mutable initialized = false

  // Looping footstep instances (long-lived, reused every frame).
  let mutable playerFootstep: SoundEffectInstance = null
  let mutable enemyFootstep: SoundEffectInstance = null

  // Cached listener for Consume (positional one-shots). Updated each Update.
  let mutable cachedListener: AudioListener = AudioListener()

  // Converts a raylib-style asset path to a MonoGame content pipeline path.
  let mgPath(path: string) =
    path
      .Replace("assets/", "")
      .Replace(".glb", "")
      .Replace(".fbx", "")
      .Replace(".mp3", "")
      .Replace(".wav", "")

  // ── Play a one-shot with optional positional attenuation ──
  let playOneShot (assets: IAssets) (msg: AudioMsg) =
    match msg with
    | AudioMsg.OneShot(path, position, isPositional) ->
      let snd = assets.Sound(mgPath path)

      if isPositional then
        let ePos = Vector3.op_Implicit position
        let emitter = AudioEmitter(Position = ePos)
        let instance = snd.CreateInstance()
        instance.Apply3D(cachedListener, emitter)
        instance.Play()
      else
        snd.Play() |> ignore

  interface IAudioService with
    member _.Init(gameCtx: GameContext) : unit =
      ctx <- gameCtx
      initialized <- true

    member _.Consume(audioMsg: AudioMsg) : unit =
      if not initialized then
        ()

      let assets = GameContext.getService<IAssets> ctx
      playOneShot assets audioMsg

    member _.Update(_: float32, snapshot: Snapshot) : unit =
      if not initialized then
        ()

      let assets = GameContext.getService<IAssets> ctx

      let pos = Vector3.op_Implicit snapshot.Player.Position

      let forwardNumerics =
        ViewMath.cameraForward snapshot.Player.Yaw snapshot.Player.Pitch

      let forward =
        Vector3(forwardNumerics.X, forwardNumerics.Y, forwardNumerics.Z)

      let listener =
        AudioListener(
          Position = pos,
          Forward = forward,
          Velocity = Vector3.op_Implicit snapshot.Player.Velocity
        )

      // Cache for Consume (positional one-shots).
      cachedListener <- listener

      // ── Looping player footsteps (derived from snapshot velocity) ──
      if isNull playerFootstep then
        let snd = assets.Sound(mgPath Assets.footstepsWalking)
        playerFootstep <- snd.CreateInstance()
        playerFootstep.IsLooped <- true

      let horizontalSpeed =
        MathF.Sqrt(
          snapshot.Player.Velocity.X * snapshot.Player.Velocity.X
          + snapshot.Player.Velocity.Z * snapshot.Player.Velocity.Z
        )

      let isWalking = snapshot.Player.IsGrounded && horizontalSpeed > 0.5f

      if isWalking then
        if
          playerFootstep.State = SoundState.Stopped
          || playerFootstep.State = SoundState.Paused
        then
          playerFootstep.Volume <- 0.4f
          playerFootstep.Pan <- 0.0f
          playerFootstep.Play()
      else if playerFootstep.State = SoundState.Playing then
        playerFootstep.Pause()

      // ── Looping enemy footsteps (nearest chasing enemy, from snapshot) ──
      let mutable nearestChaserPos = ValueNone
      let mutable nearestDist = Single.MaxValue

      for e in snapshot.Enemy.Enemies do
        if e.State <> EnemyState.Dead && e.IsChasing then
          let d = Vector3.Distance(Vector3.op_Implicit e.Position, pos)

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
