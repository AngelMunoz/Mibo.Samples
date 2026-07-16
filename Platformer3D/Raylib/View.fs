module Platformer3D.Raylib.View

#nowarn "9"

open System
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open Mibo.Layout3D
open Platformer3D.Types
open Platformer3D.BlockData
open Platformer3D.Raylib.Types

let loadOrGetModel
  (cache: Dictionary<string, Raylib_cs.Model>)
  (path: string)
  (ctx: GameContext)
  =
  if path = "" then
    Unchecked.defaultof<Raylib_cs.Model>
  else
    match cache.TryGetValue path with
    | true, m -> m
    | false, _ ->
      let assets = GameContext.getService<IAssets> ctx
      let m = assets.Model(path)
      cache[path] <- m
      m

// Persistent mesh/material cache keyed by model path.
let private meshMaterialCache =
  Dictionary<string, struct (Mesh * Material3D)[]>()

// Per-frame mutable context set once before rendering.
let mutable private currentModelCache =
  Unchecked.defaultof<Dictionary<string, Raylib_cs.Model>>

let mutable private currentGameContext = Unchecked.defaultof<GameContext>

let private resolveMeshesAndMaterial(blockType: BlockType) =
  let name = modelName blockType
  let path = AssetPaths.modelPath name

  match meshMaterialCache.TryGetValue path with
  | true, cached -> cached
  | false, _ ->
    let m = loadOrGetModel currentModelCache path currentGameContext

    let result =
      if m.MeshCount > 0 then
        [|
          for mi = 0 to m.MeshCount - 1 do
            let mesh = NativePtr.get m.Meshes mi
            let matIdx = NativePtr.get m.MeshMaterial mi
            let raylibMat: Material = NativePtr.get m.Materials matIdx

            let material3d: Material3D = {
              Material3D.fromRaylibMaterial raylibMat with
                  Roughness = 0.65f
            }

            struct (mesh, material3d)
        |]
      else
        Array.empty

    meshMaterialCache[path] <- result
    result

// Persistent context — allocated once, reused every frame.
let private instancedCtx =
  InstancedRenderContext<BlockType, string>(
    getKey = modelName,
    getMeshesAndMaterial = resolveMeshesAndMaterial,
    getTransform =
      fun worldPos blockType ->
        let info = lookup blockType
        let rotAngle = info.RotationY * MathF.PI / 180.0f
        let yOff = info.VerticalOffset

        if rotAngle = 0.0f && yOff = 0.0f then
          Raymath.MatrixTranslate(worldPos.X, worldPos.Y, worldPos.Z)
        elif rotAngle = 0.0f then
          Raymath.MatrixTranslate(worldPos.X, worldPos.Y + yOff, worldPos.Z)
        else
          let rot = Raymath.MatrixRotateY(rotAngle)

          let trans =
            Raymath.MatrixTranslate(worldPos.X, worldPos.Y + yOff, worldPos.Z)

          Raymath.MatrixMultiply(rot, trans)
  )

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
  let l = model.Lighting

  let camera =
    Camera3D(
      model.Physics.CameraPosition,
      model.Physics.CameraTarget,
      Vector3.UnitY,
      55.0f,
      CameraProjection.Perspective
    )

  buffer
  |> Draw3D.beginCameraWith(
    Camera3D.render camera
    |> Camera3D.withClear(Mibo.Color.op_Implicit(l.SkyColor))
  )
  |> Draw3D.setAmbientLight {
    Color = l.AmbientColor
    Intensity = l.AmbientIntensity
  }
  |> Draw3D.addDirectionalLight {
    Direction = l.LightDirection
    Color = l.LightColor
    Intensity = l.LightIntensity
    CastsShadows = true
  }
  |> Draw3D.drop

  currentModelCache <- model.ModelCache
  currentGameContext <- ctx
  instancedCtx.ResetFrameBuffers()

  for light in model.VisibleLights do
    Draw3D.addPointLight light buffer |> Draw3D.drop

  let camPos = model.Physics.CameraPosition
  let maxChunkDistSq = 3000.0f

  for KeyValue(struct (cx, cz), chunk) in model.Chunks.Chunks do
    let chunkCenter =
      Vector3(
        (chunk.Bounds.Min.X + chunk.Bounds.Max.X) * 0.5f,
        (chunk.Bounds.Min.Y + chunk.Bounds.Max.Y) * 0.5f,
        (chunk.Bounds.Min.Z + chunk.Bounds.Max.Z) * 0.5f
      )

    if (chunkCenter - camPos).LengthSquared() <= maxChunkDistSq then
      let terrainGrid, _ = LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

      CellGridRenderer3D.renderVolumeInstanced
        instancedCtx
        chunk.Bounds
        terrainGrid
        buffer

  let playerModel = model.PlayerAnim.Model

  let playerTransform =
    let rot = Raymath.MatrixRotateY(model.Physics.Facing)

    let trans =
      Raymath.MatrixTranslate(
        model.Physics.Position.X,
        model.Physics.Position.Y,
        model.Physics.Position.Z
      )

    Raymath.MatrixMultiply(rot, trans)

  Animation3DState.applyToModel model.PlayerAnim

  let p = model.Particles

  for i = 0 to p.Count - 1 do
    Draw3D.drawBillboard
      model.ParticleTexture
      p.Positions[i]
      p.Sizes[i]
      (Mibo.Color.op_Implicit(p.Colors[i]))
      buffer
    |> Draw3D.drop

  buffer
  |> Draw3D.drawModel playerModel playerTransform
  |> Draw3D.endCamera
  |> Draw3D.drop
