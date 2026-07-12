module Platformer.Raylib.Systems

#nowarn "9"


open System
open FSharp.NativeInterop
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Animation
open Platformer.Types
open Platformer.Physics
open Platformer.WorldGen
open Platformer.Raylib.Types

type Model = Types.Model
type Msg = Types.Msg

// -------------------------------------------------------------
// Backend-specific helpers
// -------------------------------------------------------------

let private uploadMinimapTexture
  (model: Model)
  (colors: Mibo.Color[])
  (w: int)
  (h: int)
  =
  let buffer = Array.zeroCreate<Raylib_cs.Color>(w * h)

  for i = 0 to buffer.Length - 1 do
    buffer[i] <-
      Raylib_cs.Color(colors[i].R, colors[i].G, colors[i].B, colors[i].A)

  if model.MinimapTexReady then
    use ptr = fixed buffer
    Raylib.UpdateTexture(model.MinimapTexture, NativePtr.toVoidPtr ptr)
  else
    let img = Raylib.GenImageColor(w, h, Raylib_cs.Color.Black)
    model.MinimapTexture <- Raylib.LoadTextureFromImage(img)
    Raylib.UnloadImage(img)
    use ptr = fixed buffer
    Raylib.UpdateTexture(model.MinimapTexture, NativePtr.toVoidPtr ptr)
    model.MinimapTexReady <- true

// -------------------------------------------------------------
// Root Update (router — dispatches to shared sub-systems + backend logic)
// -------------------------------------------------------------

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | InputMapped actions ->
    model.Actions <- actions
    model, Cmd.none

  | ChunkCreated(key, chunk) ->
    let chunkModel = Chunks.chunkCreated key chunk model.Chunks
    model.Chunks <- chunkModel
    model, Cmd.none

  | MinimapReady(colors, w, h) ->
    uploadMinimapTexture model colors w h
    model, Cmd.none

  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    // Physics (reads chunk data from Chunks sub-system)
    let physicsModel =
      PhysicsSystem.update (dt, model.Actions) model.Physics model.Chunks.Chunks

    model.Physics <- physicsModel
    // Chunks (reads player position from Physics sub-system)
    let struct (chunksModel, ccmd) =
      Chunks.update model.Physics.Position model.Chunks

    model.Chunks <- chunksModel
    // Particles
    model.ParticleState <-
      Platformer.Particles.update
        (Platformer.Particles.ParticleMsg.Tick dt)
        model.ParticleState

    // Handle jump event from physics
    if model.Physics.JumpTriggered then
      model.ParticleState <-
        Platformer.Particles.update
          (Platformer.Particles.ParticleMsg.SpawnConfetti model.Physics.Position)
          model.ParticleState

      Raylib.PlaySound(model.Assets.JumpSound)
      model.Physics.JumpTriggered <- false

    // Day/Night
    model.DayNight <- Platformer.DayNightSystem.update dt model.DayNight

    // Animation (derives from physics)
    model.Animation <-
      Platformer.Animation.update
        model.Physics.Velocity
        model.Physics.IsGrounded
        model.Physics.Facing

    // Minimap
    let struct (minimapModel, mcmd) =
      Platformer.MinimapSystem.update
        (model.Physics.Position,
         model.Chunks.Chunks,
         model.DayNight.Time.TimeOfDay)
        model.Minimap

    model.Minimap <- minimapModel
    // Diagnostics
    model.Diag <- Platformer.Diagnostics.update dt

    // Backend-specific sync
    let mutable cam = model.Camera
    Camera2D.smoothFollow &cam model.Physics.CameraTarget 0.1f
    model.Camera <- cam

    let stateName =
      match model.Animation.State with
      | Idle -> "idle"
      | Walk -> "walk"
      | Jump -> "jump"
      | Fall -> "fall"

    let sprite =
      AnimatedSprite.playIfNot stateName model.PlayerSprite
      |> AnimatedSprite.update dt

    model.PlayerSprite <-
      if model.Physics.Facing < 0.0f then
        AnimatedSprite.facingLeft sprite
      else
        AnimatedSprite.facingRight sprite

    model.TorchSprite <- AnimatedSprite.update dt model.TorchSprite

    model,
    Cmd.batch [
      ccmd
      |> Cmd.map(fun msg ->
        match msg with
        | Chunks.ChunkCreated(k, c) -> ChunkCreated(k, c))
      mcmd
      |> Cmd.map(fun msg ->
        match msg with
        | Platformer.MinimapSystem.MinimapReady(c, w, h) ->
          MinimapReady(c, w, h))
    ]
