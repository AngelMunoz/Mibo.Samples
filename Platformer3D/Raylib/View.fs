module Platformer3D.Raylib.View

#nowarn "9"

open System
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open Mibo.Layout3D
open Platformer3D.Types
open Platformer3D.BlockData
open Platformer3D.Constants
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
        // Center multi-cell meshes on their footprint (meshes are centered on
        // origin; blocks are placed at the cell corner — see BlockData).
        let cx = info.CenterOffsetX
        let cz = info.CenterOffsetZ

        if rotAngle = 0.0f && yOff = 0.0f then
          Raymath.MatrixTranslate(worldPos.X + cx, worldPos.Y, worldPos.Z + cz)
        elif rotAngle = 0.0f then
          Raymath.MatrixTranslate(
            worldPos.X + cx,
            worldPos.Y + yOff,
            worldPos.Z + cz
          )
        else
          let rot = Raymath.MatrixRotateY(rotAngle)

          let trans =
            Raymath.MatrixTranslate(
              worldPos.X + cx,
              worldPos.Y + yOff,
              worldPos.Z + cz
            )

          Raymath.MatrixMultiply(rot, trans)
  )

// -------------------------------------------------------------
// Per-key custom shader scoping (grid-instanced-shaders feature validation).
//
// Two distinct effects exercise the per-key resolver on two key groups:
//   * Snow biome (any model name containing "snow") → snow shader — frosty,
//     crystalline sparkle.
//   * LargeBlock grass ("block-grass-large") → toon shader — banded cel shading.
// Everything else falls through to the default PBR instanced path
// (ValueNone). Both shaders opt into instancing by declaring
// `in mat4 instanceTransform;` (raylib wires the instance VBO); a shader that
// doesn't declare it would silently fall back to PBR.
//
// The context is keyed by model name (getKey = modelName), so the resolver
// matches on the bare name. Precedence when a block is both snow AND large
// (e.g. "block-snow-large"): biome wins — the snow branch is checked first.
//
// The framework's raylib IAssets has no shader loader (unlike MonoGame's
// Effect), so GLSL lives here as embedded strings and loads via
// Raylib.LoadShaderFromMemory — mirroring the framework's own Shaders.fs.
// -------------------------------------------------------------

// Shared instanced vertex body: opt-in via `in mat4 instanceTransform`, view-
// projection via `viewProj` (SceneUpload uploads viewProj, NOT mvp, for user
// effects), and the normal derived in-shader from the instance transform
// (matModel/normalMatrix are identity for instanced draws). See
// Mibo/docs/shader-uniforms.md § Instancing and the built-in
// forwardVertexInstanced. Shared between the toon and snow vertex shaders.
let private instancedVsBody: string =
  """
    fragTexCoord = vertexTexCoord;
    mat3 nMat = transpose(inverse(mat3(instanceTransform)));
    fragNormal = normalize(nMat * vertexNormal);
    vec4 world = instanceTransform * vec4(vertexPosition, 1.0);
    fragWorldPos = world.xyz;
    gl_Position = viewProj * world;
"""

// Toon vertex: declares raylib's standard attributes + the instance opt-in,
// then the shared instanced body.
let private toonVs: string =
  "#version 330\n"
  + """
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
in mat4 instanceTransform;

out vec2 fragTexCoord;
out vec3 fragNormal;
out vec3 fragWorldPos;

uniform mat4 viewProj;

void main() {
"""
  + instancedVsBody
  + "}\n"

// Shared shadow sampling GLSL — mirrors the built-in raylib forward shader's
// computeShadowFromAtlas for the directional caster (registered at slot 0 by
// convention). Opt-in by declaration: declaring these uniforms makes
// SceneUpload upload the shadow data and bind the atlas to slot 15; a shader
// that omits them renders unshadowed at no cost (the Mibo raylib contract —
// see Mibo/docs/shader-uniforms.md § Shadows). GLSL #version 330 allows
// dFdx/dFdy/textureSize (unlike MonoGame SM3.0), so the slope-scale bias and
// texel size are derived in-shader rather than from the shadowTexelSize uniform.
let private dirShadowGLSL: string =
  """
uniform int dirLightCastsShadows;
uniform mat4 shadowViewProjs[16];
uniform vec4 shadowUVOffsets[16];
uniform float shadowBiases[16];
uniform sampler2D shadowAtlas;

float computeDirShadow(vec3 worldPos) {
    if (dirLightCastsShadows == 0) return 1.0;
    // Directional caster is registered first (slot 0 by convention).
    vec4 sc = shadowViewProjs[0] * vec4(worldPos, 1.0);
    vec3 ndc = sc.xyz / sc.w;
    ndc = ndc * 0.5 + 0.5;
    if (ndc.z > 1.0) return 1.0;
    if (ndc.x < 0.0 || ndc.x > 1.0 || ndc.y < 0.0 || ndc.y > 1.0) return 1.0;
    vec2 atlasUV = ndc.xy * shadowUVOffsets[0].zw + shadowUVOffsets[0].xy;
    // Slope-scale bias prevents self-shadow acne on flat receiver/caster surfaces.
    float bias = shadowBiases[0] + length(vec2(dFdx(ndc.z), dFdy(ndc.z))) * 3.0;
    vec2 texel = 1.0 / vec2(textureSize(shadowAtlas, 0));
    float shadow = 0.0;
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            float d = texture(shadowAtlas, atlasUV + vec2(float(x), float(y)) * texel).r;
            shadow += (ndc.z - bias > d) ? 0.0 : 1.0;
        }
    }
    return shadow / 9.0;
}
"""

// Toon fragment: banded N·L + rim, inheriting lights/material/shadows by name.
let private toonFs: string =
  "#version 330\n"
  + """
in vec2 fragTexCoord;
in vec3 fragNormal;
in vec3 fragWorldPos;

out vec4 finalColor;

// Scene data (uploaded by name via SceneUpload — declare only what you use).
uniform vec4 albedoColor;
uniform sampler2D texture0;
uniform float opacity;

uniform vec3 ambientColor;
uniform float ambientIntensity;

uniform vec3 dirLightDir;
uniform vec3 dirLightColor;
uniform float dirLightIntensity;

uniform vec3 cameraPos;

"""
  + dirShadowGLSL
  + """
// 3 bands: shadow / mid / lit. Smoothstep softens the step edges.
float toonBand(float NdotL) {
    float b = smoothstep(0.0, 0.05, NdotL) * 0.4;
    b += smoothstep(0.5, 0.55, NdotL) * 0.6;
    return b;
}

void main() {
    vec4 texColor = texture(texture0, fragTexCoord) * albedoColor;
    vec3 albedo = texColor.rgb;

    vec3 N = normalize(fragNormal);
    vec3 V = normalize(cameraPos - fragWorldPos);
    vec3 L = normalize(-dirLightDir);

    vec3 ambient = ambientColor * albedo * ambientIntensity;
    float NdotL = max(dot(N, L), 0.0);
    float band = toonBand(NdotL);
    float shadow = computeDirShadow(fragWorldPos);
    vec3 dir = dirLightColor * dirLightIntensity * albedo * band * shadow;

    float rim = 1.0 - max(dot(N, V), 0.0);
    rim = smoothstep(0.6, 1.0, rim);
    vec3 rimColor = dirLightColor * rim * 0.4;

    vec3 result = ambient + dir + rimColor;
    finalColor = vec4(result, texColor.a * opacity);
}
"""

// Snow vertex: same instanced opt-in as toon.
let private snowVs: string = toonVs

// Snow fragment: frosty/crystalline — cool tint + fresnel rim + time-driven
// sparkle. Distinct from the toon banded look.
let private snowFs: string =
  "#version 330\n"
  + """
in vec2 fragTexCoord;
in vec3 fragNormal;
in vec3 fragWorldPos;

out vec4 finalColor;

uniform vec4 albedoColor;
uniform sampler2D texture0;
uniform float opacity;
uniform vec2 tiling;

uniform vec3 ambientColor;
uniform float ambientIntensity;

uniform vec3 dirLightDir;
uniform vec3 dirLightColor;
uniform float dirLightIntensity;

uniform vec3 cameraPos;
uniform float time;

"""
  + dirShadowGLSL
  + """
// Pseudo-random hash for per-surface sparkle variation (cheap, no texture).
float hash13(vec3 p) {
    vec3 q = fract(p * 0.1031);
    q = q + dot(q, q.yzx + 33.33);
    return fract((q.x + q.y) * q.z);
}

void main() {
    vec2 uv = fragTexCoord * tiling;
    vec4 texColor = texture(texture0, uv) * albedoColor;
    vec3 albedo = texColor.rgb;

    vec3 N = normalize(fragNormal);
    vec3 V = normalize(cameraPos - fragWorldPos);
    vec3 L = normalize(-dirLightDir);

    // Frosty cool tint — bias the albedo toward an icy blue-white.
    vec3 frostTint = vec3(0.80, 0.88, 1.0);
    vec3 frosty = mix(albedo, albedo * frostTint + vec3(0.05, 0.07, 0.10), 0.5);

    vec3 ambient = ambientColor * frosty * ambientIntensity;

    // Shadow factor for the directional caster (opt-in by declaration above).
    float shadow = computeDirShadow(fragWorldPos);

    // Soft diffuse (not banded — snow is smooth, unlike the toon look).
    float NdotL = max(dot(N, L), 0.0);
    vec3 diffuse = dirLightColor * dirLightIntensity * frosty * NdotL * shadow;

    // Crystalline fresnel rim — brightens glancing angles for an icy sheen.
    float fresnel = pow(1.0 - max(dot(N, V), 0.0), 3.0);
    vec3 rim = dirLightColor * fresnel * 0.6;

    // Time-driven sparkle: drifting glints keyed off world position so they
    // sit on the surface rather than sliding with the camera. Quantise the
    // position to discrete crystal cells; a glint fires when its phase aligns.
    vec3 cell = floor(fragWorldPos * 8.0);
    float h = hash13(cell);
    float phase = h * 6.2831853 + time * 2.0;
    float glint = pow(max(0.0, sin(phase)), 32.0);
    glint *= smoothstep(0.2, 0.8, dot(N, V));
    vec3 sparkle = dirLightColor * glint * 0.8 * shadow;

    vec3 result = ambient + diffuse + rim + sparkle;
    finalColor = vec4(result, texColor.a * opacity);
}
"""

// Lazy-loaded, cached after first use. ValueNone until loaded (and on compile
// failure, so the sample degrades to the PBR fallback instead of crashing).
let mutable private toonShader: Raylib_cs.Shader voption = ValueNone
let mutable private snowShader: Raylib_cs.Shader voption = ValueNone

let private shaderForKey(name: string) : Raylib_cs.Shader voption =
  // Biome wins over shape: a snow LargeBlock is snow first.
  if name.Contains("snow") then snowShader
  elif name = KenneyModels.blockGrassLarge then toonShader
  else ValueNone

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

  // Lazy-compile the custom shaders on the first frame (no GL context at module
  // init). Loaded once, cached in the module-level voptions above.
  match toonShader, snowShader with
  | ValueNone, ValueNone ->
    toonShader <- ValueSome(Raylib.LoadShaderFromMemory(toonVs, toonFs))
    snowShader <- ValueSome(Raylib.LoadShaderFromMemory(snowVs, snowFs))
  | _ -> ()

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

      CellGridRenderer3D.renderVolumeInstancedWithEffect
        instancedCtx
        chunk.Bounds
        terrainGrid
        shaderForKey
        buffer

  let playerTransform =
    let rot = Raymath.MatrixRotateY(model.Physics.Facing)

    let trans =
      Raymath.MatrixTranslate(
        model.Physics.Position.X,
        model.Physics.Position.Y,
        model.Physics.Position.Z
      )

    Raymath.MatrixMultiply(rot, trans)

  let p = model.Particles

  for i = 0 to p.Count - 1 do
    Draw3D.drawBillboard
      model.ParticleTexture
      p.Positions[i]
      p.Sizes[i]
      (Mibo.Color.op_Implicit(p.Colors[i]))
      buffer
    |> Draw3D.drop

  // GPU skinning path (non-mutating): one AnimatedMesh shared by both player
  // instances; the pose is evaluated once and shared between the skinned draw
  // and the weapon attachments on both handslot sockets.
  match model.PlayerAnimatedMesh with
  | ValueSome animMesh ->
    let am = AnimatedModel.create animMesh model.PlayerAnim
    let pose = AnimatedModel.computePose am

    buffer.animatedModel(am, playerTransform, pose = pose) |> ignore

    for prop in model.PlayerProps do
      buffer.attachedMesh(
        am,
        BoneRef.ByName prop.BoneName,
        prop.LocalTransform,
        prop.Mesh,
        prop.Material,
        playerTransform,
        pose = pose
      )
      |> ignore

    // Second instance of the same Model at a different pose — no attachment.
    let am2 = AnimatedModel.create animMesh model.PlayerAnim2

    let offsetTransform =
      Raymath.MatrixTranslate(
        model.Physics.Position.X + 2.5f,
        model.Physics.Position.Y,
        model.Physics.Position.Z
      )

    buffer.animatedModel(am2, offsetTransform) |> ignore
  | ValueNone ->
    // Legacy mutating fallback when no AnimatedMesh is available.
    Animation3DState.applyToModel model.PlayerAnim

    buffer
    |> Draw3D.drawModel model.PlayerAnim.Model playerTransform
    |> Draw3D.drop

  buffer |> Draw3D.endCamera |> Draw3D.drop
