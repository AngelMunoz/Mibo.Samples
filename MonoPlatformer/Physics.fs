module MonoPlatformer.Physics

open Microsoft.Xna.Framework
open MonoPlatformer.Constants
open MonoPlatformer.Types

// -------------------------------------------------------------
// Player Bounds & Collision
// -------------------------------------------------------------

let inline checkCollision (pos: Vector2) (rect: Rectangle) =
  let aLeft = pos.X
  let aRight = pos.X + playerWidth
  let aTop = pos.Y
  let aBottom = pos.Y + playerHeight
  let bLeft = float32 rect.X
  let bRight = float32 rect.X + float32 rect.Width
  let bTop = float32 rect.Y
  let bBottom = float32 rect.Y + float32 rect.Height

  aLeft < bRight && aRight > bLeft && aTop < bBottom && aBottom > bTop

let resolvePlatformCollision
  (prevPos: Vector2)
  (newPos: Vector2)
  (velocity: Vector2)
  (platforms: ResizeArray<Rectangle>)
  : struct (Vector2 * Vector2 * bool) =
  let mutable pos = newPos
  let mutable vel = velocity
  let mutable grounded = false

  for i = 0 to platforms.Count - 1 do
    let pb = platforms[i]

    if checkCollision pos pb then
      let prevFeetY = prevPos.Y + playerHeight
      let currFeetY = pos.Y + playerHeight
      let platformTop = float32 pb.Y

      let crossedSurface =
        prevFeetY <= platformTop + 5.0f && currFeetY >= platformTop

      let movingDown = vel.Y >= 0.0f

      if crossedSurface && movingDown then
        pos <- Vector2(pos.X, platformTop - playerHeight)
        vel <- Vector2(vel.X, 0.0f)
        grounded <- true
      elif vel.Y < 0.0f then
        pos <- Vector2(pos.X, float32 pb.Y + float32 pb.Height)
        vel <- Vector2(vel.X, 0.0f)
      elif vel.X > 0.0f && prevPos.X + playerWidth <= float32 pb.X then
        pos <- Vector2(float32 pb.X - playerHeight, pos.Y)
        vel <- Vector2(0.0f, vel.Y)
      elif vel.X < 0.0f && prevPos.X >= float32 pb.X + float32 pb.Width then
        pos <- Vector2(float32 pb.X + float32 pb.Width, pos.Y)
        vel <- Vector2(0.0f, vel.Y)

  struct (pos, vel, grounded)

let getAnimationState (velocity: Vector2) (isGrounded: bool) =
  if not isGrounded then
    if velocity.Y > 0.0f then
      AnimationState.Fall
    else
      AnimationState.Jump
  elif abs velocity.X > 1.0f then
    AnimationState.Walk
  else
    AnimationState.Idle
