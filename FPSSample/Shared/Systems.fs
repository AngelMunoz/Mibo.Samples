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

  /// Ticks down weapon fire cooldown, muzzle flash timer, and tracers.
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

    // Update tracers
    for i = 0 to model.Tracers.Length - 1 do
      if model.Tracers[i].Active then
        let mutable t = model.Tracers[i]
        t.Timer <- t.Timer - dt

        if t.Timer <= 0.0f then
          t.Active <- false

        model.Tracers[i] <- t

    Combat.updateReload dt model

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
        model.Enemies
        model.Colliders

    if damage > 0.0f then
      model.PlayerHealth <- Math.Max(0.0f, model.PlayerHealth - damage)

  // ── Main Update ────────────────────────────────────────────────────────────

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
      env.Animation.Update(dt, model.Enemies)
      struct (model, Cmd.none)
