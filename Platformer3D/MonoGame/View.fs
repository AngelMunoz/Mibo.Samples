module Platformer3D.MonoGame.View

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open Mibo.Layout3D
open Platformer3D.Constants
open Platformer3D.Types
open Platformer3D.BlockData
open Platformer3D.MonoGame.Types

let loadOrGetModel
  (cache: Dictionary<string, Microsoft.Xna.Framework.Graphics.Model>)
  (path: string)
  (ctx: GameContext)
  =
  match cache.TryGetValue path with
  | true, m -> m
  | false, _ ->
    let assets = GameContext.getService<IAssets> ctx
    let m = assets.Model(path)
    cache[path] <- m
    m

let private meshMaterialCache =
  Dictionary<string, struct (PrimitiveMesh * Material3D)[]>()

/// MonoGame's content pipeline stores vertices in bone-local space, not model-root
/// space. Normal `Model.Draw` applies `CopyAbsoluteBoneTransformsTo` to position
/// each mesh; the instanced path grabs raw vertex buffers, so we must bake the
/// parent bone's absolute transform into the instance world transform manually.
/// Without this, meshes with non-identity root bones render at the wrong position.
let private boneTransformCache = Dictionary<string, Matrix>()

let mutable private currentModelCache =
  Unchecked.defaultof<Dictionary<string, Microsoft.Xna.Framework.Graphics.Model>>

let mutable private currentGameContext = Unchecked.defaultof<GameContext>

let private blockBounds = BoundingSphere(Vector3.Zero, 1.5f)

let private wrapPartAsPrimitive(part: ModelMeshPart) : PrimitiveMesh = {
  Vertices = part.VertexBuffer
  Indices = part.IndexBuffer
  PrimitiveCount = part.PrimitiveCount
  Bounds = blockBounds
}

let private resolveMeshesAndMaterial(blockType: BlockType) =
  let name = modelName blockType
  let path = AssetPaths.modelPath name

  match meshMaterialCache.TryGetValue path with
  | true, cached -> cached
  | false, _ ->
    let m = loadOrGetModel currentModelCache path currentGameContext

    // Compute and cache the absolute bone transform for this model's first mesh.
    // Block models are single-mesh, so one bone transform per model path suffices.
    if not(isNull m) && m.Meshes.Count > 0 && m.Bones.Count > 0 then
      let boneTransforms = Array.zeroCreate<Matrix> m.Bones.Count
      m.CopyAbsoluteBoneTransformsTo boneTransforms
      let boneIdx = m.Meshes[0].ParentBone.Index
      boneTransformCache[path] <- boneTransforms[boneIdx]

    let result =
      if not(isNull m) && m.Meshes.Count > 0 then
        [|
          for mesh in m.Meshes do
            for part in mesh.MeshParts do
              let mat = {
                Material3D.fromModelMeshPart part with
                    Roughness = 0.65f
                    Metallic = 0.2f
              }

              struct (wrapPartAsPrimitive part, mat)
        |]
      else
        Array.empty

    meshMaterialCache[path] <- result
    result

let private instancedCtx =
  InstancedRenderContext<BlockType, string>(
    getKey = modelName,
    getMeshesAndMaterial = resolveMeshesAndMaterial,
    getTransform =
      fun worldPos blockType ->
        let info = lookup blockType
        let rotAngle = info.RotationY * MathF.PI / 180.0f
        let yOff = info.VerticalOffset

        // Build the local-to-world transform from the block's placement.
        let worldMatrix =
          if rotAngle = 0.0f && yOff = 0.0f then
            Matrix.CreateTranslation(worldPos)
          elif rotAngle = 0.0f then
            Matrix.CreateTranslation(worldPos.X, worldPos.Y + yOff, worldPos.Z)
          else
            let rot = Matrix.CreateRotationY(rotAngle)

            let trans =
              Matrix.CreateTranslation(
                worldPos.X,
                worldPos.Y + yOff,
                worldPos.Z
              )

            rot * trans

        // Bake in the model's absolute bone transform so the mesh — whose
        // vertices are in bone-local space — lands at the correct world
        // position. Without this, MonoGame renders meshes offset from where
        // collision (which uses model-space extents) expects them.
        let path = AssetPaths.modelPath(modelName blockType)

        match boneTransformCache.TryGetValue path with
        | true, bone -> bone * worldMatrix
        | false, _ -> worldMatrix
  )

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
  let l = model.Lighting

  let camPos = model.Physics.CameraPosition
  let camTarget = model.Physics.CameraTarget

  let camera: Camera3D = {
    Position = Vector3(camPos.X, camPos.Y, camPos.Z)
    Target = Vector3(camTarget.X, camTarget.Y, camTarget.Z)
    Up = Vector3.UnitY
    FovY = MathHelper.ToRadians(55.0f)
    NearPlane = 0.1f
    FarPlane = 1000.0f
    Projection = CameraProjection.Perspective
  }

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

  let numericsCamPos = System.Numerics.Vector3(camPos.X, camPos.Y, camPos.Z)
  let maxChunkDistSq = 2500.0f

  for KeyValue(struct (cx, cz), chunk) in model.Chunks.Chunks do
    let bounds = chunk.Bounds
    let centerX = (bounds.Min.X + bounds.Max.X) * 0.5f
    let centerY = (bounds.Min.Y + bounds.Max.Y) * 0.5f
    let centerZ = (bounds.Min.Z + bounds.Max.Z) * 0.5f

    let chunkCenter = System.Numerics.Vector3(centerX, centerY, centerZ)

    if (chunkCenter - numericsCamPos).LengthSquared() <= maxChunkDistSq then
      let terrainGrid, _ = LayeredGrid3D.getOrAddLayer Layer.Terrain chunk.Grids

      CellGridRenderer3D.renderVolumeInstanced
        instancedCtx
        bounds
        terrainGrid
        buffer

  let playerPos = model.Physics.Position

  let playerTransform =
    let rot = Matrix.CreateRotationY(model.Physics.Facing)

    let trans = Matrix.CreateTranslation(playerPos.X, playerPos.Y, playerPos.Z)

    rot * trans

  let p = model.Particles

  for i = 0 to p.Count - 1 do
    Draw3D.drawBillboard
      model.ParticleTexture
      (Vector3(p.Positions[i].X, p.Positions[i].Y, p.Positions[i].Z))
      (Vector2(p.Sizes[i].X, p.Sizes[i].Y))
      (Mibo.Color.op_Implicit(p.Colors[i]))
      buffer
    |> Draw3D.drop

  buffer
  |> Draw3D.drawAnimatedModel model.PlayerAnim playerTransform
  |> Draw3D.drop

  buffer |> Draw3D.endCamera |> Draw3D.drop
