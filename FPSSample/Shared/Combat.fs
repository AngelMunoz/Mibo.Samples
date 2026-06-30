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
  let closestColliderHit
    (origin: Vector3)
    (dir: Vector3)
    (colliders: BoundingBox[])
    : float32 voption =
    let mutable closest: float32 voption = ValueNone

    for bounds in colliders do
      match rayVsAABB origin dir bounds with
      | ValueSome t ->
        match closest with
        | ValueSome ct when t >= ct -> ()
        | _ -> closest <- ValueSome t
      | ValueNone -> ()

    closest

  /// Handles the Shoot message: consumes ammo, fires a raycast, applies damage
  /// to the closest unoccluded enemy, updates score, and triggers muzzle flash.
  let handleShoot(model: GameModel) : unit =
    if model.Ammo <= 0 || model.FireCooldown > 0.0f || model.IsReloading then
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
      | ValueNone -> ()

      // Compute tracer end point (where the ray actually hit)
      let wallDist =
        match wallHit with
        | ValueSome d -> d
        | ValueNone -> Single.MaxValue

      let enemyDist =
        match enemyHit with
        | ValueSome struct (_, d) -> d
        | ValueNone -> Single.MaxValue

      let tracerEnd =
        if wallDist < enemyDist && wallDist < Single.MaxValue then
          origin + dir * wallDist
        elif enemyDist < Single.MaxValue then
          origin + dir * enemyDist
        else
          origin + dir * Constants.WeaponRange

      // Find a free tracer slot (ring buffer)
      let mutable slot = -1

      for i = 0 to model.Tracers.Length - 1 do
        if slot < 0 && not model.Tracers[i].Active then
          slot <- i

      if slot < 0 then
        slot <- 0 // overwrite oldest

      model.Tracers[slot] <- Tracer.create origin tracerEnd

      model.Ammo <- model.Ammo - 1
      model.FireCooldown <- Constants.WeaponFireCooldown

      model.MuzzleFlash <- {
        Timer = Constants.MuzzleFlashDuration
        Active = true
      }

  /// Starts a reload if ammo is not full and not already reloading.
  let startReload(model: GameModel) : unit =
    if model.Ammo < Constants.MaxAmmo && not model.IsReloading then
      model.IsReloading <- true
      model.ReloadTimer <- Constants.ReloadTime

  /// Progresses reload timer; completes reload when timer elapses.
  let updateReload (dt: float32) (model: GameModel) : unit =
    if model.IsReloading then
      model.ReloadTimer <- model.ReloadTimer - dt

      if model.ReloadTimer <= 0.0f then
        model.Ammo <- Constants.MaxAmmo
        model.IsReloading <- false
