module MonoThreeD.View

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Animation
open Mibo.Layout3D
open MonoThreeD.Constants
open MonoThreeD.Types

let loadOrGetModel
  (cache: Dictionary<string, Model>)
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

// Per-frame mutable context set once before rendering.
let mutable private currentModelCache =
  Unchecked.defaultof<Dictionary<string, Model>>

let mutable private currentGameContext = Unchecked.defaultof<GameContext>

let private blockBounds = BoundingSphere(Vector3.Zero, 1.5f)

let private wrapPartAsPrimitive(part: ModelMeshPart) : PrimitiveMesh = {
  Vertices = part.VertexBuffer
  Indices = part.IndexBuffer
  PrimitiveCount = part.PrimitiveCount
  Bounds = blockBounds
}

let private resolveMeshesAndMaterial(blockType: BlockType) =
  let path = BlockType.modelPath blockType

  match meshMaterialCache.TryGetValue path with
  | true, cached -> cached
  | false, _ ->
    let m = loadOrGetModel currentModelCache path currentGameContext

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

// Persistent context — allocated once, reused every frame.
let private instancedCtx =
  InstancedRenderContext<BlockType, string>(
    getKey = BlockType.modelPath,
    getMeshesAndMaterial = resolveMeshesAndMaterial,
    getTransform =
      fun worldPos blockType ->
        let rotAngle = BlockType.modelRotation blockType * MathF.PI / 180.0f
        let yOff = BlockType.modelVerticalOffset blockType

        if rotAngle = 0.0f && yOff = 0.0f then
          Matrix.CreateTranslation(worldPos)
        elif rotAngle = 0.0f then
          Matrix.CreateTranslation(worldPos.X, worldPos.Y + yOff, worldPos.Z)
        else
          let rot = Matrix.CreateRotationY(rotAngle)

          let trans =
            Matrix.CreateTranslation(worldPos.X, worldPos.Y + yOff, worldPos.Z)

          rot * trans
  )

let view (ctx: GameContext) (model: GameModel) (buffer: RenderBuffer3D) =
  let l = model.Lighting

  let minimap = model.Minimap

  if minimap.PixelBuffer.Length > 0 then
    let mutable mm = minimap
    let gd = MonoGameGameContext.getGraphicsDevice ctx
    Minimap.uploadTexture minimap.PixelBuffer &mm gd
    mm.PixelBuffer <- Array.Empty()
    model.Minimap <- mm

  let camera: Camera3D = {
    Position = model.CameraPosition
    Target = model.CameraTarget
    Up = Vector3.UnitY
    FovY = MathHelper.ToRadians(55.0f)
    NearPlane = 0.1f
    FarPlane = 1000.0f
    Projection = CameraProjection.Perspective
  }

  buffer
  |> Draw3D.beginCameraWith(
    Camera3D.render camera |> Camera3D.withClear l.SkyColor
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

  let camPos = model.CameraPosition
  let maxChunkDistSq = 2500.0f

  for KeyValue(struct (cx, cz), chunk) in model.Chunks do
    let bounds = chunk.Bounds
    let centerX = (bounds.Min.X + bounds.Max.X) * 0.5f
    let centerY = (bounds.Min.Y + bounds.Max.Y) * 0.5f
    let centerZ = (bounds.Min.Z + bounds.Max.Z) * 0.5f
    let chunkCenter = Numerics.Vector3(centerX, centerY, centerZ)

    let camPosNumerics = Numerics.Vector3(camPos.X, camPos.Y, camPos.Z)

    if (chunkCenter - camPosNumerics).LengthSquared() <= maxChunkDistSq then
      CellGridRenderer3D.renderVolumeInstanced
        instancedCtx
        bounds
        chunk.Grid
        buffer

  let playerTransform =
    let rot = Matrix.CreateRotationY(model.PlayerFacing)

    let trans =
      Matrix.CreateTranslation(
        model.PlayerPosition.X,
        model.PlayerPosition.Y,
        model.PlayerPosition.Z
      )

    rot * trans

  let p = model.Particles

  for i = 0 to p.Count - 1 do
    Draw3D.drawBillboard p.Texture p.Positions[i] p.Sizes[i] p.Colors[i] buffer
    |> Draw3D.drop

  buffer
  |> Draw3D.drawAnimatedModel model.PlayerAnim playerTransform
  |> Draw3D.drop

  buffer |> Draw3D.endCamera |> Draw3D.drop
