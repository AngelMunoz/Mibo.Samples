module Platformer3D.Physics

open System
open System.Collections.Concurrent
open System.Numerics
open Mibo.Input
open Mibo.Layout3D
open Platformer3D.Constants
open Platformer3D.Types

// ── Player bounds (cylinder) ──
// The player is a Y-axis-aligned cylinder:
//   Center XZ: (pos.X, pos.Z)
//   Radius:    playerRadius (0.21)
//   Bottom Y:  pos.Y           (feet)
//   Top Y:     pos.Y + playerHeight (head)

// ── Ground probe constants ──

/// Maximum slope angle (radians) the player can walk on. Surfaces steeper
/// than this are not detected as ground by the cone probe.
let maxWalkableSlopeAngle = 50.0f * MathF.PI / 180.0f

/// tan(maxWalkableSlopeAngle) — precomputed for the cone radius formula.
let coneTanAngle = MathF.Tan(maxWalkableSlopeAngle)

/// How far above a surface the player's feet can be while still counting as
/// grounded. Kept very small — just enough for float jitter, NOT enough to
/// catch the first frame of a jump. Grounding is further guarded by a
/// `vel.Y <= 0` check so a rising player is never snapped back down.
let groundTolerance = 0.02f

/// How far below the player's feet the cone probe searches for ground.
let groundProbeDepth = playerHeight

// ── Camera-relative movement ──

let computeMoveDirection (actions: ActionState<GameAction>) (yaw: float32) =
  let forward = Vector3(-MathF.Sin(yaw), 0.0f, -MathF.Cos(yaw))
  let right = Vector3(MathF.Cos(yaw), 0.0f, -MathF.Sin(yaw))
  let mutable dir = Vector3.Zero

  if actions.Held.Contains(GameAction.MoveForward) then
    dir <- dir + forward

  if actions.Held.Contains(GameAction.MoveBackward) then
    dir <- dir - forward

  if actions.Held.Contains(GameAction.MoveRight) then
    dir <- dir + right

  if actions.Held.Contains(GameAction.MoveLeft) then
    dir <- dir - right

  if dir.LengthSquared() > 0.0f then
    Vector3.Normalize(dir)
  else
    Vector3.Zero

let computeCameraPosition (target: Vector3) (yaw: float32) (pitch: float32) =
  let dx = cameraDistance * MathF.Cos(pitch) * MathF.Sin(yaw)
  let dy = cameraDistance * MathF.Sin(pitch)
  let dz = cameraDistance * MathF.Cos(pitch) * MathF.Cos(yaw)
  target + Vector3(dx, dy, dz)

// ── Acceleration / Friction ──

let applyMovement (dt: float32) (moveDir: Vector3) (velocity: Vector3) =
  let horizontalVel = Vector3(velocity.X, 0.0f, velocity.Z)
  let hasInput = moveDir.LengthSquared() > 0.0f

  let newHorizontalVel =
    if hasInput then
      let targetVel =
        Vector3(moveDir.X * moveSpeed, 0.0f, moveDir.Z * moveSpeed)

      let diff = targetVel - horizontalVel
      let accel = acceleration * dt

      if diff.Length() <= accel then
        targetVel
      else
        horizontalVel + Vector3.Normalize(diff) * accel
    else
      let frictionAmount = friction * dt
      let speed = horizontalVel.Length()

      if speed <= frictionAmount then
        Vector3.Zero
      else
        horizontalVel * ((speed - frictionAmount) / speed)

  Vector3(newHorizontalVel.X, velocity.Y, newHorizontalVel.Z)

// ── Collision primitives ──

/// Test whether a Y-axis-aligned cylinder overlaps an AABB along the Y axis.
/// Returns (overlaps, pushUp, pushDown) where pushUp = boxMaxY - yBottom
/// and pushDown = yTop - boxMinY. Both are positive when overlapping.
let inline cylinderYOverlap
  (yBottom: float32)
  (yTop: float32)
  (boxMinY: float32)
  (boxMaxY: float32)
  : struct (bool * float32 * float32) =
  if yBottom < boxMaxY && yTop > boxMinY then
    struct (true, boxMaxY - yBottom, yTop - boxMinY)
  else
    struct (false, 0.0f, 0.0f)

/// Test XZ circle-vs-rectangle overlap and compute push direction.
/// Returns (overlaps, penetration, pushDirX, pushDirZ).
/// pushDir is normalized AWAY from the closest point on the rectangle.
/// When the circle center is inside the rectangle (distSq ≈ 0) the push
/// direction is undefined (0,0) and penetration equals r.
let inline circleVsRectXZ
  (cx: float32)
  (cz: float32)
  (r: float32)
  (rectMinX: float32)
  (rectMaxX: float32)
  (rectMinZ: float32)
  (rectMaxZ: float32)
  : struct (bool * float32 * float32 * float32) =
  let closestX =
    if cx < rectMinX then rectMinX
    elif cx > rectMaxX then rectMaxX
    else cx

  let closestZ =
    if cz < rectMinZ then rectMinZ
    elif cz > rectMaxZ then rectMaxZ
    else cz

  let dx = cx - closestX
  let dz = cz - closestZ
  let distSq = dx * dx + dz * dz

  if distSq > r * r then
    struct (false, 0.0f, 0.0f, 0.0f)
  elif distSq > 1e-8f then
    let dist = MathF.Sqrt distSq
    struct (true, r - dist, dx / dist, dz / dist)
  else
    // Center inside rectangle — degenerate: full radius penetration.
    struct (true, r, 0.0f, 0.0f)

// ── Collision resolution ──

let resolveCollision
  (prevPos: Vector3)
  (newPos: Vector3)
  (velocity: Vector3)
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  : struct (Vector3 * Vector3 * bool * int) =
  let mutable pos = newPos
  let mutable vel = velocity
  let mutable grounded = false
  let mutable scoreDelta = 0

  let r = playerRadius

  let pcx = int(Math.Floor(float pos.X / float chunkWorldWidth))
  let pcz = int(Math.Floor(float pos.Z / float chunkWorldDepth))

  let bx = int(Math.Floor(float pos.X / float cellSize))

  let localX =
    bx - int(Math.Floor(float pos.X / float chunkWorldWidth)) * chunkWidth

  let by = int(Math.Floor(float pos.Y / float cellSize))

  let bz = int(Math.Floor(float pos.Z / float cellSize))

  let localZ =
    bz - int(Math.Floor(float pos.Z / float chunkWorldDepth)) * chunkDepth

  // ── Phase A: Ground detection (cone probe) ──
  // Scan the neighborhood downward for the highest walkable surface.
  // The cone radius at depth dy below the feet is:
  //   playerRadius + dy * tan(maxWalkableSlopeAngle)
  // This widens with depth, naturally filtering steep surfaces.
  let mutable groundY = Single.MinValue

  for KeyValue(struct (cx, cz), chunk) in chunks do
    if abs(cx - pcx) <= 2 && abs(cz - pcz) <= 2 then
      let terrainGrid, _ = LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

      let origin = terrainGrid.Origin
      let blockOriginX = int origin.X
      let blockOriginZ = int origin.Z

      for dy in -1 .. 2 do
        for dx in -2 .. 1 do
          for dz in -2 .. 1 do
            let gx = localX - (cx * chunkWidth - blockOriginX) + dx
            let gy = by + dy
            let gz = localZ - (cz * chunkDepth - blockOriginZ) + dz

            if
              gx >= 0
              && gx < chunkWidth
              && gy >= 0
              && gy < chunkHeight
              && gz >= 0
              && gz < chunkDepth
            then
              match CellGrid3D.get gx gy gz terrainGrid with
              | ValueSome blockType when BlockData.isSolid blockType ->
                let worldX = origin.X + float32 gx * cellSize
                let worldY = origin.Y + float32 gy * cellSize
                let worldZ = origin.Z + float32 gz * cellSize

                // Surface height: analytical for slopes, AABB top otherwise.
                let surfaceY =
                  match
                    BlockData.slopeSurfaceY
                      blockType
                      worldX
                      worldY
                      worldZ
                      pos.X
                      pos.Z
                  with
                  | ValueSome sy -> sy
                  | ValueNone ->
                    let struct (_, eh, _) = BlockData.colliderExtents blockType

                    worldY + eh

                // Surface must be below the player's previous feet position
                // (within tolerance) and within probe depth of the current
                // position. Using prevPos.Y as the upper bound catches the
                // case where the player crossed the surface in a single frame
                // (fell fast enough that pos.Y < surfaceY < prevPos.Y).
                if
                  surfaceY <= prevPos.Y + groundTolerance
                  && surfaceY >= pos.Y - groundProbeDepth
                then
                  // Clamp depth to 0 — when the player overshoots the surface
                  // (pos.Y < surfaceY), the cone shouldn't shrink below the
                  // player's base radius.
                  let depth = max 0.0f (pos.Y - surfaceY)
                  let coneR = r + depth * coneTanAngle
                  let struct (ew, _, ed) = BlockData.colliderExtents blockType

                  let struct (overlaps, _, _, _) =
                    circleVsRectXZ
                      pos.X
                      pos.Z
                      coneR
                      worldX
                      (worldX + ew)
                      worldZ
                      (worldZ + ed)

                  if overlaps && surfaceY > groundY then
                    groundY <- surfaceY
              | _ -> ()

  // Only ground when the player is descending or stationary (vel.Y <= 0).
  // If the player just jumped (vel.Y > 0), Phase A must NOT snap them back
  // down — otherwise the first frame of the jump is killed by re-grounding.
  if
    groundY > Single.MinValue
    && vel.Y <= 0.0f
    && pos.Y <= groundY + groundTolerance
  then
    pos <- Vector3(pos.X, groundY, pos.Z)
    vel <- Vector3(vel.X, 0.0f, vel.Z)
    grounded <- true

  // Refresh cylinder Y bounds after ground snap.
  let yBottom = pos.Y
  let yTop = pos.Y + playerHeight

  // ── Phase B: Body collision (cylinder vs AABB) ──
  // Resolve overlaps by minimum penetration axis:
  //   Y-axis: push up (land/step) or push down (head bonk).
  //   XZ-axis: push horizontally along closest-point direction.
  // Phase B never sets grounded or zeroes velocity on push-up — Phase A
  // is the sole authority on grounding. Otherwise float-precision overlaps
  // on the block the player stands on would re-ground them every frame.
  for KeyValue(struct (cx, cz), chunk) in chunks do
    if abs(cx - pcx) <= 2 && abs(cz - pcz) <= 2 then
      let terrainGrid, _ = LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

      let origin = terrainGrid.Origin
      let blockOriginX = int origin.X
      let blockOriginZ = int origin.Z

      for dy in -1 .. 2 do
        for dx in -2 .. 1 do
          for dz in -2 .. 1 do
            let gx = localX - (cx * chunkWidth - blockOriginX) + dx
            let gy = by + dy
            let gz = localZ - (cz * chunkDepth - blockOriginZ) + dz

            if
              gx >= 0
              && gx < chunkWidth
              && gy >= 0
              && gy < chunkHeight
              && gz >= 0
              && gz < chunkDepth
            then
              match CellGrid3D.get gx gy gz terrainGrid with
              | ValueSome blockType when BlockData.isSolid blockType ->
                let worldX = origin.X + float32 gx * cellSize
                let worldY = origin.Y + float32 gy * cellSize
                let worldZ = origin.Z + float32 gz * cellSize

                let struct (ew, eh, ed) = BlockData.colliderExtents blockType

                let struct (yOverlaps, yPenUp, yPenDown) =
                  cylinderYOverlap yBottom yTop worldY (worldY + eh)

                if yOverlaps then
                  let struct (xzOverlaps, xzPen, pushDirX, pushDirZ) =
                    circleVsRectXZ
                      pos.X
                      pos.Z
                      r
                      worldX
                      (worldX + ew)
                      worldZ
                      (worldZ + ed)

                  if xzOverlaps then
                    let yPen = min yPenUp yPenDown

                    if pushDirX = 0.0f && pushDirZ = 0.0f then
                      // Center inside block (degenerate) — resolve on Y only.
                      if yPenUp < yPenDown then
                        pos <- Vector3(pos.X, worldY + eh, pos.Z)
                      else
                        pos <- Vector3(pos.X, worldY - playerHeight, pos.Z)
                    elif yPen < xzPen then
                      // Y penetration is smaller — resolve vertically.
                      if yPenUp < yPenDown then
                        // Push up — position correction only, no velocity change.
                        pos <- Vector3(pos.X, worldY + eh, pos.Z)
                      else
                        // Head bonk — push down and kill upward velocity.
                        pos <- Vector3(pos.X, worldY - playerHeight, pos.Z)
                        vel <- Vector3(vel.X, 0.0f, vel.Z)
                    else
                      // XZ penetration is smaller — push horizontally.
                      let pen = xzPen + 0.01f

                      pos <-
                        Vector3(
                          pos.X + pushDirX * pen,
                          pos.Y,
                          pos.Z + pushDirZ * pen
                        )

                      // Cancel velocity component into the wall.
                      let pushVel = pushDirX * vel.X + pushDirZ * vel.Z

                      if pushVel < 0.0f then
                        vel <-
                          Vector3(
                            vel.X - pushDirX * pushVel,
                            vel.Y,
                            vel.Z - pushDirZ * pushVel
                          )
              | _ -> ()

  // ── Phase C: Collectibles ──
  let playerCenterY = pos.Y + playerHeight * 0.5f

  for KeyValue(struct (cx, cz), chunk) in chunks do
    if abs(cx - pcx) <= 2 && abs(cz - pcz) <= 2 then
      let terrainGrid, _ = LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

      let origin = terrainGrid.Origin
      let blockOriginX = int origin.X
      let blockOriginZ = int origin.Z

      for dy in -1 .. 2 do
        for dx in -1 .. 1 do
          for dz in -1 .. 1 do
            let gx = localX - (cx * chunkWidth - blockOriginX) + dx
            let gy = by + dy
            let gz = localZ - (cz * chunkDepth - blockOriginZ) + dz

            if
              gx >= 0
              && gx < chunkWidth
              && gy >= 0
              && gy < chunkHeight
              && gz >= 0
              && gz < chunkDepth
            then
              match CellGrid3D.get gx gy gz terrainGrid with
              | ValueSome blockType when BlockData.isCollectible blockType ->
                let worldX = origin.X + float32 gx * cellSize + cellSize * 0.5f
                let worldY = origin.Y + float32 gy * cellSize + cellSize * 0.5f
                let worldZ = origin.Z + float32 gz * cellSize + cellSize * 0.5f

                let dx' = pos.X - worldX
                let dy' = playerCenterY - worldY
                let dz' = pos.Z - worldZ

                let distSq = dx' * dx' + dy' * dy' + dz' * dz'

                if distSq < (playerRadius + 0.5f) * (playerRadius + 0.5f) then
                  CellGrid3D.clear gx gy gz terrainGrid |> ignore
                  scoreDelta <- scoreDelta + 1

              | _ -> ()

  struct (pos, vel, grounded, scoreDelta)

// -------------------------------------------------------------
// Physics Sub-system (backend-agnostic)
// -------------------------------------------------------------

module PhysicsSystem =

  type PhysicsModel() =
    member val Position = Constants.spawnPosition with get, set
    member val Velocity = Vector3.Zero with get, set
    member val IsGrounded = false with get, set
    member val Facing = 0.0f with get, set
    member val Score = 0 with get, set
    member val CameraYaw = Constants.cameraDefaultYaw with get, set
    member val CameraPitch = Constants.cameraDefaultPitch with get, set

    member val CameraPosition =
      Constants.spawnPosition + Vector3(0.0f, 4.0f, 8.0f) with get, set

    member val CameraTarget = Constants.spawnPosition with get, set
    member val JumpTriggered = false with get, set

  let init() = PhysicsModel()

  let update
    (dt: float32)
    (actions: ActionState<GameAction>)
    (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
    (model: PhysicsModel)
    : PhysicsModel =
    // Camera input — yaw/pitch from RotateCamera* actions
    let mutable yaw = model.CameraYaw
    let mutable pitch = model.CameraPitch

    if actions.Held.Contains(GameAction.RotateCameraLeft) then
      yaw <- yaw - 2.0f * dt

    if actions.Held.Contains(GameAction.RotateCameraRight) then
      yaw <- yaw + 2.0f * dt

    if actions.Held.Contains(GameAction.RotateCameraUp) then
      pitch <- pitch + 1.5f * dt

    if actions.Held.Contains(GameAction.RotateCameraDown) then
      pitch <- pitch - 1.5f * dt

    model.CameraYaw <- yaw
    model.CameraPitch <- Math.Clamp(pitch, -0.5f, 1.3f)

    // Movement + gravity
    let moveDir = computeMoveDirection actions model.CameraYaw

    let vel =
      if model.IsGrounded && actions.Started.Contains(GameAction.Jump) then
        model.JumpTriggered <- true
        Vector3(model.Velocity.X, jumpSpeed, model.Velocity.Z)
      else
        model.Velocity

    let vel = Vector3(vel.X, vel.Y + gravity * dt, vel.Z)
    let vel = applyMovement dt moveDir vel

    let prevPos = model.Position
    let newPos = prevPos + vel * dt

    let struct (finalPos, finalVel, grounded, scoreDelta) =
      resolveCollision prevPos newPos vel chunks

    let mutable finalPos = finalPos
    let mutable finalVel = finalVel
    let mutable grounded = grounded

    model.Score <- model.Score + scoreDelta

    if finalPos.Y < fallLimit then
      finalPos <- spawnPosition
      finalVel <- Vector3.Zero
      grounded <- false

    if actions.Started.Contains(GameAction.Respawn) then
      finalPos <- spawnPosition
      finalVel <- Vector3.Zero
      grounded <- false

    model.Position <- finalPos
    model.Velocity <- finalVel
    model.IsGrounded <- grounded

    if moveDir.LengthSquared() > 0.1f then
      model.Facing <- MathF.Atan2(moveDir.X, moveDir.Z)

    // Camera follows the player
    let target = finalPos + Vector3(0.0f, playerHeight * 0.5f, 0.0f)

    let desiredCamPos =
      computeCameraPosition target model.CameraYaw model.CameraPitch

    let lerpFactor = 1.0f - MathF.Exp(-dt * cameraLerpSpeed)

    model.CameraPosition <-
      Vector3.Lerp(model.CameraPosition, desiredCamPos, lerpFactor)

    model.CameraTarget <- Vector3.Lerp(model.CameraTarget, target, lerpFactor)

    model
