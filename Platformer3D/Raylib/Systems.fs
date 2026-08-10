module Platformer3D.Raylib.Systems

#nowarn "9"

open System
open System.Collections.Concurrent
open FSharp.NativeInterop
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open Mibo.Layout3D
open Platformer3D.Types
open Platformer3D.Physics
open Platformer3D.WorldGen
open Platformer3D.DayNight
open Platformer3D.Lighting
open Platformer3D.Raylib.Types

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
    let img =
      Raylib.GenImageColor(w, h, Color.Black |> RaylibColor.toRaylibColor)

    model.MinimapTexture <- Raylib.LoadTextureFromImage(img)
    Raylib.UnloadImage(img)
    use ptr = fixed buffer
    Raylib.UpdateTexture(model.MinimapTexture, NativePtr.toVoidPtr ptr)
    model.MinimapTexReady <- true

let private collectMushroomLights
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  (camPos: Numerics.Vector3)
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
              Position = worldPos + Numerics.Vector3(0.0f, 0.5f, 0.0f)
              Color = Color.rgb 255uy 200uy 120uy
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

  | MushroomLightsReady lights ->
    model.VisibleLights.Clear()
    model.VisibleLights.AddRange(lights)
    model, Cmd.none

  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    // Physics (reads chunk data from Chunks sub-system)
    let physicsModel =
      PhysicsSystem.update dt model.Actions model.Chunks.Chunks model.Physics

    model.Physics <- physicsModel

    // Particles (tick physics + fade)
    model.Particles <-
      Platformer3D.Particles.update
        (Platformer3D.Particles.ParticleMsg.Tick dt)
        model.Particles

    // Handle jump event from physics
    if model.Physics.JumpTriggered then
      model.Particles <-
        Platformer3D.Particles.update
          (Platformer3D.Particles.ParticleMsg.SpawnConfetti
            model.Physics.Position)
          model.Particles

      Raylib.PlaySound(model.JumpSound)
      model.Physics.JumpTriggered <- false

    // Chunks (reads player position from Physics sub-system)
    let struct (chunksModel, ccmd) =
      Chunks.update model.Physics.Position model.Chunks

    model.Chunks <- chunksModel

    // Day/Night
    model.DayNight <- DayNightSystem.update dt model.DayNight

    // Lighting (derives from DayNight)
    model.Lighting <-
      LightingSystem.update model.DayNight.TimeOfDay model.Lighting

    // Minimap (reads player position + chunks + time)
    let struct (minimapModel, mcmd) =
      Platformer3D.MinimapSystem.update
        model.Physics.Position
        model.Chunks.Chunks
        model.DayNight.TimeOfDay
        model.Minimap

    model.Minimap <- minimapModel

    // Diagnostics are sampled wall-clock in DiagnosticsView (once per Draw),
    // not here: measuring in Update hides frame drops. See Platformer3D.Diagnostics.

    // Animation (derives target clip from physics, plays on backend)
    if model.PlayerAnimClips.Clips.Length > 0 then
      let targetAnim =
        Platformer3D.Animation.targetClip model.Physics.IsGrounded model.Actions

      let anim =
        model.PlayerAnim
        |> Animation3DState.blendTo targetAnim 0.15f
        |> Animation3DState.update dt

      model.PlayerAnim <- anim

      // Second pose instance (multi-pose demo) — runs its fixed clip.
      model.PlayerAnim2 <- Animation3DState.update dt model.PlayerAnim2

    // Mushroom lights (periodic collection from chunks)
    mushroomLightFrameCounter <- mushroomLightFrameCounter + 1

    let mlightCmd =
      if mushroomLightFrameCounter % 6 = 0 then
        Cmd.ofAsync
          (async {
            return
              collectMushroomLights
                model.Chunks.Chunks
                model.Physics.CameraPosition
          })
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
