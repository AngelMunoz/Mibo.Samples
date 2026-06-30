namespace FPSSample

open System
open System.Numerics
open Mibo.Layout3D

/// Enemy AI: idle -> chasing -> attacking state machine.
/// Simple seek behavior toward the player. Pure logic - no renderer dependency.
module EnemyAi =

  open Types

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

  /// Updates a single enemy''s AI state and movement. Mutates the enemy in place.
  let updateEnemyByRef
    (dt: float32)
    (playerPos: Vector3)
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
    | _ ->
      let distToPlayer = xzDistance enemy.Position playerPos

      if distToPlayer <= Constants.EnemyAttackRange then
        enemy.State <- EnemyState.Attacking
      elif distToPlayer <= Constants.EnemyActivationRange then
        enemy.State <- EnemyState.Chasing
      else
        enemy.State <- EnemyState.Idle

      match enemy.State with
      | EnemyState.Chasing ->
        let dir = xzDirection enemy.Position playerPos
        enemy.Velocity <- dir * Constants.EnemyMoveSpeed
        enemy.Position <- enemy.Position + enemy.Velocity * dt
        // Face the player when chasing
        enemy.Facing <- MathF.Atan2(dir.X, dir.Z)
        enemy.CurrentAnim <- "walk"
      | EnemyState.Attacking ->
        enemy.Velocity <- Vector3.Zero
        enemy.AttackCooldown <- enemy.AttackCooldown - dt
        // Face the player when attacking
        let dir = xzDirection enemy.Position playerPos

        if dir.LengthSquared() > 0.01f then
          enemy.Facing <- MathF.Atan2(dir.X, dir.Z)

        enemy.CurrentAnim <- "idle"
      | _ ->
        enemy.Velocity <- Vector3.Zero
        enemy.CurrentAnim <- "idle"

  /// Updates all enemies and handles their attacks on the player.
  /// Returns damage dealt to the player this frame (0 if none).
  let update
    (dt: float32)
    (playerPos: Vector3)
    (playerHealth: float32)
    (enemies: Enemy[])
    (colliders: BoundingBox[])
    : float32 =
    let mutable totalDamage = 0.0f

    for i = 0 to enemies.Length - 1 do
      let mutable enemy = enemies[i]
      updateEnemyByRef dt playerPos &enemy

      // Attacking logic
      if enemy.State = EnemyState.Attacking && enemy.AttackCooldown <= 0.0f then
        totalDamage <- totalDamage + Constants.EnemyAttackDamage
        enemy.AttackCooldown <- Constants.EnemyAttackCooldown

      // Collider resolution
      let mutable pos = enemy.Position

      for bounds in colliders do
        pos <- resolveEnemyCollider pos bounds

      enemy.Position <- pos
      enemies[i] <- enemy

    totalDamage
