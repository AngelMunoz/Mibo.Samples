namespace FPSSample

open System
open System.Numerics
open Mibo.Elmish
open Mibo.Input

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
    // SFX timers (count down to 0, then the system pushes a sound and resets).
    mutable RoboticTimer: float32
    mutable FootstepTimer: float32
    mutable IdleLaughTimer: float32
    // True while the enemy is actively chasing (moving) — the view uses this
    // to manage looping footstep audio (start when true, stop when false).
    mutable IsChasing: bool
    SpawnPoint: Vector3
  }

  module Enemy =
    let create(spawn: Vector3) = {
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
    let create (kind: Level.PickupKind) (pos: Vector3) = {
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

  // ── Sound Events ──────────────────────────────────────────────────────────

  /// A queued sound event with an optional 3D source position. Non-positional
  /// sounds (fire, reload) use the player position; positional sounds
  /// (footsteps, child laugh) use the emitter's world position so backends with
  /// 3D audio (MonoGame) can apply distance attenuation + panning.
  [<Struct>]
  type SoundEvent = {
    Path: string
    Position: Vector3
    IsPositional: bool
  }

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

    let create (pos: Vector3) (dir: Vector3) (scale: float32) = {
      Position = pos
      // Initial burst matches bullet velocity plus a small outward spread.
      Velocity = dir * 12.0f + Vector3(0.0f, 1.5f, 0.0f)
      Timer = duration
      Active = true
      Scale = scale
    }

  // ── Messages ───────────────────────────────────────────────────────────────

  /// Messages dispatched through the Elmish update loop.
  [<Struct; RequireQualifiedAccess>]
  type Msg =
    | Tick of tick: GameTime
    | InputMapped of actions: ActionState<GameAction>
    | MouseLook of deltaX: float32 * deltaY: float32
    | Shoot
    | Reload

  // ── Game Model ─────────────────────────────────────────────────────────────

  /// The main mutable game model. Uses mutable class members (like ThreeDSample
  /// and SpaceBattle) for allocation-free per-frame updates in the System pipeline.
  type GameModel() =
    // Player
    member val PlayerPosition = Constants.SpawnPosition with get, set
    member val PlayerVelocity = Vector3.Zero with get, set
    member val PlayerYaw = 0.0f with get, set
    member val PlayerPitch = 0.0f with get, set
    member val PlayerHealth = Constants.PlayerMaxHealth with get, set
    member val IsGrounded = true with get, set
    member val Score = 0 with get, set

    // Weapon
    member val Ammo = Constants.MaxAmmo with get, set
    member val FireCooldown = 0.0f with get, set
    member val IsReloading = false with get, set
    member val ReloadTimer = 0.0f with get, set
    member val MuzzleFlash = MuzzleFlash.empty with get, set
    member val SmokePuffs: SmokePuff[] = Array.zeroCreate 8 with get, set

    // Recoil kick applied to the viewmodel after each shot.
    member val RecoilTimer = 0.0f with get, set
    member val RecoilOffset = 0.0f with get, set

    // Active weapon model path and queued sound paths (renderer consumes them).
    member val EquippedWeapon = Assets.blasterA with get, set
    member val LastFireSound = "" with get, set
    member val LastReloadSound = "" with get, set

    // Sound event ring buffer — systems push SoundEvent structs directly (no
    // Cmd round-trip, same pattern as SpaceBattle's EffectState for particles).
    // The view drains it each frame and resets Count to 0.
    member val SoundQueue: SoundEvent[] = Array.zeroCreate 32 with get, set
    member val SoundQueueCount = 0 with get, set

    // Movement flags for looping footstep audio. The view manages
    // SoundEffectInstance lifecycle based on these — starts looping when
    // true, stops when false. Avoids queue-and-forget stacking.
    member val IsPlayerWalking = false with get, set

    // Input
    member val Actions: ActionState<GameAction> =
      ActionState.empty with get, set

    // Level + entities
    member val Level: Level.LevelData =
      Level.LevelData.createDefault() with get, set

    member val Colliders: Mibo.Layout3D.BoundingBox[] = [||] with get, set
    member val Enemies: Enemy[] = [||] with get, set
    member val Pickups: Pickup[] = [||] with get, set

    // Time
    member val TotalTime = 0.0f with get, set

    // Hit feedback: when > 0 the player has recently taken damage.
    member val HitEffectTimer = 0.0f with get, set

  // ── Sound Queue helpers ────────────────────────────────────────────────────

  /// Pushes a non-positional sound onto the model's sound queue. Called by
  /// systems in the hot loop (no Cmd/Msg — direct mutation, like EffectState
  /// in SpaceBattle). Wraps around silently when the ring buffer is full.
  let queueSound (model: GameModel) (path: string) =
    let i = model.SoundQueueCount % model.SoundQueue.Length

    model.SoundQueue[i] <-
      {
        Path = path
        Position = model.PlayerPosition
        IsPositional = false
      }

    model.SoundQueueCount <- model.SoundQueueCount + 1

  /// Pushes a positional sound — the backend applies distance attenuation and
  /// stereo panning based on the emitter's world position relative to the player.
  let queueSoundAt (model: GameModel) (path: string) (pos: Vector3) =
    let i = model.SoundQueueCount % model.SoundQueue.Length

    model.SoundQueue[i] <-
      {
        Path = path
        Position = pos
        IsPositional = true
      }

    model.SoundQueueCount <- model.SoundQueueCount + 1
