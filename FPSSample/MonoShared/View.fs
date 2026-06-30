module FPSSample.MonoShared.View

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
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

/// Renders the 3D scene from a first-person camera.
let view
  (animService: EnemyAnimationService)
  (ctx: GameContext)
  (model: GameModel)
  (buffer: RenderBuffer3D)
  =
  // ── First-person camera ───────────────────────────────────────────────────
  let forwardNumerics = ViewMath.cameraForward model.PlayerYaw model.PlayerPitch
  let forward = Vector3(forwardNumerics.X, forwardNumerics.Y, forwardNumerics.Z)

  let pos =
    Vector3(
      model.PlayerPosition.X,
      model.PlayerPosition.Y,
      model.PlayerPosition.Z
    )

  let target = pos + forward

  let camera: Camera3D = {
    Position = pos
    Target = target
    Up = Vector3.Up
    FovY = MathHelper.ToRadians(75.0f)
    NearPlane = 0.1f
    FarPlane = 1000.0f
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

  // ── Level geometry (instanced) ────────────────────────────────────────────
  currentGameContext <- ctx
  instancedCtx.ResetFrameBuffers()

  CellGridRenderer3D.renderInstanced instancedCtx model.Level.Grid buffer

  // ── Enemies (animated models) ─────────────────────────────────────────────
  for i = 0 to model.Enemies.Length - 1 do
    let enemy = model.Enemies[i]

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

  for pickup in model.Pickups do
    if pickup.IsActive then
      let mdl, p =
        match pickup.Kind with
        | Level.PickupKind.Health -> healthModel, pickup.Position
        | Level.PickupKind.Ammo -> ammoModel, pickup.Position

      if not(isNull mdl) && mdl.Meshes.Count > 0 then
        let bobY = ViewMath.pickupBob model.TotalTime
        let transform = Matrix.CreateTranslation(p.X, p.Y + bobY, p.Z)
        buffer |> Draw3D.drawModel mdl transform |> Draw3D.drop

  // ── Shot tracers (traveling bullet model) ────────────────────────────────
  let bulletModel = loadOrGetModel Assets.bulletFoamTip ctx

  for tracer in model.Tracers do
    if tracer.Active then
      let progress = ViewMath.tracerProgress tracer

      let pos =
        Microsoft.Xna.Framework.Vector3.Lerp(
          Microsoft.Xna.Framework.Vector3(
            tracer.Start.X,
            tracer.Start.Y,
            tracer.Start.Z
          ),
          Microsoft.Xna.Framework.Vector3(
            tracer.End.X,
            tracer.End.Y,
            tracer.End.Z
          ),
          progress
        )

      let dir =
        Microsoft.Xna.Framework.Vector3(
          tracer.End.X - tracer.Start.X,
          tracer.End.Y - tracer.Start.Y,
          tracer.End.Z - tracer.Start.Z
        )

      if not(isNull bulletModel) && bulletModel.Meshes.Count > 0 then
        let bulletTransform =
          if dir.LengthSquared() > 0.001f then
            let n = Microsoft.Xna.Framework.Vector3.Normalize(dir)
            // The bullet model points up (+Y) by default
            let srcForward = Microsoft.Xna.Framework.Vector3.Up
            let rotAxis = Microsoft.Xna.Framework.Vector3.Cross(srcForward, n)
            let dot = Microsoft.Xna.Framework.Vector3.Dot(srcForward, n)
            let rotAngle = MathF.Acos(Math.Max(-1.0f, Math.Min(1.0f, dot)))

            let rot =
              if rotAxis.LengthSquared() > 0.001f then
                Microsoft.Xna.Framework.Matrix.CreateFromAxisAngle(
                  Microsoft.Xna.Framework.Vector3.Normalize(rotAxis),
                  rotAngle
                )
              else
                Microsoft.Xna.Framework.Matrix.Identity

            rot * Microsoft.Xna.Framework.Matrix.CreateTranslation(pos)
          else
            Microsoft.Xna.Framework.Matrix.CreateTranslation(pos)

        buffer |> Draw3D.drawModel bulletModel bulletTransform |> Draw3D.drop

      // Faint tracer line from start to current pos
      let alpha = ViewMath.tracerAlpha tracer
      let c = Color(255, 230, 100, int(alpha * 255.0f))

      let startV =
        Microsoft.Xna.Framework.Vector3(
          tracer.Start.X,
          tracer.Start.Y,
          tracer.Start.Z
        )

      buffer |> Draw3D.drawLine3D startV pos c |> Draw3D.drop

  // ── Weapon viewmodel (blaster) ────────────────────────────────────────────
  let blasterModel = loadOrGetModel Assets.blasterA ctx

  if not(isNull blasterModel) && blasterModel.Meshes.Count > 0 then
    let weaponPosNumerics =
      ViewMath.weaponPosition
        model.PlayerPosition
        forwardNumerics
        model.PlayerYaw

    let weaponPos =
      Microsoft.Xna.Framework.Vector3(
        weaponPosNumerics.X,
        weaponPosNumerics.Y,
        weaponPosNumerics.Z
      )

    let transform =
      let rot = Matrix.CreateRotationY(model.PlayerYaw)
      let trans = Matrix.CreateTranslation(weaponPos)
      rot * trans

    buffer |> Draw3D.drawModel blasterModel transform |> Draw3D.drop

  // ── Muzzle flash point light ──────────────────────────────────────────────
  if model.MuzzleFlash.Active then
    let flashPosNumerics =
      ViewMath.muzzleFlashPosition model.PlayerPosition forwardNumerics

    buffer
    |> Draw3D.addPointLight(ViewMath.muzzleFlashLight flashPosNumerics)
    |> Draw3D.drop

  buffer |> Draw3D.endCamera |> Draw3D.drop
