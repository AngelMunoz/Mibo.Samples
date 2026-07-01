namespace FPSSample

open System
open System.Numerics
open Mibo.Layout3D

/// Enemy AI: idle -> chasing -> attacking state machine.
/// Simple seek behavior toward the player. Pure logic - no renderer dependency.
/// Sound events are pushed directly to the model's SoundQueue (same pattern as
/// SpaceBattle's EffectState for particles — no Cmd round-trip).
module EnemyAi =

  open Types

  let private rng = Random(42)

  let private randomInterval (min: float32) (max: float32) : float32 =
    let r = float32(rng.NextDouble())
    min + (max - min) * r

  /// Distance in the XZ plane between two points (ignores Y).
  let inline xzDistance (a: Vector3) (b: Vector3) : float32 =
    let dx = a.X - b.X
    let dz = a.Z - b.Z
    MathF.Sqrt(dx * dx + dz * dz)

  /// Horizontal direction from a to b (normalized, Y=0).
  let inline xzDirection (a: Vector3) (b: Vector3) : Vector3 =
    let dx = b.X - a.X
    let dz = b.Z - a.Z
    let len = MathF.Sqrt(dx * dx + dz * dz)

    if len > 1e-6f then
      Vector3(dx / len, 0.0f, dz / len)
    else
      Vector3.Zero

  /// Resolves enemy-vs-collider collisions by pushing the enemy out of a box.
  let inline resolveEnemyCollider
    (enemyPos: Vector3)
    (bounds: BoundingBox)
    : Vector3 =
    let closest =
      Vector3(
        Math.Clamp(enemyPos.X, bounds.Min.X, bounds.Max.X),
        Math.Clamp(enemyPos.Y, bounds.Min.Y, bounds.Max.Y),
        Math.Clamp(enemyPos.Z, bounds.Min.Z, bounds.Max.Z)
      )

    let diff = enemyPos - closest
    let distSq = diff.LengthSquared()
    let r = Constants.EnemyRadius

    if distSq < r * r && distSq > 1e-8f then
      let dist = MathF.Sqrt(distSq)
      enemyPos + diff * ((r - dist) / dist)
    else
      enemyPos

  /// Updates a single enemy's AI state and movement. Mutates the enemy in place.
  /// SFX timers tick down and push sounds to the model queue when they expire.
  let updateEnemyByRef
    (dt: float32)
    (playerPos: Vector3)
    (model: GameModel)
    (enemy: Enemy byref)
    : unit =
    match enemy.State with
    | EnemyState.Dead ->
      enemy.RespawnTimer <- enemy.RespawnTimer - dt

      if enemy.RespawnTimer <= 0.0f then
        enemy.Position <- enemy.SpawnPoint
        enemy.Velocity <- Vector3.Zero
        enemy.Health <- Constants.EnemyMaxHealth
        enemy.State <- EnemyState.Idle
        enemy.AttackCooldown <- 0.0f
        enemy.CurrentAnim <- "idle"

        enemy.RoboticTimer <-
          randomInterval
            Constants.RoboticSoundMinInterval
            Constants.RoboticSoundMaxInterval

        enemy.IdleLaughTimer <-
          randomInterval
            Constants.ChildLaughMinInterval
            Constants.ChildLaughMaxInterval
    | _ ->
      let distToPlayer = xzDistance enemy.Position playerPos

      if distToPlayer <= Constants.EnemyAttackRange then
        enemy.State <- EnemyState.Attacking
      elif distToPlayer <= Constants.EnemyActivationRange then
        enemy.State <- EnemyState.Chasing
      else
        enemy.State <- EnemyState.Idle

      // ── Tick SFX timers (common to all alive states) ──
      enemy.RoboticTimer <- enemy.RoboticTimer - dt

      if enemy.RoboticTimer <= 0.0f then
        queueSoundAt model (Assets.roboticSound rng) enemy.Position

        enemy.RoboticTimer <-
          randomInterval
            Constants.RoboticSoundMinInterval
            Constants.RoboticSoundMaxInterval

      match enemy.State with
      | EnemyState.Chasing ->
        let dir = xzDirection enemy.Position playerPos
        enemy.Velocity <- dir * Constants.EnemyMoveSpeed
        enemy.Position <- enemy.Position + enemy.Velocity * dt
        enemy.Facing <- MathF.Atan2(dir.X, dir.Z)
        enemy.CurrentAnim <- "walk"
        enemy.IsChasing <- true
      | EnemyState.Attacking ->
        enemy.Velocity <- Vector3.Zero
        enemy.AttackCooldown <- enemy.AttackCooldown - dt
        let dir = xzDirection enemy.Position playerPos

        if dir.LengthSquared() > 0.01f then
          enemy.Facing <- MathF.Atan2(dir.X, dir.Z)

        enemy.CurrentAnim <- "idle"
        enemy.IsChasing <- false
      | _ ->
        enemy.Velocity <- Vector3.Zero
        enemy.CurrentAnim <- "idle"
        enemy.IsChasing <- false

        // Child laugh when idle (player is far enough that AI doesn't engage)
        enemy.IdleLaughTimer <- enemy.IdleLaughTimer - dt

        if enemy.IdleLaughTimer <= 0.0f then
          queueSoundAt model Assets.childLaugh enemy.Position

          enemy.IdleLaughTimer <-
            randomInterval
              Constants.ChildLaughMinInterval
              Constants.ChildLaughMaxInterval

  /// Updates all enemies and handles their attacks on the player.
  /// Returns damage dealt to the player this frame (0 if none).
  let update
    (dt: float32)
    (playerPos: Vector3)
    (playerHealth: float32)
    (model: GameModel)
    (enemies: Enemy[])
    (colliders: BoundingBox[])
    : float32 =
    let mutable totalDamage = 0.0f

    for i = 0 to enemies.Length - 1 do
      let mutable enemy = enemies[i]
      updateEnemyByRef dt playerPos model &enemy

      // Attacking logic — bite sound on successful attack
      if enemy.State = EnemyState.Attacking && enemy.AttackCooldown <= 0.0f then
        totalDamage <- totalDamage + Constants.EnemyAttackDamage
        enemy.AttackCooldown <- Constants.EnemyAttackCooldown
        queueSound model Assets.bite

      // Collider resolution
      let mutable pos = enemy.Position

      for bounds in colliders do
        pos <- resolveEnemyCollider pos bounds

      enemy.Position <- pos
      enemies[i] <- enemy

    totalDamage
