namespace FPSSample

open System.Numerics

/// Tunable constants for the FPS sample.
[<RequireQualifiedAccess>]
module Constants =

  // ── Player ─────────────────────────────────────────────────────────────────

  [<Literal>]
  let PlayerHeight = 1.7f

  [<Literal>]
  let PlayerRadius = 0.3f

  [<Literal>]
  let PlayerEyeHeight = 1.6f

  [<Literal>]
  let PlayerMaxHealth = 100.0f

  [<Literal>]
  let MoveSpeed = 6.0f

  [<Literal>]
  let SprintSpeed = 10.0f

  [<Literal>]
  let Acceleration = 40.0f

  [<Literal>]
  let Friction = 10.0f

  [<Literal>]
  let Gravity = -20.0f

  [<Literal>]
  let JumpSpeed = 7.0f

  // ── Mouse look ─────────────────────────────────────────────────────────────

  [<Literal>]
  let MouseSensitivity = 0.0025f

  /// ~89 degrees in radians
  [<Literal>]
  let MaxPitch = 1.5533f

  [<Literal>]
  let MinPitch = -1.5533f

  // ── Combat / Weapon ────────────────────────────────────────────────────────

  [<Literal>]
  let WeaponDamage = 25.0f

  [<Literal>]
  let WeaponRange = 100.0f

  [<Literal>]
  let WeaponFireCooldown = 0.15f

  [<Literal>]
  let MaxAmmo = 30

  [<Literal>]
  let ReloadTime = 1.5f

  [<Literal>]
  let MuzzleFlashDuration = 0.05f

  // ── Hit feedback ───────────────────────────────────────────────────────────

  /// How long (seconds) the hit-feedback effect lasts after the player takes
  /// damage.
  [<Literal>]
  let HitEffectDuration = 0.4f

  // ── Enemy ──────────────────────────────────────────────────────────────────

  [<Literal>]
  let EnemyMaxHealth = 100.0f

  [<Literal>]
  let EnemyMoveSpeed = 3.5f

  [<Literal>]
  let EnemyAttackRange = 1.5f

  [<Literal>]
  let EnemyAttackDamage = 10.0f

  [<Literal>]
  let EnemyAttackCooldown = 1.0f

  [<Literal>]
  let EnemyActivationRange = 25.0f

  [<Literal>]
  let EnemyRadius = 0.4f

  [<Literal>]
  let EnemyHeight = 1.8f

  [<Literal>]
  let EnemyRespawnTime = 5.0f

  // ── Pickups ────────────────────────────────────────────────────────────────

  [<Literal>]
  let HealthPickupAmount = 25.0f

  [<Literal>]
  let AmmoPickupAmount = 10

  [<Literal>]
  let PickupRadius = 0.5f

  [<Literal>]
  let PickupRespawnTime = 15.0f

  // ── Horror SFX timing ──────────────────────────────────────────────────────

  [<Literal>]
  let RoboticSoundMinInterval = 4.0f

  [<Literal>]
  let RoboticSoundMaxInterval = 8.0f

  [<Literal>]
  let EnemyFootstepInterval = 0.4f

  [<Literal>]
  let PlayerFootstepInterval = 0.35f

  [<Literal>]
  let ChildLaughMinInterval = 8.0f

  [<Literal>]
  let ChildLaughMaxInterval = 15.0f

  // ── Level ──────────────────────────────────────────────────────────────────

  [<Literal>]
  let FloorSize = 60.0f

  [<Literal>]
  let WallThickness = 0.5f

  [<Literal>]
  let WallHeight = 4.0f

  let SpawnPosition = Vector3(0.0f, PlayerEyeHeight, 0.0f)
