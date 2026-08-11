module FPSSample.Raylib.View

#nowarn "9"


open System
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open Mibo.Layout3D
open FPSSample
open FPSSample.Types

/// Loads a model from the asset service, caching by path in the provided dictionary.
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

// Persistent mesh/material cache keyed by model path.
let private meshMaterialCache =
  Dictionary<string, struct (Raylib_cs.Mesh * Material3D)[]>()

// Per-frame mutable context set once before rendering.
let private persistentModelCache = Dictionary<string, Model>()

let mutable private currentModelCache = persistentModelCache

let mutable private currentGameContext = Unchecked.defaultof<GameContext>

/// Shared starry skybox instance.
let skybox = Skybox.create()

let private resolveMeshesAndMaterial(cell: Level.Cell) =
  let path = Level.Cell.modelPath cell

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
                  Metallic = 0.2f
            }

            struct (mesh, material3d)
        |]
      else
        Array.empty

    meshMaterialCache[path] <- result
    result

// Persistent instanced render context for level geometry.
let private instancedCtx =
  InstancedRenderContext<Level.Cell, string>(
    getKey = Level.Cell.modelPath,
    getMeshesAndMaterial = resolveMeshesAndMaterial,
    getTransform =
      fun worldPos _cell ->
        Raymath.MatrixTranslate(worldPos.X, worldPos.Y, worldPos.Z)
  )

// ─────────────────────────────────────────────────────────────────────────────
// Enemy animation registry.
//
// raylib's UpdateModelAnimation mutates the model''s bone transforms in place,
// so each enemy needs its own Model instance + Animation3DState. The Program.fs
// init function populates this registry; the view reads it each frame.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Raylib-specific enemy animation service. Manages per-enemy Model copies +
/// Animation3DState. The Model copy is necessary because raylib's
/// UpdateModelAnimation mutates bone transforms in place.
/// </summary>
type EnemyAnimationService() =
  let states = ResizeArray<Animation3DState>()

  member _.States = states

  interface IEnemyAnimationService with
    member _.Init(ctx: GameContext, enemyCount: int) : unit =
      states.Clear()
      let assets = GameContext.getService<IAssets> ctx

      for i in 0 .. enemyCount - 1 do
        let path = Assets.character(i)
        let animClips = assets.ModelAnimations(path)
        let clips = Animation3DClips.fromModelAnimations animClips
        let model = assets.Model(path)

        let state = Animation3DState.create model clips "idle" 60.0f

        states.Add(state)

    member _.Update(dt: float32, enemies: Enemy[]) : unit =
      for i = 0 to min enemies.Length states.Count - 1 do
        let enemy = enemies[i]

        if enemy.State <> EnemyState.Dead then
          let state = states[i]

          let newState =
            state
            |> Animation3DState.blendTo enemy.CurrentAnim 0.15f
            |> Animation3DState.update dt

          states[i] <- newState

// ─────────────────────────────────────────────────────────────────────────────
// Hit-flash post-process: desaturates the whole 3D scene based on the remaining
// hit-effect timer. The shader is loaded once (raylib's default vertex shader is
// used by passing null for the VS, so the fragment shader reads the standard
// fragTexCoord / texture0 the batch provides).
// ─────────────────────────────────────────────────────────────────────────────

let private grayscaleFragSrc =
  "
#version 330
in vec2 fragTexCoord;
uniform sampler2D texture0;
uniform float intensity;
out vec4 finalColor;

void main() {
  vec4 c = texture(texture0, fragTexCoord);
  float gray = dot(c.rgb, vec3(0.299, 0.587, 0.114));
  finalColor = vec4(mix(c.rgb, vec3(gray), intensity), c.a);
}
"

let mutable private grayscaleShader: Shader voption = ValueNone

let mutable private intensityLoc: int = -1

/// Desaturates the rendered scene by <c>intensity</c> (0 = full color, 1 = full gray),
/// blitting the scene texture to the active target. Called from the post-process action.
let private applyGrayscale (pp: PostProcessContext3D) (intensity: float32) =
  match grayscaleShader with
  | ValueNone ->
    let s = Raylib.LoadShaderFromMemory(null, grayscaleFragSrc)
    grayscaleShader <- ValueSome s
    intensityLoc <- Raylib.GetShaderLocation(s, "intensity")
  | ValueSome _ -> ()

  match grayscaleShader with
  | ValueSome s ->
    use p = fixed &intensity

    Raylib.SetShaderValue(
      s,
      intensityLoc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Float
    )

    Raylib.BeginShaderMode s

    // Negative source height: raylib's FBO texture is vertically flipped relative
    // to the back buffer, so the blit reads it the right way up.
    let src =
      Raylib_cs.Rectangle(0.0f, 0.0f, float32 pp.Width, float32 -pp.Height)

    let dst =
      Raylib_cs.Rectangle(0.0f, 0.0f, float32 pp.Width, float32 pp.Height)

    Raylib.DrawTexturePro(
      pp.Source.Texture,
      src,
      dst,
      Vector2.Zero,
      0.0f,
      Raylib_cs.Color.White
    )

    Raylib.EndShaderMode()
  | ValueNone -> ()

// ─────────────────────────────────────────────────────────────────────────────
// Distance fog post-process: fades the lit scene toward a dark color with distance,
// using the scene's camera-POV depth. Same look + tuning as the MonoGame backend.
// ─────────────────────────────────────────────────────────────────────────────

// Distance fog — fades the lit scene toward a dark color with distance. raylib renders 3D with
// global clip planes (RL_CULL_DISTANCE_NEAR/FAR, default 0.05/4000) rather than per-camera near/far,
// so the fog shader's linearization MUST use those same values — a hardcoded mismatch produces
// wildly wrong distances because OpenGL's hyperbolic depth amplifies any near-plane error. We query
// rlgl at runtime to stay correct.
let private fogColor = Vector3(0.02f, 0.02f, 0.03f)
let private fogNear = 4.0f
let private fogFar = 10.0f

// Height fog: dense near the ground, thinning above a ceiling height. Combined with distance
// fog so distant ground is fully fogged while the skybox stays clear.
let private fogCeiling = 4.0f
let private fogDensity = 2.5f

let private fogFragSrc =
  "
#version 330
in vec2 fragTexCoord;
uniform sampler2D texture0;   // scene color
uniform sampler2D texture1;   // scene depth
uniform vec3 fogColor;
uniform float fogNear;
uniform float fogFar;
uniform float cameraNear;
uniform float cameraFar;
uniform float fogStrength;
uniform vec3 camPos;
uniform vec3 camForward;
uniform vec3 camRight;
uniform vec3 camUp;
uniform float fovY;
uniform float aspect;
uniform float fogCeiling;
uniform float fogDensity;
out vec4 finalColor;

void main() {
  vec4 scene = texture(texture0, fragTexCoord);
  float depth = texture(texture1, fragTexCoord).r;

  // Skybox / uncovered pixels: depth ≈ 1.0, skip fog so the sky stays visible.
  if (depth >= 0.999) {
    finalColor = scene;
    return;
  }

  // OpenGL depth linearization to positive view-space distance.
  float z = depth * 2.0 - 1.0;
  float dist = (2.0 * cameraNear * cameraFar) / (cameraFar + cameraNear - z * (cameraFar - cameraNear));

  // Reconstruct world position from depth + camera basis vectors.
  // raylib's DrawTexturePro with negative source height produces fragTexCoord.y=1 at
  // the top of the screen and 0 at the bottom (opposite of standard GL). So
  // ndc.y = fragTexCoord.y * 2 - 1 gives +1 at top (up), -1 at bottom (down).
  float tanHalfFov = tan(fovY * 0.5);
  vec2 ndc = vec2(fragTexCoord.x * 2.0 - 1.0, fragTexCoord.y * 2.0 - 1.0);
  vec3 worldPos = camPos
    + ndc.x * aspect * tanHalfFov * dist * camRight
    + ndc.y * tanHalfFov * dist * camUp
    + dist * camForward;

  // Distance fog (ramps from fogNear to fogFar).
  float distFog = clamp((dist - fogNear) / (fogFar - fogNear), 0.0, 1.0);

  // Height fog: dense below fogCeiling, thin above it.
  float heightFog = clamp((fogCeiling - worldPos.y) / fogCeiling, 0.0, 1.0);
  heightFog = pow(heightFog, fogDensity);

  // Combined: height fog applies at any distance (ground-level haze),
  // distance fog adds on top for far geometry. Capped at 1.0.
  float fog = clamp(heightFog * 0.7 + distFog * 0.3, 0.0, 1.0) * fogStrength;
  finalColor = vec4(mix(scene.rgb, fogColor, fog), scene.a);
}
"

let mutable private fogShader: Shader voption = ValueNone
let mutable private fogUniforms: Map<string, int> = Map.empty

let private applyFog
  (camPos: Vector3)
  (camFwd: Vector3)
  (camRight: Vector3)
  (camUp: Vector3)
  (fovY: float32)
  (pp: PostProcessContext3D)
  =
  match fogShader with
  | ValueNone ->
    let s = Raylib.LoadShaderFromMemory(null, fogFragSrc)
    fogShader <- ValueSome s

    fogUniforms <-
      [
        "fogColor", Raylib.GetShaderLocation(s, "fogColor")
        "fogNear", Raylib.GetShaderLocation(s, "fogNear")
        "fogFar", Raylib.GetShaderLocation(s, "fogFar")
        "cameraNear", Raylib.GetShaderLocation(s, "cameraNear")
        "cameraFar", Raylib.GetShaderLocation(s, "cameraFar")
        "fogStrength", Raylib.GetShaderLocation(s, "fogStrength")
        "depthSampler", Raylib.GetShaderLocation(s, "texture1")
        "camPos", Raylib.GetShaderLocation(s, "camPos")
        "camForward", Raylib.GetShaderLocation(s, "camForward")
        "camRight", Raylib.GetShaderLocation(s, "camRight")
        "camUp", Raylib.GetShaderLocation(s, "camUp")
        "fovY", Raylib.GetShaderLocation(s, "fovY")
        "aspect", Raylib.GetShaderLocation(s, "aspect")
        "fogCeiling", Raylib.GetShaderLocation(s, "fogCeiling")
        "fogDensity", Raylib.GetShaderLocation(s, "fogDensity")
      ]
      |> Map.ofList
  | ValueSome _ -> ()

  match fogShader with
  | ValueSome s ->
    // Upload scalar uniforms via fixed pointers (DisableRuntimeMarshalling requirement).
    let mutable v = Vector3(0.0f, 0.0f, 0.0f)
    let loc name = Map.find name fogUniforms

    v <- fogColor
    use pv = fixed &v

    Raylib.SetShaderValue(
      s,
      loc "fogColor",
      NativePtr.toVoidPtr pv,
      ShaderUniformDataType.Vec3
    )

    let mutable near = fogNear
    let mutable far = fogFar
    // Query raylib's actual runtime clip planes — these are what the scene was rendered with,
    // so the linearization must use them (not hardcoded guesses). Convert double→float32 here:
    // GetCullDistanceNear/Far return double (8 bytes), but SetShaderValue(Float) reads 4 bytes,
    // so passing a double directly would upload garbage bytes as the near/far plane.
    let mutable camN = float32(Rlgl.GetCullDistanceNear())
    let mutable camF = float32(Rlgl.GetCullDistanceFar())

    use pn = fixed &near

    Raylib.SetShaderValue(
      s,
      loc "fogNear",
      NativePtr.toVoidPtr pn,
      ShaderUniformDataType.Float
    )

    use pf = fixed &far

    Raylib.SetShaderValue(
      s,
      loc "fogFar",
      NativePtr.toVoidPtr pf,
      ShaderUniformDataType.Float
    )

    use pcn = fixed &camN

    Raylib.SetShaderValue(
      s,
      loc "cameraNear",
      NativePtr.toVoidPtr pcn,
      ShaderUniformDataType.Float
    )

    use pcf = fixed &camF

    Raylib.SetShaderValue(
      s,
      loc "cameraFar",
      NativePtr.toVoidPtr pcf,
      ShaderUniformDataType.Float
    )

    // Camera basis vectors for world-position reconstruction from depth.
    let mutable camPosV = camPos
    let mutable camFwdV = camFwd
    let mutable camRightVec = camRight
    let mutable camUpVec = camUp
    let mutable fov = fovY
    let mutable asp = float32 pp.Width / float32 pp.Height
    let mutable ceiling = fogCeiling
    let mutable density = fogDensity

    use pcp = fixed &camPosV

    Raylib.SetShaderValue(
      s,
      loc "camPos",
      NativePtr.toVoidPtr pcp,
      ShaderUniformDataType.Vec3
    )

    use pcfwd = fixed &camFwdV

    Raylib.SetShaderValue(
      s,
      loc "camForward",
      NativePtr.toVoidPtr pcfwd,
      ShaderUniformDataType.Vec3
    )

    use pcrt = fixed &camRightVec

    Raylib.SetShaderValue(
      s,
      loc "camRight",
      NativePtr.toVoidPtr pcrt,
      ShaderUniformDataType.Vec3
    )

    use pcup = fixed &camUpVec

    Raylib.SetShaderValue(
      s,
      loc "camUp",
      NativePtr.toVoidPtr pcup,
      ShaderUniformDataType.Vec3
    )

    use pfov = fixed &fov

    Raylib.SetShaderValue(
      s,
      loc "fovY",
      NativePtr.toVoidPtr pfov,
      ShaderUniformDataType.Float
    )

    use pasp = fixed &asp

    Raylib.SetShaderValue(
      s,
      loc "aspect",
      NativePtr.toVoidPtr pasp,
      ShaderUniformDataType.Float
    )

    use pceil = fixed &ceiling

    Raylib.SetShaderValue(
      s,
      loc "fogCeiling",
      NativePtr.toVoidPtr pceil,
      ShaderUniformDataType.Float
    )

    use pdens = fixed &density

    Raylib.SetShaderValue(
      s,
      loc "fogDensity",
      NativePtr.toVoidPtr pdens,
      ShaderUniformDataType.Float
    )

    Raylib.BeginShaderMode s

    match pp.Depth with
    | ValueSome depthTex ->
      let mutable strength = 1.0f
      use ps = fixed &strength

      Raylib.SetShaderValue(
        s,
        loc "fogStrength",
        NativePtr.toVoidPtr ps,
        ShaderUniformDataType.Float
      )

      // Bind the depth texture via raylib's batch-aware sampler API. SetShaderValueTexture
      // registers the texture in rlgl's activeTextureId[] so the DrawTexturePro batch flush
      // re-binds it to a sampler slot automatically, AND points the sampler uniform at that slot.
      // The original code used raw rlgl (ActiveTextureSlot + EnableTexture) which sets GL state
      // but bypasses the batch registry — the batch flush only re-binds textures registered through
      // this API, leaving the depth sampler unbound and reading 0, which produces no fog at all.
      Raylib.SetShaderValueTexture(s, loc "depthSampler", depthTex)
    | ValueNone ->
      let mutable strength = 0.0f
      use ps = fixed &strength

      Raylib.SetShaderValue(
        s,
        loc "fogStrength",
        NativePtr.toVoidPtr ps,
        ShaderUniformDataType.Float
      )

    // Negative source height: raylib's FBO texture is vertically flipped relative
    // to the back buffer, so the blit reads it the right way up.
    let src =
      Raylib_cs.Rectangle(0.0f, 0.0f, float32 pp.Width, float32 -pp.Height)

    let dst =
      Raylib_cs.Rectangle(0.0f, 0.0f, float32 pp.Width, float32 pp.Height)

    Raylib.DrawTexturePro(
      pp.Source.Texture,
      src,
      dst,
      Vector2.Zero,
      0.0f,
      Raylib_cs.Color.White
    )

    Raylib.EndShaderMode()
  | ValueNone -> ()

// ─────────────────────────────────────────────────────────────────────────────
// Bullet-impact decals — textured, semi-transparent planes (PR #99 probe).
//
// A decal is a flat Primitive3D.plane oriented so its +Y normal aligns to the
// impact normal (raylib's GenMeshPlane lies on XZ, normal +Y), drawn with a
// Material3D whose Opacity < 1. The PBR shader outputs
// alpha = texColor.a * opacity, so the decal keeps its alpha outline while
// blending through the sorted, depth-write-off pass. The texture is loaded
// once and reused across frames; the plane is a shared module-level mesh.
// ─────────────────────────────────────────────────────────────────────────────

let mutable private decalTexture: Texture2D voption = ValueNone
let mutable private decalMaterial: Material3D voption = ValueNone

/// Base opacity for a fresh impact decal; the per-draw opacity fades with the
/// decal's remaining lifetime.
let private decalBaseOpacity = 0.85f

/// Scale of the decal plane (world units along each edge).
let private decalScale = 1.0f

/// Small offset along the impact normal to avoid z-fighting with the surface.
let private decalOffset = 0.02f

/// Loads the laser sprite sheet and crops frame (0,0): the sheet is 180×120
/// with 60×60 frames (3 cols × 2 rows — the same grid SpaceBattle animates).
/// Cropping gives the decal a single bolt instead of all six ghost frames
/// spread over the quad.
let private loadDecalTexture(assets: IAssets) : Texture2D =
  let sheet = assets.Texture Assets.decalLaser1
  let mutable img = Raylib.LoadImageFromTexture(sheet)
  Raylib.ImageCrop(&img, Rectangle(0.0f, 0.0f, 60.0f, 60.0f))
  let tex = Raylib.LoadTextureFromImage(img)
  Raylib.UnloadImage(img)
  tex

/// Ensures the decal texture and base material are created (once). The material
/// is opaque-albedo + Opacity < 1 so draws route through the sorted alpha-blend
/// pass. The emission map repeats the albedo texture at full strength, so the
/// bolt glows with its own colors instead of vanishing under the night
/// lighting — the PBR shader adds `emissionColor * emissionMap` to the lit
/// result on both backends.
let private ensureDecalResources(assets: IAssets) =
  match decalMaterial with
  | ValueNone ->
    let tex = loadDecalTexture assets
    decalTexture <- ValueSome tex

    let mat =
      Material3D.defaults
      |> Material3D.withAlbedoMap tex
      |> fun m -> {
          m with
              EmissionMap = ValueSome tex
              Opacity = decalBaseOpacity
              EmissionColor = Color(255, 255, 255, 255)
        }

    decalMaterial <- ValueSome mat
  | ValueSome _ -> ()

/// Builds a world matrix that lays a unit plane flat against a surface: the
/// plane's +Y normal maps to `normal`, the plane is scaled to `decalScale`,
/// and nudged by `decalOffset` along the normal to avoid z-fighting. raylib's
/// GenMeshPlane lies on XZ with normal +Y, so the floor case is identity.
let private decalTransform(position: Vector3, normal: Vector3) : Matrix4x4 =
  let n =
    if normal.LengthSquared() > 0.001f then
      Vector3.Normalize normal
    else
      Vector3.UnitY

  // Rotate the plane's local +Y onto the impact normal. For the common floor
  // case (n ≈ +Y) the rotation is identity; for a wall we rotate about the
  // cross axis by the angle between +Y and n. Fallback to identity when +Y
  // and n are parallel (cross product degenerate).
  let rot =
    let src = Vector3.UnitY
    let dot = Math.Clamp(Vector3.Dot(src, n), -1.0f, 1.0f)

    if dot > 0.9999f then
      Matrix4x4.Identity
    elif dot < -0.9999f then
      // n ≈ -Y: rotate 180° about X (ceiling).
      Raymath.MatrixRotateX(MathF.PI)
    else
      let axis = Vector3.Cross(src, n)
      Raymath.MatrixRotate(Vector3.Normalize axis, MathF.Acos dot)

  let scale = Raymath.MatrixScale(decalScale, decalScale, decalScale)
  let trans = Raymath.MatrixTranslate(position.X, position.Y, position.Z)
  // Offset along the normal to lift the decal off the surface.
  let offset =
    Raymath.MatrixTranslate(
      n.X * decalOffset,
      n.Y * decalOffset,
      n.Z * decalOffset
    )

  Raymath.MatrixMultiply(
    Raymath.MatrixMultiply(scale, rot),
    Raymath.MatrixMultiply(trans, offset)
  )

/// Renders the 3D scene from a first-person camera.
let view
  (animService: EnemyAnimationService)
  (ctx: GameContext)
  (model: GameModel)
  (buffer: RenderBuffer3D)
  =
  // ── First-person camera ───────────────────────────────────────────────────
  let forward = ViewMath.cameraForward model.Player.Yaw model.Player.Pitch

  let cameraTarget = model.Player.Position + forward

  let camera =
    Raylib_cs.Camera3D(
      model.Player.Position,
      cameraTarget,
      Vector3.UnitY,
      75.0f,
      CameraProjection.Perspective
    )

  buffer
    .beginCameraWith(
      Camera3D.render camera
      |> Camera3D.withClear(Mibo.RaylibColor.toRaylibColor ViewMath.clearColor)
    )
    .setAmbientLight(ViewMath.ambientLight)
    .addDirectionalLight(ViewMath.directionalLight)
    .drop()

  // ── Starry skybox (drawn first inside camera so scene renders on top) ─────
  buffer
  |> FPSSample.Raylib.Skybox.render
    skybox
    ViewMath.skyHorizonColor
    ViewMath.skyZenithColor
    model.Player.Position

  // ── Muzzle flash point light (added before geometry so pipeline picks it up) ──
  if model.Weapon.MuzzleFlash.Active then
    let flashPos =
      ViewMath.muzzleWorldPosition
        model.Player.Position
        forward
        model.Player.Pitch
        model.Player.Yaw

    buffer.addPointLight(ViewMath.muzzleFlashLight flashPos).drop()
  // ── Static torches (flickering point lights around the arena) ───────────────
  let torches = ViewMath.torchPositions

  for i = 0 to torches.Length - 1 do
    let pos = torches[i]
    // Independent flicker per torch using a phase offset and the game time.
    let phase = float32 i * 1.7f
    let flicker = MathF.Sin(model.TotalTime * 7.0f + phase) * 0.25f

    buffer.addPointLight(ViewMath.torchLight pos flicker).drop()

  // ── Level geometry (instanced) ────────────────────────────────────────────
  currentGameContext <- ctx
  instancedCtx.ResetFrameBuffers()

  // Render the entire grid instanced by cell type
  CellGridRenderer3D.renderInstanced instancedCtx model.Level.Grid buffer

  // ── Enemies (animated models) ─────────────────────────────────────────────
  for i = 0 to model.Enemy.Enemies.Length - 1 do
    let enemy = model.Enemy.Enemies[i]

    if enemy.State <> EnemyState.Dead && i < animService.States.Count then
      let animState = animService.States[i]
      let pos = enemy.Position

      let transform =
        let rot = Raymath.MatrixRotateY(enemy.Facing)
        let trans = Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)
        Raymath.MatrixMultiply(rot, trans)

      buffer.animatedModel(animState, transform).drop()

  // ── Pickups ───────────────────────────────────────────────────────────────
  let healthModel = loadOrGetModel currentModelCache Assets.heart ctx
  let ammoModel = loadOrGetModel currentModelCache Assets.coinGold ctx

  for pickup in model.Pickup.Pickups do
    if pickup.IsActive then
      let mdl, pos =
        match pickup.Kind with
        | Level.PickupKind.Health -> healthModel, pickup.Position
        | Level.PickupKind.Ammo -> ammoModel, pickup.Position

      let bobY = MathF.Sin(model.TotalTime * 3.0f) * 0.2f
      let transform = Raymath.MatrixTranslate(pos.X, pos.Y + bobY, pos.Z)
      buffer.model(mdl, transform).drop()

  // ── Muzzle smoke puffs ────────────────────────────────────────────────────
  let smokeModel = loadOrGetModel currentModelCache Assets.smoke ctx

  for puff in model.Effect.SmokePuffs do
    if puff.Active then
      let life = 1.0f - puff.Timer / SmokePuff.duration
      let alpha = 1.0f - life

      if alpha > 0.01f then
        let pos = puff.Position
        // Keep the smoke model oriented along its velocity so it appears to
        // carry momentum from the shot.
        let dir = puff.Velocity

        // Orient the smoke cone along the velocity direction. The smoke model
        // points up (+Y) by default, so we map +Y to the travel direction.
        let smokeTransform =
          if dir.LengthSquared() > 0.001f then
            let n = Vector3.Normalize(dir)
            let srcForward = Vector3.UnitY
            let rotAxis = Vector3.Cross(srcForward, n)

            let rotAngle =
              MathF.Acos(Math.Clamp(Vector3.Dot(srcForward, n), -1.0f, 1.0f))

            if rotAxis.LengthSquared() > 0.001f then
              let axisN = Vector3.Normalize(rotAxis)

              let scaleMat =
                Raymath.MatrixScale(puff.Scale, puff.Scale, puff.Scale)

              let rot = Raymath.MatrixRotate(axisN, rotAngle)
              let trans = Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)

              Raymath.MatrixMultiply(
                Raymath.MatrixMultiply(scaleMat, rot),
                trans
              )
            else
              let scaleMat =
                Raymath.MatrixScale(puff.Scale, puff.Scale, puff.Scale)

              let trans = Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)
              Raymath.MatrixMultiply(scaleMat, trans)
          else
            let scaleMat =
              Raymath.MatrixScale(puff.Scale, puff.Scale, puff.Scale)

            let trans = Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)
            Raymath.MatrixMultiply(scaleMat, trans)

        if smokeModel.MeshCount > 0 then
          buffer.model(smokeModel, smokeTransform).drop()

  // ── Bullet tracers ────────────────────────────────────────────────────────
  let bulletModel = loadOrGetModel currentModelCache Assets.bulletFoamTip ctx

  for bullet in model.Effect.Bullets do
    if bullet.Active then
      let progress = 1.0f - bullet.Timer / Bullet.duration
      let pos = Vector3.Lerp(bullet.Start, bullet.EndPos, progress)

      // Orient the bullet model along its travel direction.
      let bulletTransform =
        let n = bullet.Direction

        if n.LengthSquared() > 0.001f then
          let srcForward = Vector3.UnitY
          let rotAxis = Vector3.Cross(srcForward, n)

          let rotAngle =
            MathF.Acos(Math.Clamp(Vector3.Dot(srcForward, n), -1.0f, 1.0f))

          let trans = Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)

          if rotAxis.LengthSquared() > 0.001f then
            let rot = Raymath.MatrixRotate(Vector3.Normalize(rotAxis), rotAngle)
            Raymath.MatrixMultiply(rot, trans)
          else
            trans
        else
          Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)

      if bulletModel.MeshCount > 0 then
        buffer.model(bulletModel, bulletTransform).drop()

  // ── Ejected shell casings ─────────────────────────────────────────────────
  let shellModel = loadOrGetModel currentModelCache Assets.bulletFoam ctx

  for shell in model.Effect.Shells do
    if shell.Active then
      let pos = shell.Position
      let rot = shell.Rotation

      let shellTransform =
        let scaleMat = Raymath.MatrixScale(1.0f, 1.0f, 1.0f)
        let rotX = Raymath.MatrixRotateX(rot.X)
        let rotY = Raymath.MatrixRotateY(rot.Y)
        let rotZ = Raymath.MatrixRotateZ(rot.Z)
        let trans = Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)

        Raymath.MatrixMultiply(
          Raymath.MatrixMultiply(
            Raymath.MatrixMultiply(scaleMat, rotX),
            Raymath.MatrixMultiply(rotY, rotZ)
          ),
          trans
        )

      if shellModel.MeshCount > 0 then
        buffer.model(shellModel, shellTransform).drop()

  // ── Weapon viewmodel (blaster) ────────────────────────────────────────────
  let blasterModel =
    loadOrGetModel currentModelCache model.Weapon.EquippedWeapon ctx

  if blasterModel.MeshCount > 0 then
    let recoilZ = model.Weapon.RecoilOffset

    let weaponPos =
      ViewMath.weaponPosition
        model.Player.Position
        forward
        model.Player.Pitch
        model.Player.Yaw
        recoilZ

    let weaponTransform =
      let yawRot = Raymath.MatrixRotateY(model.Player.Yaw)
      let pitchRot = Raymath.MatrixRotateX(model.Player.Pitch)
      let trans = Raymath.MatrixTranslate(weaponPos.X, weaponPos.Y, weaponPos.Z)
      Raymath.MatrixMultiply(Raymath.MatrixMultiply(pitchRot, yawRot), trans)

    buffer.model(blasterModel, weaponTransform).drop()

  // ── Decals (PR #99 transparency probe) ────────────────────────────────────
  // Textured, semi-transparent planes. Each Material3D with Opacity < 1 routes
  // through the sorted alpha-blend pass (depth write off, depth test on). Drawn
  // inside the camera scope so they sort against the scene geometry. The plane
  // is the shared Primitive3D.plane (raylib GenMeshPlane: XZ, normal +Y).
  ensureDecalResources(GameContext.getService<IAssets> ctx)

  match decalMaterial with
  | ValueSome baseMat ->
    // Bullet-impact decals — fade opacity over their lifetime.
    for decal in model.Effect.Decals do
      if decal.Active then
        let life = decal.Timer / Decal.duration

        let mat = {
          baseMat with
              Opacity = decalBaseOpacity * life
        }

        let tf = decalTransform(decal.Position, decal.Normal)

        buffer.mesh(Primitive3D.plane, tf, mat).drop()
  | ValueNone -> ()

  buffer.endCamera().drop()

  // ── Distance fog: blend the lit scene toward fogColor by view-space distance ──
  buffer
    .postProcessWithDepth(fun pp ->
      applyFog
        model.Player.Position
        forward
        (ViewMath.cameraRightPitched model.Player.Yaw model.Player.Pitch)
        (ViewMath.cameraUp model.Player.Yaw model.Player.Pitch)
        (75.0f * MathF.PI / 180.0f)
        pp)
    .drop()

  // ── Hit-flash post-process: desaturate the scene while the effect timer runs ──
  if HudLayout.isHitFlash model then
    let intensity = model.Effect.HitEffectTimer / Constants.HitEffectDuration

    buffer.postProcess(fun pp -> applyGrayscale pp intensity).drop()
