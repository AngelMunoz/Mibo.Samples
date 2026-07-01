namespace FPSSample

open System
open System.Numerics
open Mibo.Layout3D

/// Combat logic: ray-vs-AABB intersection, shooting, reloading, damage,
/// and score. Pure math - no renderer dependency.
module Combat =

  open Types

  /// Computes the camera look direction from yaw/pitch.
  /// Yaw=0/Pitch=0 looks towards -Z (consistent with Physics.moveDirections).
  let inline lookDirection (yaw: float32) (pitch: float32) : Vector3 =
    let cosP = MathF.Cos(pitch)
    Vector3(-MathF.Sin(yaw) * cosP, MathF.Sin(pitch), -MathF.Cos(yaw) * cosP)

  /// Slab-based ray-vs-AABB intersection test.
  /// Returns ValueSome(distance) if hit, ValueNone if missed.
  let inline rayVsAABB
    (origin: Vector3)
    (dir: Vector3)
    (bounds: BoundingBox)
    : float32 voption =
    let mutable tmin = -Single.MaxValue
    let mutable tmax = Single.MaxValue
    let mutable hit = true

    let inline checkAxis(o: float32, d: float32, minB: float32, maxB: float32) =
      if MathF.Abs(d) > 1e-8f then
        let t1 = (minB - o) / d
        let t2 = (maxB - o) / d
        tmin <- MathF.Max(tmin, MathF.Min(t1, t2))
        tmax <- MathF.Min(tmax, MathF.Max(t1, t2))
      elif o < minB || o > maxB then
        hit <- false

    checkAxis(origin.X, dir.X, bounds.Min.X, bounds.Max.X)
    checkAxis(origin.Y, dir.Y, bounds.Min.Y, bounds.Max.Y)
    checkAxis(origin.Z, dir.Z, bounds.Min.Z, bounds.Max.Z)

    if not hit || tmax < tmin || tmax < 0.0f then
      ValueNone
    else
      ValueSome(if tmin >= 0.0f then tmin else tmax)

  /// Ray-vs-sphere test (for enemy hitboxes). Returns ValueSome(distance) on hit.
  let inline rayVsSphere
    (origin: Vector3)
    (dir: Vector3)
    (center: Vector3)
    (radius: float32)
    : float32 voption =
    let oc = origin - center
    let b = 2.0f * Vector3.Dot(oc, dir)
    let c = oc.LengthSquared() - radius * radius
    let discriminant = b * b - 4.0f * c

    if discriminant < 0.0f then
      ValueNone
    else
      let sq = MathF.Sqrt(discriminant)
      let t0 = (-b - sq) * 0.5f

      if t0 >= 0.0f && t0 <= Constants.WeaponRange then
        ValueSome t0
      else
        let t1 = (-b + sq) * 0.5f

        if t1 >= 0.0f && t1 <= Constants.WeaponRange then
          ValueSome t1
        else
          ValueNone

  /// Finds the closest enemy hit by a ray from origin along dir.
  /// Returns the enemy index and distance, or ValueNone.
  let findClosestEnemyHit
    (origin: Vector3)
    (dir: Vector3)
    (enemies: Enemy[])
    : struct (int * float32) voption =
    let mutable closest: struct (int * float32) voption = ValueNone
    let radius = Constants.EnemyHeight * 0.5f

    for i = 0 to enemies.Length - 1 do
      let e = enemies[i]

      if e.State <> EnemyState.Dead then
        let center = Vector3(e.Position.X, e.Position.Y + radius, e.Position.Z)

        match rayVsSphere origin dir center radius with
        | ValueSome t ->
          match closest with
          | ValueSome(_, cd) when t >= cd -> ()
          | _ -> closest <- ValueSome struct (i, t)
        | ValueNone -> ()

    closest

  /// Finds the distance to the closest collider hit by a ray (for occlusion).
  /// Ignores hits very close to the origin so the shot doesn't clip the
  /// player's own nearby geometry.
  let closestColliderHit
    (origin: Vector3)
    (dir: Vector3)
    (colliders: BoundingBox[])
    : float32 voption =
    let mutable closest: float32 voption = ValueNone

    for bounds in colliders do
      match rayVsAABB origin dir bounds with
      | ValueSome t when t >= 0.35f ->
        match closest with
        | ValueSome ct when t >= ct -> ()
        | _ -> closest <- ValueSome t
      | _ -> ()

    closest

  /// Handles the Shoot message: consumes ammo, fires a raycast, applies damage
  /// to the closest unoccluded enemy, updates score, and triggers muzzle flash,
  /// smoke puffs, recoil, and the weapon-class-matched fire sound.
  let handleShoot(model: GameModel) : unit =
    let wc = Assets.weaponClass model.EquippedWeapon

    if model.Ammo <= 0 || model.IsReloading then
      ()
    elif model.FireCooldown > 0.0f then
      // Fast-firing weapons ignore the exact per-shot cooldown window and fire
      // as soon as their class cooldown has elapsed.
      ()
    else
      let origin = model.PlayerPosition
      let dir = lookDirection model.PlayerYaw model.PlayerPitch

      let enemyHit = findClosestEnemyHit origin dir model.Enemies
      let wallHit = closestColliderHit origin dir model.Colliders

      let hitEnemy =
        match enemyHit, wallHit with
        | ValueSome struct (idx, eDist), ValueSome wDist when eDist <= wDist ->
          ValueSome idx
        | ValueSome struct (idx, _), ValueNone -> ValueSome idx
        | _ -> ValueNone

      match hitEnemy with
      | ValueSome idx ->
        let mutable e = model.Enemies[idx]
        e.Health <- e.Health - Constants.WeaponDamage

        if e.Health <= 0.0f then
          e.Health <- 0.0f
          e.State <- EnemyState.Dead
          e.RespawnTimer <- Constants.EnemyRespawnTime
          model.Score <- model.Score + 100

        model.Enemies[idx] <- e
        queueSound model Assets.injured
      | ValueNone -> ()

      // Spawn a smoke puff at the muzzle, tilted along the shot direction.
      let mutable sSlot = -1

      for i = 0 to model.SmokePuffs.Length - 1 do
        if sSlot < 0 && not model.SmokePuffs[i].Active then
          sSlot <- i

      if sSlot < 0 then
        sSlot <- 0

      let muzzlePos =
        ViewMath.muzzleWorldPosition
          model.PlayerPosition
          dir
          model.PlayerPitch
          model.PlayerYaw

      model.SmokePuffs[sSlot] <- SmokePuff.create muzzlePos dir 1.0f

      // Kick recoil and play fire sound matched to the equipped weapon class.
      let wc = Assets.weaponClass model.EquippedWeapon

      model.RecoilTimer <- 0.12f
      model.RecoilOffset <- 0.08f
      model.LastFireSound <- Assets.gunSound wc

      model.Ammo <- model.Ammo - 1
      model.FireCooldown <- Assets.fireCooldown wc

      model.MuzzleFlash <- {
        Timer = Constants.MuzzleFlashDuration
        Active = true
      }

  /// Starts a reload if ammo is not full and not already reloading.
  /// Queues the class-appropriate reload sound.
  let startReload(model: GameModel) : unit =
    if model.Ammo < Constants.MaxAmmo && not model.IsReloading then
      let wc = Assets.weaponClass model.EquippedWeapon
      model.IsReloading <- true
      model.ReloadTimer <- Constants.ReloadTime
      model.LastReloadSound <- Assets.reloadSound wc

  /// Progresses reload timer; completes reload when timer elapses.
  let updateReload (dt: float32) (model: GameModel) : unit =
    if model.IsReloading then
      model.ReloadTimer <- model.ReloadTimer - dt

      if model.ReloadTimer <= 0.0f then
        model.Ammo <- Constants.MaxAmmo
        model.IsReloading <- false
