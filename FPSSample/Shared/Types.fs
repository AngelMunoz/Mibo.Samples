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

  // ── Shot Tracers ───────────────────────────────────────────────────────────

  /// A visible tracer line from a shot. Fades out over its lifetime.
  [<Struct>]
  type Tracer = {
    Start: Vector3
    mutable End: Vector3
    mutable Timer: float32
    mutable Active: bool
  }

  module Tracer =
    let empty: Tracer = {
      Start = Vector3.Zero
      End = Vector3.Zero
      Timer = 0.0f
      Active = false
    }

    let duration = 0.3f

    let create (start: Vector3) (endPos: Vector3) = {
      Start = start
      End = endPos
      Timer = duration
      Active = true
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
    member val Tracers: Tracer[] = Array.zeroCreate 16 with get, set

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
