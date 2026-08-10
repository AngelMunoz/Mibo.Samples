module Platformer3D.MonoGame.Systems

open System
open System.Collections.Concurrent
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open Mibo.Input
open Mibo.Layout3D
open Platformer3D.Constants
open Platformer3D.Types
open Platformer3D.Physics
open Platformer3D.WorldGen
open Platformer3D.DayNight
open Platformer3D.Lighting
open Platformer3D.MonoGame.Types

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
    buffer[i] <- Mibo.Color.op_Implicit(colors[i])

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

let private collectMushroomLights
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  (camPos: Vector3)
  : PointLight3D[] =
  let lights = ResizeArray<PointLight3D>(8)

  for KeyValue(struct (_cx, _cz), chunk) in chunks do
    if lights.Count < 8 then
      let struct (terrainGrid, _) =
        LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

      CellGridRenderer3D.renderVolume
        chunk.Bounds
        terrainGrid
        (fun worldPos blockType ->
          if
            blockType = BlockType.MushroomLight
            && lights.Count < 8
            && (worldPos - camPos).LengthSquared() <= 1600.0f
          then
            lights.Add {
              Position = (worldPos + Vector3(0.0f, 0.5f, 0.0f)).ToNumerics()
              Color = Mibo.Color.rgb 255uy 200uy 120uy
              Intensity = 1.2f
              Radius = 8.0f
              Falloff = 1.2f
              CastsShadows = false
              ShadowDirection = ValueNone
              ShadowBias = ValueNone
            })

  lights.ToArray()

let mutable private mushroomLightFrameCounter = 0

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

  | MushroomLightsReady lights ->
    model.VisibleLights.Clear()
    model.VisibleLights.AddRange(lights)
    model, Cmd.none

  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    // Physics
    let physicsModel =
      PhysicsSystem.update dt model.Actions model.Chunks.Chunks model.Physics

    model.Physics <- physicsModel

    // Particles
    model.Particles <-
      Platformer3D.Particles.update
        (Platformer3D.Particles.ParticleMsg.Tick dt)
        model.Particles

    // Handle jump event
    if model.Physics.JumpTriggered then
      model.Particles <-
        Platformer3D.Particles.update
          (Platformer3D.Particles.ParticleMsg.SpawnConfetti
            model.Physics.Position)
          model.Particles

      if not(isNull model.JumpSound) then
        model.JumpSound.Play() |> ignore

      model.Physics.JumpTriggered <- false

    // Chunks
    let struct (chunksModel, ccmd) =
      Chunks.update model.Physics.Position model.Chunks

    model.Chunks <- chunksModel

    // Day/Night
    model.DayNight <- DayNightSystem.update dt model.DayNight

    // Lighting
    model.Lighting <-
      LightingSystem.update model.DayNight.TimeOfDay model.Lighting

    // Minimap
    let struct (minimapModel, mcmd) =
      Platformer3D.MinimapSystem.update
        model.Physics.Position
        model.Chunks.Chunks
        model.DayNight.TimeOfDay
        model.Minimap

    model.Minimap <- minimapModel

    // Diagnostics are sampled wall-clock in DiagnosticsView (once per Draw),
    // not here: under MonoGame's fixed timestep Tick runs at the fixed rate and
    // hides real frame drops. See Platformer3D.Diagnostics.

    // Animation
    let clips = model.PlayerAnim.State.Clips

    if clips.Clips.Length > 0 then
      let targetAnim =
        Platformer3D.Animation.targetClip model.Physics.IsGrounded model.Actions

      let anim =
        model.PlayerAnim
        |> AnimatedModel.blendTo targetAnim 0.15f
        |> AnimatedModel.update dt

      model.PlayerAnim <- anim

      // Second pose instance (multi-pose demo) — runs its fixed clip.
      model.PlayerAnim2 <- AnimatedModel.update dt model.PlayerAnim2

    // Mushroom lights
    mushroomLightFrameCounter <- mushroomLightFrameCounter + 1

    let mlightCmd =
      if mushroomLightFrameCounter % 6 = 0 then
        let camPos = model.Physics.CameraPosition
        let xnaCamPos = Vector3(camPos.X, camPos.Y, camPos.Z)

        Cmd.ofAsync
          (async { return collectMushroomLights model.Chunks.Chunks xnaCamPos })
          (fun lights -> MushroomLightsReady lights)
          (fun _ex -> MushroomLightsReady Array.empty)
      else
        Cmd.none

    model,
    Cmd.batch3(
      ccmd
      |> Cmd.map(fun msg ->
        match msg with
        | Chunks.ChunkCreated(k, c) -> ChunkCreated(k, c)),
      mcmd
      |> Cmd.map(fun msg ->
        match msg with
        | Platformer3D.MinimapSystem.MinimapReady(c, w, h) ->
          MinimapReady(c, w, h)),
      mlightCmd
    )
