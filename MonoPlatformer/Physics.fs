module MonoPlatformer.Physics

open Microsoft.Xna.Framework
open System.Numerics
open MonoPlatformer.Constants
open MonoPlatformer.Types

// -------------------------------------------------------------
// Player Bounds & Collision
// -------------------------------------------------------------

let inline playerBounds(pos: System.Numerics.Vector2) =
  Rectangle(int pos.X, int pos.Y, int playerWidth, int playerHeight)

let inline checkCollision (a: Rectangle) (b: Rectangle) =
  a.X < b.X + b.Width
  && a.X + a.Width > b.X
  && a.Y < b.Y + b.Height
  && a.Y + a.Height > b.Y

let resolvePlatformCollision
  (prevPos: System.Numerics.Vector2)
  (newPos: System.Numerics.Vector2)
  (velocity: System.Numerics.Vector2)
  (platforms: ResizeArray<Rectangle>)
  : struct (System.Numerics.Vector2 * System.Numerics.Vector2 * bool) =
  let mutable pos = newPos
  let mutable vel = velocity
  let mutable grounded = false

  for i = 0 to platforms.Count - 1 do
    let pb = platforms[i]

    if checkCollision (playerBounds pos) pb then
      let prevFeetY = prevPos.Y + playerHeight
      let currFeetY = pos.Y + playerHeight
      let platformTop = float32 pb.Y

      let crossedSurface =
        prevFeetY <= platformTop + 5.0f && currFeetY >= platformTop

      let movingDown = vel.Y >= 0.0f

      if crossedSurface && movingDown then
        pos <- System.Numerics.Vector2(pos.X, platformTop - playerHeight)
        vel <- System.Numerics.Vector2(vel.X, 0.0f)
        grounded <- true
      elif vel.Y < 0.0f then
        pos <- System.Numerics.Vector2(pos.X, float32 pb.Y + float32 pb.Height)
        vel <- System.Numerics.Vector2(vel.X, 0.0f)
      elif vel.X > 0.0f && prevPos.X + playerWidth <= float32 pb.X then
        pos <- System.Numerics.Vector2(float32 pb.X - playerHeight, pos.Y)
        vel <- System.Numerics.Vector2(0.0f, vel.Y)
      elif vel.X < 0.0f && prevPos.X >= float32 pb.X + float32 pb.Width then
        pos <- System.Numerics.Vector2(float32 pb.X + float32 pb.Width, pos.Y)
        vel <- System.Numerics.Vector2(0.0f, vel.Y)

  struct (pos, vel, grounded)

let getAnimationState (velocity: System.Numerics.Vector2) (isGrounded: bool) =
  if not isGrounded then
    if velocity.Y > 0.0f then
      AnimationState.Fall
    else
      AnimationState.Jump
  elif abs velocity.X > 1.0f then
    AnimationState.Walk
  else
    AnimationState.Idle
