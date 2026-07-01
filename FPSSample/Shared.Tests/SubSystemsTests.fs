module FPSSample.Tests.SubSystemsTests

open Expecto
open System
open System.Collections.Generic
open System.Numerics
open Mibo.Elmish
open Mibo.Input
open FPSSample
open FPSSample.Types
open FPSSample.Systems

// ── Test helpers ────────────────────────────────────────────────────────────

/// No-op animation service for tests (avoids raylib/asset dependencies).
type private NoopAnimationService() =
  interface IEnemyAnimationService with
    member _.Init(_, _) = ()
    member _.Update(_, _) = ()

/// Recording audio service: captures every AudioMsg passed to Consume and
/// every (dt, snapshot) passed to Update, so tests can assert on emitted
/// AudioMsg values.
type private RecordingAudioService() =
  let consumed = ResizeArray<AudioMsg>()
  let updates = ResizeArray<float32 * Snapshot>()

  member _.Consumed = consumed
  member _.Updates = updates

  interface IAudioService with
    member _.Init(_) = ()
    member _.Consume audioMsg = consumed.Add audioMsg
    member _.Update(dt, snapshot) = updates.Add((dt, snapshot))

/// Creates a GameModel with an empty level (no colliders) for testing.
let private createTestModel() : GameModel =
  let model = GameModel()
  model.Colliders <- [||]
  model.Enemy.Colliders <- [||]
  model.Level <- Level.LevelData.createDefault()
  model

/// Creates a GameTime for a tick message.
let private gameTime(seconds: float32) : GameTime = {
  ElapsedGameTime = TimeSpan.FromSeconds(float seconds)
  TotalTime = TimeSpan.Zero
}

/// Builds a test env with a recording audio service and returns both.
let private recordingEnv() : Env * RecordingAudioService =
  let audio = RecordingAudioService()

  let env: Env = {
    Animation = NoopAnimationService()
    Audio = audio
  }

  env, audio

/// Drains a Cmd<Msg> into a flat list of Msg values by executing its effects
/// against a capturing dispatch. This mirrors what the Elmish loop does when it
/// processes a returned Cmd: it invokes each Effect with a dispatch function.
/// Returns the list of Msg values that would be dispatched (in order).
let private drainCmd(cmd: Cmd<Msg>) : Msg list =
  let collected = ResizeArray<Msg>()

  let dispatch(msg: Msg) = collected.Add msg

  match cmd with
  | Cmd.Empty -> ()
  | Cmd.Msg msg -> dispatch msg
  | Cmd.Single eff -> eff.Invoke dispatch
  | Cmd.Batch effs ->
    for i = 0 to effs.Length - 1 do
      effs[i].Invoke dispatch
  | Cmd.DeferNextFrame effs ->
    for i = 0 to effs.Length - 1 do
      effs[i].Invoke dispatch
  | Cmd.NowAndDeferNextFrame(now, next) ->
    for i = 0 to now.Length - 1 do
      now[i].Invoke dispatch

    for i = 0 to next.Length - 1 do
      next[i].Invoke dispatch
  | Cmd.Quit -> ()

  List.ofSeq collected

// ── Sub-system update function tests ─────────────────────────────────────────
//
// These test the per-subsystem update functions directly (not through the
// router's update). The sub-system functions take (dt, GameModel) and return
// struct (GameModel * Cmd<Msg>) — they mutate only their own sub-model slice
// and build the cross-system Cmd internally. Tests inspect the sub-model after
// the call and drain the Cmd where cross-system messages are expected.

[<Tests>]
let subSystemTests =
  testList "SubSystems" [

    // ── weaponSystem (WeaponModel mutation) ────────────────────────────────
    testList "weaponSystem" [
      testCase "ticks down fire cooldown"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.FireCooldown <- 0.3f
        let struct (m, _) = weaponSystem 0.1f model

        Expect.floatClose
          Accuracy.low
          (float m.Weapon.FireCooldown)
          0.2
          "Cooldown decremented"

      testCase "ticks down muzzle flash to inactive"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.MuzzleFlash <- { Timer = 0.05f; Active = true }
        let struct (m, _) = weaponSystem 0.1f model
        Expect.isFalse m.Weapon.MuzzleFlash.Active "Muzzle flash expired"
        Expect.equal m.Weapon.MuzzleFlash.Timer 0.0f "Timer at 0"

      testCase "ticks down active muzzle flash without expiring"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.MuzzleFlash <- { Timer = 0.5f; Active = true }
        let struct (m, _) = weaponSystem 0.1f model
        Expect.isTrue m.Weapon.MuzzleFlash.Active "Still active"

        Expect.floatClose
          Accuracy.low
          (float m.Weapon.MuzzleFlash.Timer)
          0.4
          "Timer decremented"

      testCase "recovers recoil back toward zero"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.RecoilTimer <- 0.12f
        model.Weapon.RecoilOffset <- 0.08f
        let struct (m, _) = weaponSystem 0.06f model

        Expect.floatClose
          Accuracy.low
          (float m.Weapon.RecoilTimer)
          0.06
          "Recoil timer decremented"
        // Offset should still be positive (recovery not complete).
        Expect.isGreaterThan
          m.Weapon.RecoilOffset
          0.0f
          "Recoil offset still positive"

      testCase "completes reload when timer elapses"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.IsReloading <- true
        model.Weapon.ReloadTimer <- 0.1f
        model.Weapon.Ammo <- 5
        let struct (m, _) = weaponSystem 0.2f model
        Expect.isFalse m.Weapon.IsReloading "Reload finished"
        Expect.equal m.Weapon.Ammo Constants.MaxAmmo "Ammo refilled"

      testCase "does not complete reload when timer not elapsed"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.IsReloading <- true
        model.Weapon.ReloadTimer <- 0.5f
        model.Weapon.Ammo <- 5
        let struct (m, _) = weaponSystem 0.1f model
        Expect.isTrue m.Weapon.IsReloading "Still reloading"
        Expect.equal m.Weapon.Ammo 5 "Ammo unchanged until reload completes"

      testCase "does not mutate EffectModel (smoke puffs owned by effectSystem)"
      <| fun _ ->
        let model = createTestModel()

        model.Effect.SmokePuffs[0] <-
          SmokePuff.create
            (Vector3(1.0f, 2.0f, 3.0f))
            (Vector3(0.0f, 0.0f, -1.0f))
            1.0f

        let puffsBefore = model.Effect.SmokePuffs |> Array.map id
        let struct (_, _) = weaponSystem 0.1f model
        let puffsAfter = model.Effect.SmokePuffs |> Array.map id

        // weaponSystem should not touch EffectModel.SmokePuffs.
        Expect.equal puffsAfter.Length puffsBefore.Length "Puff count unchanged"

        Expect.equal
          puffsAfter[0]
          puffsBefore[0]
          "Smoke puff unchanged by weaponSystem"
    ]

    // ── effectSystem (EffectModel mutation) ────────────────────────────────
    testList "effectSystem" [
      testCase "ticks down hit-effect timer"
      <| fun _ ->
        let model = createTestModel()
        model.Effect.HitEffectTimer <- 0.4f
        let struct (m, _) = effectSystem 0.1f model

        Expect.floatClose
          Accuracy.low
          (float m.Effect.HitEffectTimer)
          0.3
          "Hit-effect timer decremented"

      testCase "clamps hit-effect timer at zero"
      <| fun _ ->
        let model = createTestModel()
        model.Effect.HitEffectTimer <- 0.05f
        let struct (m, _) = effectSystem 0.2f model

        Expect.equal
          m.Effect.HitEffectTimer
          0.0f
          "Hit-effect timer clamped at 0"

      testCase "ticks active smoke puff physics (drag + buoyancy + scale)"
      <| fun _ ->
        let model = createTestModel()

        model.Effect.SmokePuffs[0] <-
          SmokePuff.create
            (Vector3(0.0f, 0.0f, 0.0f))
            (Vector3(0.0f, 0.0f, -1.0f))
            1.0f

        let timerBefore = model.Effect.SmokePuffs[0].Timer
        let posBefore = model.Effect.SmokePuffs[0].Position
        let struct (m, _) = effectSystem 0.1f model
        let puff = m.Effect.SmokePuffs[0]

        Expect.isTrue puff.Active "Still active (timer > 0)"

        Expect.isLessThan
          (float puff.Timer)
          (float timerBefore)
          "Timer decremented"

        Expect.isGreaterThan
          puff.Position.Y
          posBefore.Y
          "Smoke rises (buoyancy)"

        Expect.isGreaterThan puff.Scale 1.0f "Scale grows over life"

      testCase "deactivates smoke puff when timer elapses"
      <| fun _ ->
        let model = createTestModel()

        model.Effect.SmokePuffs[0] <-
          SmokePuff.create
            (Vector3(0.0f, 0.0f, 0.0f))
            (Vector3(0.0f, 0.0f, -1.0f))
            1.0f

        // Set timer just above zero so one tick expires it.
        model.Effect.SmokePuffs[0].Timer <- 0.05f
        let struct (m, _) = effectSystem 0.1f model
        Expect.isFalse m.Effect.SmokePuffs[0].Active "Smoke puff deactivated"

      testCase "does not touch WeaponModel"
      <| fun _ ->
        let model = createTestModel()
        model.Weapon.FireCooldown <- 0.3f
        model.Weapon.MuzzleFlash <- { Timer = 0.5f; Active = true }

        let cdBefore = model.Weapon.FireCooldown
        let mfBefore = model.Weapon.MuzzleFlash

        let struct (_, _) = effectSystem 0.1f model

        Expect.equal
          model.Weapon.FireCooldown
          cdBefore
          "Weapon cooldown untouched"

        Expect.equal
          model.Weapon.MuzzleFlash
          mfBefore
          "Weapon muzzle flash untouched"
    ]

    // ── pickupSystem (PickupModel mutation + cross-system Cmd) ────────────
    // pickupSystem returns struct (GameModel * Cmd<Msg>) — the events are
    // translated to cross-system Cmd internally. Tests drain the Cmd to find
    // the emitted PlayerMsg.Heal / WeaponMsg.RefillAmmo messages.
    testList "pickupSystem" [
      testCase "health pickup in range emits Heal PlayerMsg"
      <| fun _ ->
        let model = createTestModel()
        model.Player.Position <- Vector3(0.0f, 0.0f, 0.0f)

        model.Pickup.Pickups <- [|
          {
            Kind = Level.PickupKind.Health
            Position = Vector3(0.1f, 0.0f, 0.0f)
            IsActive = true
            RespawnTimer = 0.0f
          }
        |]

        let struct (m, cmd) = pickupSystem 0.1f model
        let msgs = drainCmd cmd

        // Router translates HealthPickup → PlayerMsg.Heal.
        let hasHeal =
          msgs
          |> List.exists (function
            | Msg.PlayerMsg(PlayerMsg.Heal _) -> true
            | _ -> false)

        Expect.isTrue hasHeal "Heal PlayerMsg emitted"
        Expect.isFalse m.Pickup.Pickups[0].IsActive "Pickup consumed"

        Expect.equal
          m.Pickup.Pickups[0].RespawnTimer
          Constants.PickupRespawnTime
          "Respawn timer set"

      testCase "ammo pickup in range emits RefillAmmo WeaponMsg"
      <| fun _ ->
        let model = createTestModel()
        model.Player.Position <- Vector3(0.0f, 0.0f, 0.0f)

        model.Pickup.Pickups <- [|
          {
            Kind = Level.PickupKind.Ammo
            Position = Vector3(0.4f, 0.0f, 0.0f)
            IsActive = true
            RespawnTimer = 0.0f
          }
        |]

        let struct (_, cmd) = pickupSystem 0.1f model
        let msgs = drainCmd cmd

        let hasRefill =
          msgs
          |> List.exists (function
            | Msg.WeaponMsg WeaponMsg.RefillAmmo -> true
            | _ -> false)

        Expect.isTrue hasRefill "RefillAmmo WeaponMsg emitted"

      testCase "pickup out of range emits no messages and stays active"
      <| fun _ ->
        let model = createTestModel()
        model.Player.Position <- Vector3(0.0f, 0.0f, 0.0f)

        model.Pickup.Pickups <- [|
          {
            Kind = Level.PickupKind.Health
            Position = Vector3(100.0f, 0.0f, 0.0f)
            IsActive = true
            RespawnTimer = 0.0f
          }
        |]

        let struct (m, cmd) = pickupSystem 0.1f model
        let msgs = drainCmd cmd

        Expect.isEmpty msgs "No messages when out of range"
        Expect.isTrue m.Pickup.Pickups[0].IsActive "Pickup still active"

      testCase "respawns consumed pickup after timer elapses"
      <| fun _ ->
        let model = createTestModel()

        model.Pickup.Pickups <- [|
          {
            Kind = Level.PickupKind.Ammo
            Position = Vector3(100.0f, 0.0f, 0.0f)
            IsActive = false
            RespawnTimer = 0.1f
          }
        |]

        let struct (m, cmd) = pickupSystem 0.2f model
        let msgs = drainCmd cmd

        Expect.isEmpty msgs "No messages on respawn"
        Expect.isTrue m.Pickup.Pickups[0].IsActive "Pickup respawned"

      testCase "does not mutate PlayerModel or WeaponModel"
      <| fun _ ->
        let model = createTestModel()
        model.Player.Health <- 50.0f
        model.Player.Position <- Vector3(0.0f, 0.0f, 0.0f)
        model.Weapon.Ammo <- 5

        model.Pickup.Pickups <- [|
          {
            Kind = Level.PickupKind.Health
            Position = Vector3(0.1f, 0.0f, 0.0f)
            IsActive = true
            RespawnTimer = 0.0f
          }
        |]

        let struct (_, _) = pickupSystem 0.1f model

        // pickupSystem should NOT heal the player or refill ammo — it only
        // emits events (as Cmd); the router/loop translates those into
        // PlayerMsg/WeaponMsg which actually mutate the sub-models.
        Expect.equal
          model.Player.Health
          50.0f
          "Player health untouched by pickupSystem"

        Expect.equal model.Weapon.Ammo 5 "Weapon ammo untouched by pickupSystem"
    ]

    // ── enemySystem (EnemyModel mutation + cross-system Cmd) ──────────────
    // enemySystem returns struct (GameModel * Cmd<Msg>) — the events are
    // translated to cross-system Cmd internally. Tests drain the Cmd to find
    // the emitted PlayerMsg.TakeDamage / AudioMsg / EffectMsg messages.
    testList "enemySystem" [
      testCase "idle enemy far from player emits no messages and stays idle"
      <| fun _ ->
        let model = createTestModel()
        model.Enemy.Enemies <- [| Enemy.create(Vector3(100.0f, 0.0f, 100.0f)) |]

        let struct (m, cmd) = enemySystem 0.016f model
        let msgs = drainCmd cmd

        Expect.isEmpty msgs "No messages from idle far enemy"
        Expect.equal m.Enemy.Enemies[0].State EnemyState.Idle "Still idle"

      testCase "enemy in attack range emits PlayerDamaged + AttackBite cmds"
      <| fun _ ->
        let model = createTestModel()
        model.Player.Position <- Vector3(0.0f, 0.0f, 0.0f)
        model.Enemy.Enemies <- [| Enemy.create(Vector3(1.0f, 0.0f, 0.0f)) |]

        let struct (m, cmd) = enemySystem 0.016f model
        let msgs = drainCmd cmd

        // Router translates PlayerDamaged → PlayerMsg.TakeDamage + AudioMsg(gasp)
        // + EffectMsg.TriggerHitFlash; AttackBite → AudioMsg(bite).
        let hasDamage =
          msgs
          |> List.exists (function
            | Msg.PlayerMsg(PlayerMsg.TakeDamage _) -> true
            | _ -> false)

        let hasBiteAudio =
          msgs
          |> List.exists (function
            | Msg.AudioMsg(AudioMsg.OneShot(p, _, true)) when p = Assets.bite ->
              true
            | _ -> false)

        Expect.isTrue hasDamage "TakeDamage PlayerMsg emitted"
        Expect.isTrue hasBiteAudio "Bite AudioMsg emitted (positional)"

        // Verify the damage amount.
        match
          msgs
          |> List.tryPick (function
            | Msg.PlayerMsg(PlayerMsg.TakeDamage amt) -> Some amt
            | _ -> None)
        with
        | Some amt ->
          Expect.equal amt Constants.EnemyAttackDamage "Correct damage amount"
        | None -> failwith "Expected PlayerMsg.TakeDamage"

      testCase "enemy with expired robotic timer emits Robotic AudioMsg"
      <| fun _ ->
        let model = createTestModel()
        let mutable enemy = Enemy.create(Vector3(100.0f, 0.0f, 100.0f))
        enemy.RoboticTimer <- 0.001f
        model.Enemy.Enemies <- [| enemy |]

        let struct (_, cmd) = enemySystem 0.016f model
        let msgs = drainCmd cmd

        // Router translates Robotic → AudioMsg.OneShot(path, pos, positional).
        let hasRobotic =
          msgs
          |> List.exists (function
            | Msg.AudioMsg(AudioMsg.OneShot(_, _, true)) -> true
            | _ -> false)

        Expect.isTrue hasRobotic "Robotic AudioMsg emitted when timer expires"

      testCase "does not mutate PlayerModel (damage is router's job)"
      <| fun _ ->
        let model = createTestModel()
        model.Player.Position <- Vector3(0.0f, 0.0f, 0.0f)
        model.Player.Health <- Constants.PlayerMaxHealth
        model.Enemy.Enemies <- [| Enemy.create(Vector3(1.0f, 0.0f, 0.0f)) |]

        let struct (_, _) = enemySystem 0.016f model

        // enemySystem emits PlayerDamaged as a Cmd but does NOT reduce player
        // health directly — the router/loop translates the Cmd into
        // PlayerMsg.TakeDamage which actually mutates the player.
        Expect.equal
          model.Player.Health
          Constants.PlayerMaxHealth
          "Player health untouched by enemySystem"
    ]
  ]

// ── Per-Msg input/output processing tests ───────────────────────────────────
//
// These test that each consuming sub-message handler processes its input Msg
// correctly and produces the correct model mutation (and, where applicable,
// the correct output Cmd). Each Msg case is tested in isolation through the
// router's `update` function.

[<Tests>]
let msgProcessingTests =
  testList "Msg processing" [

    // ── Msg.PlayerMsg ──────────────────────────────────────────────────────
    testList "Msg.PlayerMsg" [
      testCase "TakeDamage reduces health (hit-flash is a separate EffectMsg)"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        model.Player.Health <- 80.0f

        let struct (m, cmd) =
          Systems.update env (Msg.PlayerMsg(PlayerMsg.TakeDamage 30.0f)) model

        Expect.equal m.Player.Health 50.0f "Health reduced by damage amount"
        // Hit-flash is owned by EffectModel and set by EffectMsg.TriggerHitFlash
        // (the router emits both TakeDamage + TriggerHitFlash from a PlayerDamaged
        // event). TakeDamage alone does not touch the effect timer.
        Expect.equal
          m.Effect.HitEffectTimer
          0.0f
          "Hit-flash untouched by TakeDamage"

        Expect.equal cmd Cmd.Empty "No Cmd emitted by TakeDamage"

      testCase "TakeDamage clamps health at zero"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        model.Player.Health <- 5.0f

        let struct (m, _) =
          Systems.update env (Msg.PlayerMsg(PlayerMsg.TakeDamage 30.0f)) model

        Expect.equal m.Player.Health 0.0f "Health clamped at 0"

      testCase "Heal increases health up to max"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        model.Player.Health <- 80.0f

        let struct (m, _) =
          Systems.update env (Msg.PlayerMsg(PlayerMsg.Heal 25.0f)) model

        Expect.equal
          m.Player.Health
          Constants.PlayerMaxHealth
          "Health clamped at max"

      testCase "Heal below max adds exactly the heal amount"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        model.Player.Health <- 50.0f

        let struct (m, _) =
          Systems.update env (Msg.PlayerMsg(PlayerMsg.Heal 25.0f)) model

        Expect.equal m.Player.Health 75.0f "Health increased by heal amount"

      testCase "AddScore increases score"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        model.Player.Score <- 0

        let struct (m, _) =
          Systems.update env (Msg.PlayerMsg(PlayerMsg.AddScore 100)) model

        Expect.equal m.Player.Score 100 "Score increased"

      testCase "AddScore accumulates"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        model.Player.Score <- 50

        let struct (m, _) =
          Systems.update env (Msg.PlayerMsg(PlayerMsg.AddScore 100)) model

        Expect.equal m.Player.Score 150 "Score accumulates"

      testCase
        "MouseLook sub-msg is a no-op (handled at top-level Msg.MouseLook)"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        let yawBefore = model.Player.Yaw

        let struct (m, cmd) =
          Systems.update
            env
            (Msg.PlayerMsg(PlayerMsg.MouseLook(100.0f, 50.0f)))
            model

        Expect.equal
          m.Player.Yaw
          yawBefore
          "Yaw unchanged by PlayerMsg.MouseLook"

        Expect.equal cmd Cmd.Empty "No Cmd emitted"

      testCase
        "InputMapped sub-msg is a no-op (handled at top-level Msg.InputMapped)"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        let actionsBefore = model.Actions

        let struct (m, cmd) =
          Systems.update
            env
            (Msg.PlayerMsg(PlayerMsg.InputMapped ActionState.empty))
            model

        Expect.equal
          m.Actions
          actionsBefore
          "Actions unchanged by PlayerMsg.InputMapped"

        Expect.equal cmd Cmd.Empty "No Cmd emitted"
    ]

    // ── Msg.WeaponMsg ──────────────────────────────────────────────────────
    testList "Msg.WeaponMsg" [
      testCase "RefillAmmo adds amount up to max"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        model.Weapon.Ammo <- 5

        let struct (m, cmd) =
          Systems.update env (Msg.WeaponMsg WeaponMsg.RefillAmmo) model

        Expect.equal m.Weapon.Ammo 15 "Ammo increased by pickup amount"
        Expect.equal cmd Cmd.Empty "No Cmd emitted by RefillAmmo"

      testCase "RefillAmmo clamps at max"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        model.Weapon.Ammo <- 25

        let struct (m, _) =
          Systems.update env (Msg.WeaponMsg WeaponMsg.RefillAmmo) model

        Expect.equal m.Weapon.Ammo Constants.MaxAmmo "Ammo clamped at max"
    ]

    // ── Msg.EffectMsg ─────────────────────────────────────────────────────
    testList "Msg.EffectMsg" [
      testCase "SpawnSmoke allocates a smoke puff to a free slot"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        let pos = Vector3(1.0f, 2.0f, 3.0f)
        let dir = Vector3(0.0f, 0.0f, -1.0f)

        let struct (m, cmd) =
          Systems.update
            env
            (Msg.EffectMsg(EffectMsg.SpawnSmoke(pos, dir)))
            model

        // Find the active puff — there should be exactly one.
        let activePuffs = m.Effect.SmokePuffs |> Array.filter(fun p -> p.Active)

        Expect.equal activePuffs.Length 1 "Exactly one active puff"
        Expect.equal cmd Cmd.Empty "No Cmd emitted by SpawnSmoke"
        let puff = activePuffs[0]
        Expect.equal puff.Position pos "Puff spawned at given position"
        Expect.isGreaterThan puff.Timer 0.0f "Puff timer set"

      testCase "SpawnSmoke reuses first slot when all are inactive"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()

        let struct (m, _) =
          Systems.update
            env
            (Msg.EffectMsg(EffectMsg.SpawnSmoke(Vector3.Zero, Vector3.UnitZ)))
            model

        Expect.isTrue m.Effect.SmokePuffs[0].Active "First slot used"

      testCase "SpawnSmoke overwrites slot 0 when pool is full"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()

        // Fill all smoke puff slots.
        for i = 0 to model.Effect.SmokePuffs.Length - 1 do
          model.Effect.SmokePuffs[i] <-
            SmokePuff.create
              (Vector3(0.0f, 0.0f, 0.0f))
              (Vector3(0.0f, 0.0f, -1.0f))
              1.0f

        let struct (m, _) =
          Systems.update
            env
            (Msg.EffectMsg(
              EffectMsg.SpawnSmoke(Vector3(9.0f, 9.0f, 9.0f), Vector3.UnitZ)
            ))
            model

        // No free slot → falls back to slot 0 (overwrites oldest).
        Expect.equal
          m.Effect.SmokePuffs[0].Position
          (Vector3(9.0f, 9.0f, 9.0f))
          "Slot 0 overwritten"

      testCase "MuzzleFlash sets the weapon muzzle flash timer active"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()

        let struct (m, cmd) =
          Systems.update env (Msg.EffectMsg EffectMsg.MuzzleFlash) model

        Expect.isTrue m.Weapon.MuzzleFlash.Active "Muzzle flash active"

        Expect.equal
          m.Weapon.MuzzleFlash.Timer
          Constants.MuzzleFlashDuration
          "Timer set to duration"

        Expect.equal cmd Cmd.Empty "No Cmd emitted by MuzzleFlash"

      testCase "TriggerHitFlash sets the effect hit-flash timer"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()

        let struct (m, cmd) =
          Systems.update env (Msg.EffectMsg EffectMsg.TriggerHitFlash) model

        Expect.equal
          m.Effect.HitEffectTimer
          Constants.HitEffectDuration
          "Hit-flash timer set"

        Expect.equal cmd Cmd.Empty "No Cmd emitted by TriggerHitFlash"
    ]

    // ── Msg.AudioMsg ───────────────────────────────────────────────────────
    testList "Msg.AudioMsg" [
      testCase "routes the AudioMsg to env.Audio.Consume"
      <| fun _ ->
        let env, audio = recordingEnv()
        let model = createTestModel()
        let msg = AudioMsg.OneShot("foo.wav", Vector3.Zero, false)

        let struct (_, cmd) = Systems.update env (Msg.AudioMsg msg) model

        Expect.equal audio.Consumed.Count 1 "Consume called once"
        Expect.equal audio.Consumed[0] msg "Same AudioMsg consumed"
        Expect.equal cmd Cmd.Empty "No Cmd emitted (Consume is a side effect)"

      testCase "positional AudioMsg is consumed with its position"
      <| fun _ ->
        let env, audio = recordingEnv()
        let model = createTestModel()
        let pos = Vector3(5.0f, 0.0f, -3.0f)
        let msg = AudioMsg.OneShot("robot.wav", pos, true)

        let struct (_, _) = Systems.update env (Msg.AudioMsg msg) model

        Expect.equal audio.Consumed.Count 1 "Consume called"

        match audio.Consumed[0] with
        | AudioMsg.OneShot(path, p, isPositional) ->
          Expect.equal path "robot.wav" "Path preserved"
          Expect.equal p pos "Position preserved"
          Expect.isTrue isPositional "Positional flag preserved"
    ]

    // ── Msg.EnemyMsg / Msg.PickupMsg (reserved, no-op) ────────────────────
    testList "Msg.EnemyMsg / Msg.PickupMsg" [
      testCase "EnemyMsg.Tick is a no-op (tick runs in Tick pipeline)"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()
        model.Enemy.Enemies <- [| Enemy.create(Vector3(10.0f, 0.0f, 0.0f)) |]
        let stateBefore = model.Enemy.Enemies[0].State

        let struct (m, cmd) =
          Systems.update env (Msg.EnemyMsg(EnemyMsg.Tick 0.016f)) model

        Expect.equal
          m.Enemy.Enemies[0].State
          stateBefore
          "Enemy state unchanged"

        Expect.equal cmd Cmd.Empty "No Cmd emitted"

      testCase "PickupMsg.Tick is a no-op (tick runs in Tick pipeline)"
      <| fun _ ->
        let env, _ = recordingEnv()
        let model = createTestModel()

        model.Pickup.Pickups <- [|
          {
            Kind = Level.PickupKind.Ammo
            Position = Vector3(100.0f, 0.0f, 0.0f)
            IsActive = false
            RespawnTimer = 0.1f
          }
        |]

        let timerBefore = model.Pickup.Pickups[0].RespawnTimer

        let struct (m, cmd) =
          Systems.update env (Msg.PickupMsg(PickupMsg.Tick 0.2f)) model

        Expect.equal
          m.Pickup.Pickups[0].RespawnTimer
          timerBefore
          "Pickup timer unchanged"

        Expect.isFalse m.Pickup.Pickups[0].IsActive "Pickup still inactive"
        Expect.equal cmd Cmd.Empty "No Cmd emitted"
    ]
  ]

// ── Event→Cmd translation tests ─────────────────────────────────────────────
//
// These test the router's translate functions directly, verifying each event
// variant produces the correct set of output Msg values (drained from the Cmd).

[<Tests>]
let translationTests =
  testList "Event→Cmd translation" [

    // ── translateWeaponEvent ───────────────────────────────────────────────
    testList "translateWeaponEvent" [
      testCase
        "Fired emits fire AudioMsg + SpawnSmoke EffectMsg + MuzzleFlash EffectMsg"
      <| fun _ ->
        let path = "fire.mp3"
        let muzzlePos = Vector3(1.0f, 2.0f, 3.0f)
        let dir = Vector3(0.0f, 0.0f, -1.0f)

        let msgs =
          Systems.translateWeaponEvent(WeaponEvent.Fired(path, muzzlePos, dir))
          |> drainCmd

        Expect.equal msgs.Length 3 "Three messages emitted"

        let hasFireAudio =
          msgs
          |> List.exists (function
            | Msg.AudioMsg(AudioMsg.OneShot(p, _, false)) when p = path -> true
            | _ -> false)

        let hasSmoke =
          msgs
          |> List.exists (function
            | Msg.EffectMsg(EffectMsg.SpawnSmoke(p, d)) when
              p = muzzlePos && d = dir
              ->
              true
            | _ -> false)

        let hasMuzzle =
          msgs
          |> List.exists (function
            | Msg.EffectMsg EffectMsg.MuzzleFlash -> true
            | _ -> false)

        Expect.isTrue hasFireAudio "Fire AudioMsg emitted (non-positional)"
        Expect.isTrue hasSmoke "SpawnSmoke EffectMsg emitted with position+dir"
        Expect.isTrue hasMuzzle "MuzzleFlash EffectMsg emitted"

      testCase "ReloadStarted emits non-positional reload AudioMsg"
      <| fun _ ->
        let path = "reload.mp3"

        let msgs =
          Systems.translateWeaponEvent(WeaponEvent.ReloadStarted path)
          |> drainCmd

        Expect.equal msgs.Length 1 "One message emitted"

        match msgs with
        | [ Msg.AudioMsg(AudioMsg.OneShot(p, _, isPositional)) ] ->
          Expect.equal p path "Reload sound path"
          Expect.isFalse isPositional "Reload is non-positional"
        | _ -> failwith "Expected exactly one AudioMsg"

      testCase "EnemyKilled emits injured AudioMsg + AddScore PlayerMsg"
      <| fun _ ->
        let pos = Vector3(5.0f, 0.0f, -3.0f)

        let msgs =
          Systems.translateWeaponEvent(WeaponEvent.EnemyKilled pos) |> drainCmd

        Expect.equal msgs.Length 2 "Two messages emitted"

        let hasInjuredAudio =
          msgs
          |> List.exists (function
            | Msg.AudioMsg(AudioMsg.OneShot(p, enemyPos, true)) when
              p = Assets.injured && enemyPos = pos
              ->
              true
            | _ -> false)

        let hasAddScore =
          msgs
          |> List.exists (function
            | Msg.PlayerMsg(PlayerMsg.AddScore 100) -> true
            | _ -> false)

        Expect.isTrue hasInjuredAudio "Injured AudioMsg emitted (positional)"
        Expect.isTrue hasAddScore "AddScore 100 PlayerMsg emitted"
    ]

    // ── translateEnemyEvent ───────────────────────────────────────────────
    testList "translateEnemyEvent" [
      testCase "PlayerDamaged emits TakeDamage + gasp + TriggerHitFlash"
      <| fun _ ->
        let msgs =
          Systems.translateEnemyEvent(EnemyEvent.PlayerDamaged 10.0f)
          |> drainCmd

        Expect.equal msgs.Length 3 "Three messages emitted"

        let hasDamage =
          msgs
          |> List.exists (function
            | Msg.PlayerMsg(PlayerMsg.TakeDamage 10.0f) -> true
            | _ -> false)

        let hasGasp =
          msgs
          |> List.exists (function
            | Msg.AudioMsg(AudioMsg.OneShot(p, _, false)) when p = Assets.gasp ->
              true
            | _ -> false)

        let hasHitFlash =
          msgs
          |> List.exists (function
            | Msg.EffectMsg EffectMsg.TriggerHitFlash -> true
            | _ -> false)

        Expect.isTrue hasDamage "TakeDamage PlayerMsg emitted"
        Expect.isTrue hasGasp "Gasp AudioMsg emitted (non-positional)"
        Expect.isTrue hasHitFlash "TriggerHitFlash EffectMsg emitted"

      testCase "AttackBite emits positional bite AudioMsg"
      <| fun _ ->
        let pos = Vector3(2.0f, 0.0f, 1.0f)

        let msgs =
          Systems.translateEnemyEvent(EnemyEvent.AttackBite pos) |> drainCmd

        Expect.equal msgs.Length 1 "One message emitted"

        match msgs with
        | [ Msg.AudioMsg(AudioMsg.OneShot(p, enemyPos, isPositional)) ] ->
          Expect.equal p Assets.bite "Bite sound path"
          Expect.equal enemyPos pos "Bite at enemy position"
          Expect.isTrue isPositional "Bite is positional"
        | _ -> failwith "Expected exactly one AudioMsg"

      testCase "Robotic emits positional robotic AudioMsg with path"
      <| fun _ ->
        let path = "robot.wav"
        let pos = Vector3(-5.0f, 0.0f, 3.0f)

        let msgs =
          Systems.translateEnemyEvent(EnemyEvent.Robotic(path, pos)) |> drainCmd

        Expect.equal msgs.Length 1 "One message emitted"

        match msgs with
        | [ Msg.AudioMsg(AudioMsg.OneShot(p, enemyPos, isPositional)) ] ->
          Expect.equal p path "Robotic sound path"
          Expect.equal enemyPos pos "Robotic at enemy position"
          Expect.isTrue isPositional "Robotic is positional"
        | _ -> failwith "Expected exactly one AudioMsg"

      testCase "ChildLaugh emits positional child-laugh AudioMsg"
      <| fun _ ->
        let pos = Vector3(7.0f, 0.0f, -2.0f)

        let msgs =
          Systems.translateEnemyEvent(EnemyEvent.ChildLaugh pos) |> drainCmd

        Expect.equal msgs.Length 1 "One message emitted"

        match msgs with
        | [ Msg.AudioMsg(AudioMsg.OneShot(p, enemyPos, isPositional)) ] ->
          Expect.equal p Assets.childLaugh "Child laugh sound path"
          Expect.equal enemyPos pos "Child laugh at enemy position"
          Expect.isTrue isPositional "Child laugh is positional"
        | _ -> failwith "Expected exactly one AudioMsg"

      testCase "EnemyKilled emits positional injured AudioMsg (no score)"
      <| fun _ ->
        let pos = Vector3(0.0f, 0.0f, -8.0f)

        let msgs =
          Systems.translateEnemyEvent(EnemyEvent.EnemyKilled pos) |> drainCmd

        Expect.equal
          msgs.Length
          1
          "One message emitted (no AddScore for enemy kills)"

        match msgs with
        | [ Msg.AudioMsg(AudioMsg.OneShot(p, enemyPos, isPositional)) ] ->
          Expect.equal p Assets.injured "Injured sound path"
          Expect.equal enemyPos pos "Injured at enemy position"
          Expect.isTrue isPositional "Injured is positional"
        | _ -> failwith "Expected exactly one AudioMsg"
    ]

    // ── translatePickupEvent ──────────────────────────────────────────────
    testList "translatePickupEvent" [
      testCase "HealthPickup emits Heal PlayerMsg with pickup amount"
      <| fun _ ->
        let msgs =
          Systems.translatePickupEvent PickupEvent.HealthPickup |> drainCmd

        Expect.equal msgs.Length 1 "One message emitted"

        match msgs with
        | [ Msg.PlayerMsg(PlayerMsg.Heal amt) ] ->
          Expect.equal
            amt
            Constants.HealthPickupAmount
            "Heal amount is HealthPickupAmount"
        | _ -> failwith "Expected exactly one PlayerMsg.Heal"

      testCase "AmmoPickup emits RefillAmmo WeaponMsg"
      <| fun _ ->
        let msgs =
          Systems.translatePickupEvent PickupEvent.AmmoPickup |> drainCmd

        Expect.equal msgs.Length 1 "One message emitted"

        match msgs with
        | [ Msg.WeaponMsg WeaponMsg.RefillAmmo ] -> ()
        | _ -> failwith "Expected exactly one WeaponMsg.RefillAmmo"
    ]
  ]
