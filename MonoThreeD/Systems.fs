module MonoThreeD.Systems

open System
open System.Collections.Concurrent
open Microsoft.Xna.Framework
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open Mibo.Input
open Mibo.Layout3D
open MonoThreeD.Constants
open MonoThreeD.Types
open MonoThreeD.Physics
open MonoThreeD.WorldGen
open MonoThreeD.DayNight

let inputSystem
  (dt: float32)
  (model: GameModel)
  : struct (GameModel * Cmd<Msg>) =
  let mutable yaw = model.CameraYaw
  let mutable pitch = model.CameraPitch

  if model.Actions.Held.Contains(GameAction.RotateCameraLeft) then
    yaw <- yaw - 2.0f * dt

  if model.Actions.Held.Contains(GameAction.RotateCameraRight) then
    yaw <- yaw + 2.0f * dt

  if model.Actions.Held.Contains(GameAction.RotateCameraUp) then
    pitch <- pitch + 1.5f * dt

  if model.Actions.Held.Contains(GameAction.RotateCameraDown) then
    pitch <- pitch - 1.5f * dt

  model.CameraYaw <- yaw
  model.CameraPitch <- Math.Clamp(pitch, -0.5f, 1.3f)
  struct (model, Cmd.none)

let private confettiColors = [|
  Color(255uy, 50uy, 50uy, 255uy)
  Color(50uy, 255uy, 50uy, 255uy)
  Color(50uy, 50uy, 255uy, 255uy)
  Color(255uy, 255uy, 50uy, 255uy)
  Color(255uy, 50uy, 255uy, 255uy)
  Color(50uy, 255uy, 255uy, 255uy)
  Color(255uy, 150uy, 50uy, 255uy)
  Color(255uy, 50uy, 150uy, 255uy)
|]

let private spawnConfetti(model: GameModel) =
  let rng = System.Random.Shared
  let p = model.Particles
  let mutable pc = p.Count

  for _ in 0..100 do
    if pc < p.Positions.Length then
      let offset =
        Vector3(
          float32(rng.NextDouble() * 0.4 - 0.2),
          float32(rng.NextDouble() * 0.2),
          float32(rng.NextDouble() * 0.4 - 0.2)
        )

      p.Positions[pc] <-
        model.PlayerPosition + Vector3(0.0f, playerHeight * 0.5f, 0.0f) + offset

      p.Sizes[pc] <- Vector2(0.05f, 0.05f)
      p.Colors[pc] <- confettiColors[rng.Next(confettiColors.Length)]

      let angle = float32(rng.NextDouble()) * MathF.PI * 6.0f
      let speed = float32(rng.NextDouble()) * 3.0f + 5.0f

      p.Velocities[pc] <-
        Vector3(
          MathF.Cos(angle) * speed,
          float32(rng.NextDouble()) * 3.0f + 2.0f,
          MathF.Sin(angle) * speed
        )

      pc <- pc + 1

  p.Count <- pc

  if not(isNull model.JumpSound) then
    model.JumpSound.Play() |> ignore

let physicsSystem
  (dt: float32)
  (model: GameModel)
  : struct (GameModel * Cmd<Msg>) =
  let moveDir = computeMoveDirection model.Actions model.CameraYaw

  let vel =
    if model.IsGrounded && model.Actions.Started.Contains(GameAction.Jump) then
      spawnConfetti model
      Vector3(model.PlayerVelocity.X, jumpSpeed, model.PlayerVelocity.Z)
    else
      model.PlayerVelocity

  let vel = Vector3(vel.X, vel.Y + gravity * dt, vel.Z)
  let vel = applyMovement dt moveDir vel

  let prevPos = model.PlayerPosition
  let newPos = prevPos + vel * dt

  let struct (finalPos, finalVel, grounded, scoreDelta) =
    resolveCollision prevPos newPos vel model.Chunks

  let mutable finalPos = finalPos
  let mutable finalVel = finalVel
  let mutable grounded = grounded

  model.Score <- model.Score + scoreDelta

  if finalPos.Y < fallLimit then
    finalPos <- spawnPosition
    finalVel <- Vector3.Zero
    grounded <- false

  if model.Actions.Started.Contains(GameAction.Respawn) then
    finalPos <- spawnPosition
    finalVel <- Vector3.Zero
    grounded <- false

  model.PlayerPosition <- finalPos
  model.PlayerVelocity <- finalVel
  model.IsGrounded <- grounded

  if moveDir.LengthSquared() > 0.1f then
    model.PlayerFacing <- MathF.Atan2(moveDir.X, moveDir.Z)

  let target = finalPos + Vector3(0.0f, playerHeight * 0.5f, 0.0f)

  let desiredCamPos =
    computeCameraPosition target model.CameraYaw model.CameraPitch

  let lerpFactor = 1.0f - MathF.Exp(-dt * cameraLerpSpeed)

  model.CameraPosition <-
    Vector3.Lerp(model.CameraPosition, desiredCamPos, lerpFactor)

  model.CameraTarget <- Vector3.Lerp(model.CameraTarget, target, lerpFactor)

  struct (model, Cmd.none)

let private generateChunkAsync (cx: int) (cz: int) (seed: int) : Cmd<Msg> =
  Cmd.ofAsync
    (async { return generateChunk cx cz seed })
    (fun chunk -> ChunkCreated(struct (cx, cz), chunk))
    (fun _ex -> ChunkCreated(struct (cx, cz), generateChunk cx cz seed))

let chunkSystem
  (dt: float32)
  (model: GameModel)
  : struct (GameModel * Cmd<Msg>) =
  // Physics stores player position in XNA Vector3; the world-gen helpers take
  // System.Numerics.Vector3 (the Core grid's native type). Convert the float triple.
  let pos = model.PlayerPosition
  let numericsPos = System.Numerics.Vector3(pos.X, pos.Y, pos.Z)

  let pcx = int(Math.Floor(float pos.X / float chunkWorldWidth))

  let pcz = int(Math.Floor(float pos.Z / float chunkWorldDepth))

  let keysToGenerate = ResizeArray<struct (int * int)>()

  for x in pcx - chunkLoadRadius .. pcx + chunkLoadRadius do
    for z in pcz - chunkLoadRadius .. pcz + chunkLoadRadius do
      let key = struct (x, z)

      if
        not(model.Chunks.ContainsKey(key))
        && not(model.PendingChunks.Contains(key))
      then
        model.PendingChunks.Add(key) |> ignore
        keysToGenerate.Add(key)

  evictDistantChunks numericsPos model.Chunks model.KeysToRemove

  if keysToGenerate.Count = 0 then
    struct (model, Cmd.none)
  else
    let cmd =
      Cmd.batch [|
        for struct (x, z) in keysToGenerate do
          generateChunkAsync x z model.Seed
      |]

    struct (model, cmd)

let dayNightSystem
  (dt: float32)
  (model: GameModel)
  : struct (GameModel * Cmd<Msg>) =
  let newTime =
    (model.DayNightTimeOfDay + dt * (24.0f / model.DayNightDuration)) % 24.0f

  model.DayNightTimeOfDay <- newTime
  model.TotalTime <- model.TotalTime + dt
  struct (model, Cmd.none)

let particleSystem
  (dt: float32)
  (model: GameModel)
  : struct (GameModel * Cmd<Msg>) =
  let p = model.Particles
  let positions = p.Positions
  let velocities = p.Velocities
  let colors = p.Colors
  let mutable count = p.Count

  for i = 0 to count - 1 do
    let vel = velocities[i]
    let newVel = Vector3(vel.X, vel.Y + gravity * dt * 0.6f, vel.Z)
    velocities[i] <- newVel
    positions[i] <- positions[i] + newVel * dt

  let fadeAmount = 130.0f * dt
  let mutable writeIdx = 0

  for readIdx = 0 to count - 1 do
    let c = colors[readIdx]
    let newAlpha = MathF.Max(0.0f, float32 c.A - fadeAmount)

    if newAlpha > 0.0f then
      positions[writeIdx] <- positions[readIdx]
      velocities[writeIdx] <- velocities[readIdx]
      colors[writeIdx] <- Color(c.R, c.G, c.B, byte newAlpha)
      writeIdx <- writeIdx + 1

  p.Count <- writeIdx
  struct (model, Cmd.none)

let inline minimapSystem
  (dt: float32)
  (model: GameModel)
  : struct (GameModel * Cmd<Msg>) =
  let minimap = model.Minimap
  let posDelta = model.PlayerPosition - minimap.LastPlayerPos

  let needsUpdate =
    minimap.FrameCounter % Minimap.updateInterval = 0
    || posDelta.LengthSquared() > 4.0f

  minimap.FrameCounter <- minimap.FrameCounter + 1

  if needsUpdate then
    minimap.LastPlayerPos <- model.PlayerPosition

    let pos = model.PlayerPosition
    let numericsPos = System.Numerics.Vector3(pos.X, pos.Y, pos.Z)

    let cmd =
      Cmd.ofAsync
        (async {
          return
            Minimap.generateMinimapPixels
              numericsPos
              model.DayNightTimeOfDay
              model.Chunks
        })
        (fun pixels -> MinimapReady pixels)
        (fun _ex ->
          // Fallback: a single-pixel black buffer keeps the texture path intact.
          MinimapReady(
            Array.create 1 (Color.Black |> MonoGameColor.toMonoGameColor)
          ))

    struct (model, cmd)
  else
    struct (model, Cmd.none)

let inline diagnosticsSystem
  (dt: float32)
  (model: GameModel)
  : struct (GameModel * Cmd<Msg>) =
  let diag = model.Diagnostics

  // FPS: smoothed 1/elapsed (MonoGame has no raylib-style GetFPS() global).
  if dt > 0.0f then
    let instant = 1.0f / dt
    diag.Fps <- int(MathF.Round((float32 diag.Fps) * 0.9f + instant * 0.1f))

  diag.ChunkCount <- model.Chunks.Count
  diag.Score <- model.Score
  diag.TimeOfDay <- model.DayNightTimeOfDay
  diag.PlayerX <- model.PlayerPosition.X
  diag.PlayerY <- model.PlayerPosition.Y
  diag.PlayerZ <- model.PlayerPosition.Z
  diag.IsGrounded <- model.IsGrounded
  diag.ParticleCount <- model.Particles.Count
  struct (model, Cmd.none)

let inline animationSystem
  (dt: float32)
  (model: GameModel)
  : struct (GameModel * Cmd<Msg>) =
  // MonoGame: the player is an AnimatedModel (Model+Mesh+State). Raylib used a
  // bare Animation3DState carrying a .Model; here the whole animated model updates.
  let clips = model.PlayerAnim.State.Clips

  if clips.Clips.Length = 0 then
    struct (model, Cmd.none)
  else
    let isMoving =
      model.Actions.Held.Contains(GameAction.MoveForward)
      || model.Actions.Held.Contains(GameAction.MoveBackward)
      || model.Actions.Held.Contains(GameAction.MoveLeft)
      || model.Actions.Held.Contains(GameAction.MoveRight)

    let targetAnim =
      if not model.IsGrounded then "jump"
      elif isMoving then "walk"
      else "idle"

    let anim =
      model.PlayerAnim
      |> AnimatedModel.blendTo targetAnim 0.15f
      |> AnimatedModel.update dt

    model.PlayerAnim <- anim

    struct (model, Cmd.none)

let inline lightingSystem
  (dt: float32)
  (model: GameModel)
  : struct (GameModel * Cmd<Msg>) =
  let time = model.DayNightTimeOfDay
  let l = model.Lighting
  l.SkyColor <- getSkyColor time
  l.AmbientColor <- getAmbientColor time
  l.AmbientIntensity <- getAmbientIntensity time
  l.LightDirection <- getPrimaryLightDirection time arcRadius
  l.LightColor <- getPrimaryLightColor time
  l.LightIntensity <- getPrimaryLightIntensity time
  struct (model, Cmd.none)

let private collectMushroomLights
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  (camPos: Vector3)
  : PointLight3D[] =
  let lights = ResizeArray<PointLight3D>(8)

  for KeyValue(struct (_cx, _cz), chunk) in chunks do
    if lights.Count < 8 then
      CellGridRenderer3D.renderVolume
        chunk.Bounds
        chunk.Grid
        (fun worldPos blockType ->
          if
            blockType = BlockType.MushroomLight
            && lights.Count < 8
            && (worldPos - camPos).LengthSquared() <= 1600.0f
          then
            lights.Add {
              Position = (worldPos + Vector3(0.0f, 0.5f, 0.0f)).ToNumerics()
              Color = Color(255, 200, 120).ToMiboColor()
              Intensity = 1.2f
              Radius = 8.0f
              Falloff = 1.2f
              CastsShadows = false
              ShadowBias = ValueNone
            })

  lights.ToArray()

let mutable private mushroomLightFrameCounter = 0

let mushroomLightSystem
  (dt: float32)
  (model: GameModel)
  : struct (GameModel * Cmd<Msg>) =
  mushroomLightFrameCounter <- mushroomLightFrameCounter + 1

  if mushroomLightFrameCounter % 6 = 0 then
    let camPos = model.CameraPosition

    let cmd =
      Cmd.ofAsync
        (async { return collectMushroomLights model.Chunks camPos })
        (fun lights -> MushroomLightsReady lights)
        (fun _ex -> MushroomLightsReady Array.empty)

    struct (model, cmd)
  else
    struct (model, Cmd.none)

let update (msg: Msg) (model: GameModel) : struct (GameModel * Cmd<Msg>) =
  match msg with
  | InputMapped actions ->
    model.Actions <- actions
    struct (model, Cmd.none)
  | ChunkCreated(key, chunk) ->
    model.Chunks[key] <- chunk
    model.PendingChunks.Remove(key) |> ignore
    struct (model, Cmd.none)
  | MinimapReady pixels ->
    let mutable minimap = model.Minimap
    // The minimap texture needs a GraphicsDevice; the view holds a reference to
    // the registered device. We can't upload from the message handler directly
    // (no device here), so stash the pixels and let the view upload on next draw.
    minimap.PixelBuffer <- pixels
    model.Minimap <- minimap
    struct (model, Cmd.none)
  | MushroomLightsReady lights ->
    model.VisibleLights.Clear()
    model.VisibleLights.AddRange(lights)
    struct (model, Cmd.none)
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    System.start model
    |> System.pipeMutable(inputSystem dt)
    |> System.pipeMutable(physicsSystem dt)
    |> System.pipeMutable(animationSystem dt)
    |> System.pipeMutable(chunkSystem dt)
    |> System.pipeMutable(particleSystem dt)
    |> System.pipeMutable(minimapSystem dt)
    |> System.pipeMutable(dayNightSystem dt)
    |> System.pipeMutable(lightingSystem dt)
    |> System.pipeMutable(mushroomLightSystem dt)
    |> System.pipeMutable(diagnosticsSystem dt)
    |> System.finish id
