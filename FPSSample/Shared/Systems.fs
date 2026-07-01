namespace FPSSample

open System
open System.Numerics
open Mibo.Elmish
open Mibo.Input

/// System pipeline composition for the FPS sample.
/// Each system is a function that mutates the GameModel in place.
module Systems =

  open Types

  // ── Input System ───────────────────────────────────────────────────────────

  /// Clamps player pitch to valid range (mouse look applied via MouseLook msg).
  let inputSystem (dt: float32) (model: GameModel) : unit =
    model.PlayerPitch <-
      Math.Clamp(model.PlayerPitch, Constants.MinPitch, Constants.MaxPitch)

  // ── Weapon Cooldown System ─────────────────────────────────────────────────

  /// Ticks down weapon fire cooldown, muzzle flash timer, tracers, smoke puffs,
  /// and recoil recovery.
  let weaponSystem (dt: float32) (model: GameModel) : unit =
    if model.FireCooldown > 0.0f then
      model.FireCooldown <- model.FireCooldown - dt

    if model.MuzzleFlash.Active then
      let mutable mf = model.MuzzleFlash
      mf.Timer <- mf.Timer - dt

      if mf.Timer <= 0.0f then
        model.MuzzleFlash <- MuzzleFlash.empty
      else
        model.MuzzleFlash <- mf

    // Update smoke puffs
    for i = 0 to model.SmokePuffs.Length - 1 do
      if model.SmokePuffs[i].Active then
        let mutable p = model.SmokePuffs[i]
        p.Timer <- p.Timer - dt

        if p.Timer <= 0.0f then
          p.Active <- false
        else
          // Real smoke physics: initial muzzle velocity, then drag + buoyancy.
          let drag = MathF.Exp(-2.0f * dt)
          p.Velocity <- p.Velocity * drag + Vector3(0.0f, 1.2f, 0.0f) * dt
          p.Position <- p.Position + p.Velocity * dt

          let life = 1.0f - p.Timer / SmokePuff.duration
          p.Scale <- 1.0f + life * 2.0f

        model.SmokePuffs[i] <- p

    // Recover recoil back to zero.
    if model.RecoilTimer > 0.0f then
      model.RecoilTimer <- Math.Max(0.0f, model.RecoilTimer - dt)
      let t = 1.0f - model.RecoilTimer / 0.12f
      model.RecoilOffset <- 0.08f * (1.0f - t * t)

    Combat.updateReload dt model

    // Tick down the hit-effect desaturation timer.
    if model.HitEffectTimer > 0.0f then
      model.HitEffectTimer <- Math.Max(0.0f, model.HitEffectTimer - dt)

  // ── Pickups System ─────────────────────────────────────────────────────────

  /// Handles player proximity to active pickups and respawn timers for consumed ones.
  let pickupSystem (dt: float32) (model: GameModel) : unit =
    let playerPos = model.PlayerPosition

    for i = 0 to model.Pickups.Length - 1 do
      let mutable pickup = model.Pickups[i]

      if pickup.IsActive then
        // Use XZ-plane distance for pickup (player Y is eye height, pickups are on ground)
        let dx = pickup.Position.X - playerPos.X
        let dz = pickup.Position.Z - playerPos.Z
        let dist = MathF.Sqrt(dx * dx + dz * dz)

        if dist <= Constants.PickupRadius then
          pickup.IsActive <- false
          pickup.RespawnTimer <- Constants.PickupRespawnTime

          match pickup.Kind with
          | Level.PickupKind.Health ->
            model.PlayerHealth <-
              Math.Min(
                model.PlayerHealth + Constants.HealthPickupAmount,
                Constants.PlayerMaxHealth
              )
          | Level.PickupKind.Ammo ->
            model.Ammo <-
              Math.Min(
                model.Ammo + Constants.AmmoPickupAmount,
                Constants.MaxAmmo
              )
      else
        pickup.RespawnTimer <- pickup.RespawnTimer - dt

        if pickup.RespawnTimer <= 0.0f then
          pickup.IsActive <- true

      model.Pickups[i] <- pickup

  // ── Enemy System ───────────────────────────────────────────────────────────

  /// Updates all enemy AI and applies attack damage to the player.
  let enemySystem (dt: float32) (model: GameModel) : unit =
    let damage =
      EnemyAi.update
        dt
        model.PlayerPosition
        model.PlayerHealth
        model
        model.Enemies
        model.Colliders

    if damage > 0.0f then
      model.PlayerHealth <- Math.Max(0.0f, model.PlayerHealth - damage)
      // Trigger the hit-feedback flash + gasp sound.
      model.HitEffectTimer <- Constants.HitEffectDuration
      queueSound model Assets.gasp

  // ── Main Update ────────────────────────────────────────────────────────────

  /// Resets an existing GameModel back to its initial state (used by the
  /// "Press R to restart" game-over flow). Mutates the model in place.
  let restartModel(model: GameModel) : unit =
    let level = Level.LevelData.createDefault()
    model.Level <- level
    model.Colliders <- Level.LevelData.extractColliders level
    model.PlayerPosition <- level.PlayerSpawn
    model.PlayerVelocity <- Vector3.Zero
    model.PlayerYaw <- 0.0f
    model.PlayerPitch <- 0.0f
    model.PlayerHealth <- Constants.PlayerMaxHealth
    model.IsGrounded <- true
    model.Score <- 0
    model.Ammo <- Constants.MaxAmmo
    model.FireCooldown <- 0.0f
    model.IsReloading <- false
    model.ReloadTimer <- 0.0f
    model.MuzzleFlash <- MuzzleFlash.empty

    for i = 0 to model.SmokePuffs.Length - 1 do
      model.SmokePuffs[i] <- SmokePuff.empty

    model.RecoilTimer <- 0.0f
    model.RecoilOffset <- 0.0f
    model.LastFireSound <- ""
    model.LastReloadSound <- ""
    model.EquippedWeapon <- Assets.blasterA
    model.SoundQueueCount <- 0
    model.IsPlayerWalking <- false

    model.Enemies <-
      level.EnemySpawns |> Array.map(fun s -> Enemy.create s.Position)

    model.Pickups <-
      level.PickupSpawns |> Array.map(fun s -> Pickup.create s.Kind s.Position)

    model.HitEffectTimer <- 0.0f

  /// Handles all Msg cases. Tick composes the full system pipeline.
  /// The animation service from the env is called after the enemy system
  /// updates each enemy's logical animation name, so playback stays in sync
  /// with AI state.
  let update
    (env: Env)
    (msg: Msg)
    (model: GameModel)
    : struct (GameModel * Cmd<Msg>) =
    match msg with
    | Msg.InputMapped actions ->
      model.Actions <- actions
      struct (model, Cmd.none)

    | Msg.MouseLook(dx, dy) ->
      model.PlayerYaw <- model.PlayerYaw - dx * Constants.MouseSensitivity

      model.PlayerPitch <-
        Math.Clamp(
          model.PlayerPitch - dy * Constants.MouseSensitivity,
          Constants.MinPitch,
          Constants.MaxPitch
        )

      struct (model, Cmd.none)

    | Msg.Shoot ->
      Combat.handleShoot model
      struct (model, Cmd.none)

    | Msg.Reload ->
      if HudLayout.isGameOver model then
        restartModel model
      else
        Combat.startReload model

      struct (model, Cmd.none)

    | Msg.Tick gt ->
      let dt = float32 gt.ElapsedGameTime.TotalSeconds
      model.TotalTime <- model.TotalTime + dt

      inputSystem dt model
      Physics.update dt model
      weaponSystem dt model
      enemySystem dt model
      pickupSystem dt model

      // R key: restart on game over, otherwise reload the weapon.
      if model.Actions.Started.Contains(GameAction.Reload) then
        if HudLayout.isGameOver model then
          restartModel model
        else
          Combat.startReload model

      env.Animation.Update(dt, model.Enemies)
      env.Audio.Update(dt, model)
      struct (model, Cmd.none)
