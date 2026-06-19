module MonoPlatformer.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Animation
open MonoPlatformer.Constants
open MonoPlatformer.Types
open MonoPlatformer.Physics
open MonoPlatformer.WorldGen
open Mibo.Layout

// -------------------------------------------------------------
// Pre-allocated buffers (Level 4 — avoid per-frame allocation)
// -------------------------------------------------------------

let nearbyPlatforms = ResizeArray<Rectangle>(256)
let nearbySpikes = ResizeArray<Rectangle>(64)
let nearbyCoins = ResizeArray<Rectangle>(64)
let collectedCoins = ResizeArray<Rectangle>(16)
let keysToRemove = ResizeArray<struct (int * int)>(32)

let confettiColors = [|
  Color(255, 50, 50, 255)
  Color(50, 255, 50, 255)
  Color(50, 50, 255, 255)
  Color(255, 255, 50, 255)
  Color(255, 50, 255, 255)
  Color(50, 255, 255, 255)
  Color(255, 150, 50, 255)
  Color(255, 50, 150, 255)
|]

// Camera helpers (inlined because MonoGame backend lacks them)
let inline smoothFollow
  (camera: byref<Camera2D>)
  (target: Vector2)
  (speed: float32)
  =
  camera <- {
    camera with
        Position = Vector2.Lerp(camera.Position, target, speed)
  }

let inline clampTarget
  (camera: byref<Camera2D>)
  (minX: float32)
  (minY: float32)
  (maxX: float32)
  (maxY: float32)
  =
  let x = MathF.Max(minX, MathF.Min(camera.Position.X, maxX))
  let y = MathF.Max(minY, MathF.Min(camera.Position.Y, maxY))
  camera <- { camera with Position = Vector2(x, y) }

// -------------------------------------------------------------
// System: Input -> Movement Intent
// -------------------------------------------------------------

let inputSystem (dt: float32) (model: Model) : struct (Model * Cmd<Msg>) =
  let moveDir =
    if model.Actions.Held.Contains(GameAction.MoveLeft) then
      -1.0f
    elif model.Actions.Held.Contains(GameAction.MoveRight) then
      1.0f
    else
      0.0f

  model.PlayerVelocity <- Vector2(moveDir * moveSpeed, model.PlayerVelocity.Y)
  model, Cmd.none

// -------------------------------------------------------------
// System: Physics (gravity + collision + camera follow)
// -------------------------------------------------------------

let private spawnConfetti(model: Model) =
  let rng = System.Random.Shared
  let mutable pc = model.ParticleCount
  let particles = model.Particles
  let particleVelocities = model.ParticleVelocities

  for _ in 0..19 do
    if pc < particles.Length then
      let spawnPos =
        model.PlayerPosition
        + Vector2(
          playerWidth / 2.0f + float32(rng.NextDouble() * 20.0 - 10.0),
          playerHeight * 0.3f
        )

      particles[pc] <- {
        Position = spawnPos
        Size = Vector2(4.0f, 4.0f)
        Rotation = float32(rng.NextDouble() * Math.PI * 2.0)
        SourceRect = Rectangle(0, 0, 1, 1)
        Color = confettiColors[rng.Next(confettiColors.Length)]
      }

      particleVelocities[pc] <-
        Vector2(
          float32(rng.NextDouble() * 300.0 - 150.0),
          float32(rng.NextDouble() * -250.0 - 50.0)
        )

      pc <- pc + 1

  model.ParticleCount <- pc
  model.Assets.JumpSound.Play()

let physicsSystem (dt: float32) (model: Model) : struct (Model * Cmd<Msg>) =
  let canJump = model.IsGrounded
  let jumpHeld = model.Actions.Held.Contains(GameAction.Jump)
  let jumpStarted = model.Actions.Started.Contains(GameAction.Jump)

  let mutable velocityY = model.PlayerVelocity.Y + gravity * dt

  if jumpStarted && canJump then
    spawnConfetti model
    velocityY <- jumpSpeed
  elif not canJump && not jumpHeld && velocityY < 0.0f then
    velocityY <- velocityY * jumpCutMultiplier

  let velocity = Vector2(model.PlayerVelocity.X, velocityY)
  let prevPos = model.PlayerPosition
  let newPos = prevPos + velocity * dt

  // Collect platforms, spikes, coins from nearby chunks
  nearbyPlatforms.Clear()
  nearbySpikes.Clear()
  nearbyCoins.Clear()
  let pcx = int(Math.Floor(float newPos.X / float chunkWorldSize))
  let pcy = int(Math.Floor(float newPos.Y / float chunkWorldSize))

  for KeyValue(key, chunk) in model.Chunks do
    let struct (cx, cy) = key

    if abs(cx - pcx) <= chunkLoadRadius && abs(cy - pcy) <= chunkLoadRadius then
      nearbyPlatforms.AddRange(chunk.Platforms)
      nearbySpikes.AddRange(chunk.Spikes)
      nearbyCoins.AddRange(chunk.Coins)

  let struct (finalPos, finalVel, isGrounded) =
    resolvePlatformCollision prevPos newPos velocity nearbyPlatforms

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

  // Coin collection → increment score
  collectedCoins.Clear()

  for i = 0 to nearbyCoins.Count - 1 do
    if checkCollision playerRect nearbyCoins[i] then
      model.Score <- model.Score + 1
      collectedCoins.Add nearbyCoins[i]

  // Remove collected coins from chunks (mark as Empty in grid)
  for i = 0 to collectedCoins.Count - 1 do
    let coinRect = collectedCoins[i]
    let coinCx = int(Math.Floor(float coinRect.X / float chunkWorldSize))
    let coinCy = int(Math.Floor(float coinRect.Y / float chunkWorldSize))
    let key = struct (coinCx, coinCy)

    match model.Chunks.TryGetValue key with
    | true, chunk ->
      let cellX =
        int((float32 coinRect.X - chunk.Grid.Origin.X) / chunk.Grid.CellSize.X)

      let cellY =
        int((float32 coinRect.Y - chunk.Grid.Origin.Y) / chunk.Grid.CellSize.Y)

      if
        cellX >= 0
        && cellX < chunk.Grid.Width
        && cellY >= 0
        && cellY < chunk.Grid.Height
      then
        CellGrid2D.set cellX cellY Empty chunk.Grid
    | _ -> ()

  // Respawn if fallen too far
  if finalPos.Y > groundLevel + 500.0f then
    finalPos <- Vector2(spawnX, groundSurface - playerHeight)
    finalVel <- Vector2.Zero
    isGrounded <- true

  // Respawn key
  if model.Actions.Started.Contains(GameAction.Respawn) then
    finalPos <- Vector2(spawnX, groundSurface - playerHeight)
    finalVel <- Vector2.Zero
    isGrounded <- true

  model.PlayerPosition <- finalPos
  model.PlayerVelocity <- finalVel
  model.IsGrounded <- isGrounded

  // Update facing
  let moveDir =
    if model.Actions.Held.Contains(GameAction.MoveLeft) then
      -1.0f
    elif model.Actions.Held.Contains(GameAction.MoveRight) then
      1.0f
    else
      0.0f

  let newFacing =
    if moveDir < 0.0f then -1.0f
    elif moveDir > 0.0f then 1.0f
    else model.PlayerFacing

  model.PlayerFacing <- newFacing

  // Smooth camera follow
  let mutable cam = model.Camera
  smoothFollow &cam finalPos 0.1f
  clampTarget &cam -999999.0f -500.0f 999999.0f 2000.0f
  model.Camera <- cam

  // Track chunk coordinate
  model.PlayerChunk <- struct (pcx, pcy)

  model, Cmd.none

// -------------------------------------------------------------
// System: Chunk Management (only runs when player changes chunk)
// -------------------------------------------------------------

let private generateChunkAsync (cx: int) (cy: int) (seed: int) : Cmd<Msg> =
  Cmd.ofAsync
    (async { return generateChunk cx cy seed })
    (fun chunk -> ChunkCreated(struct (cx, cy), chunk))
    (fun _ex -> ChunkCreated(struct (cx, cy), generateChunk cx cy seed))

let chunkSystem (dt: float32) (model: Model) : struct (Model * Cmd<Msg>) =
  let pos = model.PlayerPosition
  let pcx = int(Math.Floor(float pos.X / float chunkWorldSize))
  let pcy = int(Math.Floor(float pos.Y / float chunkWorldSize))
  let keysToGenerate = ResizeArray<struct (int * int)>()

  for x in pcx - chunkLoadRadius .. pcx + chunkLoadRadius do
    for y in pcy - chunkLoadRadius .. pcy + chunkLoadRadius do
      let key = struct (x, y)

      if
        not(model.Chunks.ContainsKey(key))
        && not(model.PendingChunks.Contains(key))
      then
        model.PendingChunks.Add(key) |> ignore
        keysToGenerate.Add(key)

  evictDistantChunks pos model.Chunks keysToRemove

  if keysToGenerate.Count = 0 then
    struct (model, Cmd.none)
  else
    let cmd =
      Cmd.batch [|
        for struct (x, y) in keysToGenerate do
          generateChunkAsync x y model.Seed
      |]

    struct (model, cmd)

// -------------------------------------------------------------
// System: Animation
// -------------------------------------------------------------

let animationSystem (dt: float32) (model: Model) : struct (Model * Cmd<Msg>) =
  let animState = getAnimationState model.PlayerVelocity model.IsGrounded

  let playerSprite =
    match animState with
    | Idle -> AnimatedSprite.playIfNot "idle" model.PlayerSprite
    | Walk -> AnimatedSprite.playIfNot "walk" model.PlayerSprite
    | Jump -> AnimatedSprite.playIfNot "jump" model.PlayerSprite
    | Fall -> AnimatedSprite.playIfNot "fall" model.PlayerSprite

  let updatedSprite = AnimatedSprite.update dt playerSprite

  let flippedSprite =
    if model.PlayerFacing < 0.0f then
      AnimatedSprite.facingLeft updatedSprite
    else
      AnimatedSprite.facingRight updatedSprite

  model.PlayerSprite <- flippedSprite
  model.AnimationState <- animState
  model.TorchSprite <- AnimatedSprite.update dt model.TorchSprite

  model, Cmd.none

// -------------------------------------------------------------
// System: Particles
// -------------------------------------------------------------

let particleSystem (dt: float32) (model: Model) : struct (Model * Cmd<Msg>) =
  let particles = model.Particles
  let particleVelocities = model.ParticleVelocities
  let mutable particleCount = model.ParticleCount

  for i = 0 to particleCount - 1 do
    let vel = particleVelocities[i]
    let newVel = Vector2(vel.X, vel.Y + gravity * dt * 0.05f)
    particleVelocities[i] <- newVel

    particles[i] <- {
      particles[i] with
          Position = particles[i].Position + newVel * dt
    }

  ParticleSimulation.fadeAndCompact particles &particleCount 60.0f dt
  model.ParticleCount <- particleCount
  model, Cmd.none

// -------------------------------------------------------------
// System: Day / Night
// -------------------------------------------------------------

let dayNightSystem (dt: float32) (model: Model) : struct (Model * Cmd<Msg>) =
  let newTime =
    (model.DayNightTimeOfDay + dt * (24.0f / model.DayNightDuration)) % 24.0f

  model.DayNightTimeOfDay <- newTime
  model.TotalTime <- model.TotalTime + dt
  model, Cmd.none

// -------------------------------------------------------------
// System: Minimap
// -------------------------------------------------------------

let minimapSystem (dt: float32) (model: Model) : struct (Model * Cmd<Msg>) =
  let minimap = model.Minimap
  let posDelta = model.PlayerPosition - minimap.LastPlayerPos

  let needsUpdate =
    minimap.FrameCounter % MonoPlatformer.Minimap.updateInterval = 0
    || posDelta.LengthSquared() > 4.0f

  minimap.FrameCounter <- minimap.FrameCounter + 1

  if needsUpdate then
    minimap.LastPlayerPos <- model.PlayerPosition
    let gd = model.GraphicsDevice

    let cmd =
      Cmd.ofAsync
        (async {
          return
            MonoPlatformer.Minimap.generateMinimapData
              model.Chunks
              model.DayNightTimeOfDay
              model.PlayerPosition
        })
        (fun struct (colors, w, h) ->
          let tex = new Texture2D(gd, w, h)
          tex.SetData(colors)
          MinimapReady tex)
        (fun _ex ->
          let tex = new Texture2D(gd, 1, 1)
          tex.SetData([| Color.Black |])
          MinimapReady tex)

    model, cmd
  else
    model, Cmd.none

let diagnosticSystem (dt: float32) (model: Model) : struct (Model * Cmd<Msg>) =
  model.Diagnostics.Fps <- int(1.0f / max dt 0.0001f)
  model.Diagnostics.FrameTime <- dt
  model, Cmd.none

// -------------------------------------------------------------
// Combined Update Pipeline (Level 3 — explicit phase ordering)
// -------------------------------------------------------------

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | InputMapped actions ->
    model.Actions <- actions
    model, Cmd.none

  | ChunkCreated(key, chunk) ->
    model.Chunks[key] <- chunk
    model.PendingChunks.Remove(key) |> ignore
    model, Cmd.none

  | MinimapReady texture ->
    let oldTex = model.Minimap.Texture

    if oldTex <> Unchecked.defaultof<_> then
      oldTex.Dispose()

    model.Minimap.Texture <- texture
    model.Minimap.TexReady <- true
    model, Cmd.none

  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    System.start model
    |> System.pipeMutable(inputSystem dt)
    |> System.pipeMutable(physicsSystem dt)
    |> System.pipeMutable(chunkSystem dt)
    |> System.pipeMutable(animationSystem dt)
    |> System.pipeMutable(particleSystem dt)
    |> System.pipeMutable(dayNightSystem dt)
    |> System.pipeMutable(minimapSystem dt)
    |> System.pipeMutable(diagnosticSystem dt)
    |> System.finish id
