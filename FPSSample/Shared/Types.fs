namespace FPSSample

open System
open System.Numerics
open Mibo.Elmish
open Mibo.Input
open Mibo.Layout3D

/// Game entities, state, and messages for the FPS sample.
/// This module is backend-agnostic: only System.Numerics and Mibo.Core types.
module Types =

  // ── Input Actions ──────────────────────────────────────────────────────────

  /// Logical player actions mapped to backend-neutral triggers (KeyCode etc.).
  [<Struct; RequireQualifiedAccess>]
  type GameAction =
    | MoveForward
    | MoveBackward
    | MoveLeft
    | MoveRight
    | Jump
    | Sprint
    | Shoot
    | Reload

  // ── Enemy ──────────────────────────────────────────────────────────────────

  /// Enemy AI state machine.
  [<Struct; RequireQualifiedAccess>]
  type EnemyState =
    | Idle
    | Chasing
    | Attacking
    | Dead

  /// A single enemy instance. Struct for cache-friendly array storage.
  [<Struct>]
  type Enemy = {
    mutable Position: Vector3
    mutable Velocity: Vector3
    mutable Health: float32
    mutable State: EnemyState
    mutable AttackCooldown: float32
    mutable RespawnTimer: float32
    mutable Facing: float32
    mutable CurrentAnim: string
    // SFX timers (count down to 0, then the system emits an AudioMsg and resets).
    mutable RoboticTimer: float32
    mutable FootstepTimer: float32
    mutable IdleLaughTimer: float32
    // True while the enemy is actively chasing (moving) — genuine AI/behavior
    // state read by the audio service from a snapshot (like CurrentAnim for
    // animation). It is NOT an audio flag.
    mutable IsChasing: bool
    SpawnPoint: Vector3
  }

  module Enemy =
    let inline create(spawn: Vector3) = {
      Position = spawn
      Velocity = Vector3.Zero
      Health = Constants.EnemyMaxHealth
      State = EnemyState.Idle
      AttackCooldown = 0.0f
      RespawnTimer = 0.0f
      Facing = 0.0f
      CurrentAnim = "idle"
      RoboticTimer = Constants.RoboticSoundMaxInterval
      FootstepTimer = Constants.EnemyFootstepInterval
      IdleLaughTimer = Constants.ChildLaughMaxInterval
      IsChasing = false
      SpawnPoint = spawn
    }

  // ── Pickups ────────────────────────────────────────────────────────────────

  /// A pickup item instance (health pack or ammo).
  [<Struct>]
  type Pickup = {
    Kind: Level.PickupKind
    Position: Vector3
    mutable IsActive: bool
    mutable RespawnTimer: float32
  }

  module Pickup =
    let inline create (kind: Level.PickupKind) (pos: Vector3) = {
      Kind = kind
      Position = pos
      IsActive = true
      RespawnTimer = 0.0f
    }

  // ── Muzzle flash effect ────────────────────────────────────────────────────

  /// Transient muzzle flash state. Timer counts down from MuzzleFlashDuration.
  [<Struct>]
  type MuzzleFlash = {
    mutable Timer: float32
    mutable Active: bool
  }

  module MuzzleFlash =
    let empty = { Timer = 0.0f; Active = false }

  // ── Muzzle Smoke Puffs ──────────────────────────────────────────────────────

  /// A small smoke model spawned at the muzzle when a shot is fired.
  /// Carries the gun's muzzle velocity, then slows and rises like real smoke.
  [<Struct>]
  type SmokePuff = {
    mutable Position: Vector3
    mutable Velocity: Vector3
    mutable Timer: float32
    mutable Active: bool
    mutable Scale: float32
  }

  module SmokePuff =
    let empty: SmokePuff = {
      Position = Vector3.Zero
      Velocity = Vector3.Zero
      Timer = 0.0f
      Active = false
      Scale = 1.0f
    }

    let duration = 0.9f

    let inline create (pos: Vector3) (dir: Vector3) (scale: float32) = {
      Position = pos
      // Initial burst matches bullet velocity plus a small outward spread.
      Velocity = dir * 12.0f + Vector3(0.0f, 1.5f, 0.0f)
      Timer = duration
      Active = true
      Scale = scale
    }

  // ── Audio Messages ────────────────────────────────────────────────────────

  /// Rich one-shot audio event. Carries everything a backend needs to play with
  /// positional attenuation + pan. Many can fire per tick, so the router batches
  /// them (Cmd.batch) and the service drains its event buffer each frame.
  ///
  /// Loops (footsteps) are NOT events — the audio service computes loop intent
  /// from the snapshot in Update and idempotently starts/stops against the
  /// backend's native isPlaying state.
  [<Struct; RequireQualifiedAccess>]
  type AudioMsg =
    | OneShot of path: string * position: Vector3 * isPositional: bool

  // ── Per-system Msg types ───────────────────────────────────────────────────

  /// Player subsystem messages. MouseLook/InputMapped are input entry points;
  /// TakeDamage/Heal/AddScore are cross-system commands the router emits when
  /// other systems need to mutate player health or score.
  [<Struct; RequireQualifiedAccess>]
  type PlayerMsg =
    | MouseLook of deltaX: float32 * deltaY: float32
    | InputMapped of actions: ActionState<GameAction>
    | TakeDamage of amount: float32
    | Heal of amount: float32
    | AddScore of points: int

  /// Weapon subsystem messages. RefillAmmo is a cross-system command the router
  /// emits when a pickup is consumed. The fire/reload entry points are the
  /// top-level Msg.Shoot / Msg.Reload (input-driven); the weapon lifecycle tick
  /// runs in the Tick pipeline via WeaponSystem.update directly.
  [<Struct; RequireQualifiedAccess>]
  type WeaponMsg = | RefillAmmo

  /// Enemy subsystem messages. Reserved for future cross-system commands to the
  /// enemy system (e.g. a spawn event). The AI tick runs in the Tick pipeline via
  /// EnemySystem.update directly, so Tick is not dispatched as a message.
  [<Struct; RequireQualifiedAccess>]
  type EnemyMsg = Tick of dt: float32

  /// Effect subsystem messages (visual feedback: smoke puffs, muzzle flash,
  /// hit-flash). Owned by EffectModel. The router emits these when weapon/enemy
  /// events require visual feedback.
  [<Struct; RequireQualifiedAccess>]
  type EffectMsg =
    | SpawnSmoke of position: Vector3 * direction: Vector3
    | MuzzleFlash
    | TriggerHitFlash

  /// Pickup subsystem messages. Reserved for future cross-system commands to the
  /// pickup system. The pickup tick runs in the Tick pipeline via
  /// PickupSystem.update directly.
  [<Struct; RequireQualifiedAccess>]
  type PickupMsg = Tick of dt: float32

  // ── Intent / Event unions (router translates these to cross-system Cmd) ──────

  /// Events emitted by the weapon subsystem. The router translates these into
  /// AudioMsg one-shots and EffectMsg spawns (smoke, muzzle flash).
  [<Struct; RequireQualifiedAccess>]
  type WeaponEvent =
    | Fired of path: string * muzzlePos: Vector3 * direction: Vector3
    | ReloadStarted of path: string
    | EnemyKilled of enemyPos: Vector3

  /// Events emitted by the enemy subsystem. The router translates these into
  /// PlayerMsg (damage), AudioMsg one-shots, and EffectMsg (hit-flash).
  [<Struct; RequireQualifiedAccess>]
  type EnemyEvent =
    | PlayerDamaged of amount: float32
    | EnemyKilled of enemyPos: Vector3
    | AttackBite of enemyPos: Vector3
    | Robotic of path: string * enemyPos: Vector3
    | ChildLaugh of enemyPos: Vector3

  /// Events emitted by the pickup subsystem. The router translates these into
  /// PlayerMsg (heal) and WeaponMsg (refill ammo) commands.
  [<Struct; RequireQualifiedAccess>]
  type PickupEvent =
    | HealthPickup
    | AmmoPickup

  // ── Messages ───────────────────────────────────────────────────────────────

  /// Top-level message dispatched through the Elmish update loop. Input entry
  /// points (InputMapped, MouseLook, Shoot, Reload) come from subscriptions and
  /// the game loop tick. The router (Systems.update) handles each: Shoot/Reload
  /// call the combat functions and translate returned events into cross-system
  /// Cmd (AudioMsg, EffectMsg, PlayerMsg, WeaponMsg). Tick runs the full system
  /// pipeline with a snapshot boundary. Sub-message wrappers carry cross-system
  /// commands between sub-systems.
  [<Struct; RequireQualifiedAccess>]
  type Msg =
    | Tick of tick: GameTime
    | InputMapped of actions: ActionState<GameAction>
    | MouseLook of deltaX: float32 * deltaY: float32
    | Shoot
    | Reload
    | WeaponMsg of weaponMsg: WeaponMsg
    | EnemyMsg of enemyMsg: EnemyMsg
    | EffectMsg of effectMsg: EffectMsg
    | AudioMsg of audioMsg: AudioMsg
    | PlayerMsg of playerMsg: PlayerMsg
    | PickupMsg of pickupMsg: PickupMsg

  // ── Sub-models ──────────────────────────────────────────────────────────────

  /// Player subsystem model. Owns player position, velocity, look, health,
  /// grounded, and score. Mutated only by the player/physics system.
  type PlayerModel() =
    member val Position = Constants.SpawnPosition with get, set
    member val Velocity = Vector3.Zero with get, set
    member val Yaw = 0.0f with get, set
    member val Pitch = 0.0f with get, set
    member val Health = Constants.PlayerMaxHealth with get, set
    member val IsGrounded = true with get, set
    member val Score = 0 with get, set

  /// Weapon subsystem model. Owns the full weapon lifecycle:
  /// ammo, fire/reload cooldowns, reloading state, equipped weapon path,
  /// recoil kick, and the muzzle flash timer.
  type WeaponModel() =
    member val Ammo = Constants.MaxAmmo with get, set
    member val FireCooldown = 0.0f with get, set
    member val IsReloading = false with get, set
    member val ReloadTimer = 0.0f with get, set
    member val EquippedWeapon = Assets.blasterA with get, set
    member val RecoilTimer = 0.0f with get, set
    member val RecoilOffset = 0.0f with get, set
    member val MuzzleFlash = MuzzleFlash.empty with get, set

  /// Effect subsystem model. Owns transient visual feedback: hit-flash timer
  /// and the smoke puff pool.
  type EffectModel() =
    member val HitEffectTimer = 0.0f with get, set
    member val SmokePuffs: SmokePuff[] = Array.zeroCreate 8 with get, set

  /// Enemy subsystem model. Owns the enemies array (and a reference to the
  /// shared colliders array used for enemy-vs-wall resolution).
  type EnemyModel() =
    member val Enemies: Enemy[] = [||] with get, set
    member val Colliders: BoundingBox[] = [||] with get, set

  /// Pickup subsystem model. Owns the pickups array.
  type PickupModel() =
    member val Pickups: Pickup[] = [||] with get, set

  // ── Game Model (aggregate) ──────────────────────────────────────────────────

  /// The main mutable game model. Aggregates per-system sub-models by reference.
  /// Each system owns one slice and mutates only its slice. Cross-system
  /// communication goes through Cmd<Msg> events, not model flags.
  type GameModel() =
    member val Player = PlayerModel() with get, set
    member val Weapon = WeaponModel() with get, set
    member val Effect = EffectModel() with get, set
    member val Enemy = EnemyModel() with get, set
    member val Pickup = PickupModel() with get, set

    // Shared/non-owned state
    member val Actions: ActionState<GameAction> =
      ActionState.empty with get, set

    member val Level: Level.LevelData =
      Level.LevelData.createDefault() with get, set

    member val Colliders: BoundingBox[] = [||] with get, set
    member val TotalTime = 0.0f with get, set

  // ── Readonly Snapshot ──────────────────────────────────────────────────────

  /// Readonly snapshot of the game model taken after the mutation phase of the
  /// Tick pipeline. Post-snapshot systems (effect tick, audio/animation backends)
  /// read from this so they observe a consistent this-frame state. The snapshot
  /// holds the same sub-model references — it is a type-enforced boundary, not
  /// a deep copy (zero allocation). Struct record so it stays off the heap in
  /// the hot per-frame pipeline.
  [<Struct>]
  type Snapshot = {
    Player: PlayerModel
    Weapon: WeaponModel
    Effect: EffectModel
    Enemy: EnemyModel
    Pickup: PickupModel
    Actions: ActionState<GameAction>
    Level: Level.LevelData
    Colliders: BoundingBox[]
    TotalTime: float32
  }

  module GameModel =
    /// Builds a readonly Snapshot from a GameModel (zero-allocation: shares
    /// sub-model references).
    let inline toReadonly
      ((model, cmd): struct (GameModel * Cmd<Msg>))
      : struct (Snapshot * Cmd<Msg>) =
      {
        Player = model.Player
        Weapon = model.Weapon
        Effect = model.Effect
        Enemy = model.Enemy
        Pickup = model.Pickup
        Actions = model.Actions
        Level = model.Level
        Colliders = model.Colliders
        TotalTime = model.TotalTime
      },
      cmd
