namespace FPSSample

open System
open System.Numerics
open Mibo.Layout3D

/// Combat logic: ray-vs-AABB intersection, shooting, reloading, damage,
/// and score. Pure math - no renderer dependency. Operates on sub-models
/// (PlayerModel, WeaponModel) and returns WeaponEvent lists instead of
/// mutating global flags/queues.
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
  /// to the closest unoccluded enemy, and triggers muzzle flash, smoke puffs,
  /// recoil, and the weapon-class-matched fire sound. Returns WeaponEvent list
  /// (Fired + possibly EnemyKilled) instead of mutating global flags/queues.
  let handleShoot
    (player: PlayerModel)
    (weapon: WeaponModel)
    (enemies: Enemy[])
    (colliders: BoundingBox[])
    : WeaponEvent seq =
    let wc = Assets.weaponClass weapon.EquippedWeapon
    let events = ResizeArray<WeaponEvent>()

    if weapon.Ammo <= 0 || weapon.IsReloading then
      events
    elif weapon.FireCooldown > 0.0f then
      // Fast-firing weapons ignore the exact per-shot cooldown window and fire
      // as soon as their class cooldown has elapsed.
      events
    else
      let origin = player.Position
      let dir = lookDirection player.Yaw player.Pitch

      let enemyHit = findClosestEnemyHit origin dir enemies
      let wallHit = closestColliderHit origin dir colliders

      let hitEnemy =
        match enemyHit, wallHit with
        | ValueSome struct (idx, eDist), ValueSome wDist when eDist <= wDist ->
          ValueSome idx
        | ValueSome struct (idx, _), ValueNone -> ValueSome idx
        | _ -> ValueNone

      match hitEnemy with
      | ValueSome idx ->
        let mutable e = enemies[idx]
        e.Health <- e.Health - Constants.WeaponDamage

        if e.Health <= 0.0f then
          e.Health <- 0.0f
          e.State <- EnemyState.Dead
          e.RespawnTimer <- Constants.EnemyRespawnTime
          events.Add(WeaponEvent.EnemyKilled e.Position)

        enemies[idx] <- e
      | ValueNone -> ()

      // Compute muzzle world position for smoke + flash.
      let muzzlePos =
        ViewMath.muzzleWorldPosition origin dir player.Pitch player.Yaw

      // Kick recoil.
      weapon.RecoilTimer <- 0.12f
      weapon.RecoilOffset <- 0.08f

      // Consume ammo + set cooldown.
      weapon.Ammo <- weapon.Ammo - 1
      weapon.FireCooldown <- Assets.fireCooldown wc

      // Emit Fired event (router translates to AudioMsg + EffectMsg smoke +
      // EffectMsg.MuzzleFlash; the muzzle flash timer is weapon-owned state
      // applied by the EffectMsg.MuzzleFlash handler).
      events.Add(WeaponEvent.Fired(Assets.gunSound wc, muzzlePos, dir))

      events

  /// Starts a reload if ammo is not full and not already reloading.
  /// Returns a ReloadStarted event (with the class-appropriate reload sound
  /// path) instead of mutating a global flag.
  let startReload(weapon: WeaponModel) : WeaponEvent seq =

    if weapon.Ammo < Constants.MaxAmmo && not weapon.IsReloading then
      let wc = Assets.weaponClass weapon.EquippedWeapon
      weapon.IsReloading <- true
      weapon.ReloadTimer <- Constants.ReloadTime
      Seq.singleton(WeaponEvent.ReloadStarted(Assets.reloadSound wc))
    else
      Seq.empty

  /// Progresses reload timer; completes reload when timer elapses.
  let updateReload (dt: float32) (weapon: WeaponModel) : unit =
    if weapon.IsReloading then
      weapon.ReloadTimer <- weapon.ReloadTimer - dt

      if weapon.ReloadTimer <= 0.0f then
        weapon.Ammo <- Constants.MaxAmmo
        weapon.IsReloading <- false
