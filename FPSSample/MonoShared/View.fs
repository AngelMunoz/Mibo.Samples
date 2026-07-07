module FPSSample.MonoShared.View

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Audio
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open Mibo.Layout3D
open FPSSample
open FPSSample.Types

let loadOrGetModel (path: string) (ctx: GameContext) =
  let assets = GameContext.getService<IAssets> ctx
  // Convert raylib-style path to MonoGame content pipeline path:
  // "assets/kenney_platformer-kit/Models/block-grass.glb" -> "kenney_platformer-kit/Models/block-grass"
  let mgPath =
    path.Replace("assets/", "").Replace(".glb", "").Replace(".fbx", "")

  let m = assets.Model(mgPath)
  m

/// Converts a raylib-style asset path to a MonoGame content pipeline path.
let mgAssetPath(path: string) =
  path
    .Replace("assets/", "")
    .Replace(".glb", "")
    .Replace(".fbx", "")
    .Replace(".mp3", "")
    .Replace(".wav", "")

let private meshMaterialCache =
  Dictionary<string, struct (PrimitiveMesh * Material3D)[]>()

let mutable private currentGameContext = Unchecked.defaultof<GameContext>

let private blockBounds = BoundingSphere(Vector3.Zero, 1.5f)

let inline private wrapPartAsPrimitive(part: ModelMeshPart) : PrimitiveMesh = {
  Vertices = part.VertexBuffer
  Indices = part.IndexBuffer
  PrimitiveCount = part.PrimitiveCount
  Bounds = blockBounds
}

let private resolveMeshesAndMaterial(cell: Level.Cell) =
  let path = Level.Cell.modelPath cell

  match meshMaterialCache.TryGetValue path with
  | true, cached -> cached
  | false, _ ->
    let m = loadOrGetModel path currentGameContext

    let result =
      if not(isNull m) && m.Meshes.Count > 0 then
        [|
          for mesh in m.Meshes do
            for part in mesh.MeshParts do
              let mat = {
                Material3D.fromModelMeshPart part with
                    Roughness = 0.65f
                    Metallic = 0.1f
              }

              struct (wrapPartAsPrimitive part, mat)
        |]
      else
        Array.empty

    meshMaterialCache[path] <- result
    result

// Persistent instanced context for level geometry.
let private instancedCtx =
  InstancedRenderContext<Level.Cell, string>(
    getKey = Level.Cell.modelPath,
    getMeshesAndMaterial = resolveMeshesAndMaterial,
    getTransform = fun worldPos _cell -> Matrix.CreateTranslation(worldPos)
  )

// ─────────────────────────────────────────────────────────────────────────────
// Enemy animation registry.
//
// MonoGame doesn''t mutate model bones like raylib, so multiple enemies can
// share the same Model. The AnimatedModel (Model + Mesh + per-enemy State) is
// stored here, one per enemy, cycling different characters for variety.
// Program.fs init populates this; the view reads it each frame.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MonoGame-specific enemy animation service. Manages per-enemy AnimatedModel
/// states. Unlike raylib, MonoGame doesn't mutate model bones, so multiple
/// enemies can share the same Model — only the animation state is per-enemy.
/// </summary>
type EnemyAnimationService() =
  let states = ResizeArray<AnimatedModel>()
  let lastAnims = ResizeArray<string>()

  member _.States = states

  interface IEnemyAnimationService with
    member _.Init(ctx: GameContext, enemyCount: int) : unit =
      states.Clear()
      lastAnims.Clear()
      let assets = GameContext.getService<IAssets> ctx

      for i in 0 .. enemyCount - 1 do
        let assetPath = Assets.character(i)
        // Strip "assets/" prefix and file extension for content pipeline path
        let mgPath =
          assetPath.Replace("assets/", "")
          |> System.IO.Path.GetFileNameWithoutExtension

        let mgPath = "kenney_platformer-kit/Models/" + mgPath

        // Assimp loads the raw file at runtime for animation data
        let rawModelPath =
          AppContext.BaseDirectory
          + "animations/"
          + System.IO.Path.GetFileName(assetPath)

        let model = assets.Model(mgPath)
        let animatedMesh = assets.AnimatedMesh(rawModelPath)
        let clips = assets.ModelAnimations(rawModelPath)

        let am = AnimatedModel.create model animatedMesh clips "idle" 60.0f
        states.Add(am)
        lastAnims.Add("idle")

    member _.Update(dt: float32, enemies: Enemy[]) : unit =
      for i = 0 to min enemies.Length states.Count - 1 do
        let enemy = enemies[i]

        if enemy.State <> EnemyState.Dead then
          let am = states[i]

          // Only blend when the target clip changes — calling blendTo every frame
          // resets the blend progress and the animation never transitions.
          let newAm =
            if i < lastAnims.Count && lastAnims[i] <> enemy.CurrentAnim then
              lastAnims[i] <- enemy.CurrentAnim
              am |> AnimatedModel.blendTo enemy.CurrentAnim 0.15f
            else
              am

          states[i] <- AnimatedModel.update dt newAm

/// Converts a System.Numerics.Vector3 to an XNA Vector3.
let inline toXnaV(v: System.Numerics.Vector3) = Vector3(v.X, v.Y, v.Z)

/// Builds a rotation matrix that maps +Y to the given direction (for orienting
/// models that point up by default along a travel vector).
let orientAlong(dir: Vector3) : Matrix =
  if dir.LengthSquared() > 0.001f then
    let n = Vector3.Normalize(dir)
    let srcForward = Vector3.Up
    let rotAxis = Vector3.Cross(srcForward, n)
    let dot = Math.Max(-1.0f, Math.Min(1.0f, Vector3.Dot(srcForward, n)))
    let rotAngle = MathF.Acos(dot)

    if rotAxis.LengthSquared() > 0.001f then
      Matrix.CreateFromAxisAngle(Vector3.Normalize(rotAxis), rotAngle)
    else
      Matrix.Identity
  else
    Matrix.Identity

// ─────────────────────────────────────────────────────────────────────────────
// Hit-flash post-process: desaturates the whole 3D scene based on the remaining
// hit-effect timer. The effect is the compiled Grayscale.mgfx, loaded once.
// ─────────────────────────────────────────────────────────────────────────────

let mutable private grayscaleEffect: Effect voption = ValueNone
let mutable private fogEffect: Effect voption = ValueNone

// Distance fog — fades the lit scene toward a dark color with distance. The FPS arena is a
// torch-lit night, so geometry near a torch stays lit while everything further out sinks into
// fog. Tuned against the camera near/far (0.1 / 1000) and arena span (~22 units): fog starts a
// few units out (just past the torch glow) and is fully opaque before the arena's far wall.
let private fogColor = Vector3(0.02f, 0.02f, 0.03f)
let private fogNear = 6.0f
let private fogFar = 32.0f
let private cameraNear = 0.1f
let private cameraFar = 1000.0f

/// Renders the 3D scene from a first-person camera.
let view
  (animService: EnemyAnimationService)
  (ctx: GameContext)
  (model: GameModel)
  (buffer: RenderBuffer3D)
  =
  let assets = GameContext.getService<IAssets> ctx

  // ── First-person camera ───────────────────────────────────────────────────
  let forwardNumerics =
    ViewMath.cameraForward model.Player.Yaw model.Player.Pitch

  let forward = toXnaV forwardNumerics

  let pos = toXnaV model.Player.Position
  let target = pos + forward

  let camera: Camera3D = {
    Position = pos
    Target = target
    Up = Vector3.Up
    FovY = MathHelper.ToRadians(75.0f)
    NearPlane = cameraNear
    FarPlane = cameraFar
    Projection = CameraProjection.Perspective
  }

  buffer
  |> Draw3D.beginCameraWith(
    Camera3D.render camera
    |> Camera3D.withClear(
      Mibo.MonoGameColor.toMonoGameColor ViewMath.clearColor
    )
  )
  |> Draw3D.setAmbientLight ViewMath.ambientLight
  |> Draw3D.addDirectionalLight ViewMath.directionalLight
  |> Draw3D.drop

  // ── Starry skybox (drawn first inside camera so scene renders on top) ─────
  buffer |> Skybox.render assets model.TotalTime model.Player.Position

  // ── Muzzle flash point light ──────────────────────────────────────────────
  if model.Weapon.MuzzleFlash.Active then
    let flashPosNumerics =
      ViewMath.muzzleWorldPosition
        model.Player.Position
        forwardNumerics
        model.Player.Pitch
        model.Player.Yaw

    buffer
    |> Draw3D.addPointLight(ViewMath.muzzleFlashLight flashPosNumerics)
    |> Draw3D.drop

  // ── Static torches (flickering point lights around the arena) ───────────────
  let torches = ViewMath.torchPositions

  for i = 0 to torches.Length - 1 do
    let tPos = torches[i]
    let phase = float32 i * 1.7f
    let flicker = MathF.Sin(model.TotalTime * 7.0f + phase) * 0.25f

    buffer
    |> Draw3D.addPointLight(ViewMath.torchLight tPos flicker)
    |> Draw3D.drop

  // ── Level geometry (instanced) ────────────────────────────────────────────
  currentGameContext <- ctx
  instancedCtx.ResetFrameBuffers()

  CellGridRenderer3D.renderInstanced instancedCtx model.Level.Grid buffer

  // ── Enemies (animated models) ─────────────────────────────────────────────
  for i = 0 to model.Enemy.Enemies.Length - 1 do
    let enemy = model.Enemy.Enemies[i]

    if enemy.State <> EnemyState.Dead && i < animService.States.Count then
      let am = animService.States[i]
      let ePos = enemy.Position

      let transform =
        let rot = Matrix.CreateRotationY(enemy.Facing)
        let trans = Matrix.CreateTranslation(ePos.X, ePos.Y, ePos.Z)
        rot * trans

      buffer |> Draw3D.drawAnimatedModel am transform |> Draw3D.drop

  // ── Pickups ───────────────────────────────────────────────────────────────
  let healthModel = loadOrGetModel Assets.heart ctx
  let ammoModel = loadOrGetModel Assets.coinGold ctx

  for pickup in model.Pickup.Pickups do
    if pickup.IsActive then
      let mdl, p =
        match pickup.Kind with
        | Level.PickupKind.Health -> healthModel, pickup.Position
        | Level.PickupKind.Ammo -> ammoModel, pickup.Position

      if not(isNull mdl) && mdl.Meshes.Count > 0 then
        let bobY = ViewMath.pickupBob model.TotalTime
        let transform = Matrix.CreateTranslation(p.X, p.Y + bobY, p.Z)
        buffer |> Draw3D.drawModel mdl transform |> Draw3D.drop

  // ── Muzzle smoke puffs ────────────────────────────────────────────────────
  let smokeModel = loadOrGetModel Assets.smoke ctx

  for puff in model.Effect.SmokePuffs do
    if puff.Active then
      let life = 1.0f - puff.Timer / SmokePuff.duration
      let alpha = 1.0f - life

      if alpha > 0.01f then
        let pos = toXnaV puff.Position
        let dir = toXnaV puff.Velocity

        let smokeTransform =
          orientAlong dir
          * Matrix.CreateScale(puff.Scale)
          * Matrix.CreateTranslation(pos)

        if not(isNull smokeModel) && smokeModel.Meshes.Count > 0 then
          buffer |> Draw3D.drawModel smokeModel smokeTransform |> Draw3D.drop

  // ── Bullet tracers ────────────────────────────────────────────────────────
  let bulletModel = loadOrGetModel Assets.bulletFoamTip ctx

  for bullet in model.Effect.Bullets do
    if bullet.Active then
      let progress = 1.0f - bullet.Timer / Bullet.duration

      let pos =
        toXnaV(
          System.Numerics.Vector3.Lerp(bullet.Start, bullet.EndPos, progress)
        )

      let bulletTransform =
        orientAlong(toXnaV bullet.Direction) * Matrix.CreateTranslation(pos)

      if not(isNull bulletModel) && bulletModel.Meshes.Count > 0 then
        buffer |> Draw3D.drawModel bulletModel bulletTransform |> Draw3D.drop

  // ── Ejected shell casings ─────────────────────────────────────────────────
  let shellModel = loadOrGetModel Assets.bulletFoam ctx

  for shell in model.Effect.Shells do
    if shell.Active then
      let pos = toXnaV shell.Position
      let rot = toXnaV shell.Rotation

      let shellTransform =
        Matrix.CreateFromYawPitchRoll(rot.Y, rot.X, rot.Z)
        * Matrix.CreateTranslation(pos)

      if not(isNull shellModel) && shellModel.Meshes.Count > 0 then
        buffer |> Draw3D.drawModel shellModel shellTransform |> Draw3D.drop

  // ── Weapon viewmodel (blaster) ────────────────────────────────────────────
  let blasterModel = loadOrGetModel model.Weapon.EquippedWeapon ctx

  if not(isNull blasterModel) && blasterModel.Meshes.Count > 0 then
    let weaponPosNumerics =
      ViewMath.weaponPosition
        model.Player.Position
        forwardNumerics
        model.Player.Pitch
        model.Player.Yaw
        model.Weapon.RecoilOffset

    let weaponPos = toXnaV weaponPosNumerics

    let transform =
      let yawRot = Matrix.CreateRotationY(model.Player.Yaw)
      let pitchRot = Matrix.CreateRotationX(model.Player.Pitch)
      let trans = Matrix.CreateTranslation(weaponPos)
      pitchRot * yawRot * trans

    buffer |> Draw3D.drawModel blasterModel transform |> Draw3D.drop

  buffer |> Draw3D.endCamera |> Draw3D.drop

  // ── Distance fog: blend the lit scene toward fogColor by view-space distance ──
  // Runs before the hit-flash so a desaturated frame still shows the fog. Always draws (never
  // a no-op): when the pipeline couldn't produce depth, fogStrength=0 passes the scene through
  // unchanged so the back-buffer is never left blank.
  // ── Distance fog: blend the lit scene toward fogColor by view-space distance ──
  // Runs before the hit-flash so a desaturated frame still shows the fog. Always draws (never
  // a no-op): when the pipeline couldn't produce depth, fogStrength=0 passes the scene through
  // unchanged so the back-buffer is never left blank.
  buffer
  |> Draw3D.postProcessWithDepth(fun pp ->
    let e =
      match fogEffect with
      | ValueSome e -> e
      | ValueNone ->
        let e = assets.Effect("Fog")
        fogEffect <- ValueSome e
        e

    e.Parameters["SceneTexture"].SetValue(pp.Source)

    match pp.Depth with
    | ValueSome depthTex ->
      e.Parameters["DepthTexture"].SetValue(depthTex)
      e.Parameters["fogStrength"].SetValue(1.0f)
    | ValueNone ->
      // No depth this frame — bind the scene itself so the depth sampler is valid, and disable
      // the fog blend (fogStrength=0 → scene passes through unchanged).
      e.Parameters["DepthTexture"].SetValue(pp.Source)
      e.Parameters["fogStrength"].SetValue(0.0f)

    e.Parameters["fogColor"].SetValue(fogColor)
    e.Parameters["fogNear"].SetValue(fogNear)
    e.Parameters["fogFar"].SetValue(fogFar)
    e.Parameters["cameraNear"].SetValue(cameraNear)
    e.Parameters["cameraFar"].SetValue(cameraFar)
    pp.Quad.Draw(e))
  |> Draw3D.drop

  // ── Hit-flash post-process: desaturate the scene while the effect timer runs ──
  if HudLayout.isHitFlash model then
    let intensity = model.Effect.HitEffectTimer / Constants.HitEffectDuration

    buffer
    |> Draw3D.postProcess(fun pp ->
      match grayscaleEffect with
      | ValueNone ->
        let e = assets.Effect("Grayscale")
        grayscaleEffect <- ValueSome e
        e.Parameters["SceneTexture"].SetValue(pp.Source)
        e.Parameters["intensity"].SetValue(intensity)
        pp.Quad.Draw(e)
      | ValueSome e ->
        e.Parameters["SceneTexture"].SetValue(pp.Source)
        e.Parameters["intensity"].SetValue(intensity)
        pp.Quad.Draw(e))
    |> Draw3D.drop
