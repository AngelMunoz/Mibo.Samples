module Platformer.MonoGame.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
open Mibo.Animation
open Platformer.Types
open Platformer.Physics
open Platformer.WorldGen
open Platformer.MonoGame.Types
open Platformer.MonoGame.Camera

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
  let buffer = Array.zeroCreate<Microsoft.Xna.Framework.Color>(w * h)

  for i = 0 to buffer.Length - 1 do
    buffer[i] <-
      Microsoft.Xna.Framework.Color(
        colors[i].R,
        colors[i].G,
        colors[i].B,
        colors[i].A
      )

  let needsCreate =
    not model.MinimapTexReady
    || model.MinimapTexture.Width <> w
    || model.MinimapTexture.Height <> h

  if needsCreate then
    if model.MinimapTexReady then
      model.MinimapTexture.Dispose()

    model.MinimapTexture <- new Texture2D(model.GraphicsDevice, w, h)
    model.MinimapTexReady <- true

  model.MinimapTexture.SetData(buffer, 0, w * h)

// -------------------------------------------------------------
// Root Update (router)
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

    let physicsModel =
      PhysicsSystem.update (dt, model.Actions) model.Physics model.Chunks.Chunks

    model.Physics <- physicsModel

    // Chunks (reads player position from Physics sub-system)
    let struct (chunksModel, ccmd) =
      Chunks.update model.Physics.Position model.Chunks

    model.Chunks <- chunksModel

    model.ParticleState <-
      Platformer.Particles.update
        (Platformer.Particles.ParticleMsg.Tick dt)
        model.ParticleState

    if model.Physics.JumpTriggered then
      model.ParticleState <-
        Platformer.Particles.update
          (Platformer.Particles.ParticleMsg.SpawnConfetti model.Physics.Position)
          model.ParticleState

      model.Assets.JumpSound.Play() |> ignore
      model.Physics.JumpTriggered <- false

    model.DayNight <- Platformer.DayNightSystem.update dt model.DayNight

    model.Animation <-
      Platformer.Animation.update
        model.Physics.Velocity
        model.Physics.IsGrounded
        model.Physics.Facing

    let struct (minimapModel, mcmd) =
      Platformer.MinimapSystem.update
        (model.Physics.Position,
         model.Chunks.Chunks,
         model.DayNight.Time.TimeOfDay)
        model.Minimap

    model.Minimap <- minimapModel

    model.Diag <- Platformer.Diagnostics.update dt

    // Camera follows the player — framing is a camera concern, not physics'.
    let query = {
      PlayerPosition = Vector2.op_Implicit model.Physics.Position
    }

    model.Camera <- Camera.update query model.Camera

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
    model.CoinSprite <- AnimatedSprite.update dt model.CoinSprite
    model.FlagSprite <- AnimatedSprite.update dt model.FlagSprite


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
