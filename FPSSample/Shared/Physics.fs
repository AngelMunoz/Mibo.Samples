namespace FPSSample

open System
open System.Numerics
open Mibo.Layout3D

/// Player physics: collision against solid cell bounds, gravity, ground detection,
/// acceleration/friction movement, and ramp/stepped terrain walking.
/// Pure math - no renderer dependency.
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
  let update (dt: float32) (model: GameModel) : unit =
    let actions = model.Actions
    let struct (forward, right) = moveDirections model.PlayerYaw

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

      let diff =
        targetVel
        - Vector3(model.PlayerVelocity.X, 0.0f, model.PlayerVelocity.Z)

      model.PlayerVelocity <-
        Vector3(
          model.PlayerVelocity.X + diff.X * Constants.Acceleration * dt,
          model.PlayerVelocity.Y,
          model.PlayerVelocity.Z + diff.Z * Constants.Acceleration * dt
        )
    else
      // Friction
      let decay = MathF.Exp(-Constants.Friction * dt)

      model.PlayerVelocity <-
        Vector3(
          model.PlayerVelocity.X * decay,
          model.PlayerVelocity.Y,
          model.PlayerVelocity.Z * decay
        )

    // ── Jump ─────────────────────────────────────────────────────────────────
    if model.IsGrounded && actions.Started.Contains(GameAction.Jump) then
      model.PlayerVelocity <-
        Vector3(
          model.PlayerVelocity.X,
          Constants.JumpSpeed,
          model.PlayerVelocity.Z
        )

    // ── Gravity ──────────────────────────────────────────────────────────────
    model.PlayerVelocity <-
      Vector3(
        model.PlayerVelocity.X,
        model.PlayerVelocity.Y + Constants.Gravity * dt,
        model.PlayerVelocity.Z
      )

    // ── Integrate position ───────────────────────────────────────────────────
    let prevPos = model.PlayerPosition
    let newPos = prevPos + model.PlayerVelocity * dt
    let mutable resolvedPos = newPos
    let mutable grounded = false

    // ── Ground: find the highest solid surface beneath the player ────────────
    let groundY =
      Level.LevelData.groundHeightAt resolvedPos.X resolvedPos.Z model.Level

    if resolvedPos.Y <= groundY + Constants.PlayerEyeHeight then
      resolvedPos <-
        Vector3(
          resolvedPos.X,
          groundY + Constants.PlayerEyeHeight,
          resolvedPos.Z
        )

      if model.PlayerVelocity.Y < 0.0f then
        model.PlayerVelocity <-
          Vector3(model.PlayerVelocity.X, 0.0f, model.PlayerVelocity.Z)

      grounded <- true

    // ── Wall collisions (horizontal push-out from solid cells) ───────────────
    for bounds in model.Colliders do
      resolvedPos <- resolveSphereAABB Constants.PlayerRadius resolvedPos bounds

    model.PlayerPosition <- resolvedPos
    model.IsGrounded <- grounded

    // Track whether the player is walking for looping footstep audio.
    // The view manages the SoundEffectInstance lifecycle — this flag just
    // tells it whether to start or stop the loop.
    let horizontalSpeed =
      MathF.Sqrt(
        model.PlayerVelocity.X * model.PlayerVelocity.X
        + model.PlayerVelocity.Z * model.PlayerVelocity.Z
      )

    model.IsPlayerWalking <- grounded && horizontalSpeed > 0.5f
