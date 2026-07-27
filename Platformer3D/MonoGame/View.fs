module Platformer3D.MonoGame.View

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
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
    // Block models are single-mesh, so one bone transform per model name suffices.
    // Keyed by the bare model name (a static string from BlockData) so the hot
    // getTransform path can look it up without building the path string per cell.
    if not(isNull m) && m.Meshes.Count > 0 && m.Bones.Count > 0 then
      let boneTransforms = Array.zeroCreate<Matrix> m.Bones.Count
      m.CopyAbsoluteBoneTransformsTo boneTransforms
      let boneIdx = m.Meshes[0].ParentBone.Index
      boneTransformCache[name] <- boneTransforms[boneIdx]

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
        // Center multi-cell meshes on their footprint (meshes are centered on
        // origin; blocks are placed at the cell corner — see BlockData). Folded
        // into the translation so all three branches pick it up.
        let placement =
          Vector3(
            worldPos.X + info.CenterOffsetX,
            worldPos.Y,
            worldPos.Z + info.CenterOffsetZ
          )

        // Build the local-to-world transform from the block's placement.
        let worldMatrix =
          if rotAngle = 0.0f && yOff = 0.0f then
            Matrix.CreateTranslation(placement)
          elif rotAngle = 0.0f then
            Matrix.CreateTranslation(
              placement.X,
              placement.Y + yOff,
              placement.Z
            )
          else
            let rot = Matrix.CreateRotationY(rotAngle)

            let trans =
              Matrix.CreateTranslation(
                placement.X,
                placement.Y + yOff,
                placement.Z
              )

            rot * trans

        // Bake in the model's absolute bone transform so the mesh — whose
        // vertices are in bone-local space — lands at the correct world
        // position. Without this, MonoGame renders meshes offset from where
        // collision (which uses model-space extents) expects them.
        // Keyed by the model name (info.ModelName is a static string from
        // BlockData) — building the full path here allocated a fresh string
        // per occupied cell per frame (~27k strings/frame).
        match boneTransformCache.TryGetValue info.ModelName with
        | true, bone -> bone * worldMatrix
        | false, _ -> worldMatrix
  )

// -------------------------------------------------------------
// Per-key custom shader scoping (grid-instanced-shaders feature validation).
//
// Two distinct effects exercise the per-key resolver on two key groups:
//   * Snow biome (any model name containing "snow") → Snow.fx — frosty,
//     crystalline sparkle.
//   * LargeBlock grass ("block-grass-large") → Toon.fx — banded cel shading.
// Everything else falls through to the default PBR instanced path
// (ValueNone). Both shaders opt into instancing via `technique Instanced`
// (see Content/Toon.fx, Content/Snow.fx and the Mibo instancing docs); a
// shader that doesn't opt in would silently fall back to PBR.
//
// The context is keyed by model name (getKey = modelName), so the resolver
// matches on the bare name. Precedence when a block is both snow AND large
// (e.g. "block-snow-large"): biome wins — the snow branch is checked first.
// -------------------------------------------------------------

// Lazy-loaded, cached after first use (assets are unavailable at module init).
let mutable private toonEffect: Effect voption = ValueNone
let mutable private snowEffect: Effect voption = ValueNone

let private shaderForKey(name: string) : Effect voption =
  // Biome wins over shape: a snow LargeBlock is snow first.
  if name.Contains("snow") then snowEffect
  elif name = KenneyModels.blockGrassLarge then toonEffect
  else ValueNone

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

  // Lazy-load the custom effects on the first frame (IAssets is unavailable at
  // module init). Loaded once, cached in the module-level voptions above.
  match toonEffect, snowEffect with
  | ValueNone, ValueNone ->
    let assets = GameContext.getService<IAssets> ctx
    toonEffect <- ValueSome(assets.Effect "Toon")
    snowEffect <- ValueSome(assets.Effect "Snow")

    // Preload every terrain model worldgen can place (Block/LargeBlock/TallBlock/
    // LowBlock/NarrowBlock × Grass/Snow + Platform — Shared/WorldGen.fs). Without
    // this, each model loads lazily on first sight — i.e. a render-thread
    // Content.Load<Model> exactly when a streamed-in chunk reveals a new biome.
    for biome in [ Biome3D.Grass; Biome3D.Snow ] do
      resolveMeshesAndMaterial(Block biome) |> ignore
      resolveMeshesAndMaterial(LargeBlock biome) |> ignore
      resolveMeshesAndMaterial(TallBlock biome) |> ignore
      resolveMeshesAndMaterial(LowBlock biome) |> ignore
      resolveMeshesAndMaterial(NarrowBlock biome) |> ignore

    resolveMeshesAndMaterial Platform |> ignore
  | _ -> ()

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

      CellGridRenderer3D.renderVolumeInstancedWithEffect
        instancedCtx
        bounds
        terrainGrid
        shaderForKey
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

  // Share one pose evaluation between the skinned draw and the weapon
  // attachments on both arm bones (fluent Draw DSL).
  match AnimatedModel.computePose model.PlayerAnim with
  | ValueSome pose ->
    buffer.animatedModel(model.PlayerAnim, playerTransform, pose = pose).drop()

    for prop in model.PlayerProps do
      buffer
        .attachedMesh(
          model.PlayerAnim,
          BoneRef.ByName prop.BoneName,
          prop.LocalTransform,
          prop.Mesh,
          prop.Material,
          playerTransform,
          pose = pose
        )
        .drop()
  | ValueNone ->
    buffer
    |> Draw3D.drawAnimatedModel model.PlayerAnim playerTransform
    |> Draw3D.drop

  // Second instance of the same Model at a different pose — no attachment.
  let offsetTransform =
    Matrix.CreateTranslation(playerPos.X + 2.5f, playerPos.Y, playerPos.Z)

  match AnimatedModel.computePose model.PlayerAnim2 with
  | ValueSome pose2 ->
    buffer
      .animatedModel(model.PlayerAnim2, offsetTransform, pose = pose2)
      .drop()
  | ValueNone -> buffer.animatedModel(model.PlayerAnim2, offsetTransform).drop()

  buffer |> Draw3D.endCamera |> Draw3D.drop
