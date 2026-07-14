module Platformer.Physics

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Layout
open Platformer.Constants
open Platformer.Types
open Mibo.Input

let inline playerBounds(pos: Vector2) : Rect = {
  X = pos.X
  Y = pos.Y
  Width = playerWidth
  Height = playerHeight
}

let inline checkCollision (a: Rect) (b: Rect) =
  a.X < b.X + b.Width
  && a.X + a.Width > b.X
  && a.Y < b.Y + b.Height
  && a.Y + a.Height > b.Y

/// Resolve the player's collision against solid and one-way colliders.
///
/// Solid colliders (full blocks): land from above, block from below, and
/// block from the sides.
///
/// One-way colliders (cloud/bridge): land from above ONLY. The player passes
/// through from below and from the sides. When `dropDown` is set (the player
/// is holding Down to drop through), landing is suppressed so the player
/// falls through one-way platforms they are standing on.
let resolvePlatformCollision
  (prevPos: Vector2)
  (newPos: Vector2)
  (velocity: Vector2)
  (solids: ResizeArray<Rect>)
  (oneWay: ResizeArray<Rect>)
  (dropDown: bool)
  =
  let mutable pos = newPos
  let mutable vel = velocity
  let mutable grounded = false

  // Solid colliders — full collision in all directions
  for i = 0 to solids.Count - 1 do
    let pb = solids[i]

    if checkCollision (playerBounds pos) pb then
      let prevFeetY = prevPos.Y + playerHeight
      let currFeetY = pos.Y + playerHeight
      let platformTop = pb.Y

      let crossedSurface =
        prevFeetY <= platformTop + 5.0f && currFeetY >= platformTop

      let movingDown = vel.Y >= 0.0f

      if crossedSurface && movingDown then
        pos <- Vector2(pos.X, platformTop - playerHeight)
        vel <- Vector2(vel.X, 0.0f)
        grounded <- true
      elif vel.Y < 0.0f then
        pos <- Vector2(pos.X, pb.Y + pb.Height)
        vel <- Vector2(vel.X, 0.0f)
      elif vel.X > 0.0f && prevPos.X + playerWidth <= pb.X then
        pos <- Vector2(pb.X - playerWidth, pos.Y)
        vel <- Vector2(0.0f, vel.Y)
      elif vel.X < 0.0f && prevPos.X >= pb.X + pb.Width then
        pos <- Vector2(pb.X + pb.Width, pos.Y)
        vel <- Vector2(0.0f, vel.Y)

  // One-way colliders — land from above only. When the player requests a
  // drop-down (holding Down), skip landing entirely so they fall through.
  if not dropDown then
    for i = 0 to oneWay.Count - 1 do
      let pb = oneWay[i]

      if checkCollision (playerBounds pos) pb then
        let prevFeetY = prevPos.Y + playerHeight
        let currFeetY = pos.Y + playerHeight
        let platformTop = pb.Y

        let crossedSurface =
          prevFeetY <= platformTop + 5.0f && currFeetY >= platformTop

        let movingDown = vel.Y >= 0.0f

        if crossedSurface && movingDown then
          pos <- Vector2(pos.X, platformTop - playerHeight)
          vel <- Vector2(vel.X, 0.0f)
          grounded <- true

  struct (pos, vel, grounded)

let inline getAnimationState
  (velocity: Vector2)
  (isGrounded: bool)
  (isDucking: bool)
  =
  if isGrounded && isDucking then
    Duck
  elif not isGrounded then
    if velocity.Y > 0.0f then Fall else Jump
  elif abs velocity.X > 1.0f then
    Walk
  else
    Idle

// -------------------------------------------------------------
// Physics Sub-system (M_U — backend-agnostic)
// -------------------------------------------------------------

module PhysicsSystem =

  type PhysicsModel() =
    member val Position =
      Vector2(spawnX, groundSurface - playerHeight) with get, set

    member val Velocity = Vector2.Zero with get, set
    member val Facing = 1.0f with get, set
    member val IsGrounded = true with get, set

    member val JumpTriggered = false with get, set
    member val IsDucking = false with get, set
    member val Score = 0 with get, set

  let init() = PhysicsModel()

  let private nearbySolids = ResizeArray<Rect>(256)
  let private nearbyOneWay = ResizeArray<Rect>(64)
  let private nearbySpikes = ResizeArray<Rect>(64)
  let private nearbyCoins = ResizeArray<Rect>(64)
  let private collectedCoins = ResizeArray<Rect>(16)

  let update
    (dt, actions)
    (model: PhysicsModel)
    (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
    : PhysicsModel =
    // Set horizontal velocity from input
    let moveDir =
      if actions.Held.Contains MoveLeft then -1.0f
      elif actions.Held.Contains MoveRight then 1.0f
      else 0.0f

    let velocity = Vector2(moveDir * moveSpeed, model.Velocity.Y + gravity * dt)

    let canJump = model.IsGrounded
    let jumpHeld = actions.Held.Contains GameAction.Jump
    let jumpStarted = actions.Started.Contains GameAction.Jump

    let mutable velocityY = velocity.Y

    if jumpStarted && canJump then
      model.JumpTriggered <- true
      velocityY <- jumpSpeed
    elif not canJump && not jumpHeld && velocityY < 0.0f then
      velocityY <- velocityY * jumpCutMultiplier

    let velocity = Vector2(velocity.X, velocityY)
    let prevPos = model.Position
    let newPos = prevPos + velocity * dt

    // Collect platforms, spikes, coins from nearby chunks
    nearbySolids.Clear()
    nearbyOneWay.Clear()
    nearbySpikes.Clear()
    nearbyCoins.Clear()
    let pcx = int(Math.Floor(float newPos.X / float chunkWorldSize))
    let pcy = int(Math.Floor(float newPos.Y / float chunkWorldSize))

    for KeyValue(key, chunk) in chunks do
      let struct (cx, cy) = key

      if
        abs(cx - pcx) <= chunkLoadRadius && abs(cy - pcy) <= chunkLoadRadius
      then
        nearbySolids.AddRange chunk.Platforms
        nearbyOneWay.AddRange chunk.OneWayPlatforms
        nearbySpikes.AddRange chunk.Spikes
        nearbyCoins.AddRange chunk.Coins

    // Drop through one-way platforms while holding Down. Only suppresses
    // one-way landing; solid ground is unaffected.
    let dropDown = actions.Held.Contains GameAction.Down
    model.IsDucking <- dropDown

    let struct (finalPos, finalVel, isGrounded) =
      resolvePlatformCollision
        prevPos
        newPos
        velocity
        nearbySolids
        nearbyOneWay
        dropDown

    let mutable finalPos = finalPos
    let mutable finalVel = finalVel
    let mutable isGrounded = isGrounded

    // Spike collision → respawn
    let playerRect = playerBounds finalPos

    for i = 0 to nearbySpikes.Count - 1 do
      if checkCollision playerRect nearbySpikes[i] then
        finalPos <- Vector2(spawnX, groundSurface - playerHeight)
        finalVel <- Vector2.Zero
        isGrounded <- true

    // Coin collection
    collectedCoins.Clear()

    for i = 0 to nearbyCoins.Count - 1 do
      if checkCollision playerRect nearbyCoins[i] then
        model.Score <- model.Score + 1
        collectedCoins.Add nearbyCoins[i]

    for i = 0 to collectedCoins.Count - 1 do
      let coinRect = collectedCoins[i]
      let coinCx = int(Math.Floor(float coinRect.X / float chunkWorldSize))
      let coinCy = int(Math.Floor(float coinRect.Y / float chunkWorldSize))
      let key = struct (coinCx, coinCy)

      match chunks.TryGetValue key with
      | true, chunk ->
        let terrainGrid, _ =
          LayeredGrid2D.getOrAddLayer Layer.Terrain chunk.Grids

        let cellX =
          int((coinRect.X - terrainGrid.Origin.X) / terrainGrid.CellSize.X)

        let cellY =
          int((coinRect.Y - terrainGrid.Origin.Y) / terrainGrid.CellSize.Y)

        if
          cellX >= 0
          && cellX < terrainGrid.Width
          && cellY >= 0
          && cellY < terrainGrid.Height
        then
          CellGrid2D.set cellX cellY Tile.Empty terrainGrid
      | _ -> ()

    if finalPos.Y > groundLevel + 500.0f then
      finalPos <- Vector2(spawnX, groundSurface - playerHeight)
      finalVel <- Vector2.Zero
      isGrounded <- true

    if actions.Started.Contains GameAction.Respawn then
      finalPos <- Vector2(spawnX, groundSurface - playerHeight)
      finalVel <- Vector2.Zero
      isGrounded <- true

    model.Position <- finalPos
    model.Velocity <- finalVel
    model.IsGrounded <- isGrounded

    let newFacing =
      if moveDir < 0.0f then -1.0f
      elif moveDir > 0.0f then 1.0f
      else model.Facing

    model.Facing <- newFacing

    model
