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

let private resolveMeshesAndMaterial(modelName: string) =
  match modelName with
  | "" -> Array.empty
  | name ->
    let path = AssetPaths.modelPath name

    match meshMaterialCache.TryGetValue path with
    | true, cached -> cached
    | false, _ ->
      let m = loadOrGetModel currentModelCache path currentGameContext

      // Compute and cache the absolute bone transform for this model's first mesh.
      // Block models are single-mesh, so one bone transform per model name suffices.
      // Keyed by the bare model name so the hot getTransform path can look it up
      // without building the path string per cell.
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

// -------------------------------------------------------------
// Instanced rendering — the heightmap contract.
//
// The context's transform function receives each column's base position
// (footprint lifted to y = 0); the vertical axis comes from the tile:
// `CapTile.BaseY` lifts surface caps, `MassTile.Depth` scales the cliff
// unit block, `PropTile.Y` lifts everything floating above the ground.
// One instance per populated cell, one draw per model name per layer.
// -------------------------------------------------------------

/// Rotation + centering placement shared by caps and props, with the
/// model's absolute bone transform folded in front (content-pipeline
/// vertices are bone-local — see boneTransformCache). The context supplies
/// the column's base position as an XNA Vector3 (y = 0).
let private placeAt (basePos: Vector3) (y: float32) (info: BlockInfo) : Matrix =
  let rotAngle = info.RotationY * MathF.PI / 180.0f

  let worldMatrix =
    let trans =
      Matrix.CreateTranslation(
        basePos.X + info.CenterOffsetX,
        y,
        basePos.Z + info.CenterOffsetZ
      )

    if rotAngle = 0.0f then
      trans
    else
      Matrix.CreateRotationY(rotAngle) * trans

  match boneTransformCache.TryGetValue info.ModelName with
  | true, bone -> bone * worldMatrix
  | false, _ -> worldMatrix

// Persistent contexts — allocated once, reused every frame.
let private slabCtx =
  InstancedRenderContext<SlabTile, string>(
    getKey = (fun tile -> massModel tile.Material),
    getMeshesAndMaterial =
      (fun tile -> resolveMeshesAndMaterial(massModel tile.Material)),
    getTransform =
      fun basePos tile ->
        // ONE model scaled to the platform's size: the unit block spans
        // 1.082 native XZ cells (blockFootprint, BoneProbe), so X/Z scale
        // divides it out and the instance lands exactly on its W×H×D cell
        // rectangle; Y is exact (the mesh is 1.0 tall, bottom-anchored).
        // The whole platform — crust top and dirt body — is one instance.
        // The model's bone fold rides along in front.
        let scaled =
          Matrix.CreateScale(
            float32 tile.W * cellSize / blockFootprint,
            float32 tile.H * cellSize,
            float32 tile.D * cellSize / blockFootprint
          )
          * Matrix.CreateTranslation(
            basePos.X + float32 tile.W * cellSize * 0.5f,
            0.0f,
            basePos.Z + float32 tile.D * cellSize * 0.5f
          )

        match boneTransformCache.TryGetValue(massModel tile.Material) with
        | true, bone -> bone * scaled
        | false, _ -> scaled
  )

let private propCtx =
  InstancedRenderContext<PropTile, string>(
    getKey = (fun tile -> (propInfo tile.Prop).ModelName),
    getMeshesAndMaterial =
      (fun tile -> resolveMeshesAndMaterial((propInfo tile.Prop).ModelName)),
    getTransform =
      fun basePos tile ->
        let info = propInfo tile.Prop
        let y = float32 tile.Y * cellSize + info.VerticalOffset

        match tile.Prop with
        | Prop.Platform _ ->
          // One platform object `length` cells long: mesh-space X scale,
          // then placement, then the model's bone fold.
          let scaled =
            Matrix.CreateScale(info.ExtentW, 1.0f, 1.0f)
            * Matrix.CreateTranslation(
              basePos.X + info.CenterOffsetX,
              y,
              basePos.Z + info.CenterOffsetZ
            )

          match boneTransformCache.TryGetValue info.ModelName with
          | true, bone -> bone * scaled
          | false, _ -> scaled
        | _ -> placeAt basePos y info
  )

// -------------------------------------------------------------
// Per-key custom shader scoping (grid-instanced-shaders feature validation).
//
// Two distinct effects exercise the per-key resolver on two key groups:
//   * Snow biome (any model name containing "snow") → Snow.fx — frosty,
//     crystalline sparkle.
//   * Grass mega-caps (the "block-grass-large*" family) → Toon.fx — banded
//     cel shading.
// Everything else falls through to the default PBR instanced path
// (ValueNone). Both shaders opt into instancing via `technique Instanced`
// (see Content/Toon.fx, Content/Snow.fx and the Mibo instancing docs); a
// shader that doesn't opt in would silently fall back to PBR.
//
// The context is keyed by model name, so the resolver matches on the bare
// name. Precedence when a block is both snow AND a mega-cap (e.g.
// "block-snow-large"): biome wins — the snow branch is checked first.
// -------------------------------------------------------------

// Lazy-loaded, cached after first use (assets are unavailable at module init).
let mutable private toonEffect: Effect voption = ValueNone
let mutable private snowEffect: Effect voption = ValueNone

let private shaderForKey(name: string) : Effect voption =
  // Biome wins over shape: a snow mega-cap is snow first.
  if name.Contains("snow") then snowEffect
  elif name.Contains("large") then toonEffect
  else ValueNone

/// Every model the world generator can place, so the first frame preloads
/// them all instead of hitting a render-thread Content.Load<Model> exactly
/// when a streamed-in chunk reveals a new biome or prop.
let private preloadableModels() : string seq = seq {
  for material in [ Biome3D.Grass; Biome3D.Snow ] do
    massModel material

  (propInfo(Prop.Platform 5)).ModelName

  (propInfo(Prop.Hazard HazardKind.Spikes)).ModelName
  (propInfo(Prop.Hazard HazardKind.SpikesWide)).ModelName

  for kind in
    [
      PickupKind.Gold
      PickupKind.Silver
      PickupKind.Bronze
      PickupKind.Jewel
      PickupKind.Heart
      PickupKind.Star
      PickupKind.Key
    ] do
    (propInfo(Prop.Pickup kind)).ModelName

  for kind in
    [
      DecorationKind.TreePine
      DecorationKind.TreeSnow
      DecorationKind.Rock
      DecorationKind.Stones
      DecorationKind.GrassTuft
      DecorationKind.Flowers
      DecorationKind.FlowersTall
      DecorationKind.Mushrooms
      DecorationKind.GlowMushroom
      DecorationKind.Crate
      DecorationKind.Barrel
      DecorationKind.Flag
    ] do
    (propInfo(Prop.Decoration kind)).ModelName
}

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
    .beginCameraWith(
      Camera3D.render camera
      |> Camera3D.withClear(Mibo.Color.op_Implicit(l.SkyColor))
    )
    .setAmbientLight(
      {
        Color = l.AmbientColor
        Intensity = l.AmbientIntensity
      }
    )
    .addDirectionalLight(
      {
        Direction = l.LightDirection
        Color = l.LightColor
        Intensity = l.LightIntensity
        CastsShadows = true
      }
    )
    .drop()

  currentModelCache <- model.ModelCache
  currentGameContext <- ctx
  slabCtx.ResetFrameBuffers()
  propCtx.ResetFrameBuffers()

  // Lazy-load the custom effects on the first frame (IAssets is unavailable at
  // module init). Loaded once, cached in the module-level voptions above.
  match toonEffect, snowEffect with
  | ValueNone, ValueNone ->
    let assets = GameContext.getService<IAssets> ctx
    toonEffect <- ValueSome(assets.Effect "Toon")
    snowEffect <- ValueSome(assets.Effect "Snow")

    for name in preloadableModels() do
      resolveMeshesAndMaterial name |> ignore
  | _ -> ()

  for light in model.VisibleLights do
    buffer.addPointLight(light) |> ignore

  let numericsCamPos = System.Numerics.Vector3(camPos.X, camPos.Y, camPos.Z)
  let maxChunkDistSq = 2500.0f

  for KeyValue(struct (cx, cz), chunk) in model.Chunks.Chunks do
    let bounds = chunk.Bounds
    let centerX = (bounds.Min.X + bounds.Max.X) * 0.5f
    let centerY = (bounds.Min.Y + bounds.Max.Y) * 0.5f
    let centerZ = (bounds.Min.Z + bounds.Max.Z) * 0.5f

    let chunkCenter = System.Numerics.Vector3(centerX, centerY, centerZ)

    if (chunkCenter - numericsCamPos).LengthSquared() <= maxChunkDistSq then
      // The window is the chunk's XZ extent in int world coordinates — the
      // heightmap replacement for the retired volume cull.
      let left = int bounds.Min.X
      let top = int bounds.Min.Z
      let right = int bounds.Max.X
      let bottom = int bounds.Max.Z

      slabCtx.RenderWindowInstancedWithEffect(
        buffer,
        left,
        top,
        right,
        bottom,
        chunk.Slabs,
        shaderForKey
      )

      propCtx.RenderWindowInstanced(
        buffer,
        left,
        top,
        right,
        bottom,
        chunk.Props
      )

      propCtx.RenderWindowInstanced(
        buffer,
        left,
        top,
        right,
        bottom,
        chunk.Pickups
      )

  let playerPos = model.Physics.Position

  let playerTransform =
    let rot = Matrix.CreateRotationY(model.Physics.Facing)

    let trans = Matrix.CreateTranslation(playerPos.X, playerPos.Y, playerPos.Z)

    rot * trans

  let p = model.Particles

  for i = 0 to p.Count - 1 do
    buffer
      .billboard(model.ParticleTexture, p.Positions[i], p.Sizes[i], p.Colors[i])
      .drop()

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
  | ValueNone -> buffer.animatedModel(model.PlayerAnim, playerTransform).drop()

  // Skinned-instancing probe: the whole oozi ring is ONE draw call. Transforms
  // are recomposed around the player each frame (the ring follows), each
  // instance faces inward and plays its own clip, and one pose is evaluated
  // per instance into the reused pose array (computePoseInto grows each
  // pose's backing arrays once, then reuses them — no per-frame allocation).
  match model.Oozi with
  | ValueSome {
                Model = ooziModel
                AnimMesh = ValueSome ooziMesh
                States = states
                Transforms = transforms
                Poses = poses
              } ->
    let count = states.Length
    let angleStep = 2.0f * MathF.PI / float32 count

    for i = 0 to count - 1 do
      let angle = float32 i * angleStep

      let ox = MathF.Cos angle * OoziCrowd.ringRadius

      let oz = MathF.Sin angle * OoziCrowd.ringRadius

      // The rig faces +Z at yaw 0 (same convention as FPSSample's enemies);
      // Atan2(-ox, -oz) points it at the ring center.
      let yaw = MathF.Atan2(-ox, -oz)

      transforms[i] <-
        Matrix.CreateRotationY(yaw)
        * Matrix.CreateTranslation(
          playerPos.X + ox,
          playerPos.Y,
          playerPos.Z + oz
        )

      poses[i] <- Animation3DState.computePoseInto ooziMesh states[i] poses[i]

    let am: AnimatedModel = {
      Model = ooziModel
      Mesh = ValueSome ooziMesh
      State = states[0]
    }

    buffer.animatedModelInstanced(am, transforms, poses).drop()
  | _ -> ()

  buffer.endCamera().drop()
