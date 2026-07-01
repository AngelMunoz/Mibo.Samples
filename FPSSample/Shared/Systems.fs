namespace FPSSample

open System
open System.Numerics
open Mibo.Elmish
open Mibo.Input
open Mibo.Layout3D

/// System pipeline composition and router for the FPS sample.
///
/// `update` is a **router** (mirroring SpaceBattle's Program.fs): each `Msg`
/// is dispatched to its owning sub-system, and the sub-system's returned
/// `Intent`/`Event` is translated into cross-system `Cmd<Msg>` for other
/// systems. The `Tick` handler runs a type-enforced `System` pipeline
/// (start | pipeMutable | snapshot | dispatch | finish) so ordering is
/// explicit and a readonly boundary separates mutation phases from query
/// phases. Backend services (audio, animation) run on the snapshot after the
/// pipeline finishes.
module Systems =

  open Types

  // ── Input System ───────────────────────────────────────────────────────────

  /// Clamps player pitch to valid range. Input entry points (MouseLook,
  /// InputMapped) are handled at the router level; this runs in the mutation
  /// phase of the Tick pipeline.
  let inline inputSystem (dt: float32) (model: GameModel) : unit =
    model.Player.Pitch <-
      Math.Clamp(model.Player.Pitch, Constants.MinPitch, Constants.MaxPitch)

  // ── Effect System ──────────────────────────────────────────────────────────

  /// Ticks effect-owned timers: the hit-feedback desaturation timer and the
  /// smoke puff physics. Mutates only EffectModel. Runs in the mutation phase.
  let effectSystem
    (dt: float32)
    (model: GameModel)
    : struct (GameModel * Cmd<Msg>) =
    let effect = model.Effect

    if effect.HitEffectTimer > 0.0f then
      effect.HitEffectTimer <- Math.Max(0.0f, effect.HitEffectTimer - dt)

    let puffs = effect.SmokePuffs

    for i = 0 to puffs.Length - 1 do
      if puffs[i].Active then
        let mutable p = puffs[i]
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

        puffs[i] <- p

    model, Cmd.none

  // ── Weapon System ──────────────────────────────────────────────────────────

  /// Weapon lifecycle tick: ticks down fire cooldown, muzzle flash timer,
  /// recoil recovery, and reload progress. Mutates only WeaponModel. The
  /// muzzle flash timer lives on WeaponModel (weapon-owned); smoke puffs and
  /// the hit-effect timer are ticked by `effectSystem` (effect-owned).
  let weaponSystem
    (dt: float32)
    (model: GameModel)
    : struct (GameModel * Cmd<Msg>) =
    let weapon = model.Weapon

    if weapon.FireCooldown > 0.0f then
      weapon.FireCooldown <- weapon.FireCooldown - dt

    if weapon.MuzzleFlash.Active then
      let mutable mf = weapon.MuzzleFlash
      mf.Timer <- mf.Timer - dt

      if mf.Timer <= 0.0f then
        weapon.MuzzleFlash <- MuzzleFlash.empty
      else
        weapon.MuzzleFlash <- mf

    // Recover recoil back to zero.
    if weapon.RecoilTimer > 0.0f then
      weapon.RecoilTimer <- Math.Max(0.0f, weapon.RecoilTimer - dt)
      let t = 1.0f - weapon.RecoilTimer / 0.12f
      weapon.RecoilOffset <- 0.08f * (1.0f - t * t)

    Combat.updateReload dt weapon
    model, Cmd.none

  // ── Router helpers ──────────────────────────────────────────────────────────

  /// Translates a weapon event into cross-system Cmd (audio one-shot + effect
  /// spawns + player score). The weapon system owns the weapon lifecycle
  /// (ammo, cooldown, recoil); the router emits cross-system side effects
  /// (audio one-shot, smoke puff spawn, muzzle flash, score).
  let translateWeaponEvent(event: WeaponEvent) : Cmd<Msg> =
    match event with
    | WeaponEvent.Fired(path, muzzlePos, dir) ->
      Cmd.batch [|
        Cmd.ofMsg(Msg.AudioMsg(AudioMsg.OneShot(path, muzzlePos, false)))
        Cmd.ofMsg(Msg.EffectMsg(EffectMsg.SpawnSmoke(muzzlePos, dir)))
        Cmd.ofMsg(Msg.EffectMsg EffectMsg.MuzzleFlash)
      |]
    | WeaponEvent.ReloadStarted path ->
      // Non-positional: backend ignores position, plays at full volume centered.
      Cmd.ofMsg(Msg.AudioMsg(AudioMsg.OneShot(path, Vector3.Zero, false)))
    | WeaponEvent.EnemyKilled enemyPos ->
      Cmd.batch [|
        Cmd.ofMsg(
          Msg.AudioMsg(AudioMsg.OneShot(Assets.injured, enemyPos, true))
        )
        Cmd.ofMsg(Msg.PlayerMsg(PlayerMsg.AddScore 100))
      |]

  /// Translates an enemy event into cross-system Cmd (audio one-shot + player
  /// damage + effect). The enemy system owns the AI lifecycle; the router is
  /// the only place where enemy→audio/effect/player wiring exists.
  let translateEnemyEvent(event: EnemyEvent) : Cmd<Msg> =
    match event with
    | EnemyEvent.PlayerDamaged amount ->
      Cmd.batch [|
        Cmd.ofMsg(Msg.PlayerMsg(PlayerMsg.TakeDamage amount))
        // Non-positional: gasp plays at full volume centered (player's own pain).
        Cmd.ofMsg(
          Msg.AudioMsg(AudioMsg.OneShot(Assets.gasp, Vector3.Zero, false))
        )
        Cmd.ofMsg(Msg.EffectMsg EffectMsg.TriggerHitFlash)
      |]
    | EnemyEvent.EnemyKilled enemyPos ->
      Cmd.ofMsg(Msg.AudioMsg(AudioMsg.OneShot(Assets.injured, enemyPos, true)))
    | EnemyEvent.AttackBite enemyPos ->
      Cmd.ofMsg(Msg.AudioMsg(AudioMsg.OneShot(Assets.bite, enemyPos, true)))
    | EnemyEvent.Robotic(path, enemyPos) ->
      Cmd.ofMsg(Msg.AudioMsg(AudioMsg.OneShot(path, enemyPos, true)))
    | EnemyEvent.ChildLaugh enemyPos ->
      Cmd.ofMsg(
        Msg.AudioMsg(AudioMsg.OneShot(Assets.childLaugh, enemyPos, true))
      )

  /// Translates a pickup event into cross-system Cmd (player heal or weapon
  /// ammo refill). The pickup system owns the pickup lifecycle; the router is
  /// the only place where pickup→player/weapon wiring exists.
  let inline translatePickupEvent(event: PickupEvent) : Cmd<Msg> =
    match event with
    | PickupEvent.HealthPickup ->
      Cmd.ofMsg(Msg.PlayerMsg(PlayerMsg.Heal Constants.HealthPickupAmount))
    | PickupEvent.AmmoPickup -> Cmd.ofMsg(Msg.WeaponMsg WeaponMsg.RefillAmmo)


  // ── Enemy System ───────────────────────────────────────────────────────────

  /// Updates all enemy AI and returns events (PlayerDamaged / AttackBite /
  /// Robotic / ChildLaugh) so the router can emit cross-system Cmd (AudioMsg,
  /// EffectMsg, PlayerMsg.TakeDamage). The enemy system does NOT mutate player
  /// health — that's the router's job via PlayerMsg. Runs in the mutation phase.
  let inline enemySystem
    (dt: float32)
    (model: GameModel)
    : struct (GameModel * Cmd<Msg>) =
    let events =
      EnemyAi.update
        dt
        model.Player.Position
        model.Enemy.Enemies
        model.Enemy.Colliders

    let cmd = Cmd.batch [| for e in events -> translateEnemyEvent e |]
    model, cmd


  // ── Pickup System ──────────────────────────────────────────────────────────

  /// Handles player proximity to active pickups and respawn timers for consumed
  /// ones. Returns events (HealthPickup / AmmoPickup) so the router can emit
  /// cross-system commands (PlayerMsg.Heal / WeaponMsg.RefillAmmo). Runs in the
  /// mutation phase of the Tick pipeline.
  let pickupSystem
    (dt: float32)
    (model: GameModel)
    : struct (GameModel * Cmd<Msg>) =
    let playerPos = model.Player.Position
    let events = ResizeArray<PickupEvent>()

    for i = 0 to model.Pickup.Pickups.Length - 1 do
      let mutable pickup = model.Pickup.Pickups[i]

      if pickup.IsActive then
        // Use XZ-plane distance for pickup (player Y is eye height, pickups on ground)
        let dx = pickup.Position.X - playerPos.X
        let dz = pickup.Position.Z - playerPos.Z
        let dist = MathF.Sqrt(dx * dx + dz * dz)

        if dist <= Constants.PickupRadius then
          pickup.IsActive <- false
          pickup.RespawnTimer <- Constants.PickupRespawnTime

          match pickup.Kind with
          | Level.PickupKind.Health -> events.Add(PickupEvent.HealthPickup)
          | Level.PickupKind.Ammo -> events.Add(PickupEvent.AmmoPickup)
      else
        pickup.RespawnTimer <- pickup.RespawnTimer - dt

        if pickup.RespawnTimer <= 0.0f then
          pickup.IsActive <- true

      model.Pickup.Pickups[i] <- pickup

    let cmd = Cmd.batch [| for e in events -> translatePickupEvent e |]
    model, cmd

  // ── Restart ────────────────────────────────────────────────────────────────

  /// Resets an existing GameModel back to its initial state (used by the
  /// "Press R to restart" game-over flow). Mutates each sub-model in place.
  let restartModel(model: GameModel) : unit =
    let level = Level.LevelData.createDefault()
    model.Level <- level
    model.Colliders <- Level.LevelData.extractColliders level
    model.Enemy.Colliders <- model.Colliders

    // Player
    model.Player.Position <- level.PlayerSpawn
    model.Player.Velocity <- Vector3.Zero
    model.Player.Yaw <- 0.0f
    model.Player.Pitch <- 0.0f
    model.Player.Health <- Constants.PlayerMaxHealth
    model.Player.IsGrounded <- true
    model.Player.Score <- 0

    // Weapon
    model.Weapon.Ammo <- Constants.MaxAmmo
    model.Weapon.FireCooldown <- 0.0f
    model.Weapon.IsReloading <- false
    model.Weapon.ReloadTimer <- 0.0f
    model.Weapon.MuzzleFlash <- MuzzleFlash.empty
    model.Weapon.RecoilTimer <- 0.0f
    model.Weapon.RecoilOffset <- 0.0f
    model.Weapon.EquippedWeapon <- Assets.blasterA

    // Effect
    model.Effect.HitEffectTimer <- 0.0f

    for i = 0 to model.Effect.SmokePuffs.Length - 1 do
      model.Effect.SmokePuffs[i] <- SmokePuff.empty

    // Enemy
    model.Enemy.Enemies <-
      level.EnemySpawns |> Array.map(fun s -> Enemy.create s.Position)

    // Pickup
    model.Pickup.Pickups <-
      level.PickupSpawns |> Array.map(fun s -> Pickup.create s.Kind s.Position)

    model.TotalTime <- 0.0f

  // ── Main Update (router) ───────────────────────────────────────────────────

  /// Handles all Msg cases. The router dispatches each `Msg` to the owning
  /// sub-system and translates the sub-system's returned `Intent`/`Event` into
  /// cross-system `Cmd`. Tick composes the full `System` pipeline with a
  /// readonly snapshot boundary, then runs backend services on the snapshot.
  let update
    (env: Env)
    (msg: Msg)
    (model: GameModel)
    : struct (GameModel * Cmd<Msg>) =
    match msg with
    | Msg.InputMapped actions ->
      model.Actions <- actions
      model, Cmd.none

    | Msg.MouseLook(dx, dy) ->
      model.Player.Yaw <- model.Player.Yaw - dx * Constants.MouseSensitivity

      model.Player.Pitch <-
        Math.Clamp(
          model.Player.Pitch - dy * Constants.MouseSensitivity,
          Constants.MinPitch,
          Constants.MaxPitch
        )

      model, Cmd.none

    | Msg.Shoot ->
      let events =
        Combat.handleShoot
          model.Player
          model.Weapon
          model.Enemy.Enemies
          model.Enemy.Colliders

      let cmd = Cmd.batch [| for e in events -> translateWeaponEvent e |]
      model, cmd

    | Msg.Reload ->
      if HudLayout.isGameOver model then
        restartModel model
        model, Cmd.none
      else
        let events = Combat.startReload model.Weapon
        let cmd = Cmd.batch [| for e in events -> translateWeaponEvent e |]
        model, cmd

    | Msg.Tick gt ->
      let dt = float32 gt.ElapsedGameTime.TotalSeconds
      model.TotalTime <- model.TotalTime + dt

      // ── System pipeline: mutation → snapshot → dispatch → finish ──
      System.start model
      |> System.pipeMutable(fun model ->
        inputSystem dt model

        Physics.update
          dt
          model.Player
          model.Level
          model.Colliders
          model.Actions

        model, Cmd.none)
      |> System.pipeMutable(weaponSystem dt)
      |> System.pipeMutable(effectSystem dt)
      |> System.pipeMutable(enemySystem dt)
      |> System.pipeMutable(pickupSystem dt)
      |> System.pipeMutable(fun model ->
        // R key: restart on game over, otherwise reload the weapon.
        if model.Actions.Started.Contains(GameAction.Reload) then
          if HudLayout.isGameOver model then
            restartModel model
            model, Cmd.none
          else
            let events = Combat.startReload model.Weapon
            let cmd = Cmd.batch [| for e in events -> translateWeaponEvent e |]
            model, cmd
        else
          model, Cmd.none)
      |> GameModel.toReadonly
      |> System.pipe(fun snapshot ->
        env.Animation.Update(dt, snapshot.Enemy.Enemies)
        env.Audio.Update(dt, snapshot)
        snapshot, Cmd.none)
      |> System.finish(fun _ -> model)


    // ── Cross-system sub-message handlers ────────────────────────────────────
    | Msg.PlayerMsg playerMsg ->
      match playerMsg with
      | PlayerMsg.TakeDamage amount ->
        model.Player.Health <- Math.Max(0.0f, model.Player.Health - amount)
      | PlayerMsg.Heal amount ->
        model.Player.Health <-
          Math.Min(model.Player.Health + amount, Constants.PlayerMaxHealth)
      | PlayerMsg.AddScore points ->
        model.Player.Score <- model.Player.Score + points
      | PlayerMsg.MouseLook _
      | PlayerMsg.InputMapped _ ->
        // These are handled at the top-level Msg entry points; no-op here.
        ()

      model, Cmd.none

    | Msg.WeaponMsg weaponMsg ->
      match weaponMsg with
      | WeaponMsg.RefillAmmo ->
        model.Weapon.Ammo <-
          Math.Min(
            model.Weapon.Ammo + Constants.AmmoPickupAmount,
            Constants.MaxAmmo
          )

      model, Cmd.none

    | Msg.EnemyMsg _
    | Msg.PickupMsg _ ->
      // Enemy and pickup ticks run in the Tick pipeline directly (not as
      // messages); these wrappers are reserved for future cross-system
      // commands.
      model, Cmd.none

    | Msg.EffectMsg effectMsg ->
      match effectMsg with
      | EffectMsg.SpawnSmoke(pos, dir) ->
        let mutable sSlot = -1

        for i = 0 to model.Effect.SmokePuffs.Length - 1 do
          if sSlot < 0 && not model.Effect.SmokePuffs[i].Active then
            sSlot <- i

        if sSlot < 0 then
          sSlot <- 0

        model.Effect.SmokePuffs[sSlot] <- SmokePuff.create pos dir 1.0f
      | EffectMsg.MuzzleFlash ->
        model.Weapon.MuzzleFlash <- {
          Timer = Constants.MuzzleFlashDuration
          Active = true
        }
      | EffectMsg.TriggerHitFlash ->
        model.Effect.HitEffectTimer <- Constants.HitEffectDuration

      model, Cmd.none

    | Msg.AudioMsg audioMsg ->
      // The audio service consumes AudioMsg via its Consume method. Dispatching
      // an AudioMsg directly routes it to the service immediately (in addition
      // to the batched Cmd route from the Tick pipeline).
      env.Audio.Consume audioMsg
      model, Cmd.none
