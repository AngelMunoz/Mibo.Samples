namespace FPSSample

open System
open System.Numerics
open Mibo.Input
open Mibo.Layout3D

/// Player physics: collision against solid cell bounds, gravity, ground detection,
/// acceleration/friction movement, and ramp/stepped terrain walking.
/// Pure math - no renderer dependency. Operates on PlayerModel (the player
/// sub-model). Footstep audio is computed by the audio service from the
/// snapshot — there is no IsPlayerWalking model flag.
module Physics =

  open Types

  /// Computes the forward and right direction vectors from the player's yaw.
  /// (Yaw=0 looks towards -Z; consistent with raylib/MonoGame camera conventions.)
  let inline moveDirections(yaw: float32) : struct (Vector3 * Vector3) =
    let cosY = MathF.Cos(yaw)
    let sinY = MathF.Sin(yaw)
    // Forward (camera looks -Z at yaw=0)
    let forward = Vector3(-sinY, 0.0f, -cosY)
    let right = Vector3(cosY, 0.0f, -sinY)
    struct (forward, right)

  /// Resolves a sphere-vs-AABB collision by pushing the sphere out along
  /// the axis of minimum penetration. Returns adjusted position.
  let inline resolveSphereAABB
    (radius: float32)
    (pos: Vector3)
    (bounds: BoundingBox)
    : Vector3 =
    let closest =
      Vector3(
        Math.Clamp(pos.X, bounds.Min.X, bounds.Max.X),
        Math.Clamp(pos.Y, bounds.Min.Y, bounds.Max.Y),
        Math.Clamp(pos.Z, bounds.Min.Z, bounds.Max.Z)
      )

    let diff = pos - closest
    let distSq = diff.LengthSquared()

    if distSq < radius * radius && distSq > 1e-8f then
      let dist = MathF.Sqrt(distSq)
      pos + diff * ((radius - dist) / dist)
    elif distSq <= 1e-8f then
      // Center is inside the box - push out along the smallest face
      let center = (bounds.Min + bounds.Max) * 0.5f

      let dxMin = MathF.Abs(pos.X - bounds.Min.X)
      let dxMax = MathF.Abs(bounds.Max.X - pos.X)
      let dyMin = MathF.Abs(pos.Y - bounds.Min.Y)
      let dyMax = MathF.Abs(bounds.Max.Y - pos.Y)
      let dzMin = MathF.Abs(pos.Z - bounds.Min.Z)
      let dzMax = MathF.Abs(bounds.Max.Z - pos.Z)

      let minPen =
        MathF.Min(
          MathF.Min(MathF.Min(dxMin, dxMax), MathF.Min(dyMin, dyMax)),
          MathF.Min(dzMin, dzMax)
        )

      if minPen = dxMin then
        Vector3(bounds.Min.X - radius, pos.Y, pos.Z)
      elif minPen = dxMax then
        Vector3(bounds.Max.X + radius, pos.Y, pos.Z)
      elif minPen = dzMin then
        Vector3(pos.X, pos.Y, bounds.Min.Z - radius)
      elif minPen = dzMax then
        Vector3(pos.X, pos.Y, bounds.Max.Z + radius)
      else
        pos // embedded vertically - handled by ground/floor
    else
      pos

  /// Full physics update: applies input-driven acceleration, gravity, integrates
  /// position, resolves wall collisions, handles ground standing, and jump.
  /// Ground height is determined by the highest solid cell beneath the player.
  /// Mutates the PlayerModel in place. Footstep audio is NOT tracked here —
  /// the audio service derives walking intent from the snapshot each frame.
  let update
    (dt: float32)
    (player: PlayerModel)
    (level: Level.LevelData)
    (colliders: BoundingBox[])
    (actions: ActionState<GameAction>)
    : unit =
    let struct (forward, right) = moveDirections player.Yaw

    // ── Horizontal movement ──────────────────────────────────────────────────
    let mutable wishDir = Vector3.Zero

    if actions.Held.Contains(GameAction.MoveForward) then
      wishDir <- wishDir + forward

    if actions.Held.Contains(GameAction.MoveBackward) then
      wishDir <- wishDir - forward

    if actions.Held.Contains(GameAction.MoveLeft) then
      wishDir <- wishDir - right

    if actions.Held.Contains(GameAction.MoveRight) then
      wishDir <- wishDir + right

    let isSprinting = actions.Held.Contains(GameAction.Sprint)

    let maxSpeed =
      if isSprinting then
        Constants.SprintSpeed
      else
        Constants.MoveSpeed

    if wishDir.LengthSquared() > 0.0f then
      wishDir <- Vector3.Normalize(wishDir)
      let targetVel = wishDir * maxSpeed

      let diff = targetVel - Vector3(player.Velocity.X, 0.0f, player.Velocity.Z)

      player.Velocity <-
        Vector3(
          player.Velocity.X + diff.X * Constants.Acceleration * dt,
          player.Velocity.Y,
          player.Velocity.Z + diff.Z * Constants.Acceleration * dt
        )
    else
      // Friction
      let decay = MathF.Exp(-Constants.Friction * dt)

      player.Velocity <-
        Vector3(
          player.Velocity.X * decay,
          player.Velocity.Y,
          player.Velocity.Z * decay
        )

    // ── Jump ─────────────────────────────────────────────────────────────────
    if player.IsGrounded && actions.Started.Contains(GameAction.Jump) then
      player.Velocity <-
        Vector3(player.Velocity.X, Constants.JumpSpeed, player.Velocity.Z)

    // ── Gravity ──────────────────────────────────────────────────────────────
    player.Velocity <-
      Vector3(
        player.Velocity.X,
        player.Velocity.Y + Constants.Gravity * dt,
        player.Velocity.Z
      )

    // ── Integrate position ───────────────────────────────────────────────────
    let prevPos = player.Position
    let newPos = prevPos + player.Velocity * dt
    let mutable resolvedPos = newPos
    let mutable grounded = false

    // ── Ground: find the highest solid surface beneath the player ────────────
    let groundY =
      Level.LevelData.groundHeightAt resolvedPos.X resolvedPos.Z level

    if resolvedPos.Y <= groundY + Constants.PlayerEyeHeight then
      resolvedPos <-
        Vector3(
          resolvedPos.X,
          groundY + Constants.PlayerEyeHeight,
          resolvedPos.Z
        )

      if player.Velocity.Y < 0.0f then
        player.Velocity <- Vector3(player.Velocity.X, 0.0f, player.Velocity.Z)

      grounded <- true

    // ── Wall collisions (horizontal push-out from solid cells) ───────────────
    for bounds in colliders do
      resolvedPos <- resolveSphereAABB Constants.PlayerRadius resolvedPos bounds

    player.Position <- resolvedPos
    player.IsGrounded <- grounded
