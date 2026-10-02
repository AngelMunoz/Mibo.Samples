module Platformer3D.Physics

open System
open System.Collections.Concurrent
open System.Numerics
open Mibo.Input
open Mibo.Layout
open Platformer3D.BlockData
open Platformer3D.Constants
open Platformer3D.Types

// ── Player bounds (cylinder) ──
// The player is a Y-axis-aligned cylinder:
//   Center XZ: (pos.X, pos.Z)
//   Radius:    playerRadius (0.21)
//   Bottom Y:  pos.Y           (feet)
//   Top Y:     pos.Y + playerHeight (head)
//
// The world is a heightmap: physics queries footprint columns and compares
// Y against the column's ground band, cap AABB, and the prop tiles — no
// voxel iteration anywhere.

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

// ── World queries (footprint columns) ──
//
// Chunk coordinates use floor division, so negative world cells resolve
// into the chunk west/north of the origin — solid ground at x < 0 has
// colliders exactly like everywhere else.

/// The terrain column under a world cell, when its chunk is loaded.
let columnAt
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  (wx: int)
  (wz: int)
  : TerrainColumn voption =
  let cx = int(Math.Floor(float wx / float chunkWidth))
  let cz = int(Math.Floor(float wz / float chunkDepth))

  match chunks.TryGetValue(struct (cx, cz)) with
  | true, chunk ->
    CellGrid2D.get (wx - cx * chunkWidth) (wz - cz * chunkDepth) chunk.Terrain
  | _ -> ValueNone

/// The static prop tile under a world cell, when its chunk is loaded.
let propAt
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  (wx: int)
  (wz: int)
  : PropTile voption =
  let cx = int(Math.Floor(float wx / float chunkWidth))
  let cz = int(Math.Floor(float wz / float chunkDepth))

  match chunks.TryGetValue(struct (cx, cz)) with
  | true, chunk ->
    CellGrid2D.get (wx - cx * chunkWidth) (wz - cz * chunkDepth) chunk.Props
  | _ -> ValueNone

// ── Collision resolution ──

/// One ground-probe candidate: a surface at `surfaceY` over an XZ rect.
/// Returns the candidate's height when the cone touches it and it beats
/// the current best, else the current best.
let inline private considerGround
  (best: float32)
  (prevFeetY: float32)
  (feetY: float32)
  (px: float32)
  (pz: float32)
  (rectMinX: float32)
  (rectMaxX: float32)
  (rectMinZ: float32)
  (rectMaxZ: float32)
  (surfaceY: float32)
  : float32 =
  if
    surfaceY <= prevFeetY + groundTolerance
    && surfaceY >= feetY - groundProbeDepth
  then
    // Clamp depth to 0 — when the player overshoots the surface
    // (feetY < surfaceY), the cone shouldn't shrink below the player's
    // base radius.
    let depth = max 0.0f (feetY - surfaceY)
    let coneR = playerRadius + depth * coneTanAngle

    let struct (overlaps, _, _, _) =
      circleVsRectXZ px pz coneR rectMinX rectMaxX rectMinZ rectMaxZ

    if overlaps && surfaceY > best then surfaceY else best
  else
    best

/// One body-collision resolution against a world AABB. Returns the updated
/// (position, velocity). Resolves by minimum penetration axis: Y-axis push
/// up (land/step) or push down (head bonk, upward velocity killed), XZ-axis
/// push horizontally along the closest-point direction.
let resolveBody
  (pos: Vector3)
  (vel: Vector3)
  (boxMinX: float32)
  (boxMinY: float32)
  (boxMinZ: float32)
  (extW: float32)
  (extH: float32)
  (extD: float32)
  : struct (Vector3 * Vector3) =
  let struct (yOverlaps, yPenUp, yPenDown) =
    cylinderYOverlap pos.Y (pos.Y + playerHeight) boxMinY (boxMinY + extH)

  if yOverlaps then
    let struct (xzOverlaps, xzPen, pushDirX, pushDirZ) =
      circleVsRectXZ
        pos.X
        pos.Z
        playerRadius
        boxMinX
        (boxMinX + extW)
        boxMinZ
        (boxMinZ + extD)

    if xzOverlaps then
      let yPen = min yPenUp yPenDown

      if pushDirX = 0.0f && pushDirZ = 0.0f then
        // Center inside block (degenerate) — resolve on Y only.
        if yPenUp < yPenDown then
          Vector3(pos.X, boxMinY + extH, pos.Z), vel
        else
          Vector3(pos.X, boxMinY - playerHeight, pos.Z), vel
      elif yPen < xzPen then
        // Y penetration is smaller — resolve vertically.
        if yPenUp < yPenDown then
          // Push up — position correction only, no velocity change.
          Vector3(pos.X, boxMinY + extH, pos.Z), vel
        else
          // Head bonk — push down and kill upward velocity.
          Vector3(pos.X, boxMinY - playerHeight, pos.Z),
          Vector3(vel.X, 0.0f, vel.Z)
      else
        // XZ penetration is smaller — push horizontally.
        let pen = xzPen + 0.01f

        let pos' =
          Vector3(pos.X + pushDirX * pen, pos.Y, pos.Z + pushDirZ * pen)

        // Cancel velocity component into the wall.
        let pushVel = pushDirX * vel.X + pushDirZ * vel.Z

        if pushVel < 0.0f then
          let vel' =
            Vector3(
              vel.X - pushDirX * pushVel,
              vel.Y,
              vel.Z - pushDirZ * pushVel
            )

          pos', vel'
        else
          pos', vel
    else
      pos, vel
  else
    pos, vel

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

  let bx = int(Math.Floor(float pos.X / float cellSize))
  let bz = int(Math.Floor(float pos.Z / float cellSize))

  // ── Phase A: Ground detection (cone probe) ──
  // Scan the footprint window around the player for the highest walkable
  // surface: column caps (analytical for slopes), bare ground tops, and
  // solid prop tiles. The window is ±2 cells, wide enough for every
  // multi-cell cap anchored near the player.
  let mutable groundY = Single.MinValue

  for wx = bx - 2 to bx + 1 do
    for wz = bz - 2 to bz + 1 do
      let worldX = float32 wx * cellSize
      let worldZ = float32 wz * cellSize

      match columnAt chunks wx wz with
      | ValueSome col when not(TerrainColumn.isPit col) ->
        let groundTop = float32 col.Height * cellSize

        match col.Cap with
        | ValueSome shape ->
          let info = capInfo col.Material shape
          let struct (ew, eh, ed) = capExtents shape

          let surfaceY =
            match slopeSurfaceY shape worldX groundTop worldZ pos.X pos.Z with
            | ValueSome sy -> sy
            | ValueNone -> groundTop + eh

          // Kenney meshes are centered on their footprint (see BlockData):
          // the collider is center ± half the snapped extent.
          let centerX = worldX + info.CenterOffsetX
          let centerZ = worldZ + info.CenterOffsetZ

          groundY <-
            considerGround
              groundY
              prevPos.Y
              pos.Y
              pos.X
              pos.Z
              (centerX - ew * 0.5f)
              (centerX + ew * 0.5f)
              (centerZ - ed * 0.5f)
              (centerZ + ed * 0.5f)
              surfaceY
        | ValueNone ->
          groundY <-
            considerGround
              groundY
              prevPos.Y
              pos.Y
              pos.X
              pos.Z
              worldX
              (worldX + cellSize)
              worldZ
              (worldZ + cellSize)
              groundTop
      | _ -> ()

      match propAt chunks wx wz with
      | ValueSome { Prop = prop; Y = y } when isSolidProp prop ->
        let info = propInfo prop
        let struct (ew, eh, ed) = propExtents prop

        // The prop's rendered base sits at its cell plus the vertical
        // offset (platforms float half a cell); the collider follows the
        // render so the player stands on what they see.
        let propY = float32 y * cellSize + info.VerticalOffset
        let centerX = worldX + info.CenterOffsetX
        let centerZ = worldZ + info.CenterOffsetZ

        groundY <-
          considerGround
            groundY
            prevPos.Y
            pos.Y
            pos.X
            pos.Z
            (centerX - ew * 0.5f)
            (centerX + ew * 0.5f)
            (centerZ - ed * 0.5f)
            (centerZ + ed * 0.5f)
            (propY + eh)
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

  // ── Phase B: Body collision (cylinder vs column AABBs) ──
  // Every solid box in the window: the ground band [0, Height], the cap
  // AABB (multi-cell caps included via the window), and solid props.
  // Phase B never sets grounded or zeroes velocity on push-up — Phase A
  // is the sole authority on grounding. Otherwise float-precision overlaps
  // on the block the player stands on would re-ground them every frame.
  for wx = bx - 2 to bx + 1 do
    for wz = bz - 2 to bz + 1 do
      let worldX = float32 wx * cellSize
      let worldZ = float32 wz * cellSize

      match columnAt chunks wx wz with
      | ValueSome col when not(TerrainColumn.isPit col) ->
        let groundTop = float32 col.Height * cellSize

        // The solid ground band under the column.
        if groundTop > 0.0f then
          let struct (pos', vel') =
            resolveBody pos vel worldX 0.0f worldZ cellSize groundTop cellSize

          pos <- pos'
          vel <- vel'

        match col.Cap with
        | ValueSome shape ->
          let info = capInfo col.Material shape
          let struct (ew, eh, ed) = capExtents shape

          // Center-based collider (meshes are footprint-centered).
          let struct (pos', vel') =
            resolveBody
              pos
              vel
              (worldX + info.CenterOffsetX - ew * 0.5f)
              groundTop
              (worldZ + info.CenterOffsetZ - ed * 0.5f)
              ew
              eh
              ed

          pos <- pos'
          vel <- vel'
        | ValueNone -> ()
      | _ -> ()

      match propAt chunks wx wz with
      | ValueSome { Prop = prop; Y = y } when isSolidProp prop ->
        let info = propInfo prop
        let struct (ew, eh, ed) = propExtents prop

        let struct (pos', vel') =
          resolveBody
            pos
            vel
            (worldX + info.CenterOffsetX - ew * 0.5f)
            (float32 y * cellSize + info.VerticalOffset)
            (worldZ + info.CenterOffsetZ - ed * 0.5f)
            ew
            eh
            ed

        pos <- pos'
        vel <- vel'
      | _ -> ()

  // ── Phase C: Pickups ──
  // Pickup tiles in the ±1 window whose center sphere touches the player's
  // cylinder; collecting clears the cell so the instance disappears.
  // Floor division keeps negative cells working (ground west of spawn).
  let playerCenterY = pos.Y + playerHeight * 0.5f

  for wx = bx - 1 to bx + 1 do
    for wz = bz - 1 to bz + 1 do
      let cx = int(Math.Floor(float wx / float chunkWidth))
      let cz = int(Math.Floor(float wz / float chunkDepth))

      match chunks.TryGetValue struct (cx, cz) with
      | true, chunk ->
        let lx = wx - cx * chunkWidth
        let lz = wz - cz * chunkDepth

        match CellGrid2D.get lx lz chunk.Pickups with
        | ValueSome { Prop = Prop.Pickup kind; Y = y } ->
          let worldX = float32 wx * cellSize + cellSize * 0.5f
          let worldY = float32 y * cellSize + cellSize * 0.5f
          let worldZ = float32 wz * cellSize + cellSize * 0.5f

          let dx = pos.X - worldX
          let dy = playerCenterY - worldY
          let dz = pos.Z - worldZ

          let distSq = dx * dx + dy * dy + dz * dz

          if distSq < (playerRadius + 0.5f) * (playerRadius + 0.5f) then
            CellGrid2D.clear lx lz chunk.Pickups
            scoreDelta <- scoreDelta + PickupKind.score kind
        | _ -> ()
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
