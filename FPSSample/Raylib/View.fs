module FPSSample.Raylib.View

#nowarn "9"

open System
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open Raylib_cs
open Mibo.Elmish
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
  |> Draw3D.beginCameraWith(
    Camera3D.render camera
    |> Camera3D.withClear(Mibo.RaylibColor.toRaylibColor ViewMath.clearColor)
  )
  |> Draw3D.setAmbientLight ViewMath.ambientLight
  |> Draw3D.addDirectionalLight ViewMath.directionalLight
  |> Draw3D.drop

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

    buffer
    |> Draw3D.addPointLight(ViewMath.muzzleFlashLight flashPos)
    |> Draw3D.drop
  // ── Static torches (flickering point lights around the arena) ───────────────
  let torches = ViewMath.torchPositions

  for i = 0 to torches.Length - 1 do
    let pos = torches[i]
    // Independent flicker per torch using a phase offset and the game time.
    let phase = float32 i * 1.7f
    let flicker = MathF.Sin(model.TotalTime * 7.0f + phase) * 0.25f

    buffer
    |> Draw3D.addPointLight(ViewMath.torchLight pos flicker)
    |> Draw3D.drop

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

      Animation3DState.applyToModel animState

      let transform =
        let rot = Raymath.MatrixRotateY(enemy.Facing)
        let trans = Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)
        Raymath.MatrixMultiply(rot, trans)

      buffer |> Draw3D.drawModel animState.Model transform |> Draw3D.drop

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
      buffer |> Draw3D.drawModel mdl transform |> Draw3D.drop

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
          buffer |> Draw3D.drawModel smokeModel smokeTransform |> Draw3D.drop

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

    buffer |> Draw3D.drawModel blasterModel weaponTransform |> Draw3D.drop

  buffer |> Draw3D.endCamera |> Draw3D.drop
