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

  /// Ray-vs-sphere test (for shadow/legacy use). Returns ValueSome(distance) on hit.
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

  /// Ray-vs-vertical-cylinder test (for enemy hitboxes). The cylinder is aligned
  /// along the Y axis at (cx, cz) with the given radius, from yMin to yMax.
  /// Solves the 2D ray-vs-circle in the XZ plane, then clips to the Y slab.
  let inline rayVsCylinder
    (origin: Vector3)
    (dir: Vector3)
    (cx: float32)
    (cz: float32)
    (radius: float32)
    (yMin: float32)
    (yMax: float32)
    : float32 voption =
    // ── XZ-plane intersection (2D ray vs circle) ──
    let ox = origin.X - cx
    let oz = origin.Z - cz
    let dx = dir.X
    let dz = dir.Z
    let a = dx * dx + dz * dz

    if a < 1e-10f then
      // Ray is vertical — check if origin is inside the circle radius
      if ox * ox + oz * oz <= radius * radius then
        // Hit the Y slab
        let mutable tmin = -Single.MaxValue
        let mutable tmax = Single.MaxValue

        if MathF.Abs(dir.Y) > 1e-8f then
          tmin <- (yMin - origin.Y) / dir.Y
          tmax <- (yMax - origin.Y) / dir.Y

          if tmin > tmax then
            let tmp = tmin
            tmin <- tmax
            tmax <- tmp

        let t = if tmin >= 0.0f then tmin else tmax

        if t >= 0.0f && t <= Constants.WeaponRange then
          ValueSome t
        else
          ValueNone
      else
        ValueNone
    else
      let b = 2.0f * (ox * dx + oz * dz)
      let c = ox * ox + oz * oz - radius * radius
      let disc = b * b - 4.0f * a * c

      if disc < 0.0f then
        ValueNone
      else
        let sq = MathF.Sqrt(disc)
        let t0 = (-b - sq) / (2.0f * a)
        let t1 = (-b + sq) / (2.0f * a)

        // Check both intersection points against the Y slab
        let inline checkT(t: float32) =
          if t < 0.0f || t > Constants.WeaponRange then
            ValueNone
          else
            let y = origin.Y + dir.Y * t

            if y >= yMin && y <= yMax then ValueSome t else ValueNone

        match checkT t0 with
        | ValueSome _ as v -> v
        | ValueNone ->
          match checkT t1 with
          | ValueSome _ as v -> v
          | ValueNone -> ValueNone

  /// Finds the closest enemy hit by a ray from origin along dir.
  /// Returns the enemy index and distance, or ValueNone.
  let findClosestEnemyHit
    (origin: Vector3)
    (dir: Vector3)
    (enemies: Enemy[])
    : struct (int * float32) voption =
    let mutable closest: struct (int * float32) voption = ValueNone
    // Vertical cylinder hitbox: radius matches the body (EnemyRadius), height
    // spans the full model (0 to EnemyHeight). Tighter than a sphere so shots
    // passing beside the enemy don't register.
    let radius = Constants.EnemyRadius
    let yMax = Constants.EnemyHeight

    for i = 0 to enemies.Length - 1 do
      let e = enemies[i]

      if e.State <> EnemyState.Dead then
        match
          rayVsCylinder
            origin
            dir
            e.Position.X
            e.Position.Z
            radius
            e.Position.Y
            yMax
        with
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

      // Compute the impact point for the tracer visual. The nearest hit (enemy
      // or wall) determines where the bullet model stops; if nothing is hit, the
      // tracer flies to max weapon range.
      let hitDist =
        match enemyHit, wallHit with
        | ValueSome struct (_, eDist), ValueSome wDist -> min eDist wDist
        | ValueSome struct (_, eDist), ValueNone -> eDist
        | ValueNone, ValueSome wDist -> wDist
        | ValueNone, ValueNone -> Constants.WeaponRange

      let hitPoint = origin + dir * hitDist

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

      // Compute the camera-right vector for shell ejection (perpendicular to
      // look direction, in the horizontal plane).
      let right = ViewMath.cameraRight player.Yaw

      // Emit Fired event (router translates to AudioMsg + EffectMsg smoke +
      // EffectMsg.MuzzleFlash + bullet + shell; the muzzle flash timer is
      // weapon-owned state applied by the EffectMsg.MuzzleFlash handler).
      events.Add(
        WeaponEvent.Fired(Assets.gunSound wc, muzzlePos, dir, hitPoint, right)
      )

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
