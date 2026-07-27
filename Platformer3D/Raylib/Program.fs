module Platformer3D.Raylib.Program

#nowarn "9"

open System
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Elmish.Graphics2D
open Mibo.Animation
open Mibo.Input
open Raylib_cs
open Platformer3D.Constants
open Platformer3D.Types
open Platformer3D.Physics
open Platformer3D.WorldGen
open Platformer3D.Raylib.Types
open Platformer3D.Raylib.Systems

let loadInitialChunks(model: Model) =
  loadChunks spawnPosition model.Chunks.Chunks model.Chunks.Seed

// First sub-mesh + material of a loaded model — mirrors View.resolveMeshesAndMaterial.
let private firstMeshAndMaterial
  (m: Raylib_cs.Model)
  : struct (Mesh * Material3D) voption =
  if m.MeshCount > 0 then
    let mesh = NativePtr.get m.Meshes 0
    let matIdx = NativePtr.get m.MeshMaterial 0
    let raylibMat: Material = NativePtr.get m.Materials matIdx

    let material3d: Material3D = {
      Material3D.fromRaylibMaterial raylibMat with
          Roughness = 0.65f
    }

    ValueSome struct (mesh, material3d)
  else
    ValueNone

let init(ctx: GameContext) =
  let inputMap: InputMap<GameAction> =
    InputMap.empty
    |> InputMap.key MoveLeft KeyCode.A
    |> InputMap.key MoveLeft KeyCode.Left
    |> InputMap.key MoveRight KeyCode.D
    |> InputMap.key MoveRight KeyCode.Right
    |> InputMap.key MoveForward KeyCode.W
    |> InputMap.key MoveForward KeyCode.Up
    |> InputMap.key MoveBackward KeyCode.S
    |> InputMap.key MoveBackward KeyCode.Down
    |> InputMap.key Jump KeyCode.Space
    |> InputMap.key Respawn KeyCode.R
    |> InputMap.key RotateCameraLeft KeyCode.Q
    |> InputMap.key RotateCameraRight KeyCode.E
    |> InputMap.key RotateCameraUp KeyCode.PageUp
    |> InputMap.key RotateCameraDown KeyCode.PageDown

  let model = Model()
  model.InputMap <- inputMap
  model.Chunks <- Chunks.init(Random.Shared.Next())
  loadInitialChunks model

  let particleImg =
    Raylib.GenImageColor(1, 1, Color(255uy, 255uy, 255uy, 255uy))

  model.ParticleTexture <- Raylib.LoadTextureFromImage(particleImg)
  Raylib.UnloadImage(particleImg)

  let assets = GameContext.getService<IAssets> ctx
  model.JumpSound <- assets.Sound("assets/sfx_jump.ogg")

  // KayKit mannequin rig: one glb carries the meshes, the skeleton (including
  // the handslot.r/handslot.l attachment sockets), and the movement clips.
  // The general-purpose clips (Idle_A among them) ship in a second rig file
  // with the same skeleton — but a DIFFERENT bone order (right-side joints
  // first vs left-side first), so the merge must remap by bone name or the
  // general clips drive mirrored limbs. Clip names are the KayKit originals
  // (Idle_A/Walking_A/Jump_Start), which is what
  // Platformer3D.Animation.targetClip returns.
  let playerRigPath =
    "assets/kaykit_character_animations/Rig_Medium_MovementBasic.glb"

  let generalRigPath =
    "assets/kaykit_character_animations/Rig_Medium_General.glb"

  let playerModel = assets.Model(playerRigPath)
  model.PlayerModel <- playerModel

  let movementAnims = assets.ModelAnimations(playerRigPath)
  let generalAnims = assets.ModelAnimations(generalRigPath)

  // raylib 6 clips carry no bone names, so each file's order comes from its
  // own model skeleton (the General model is loaded only for its bone names).
  let movementBoneNames = Animation3DClips.boneNamesOf playerModel

  let generalBoneNames =
    Animation3DClips.boneNamesOf(assets.Model(generalRigPath))

  let clips =
    Animation3DClips.merge movementBoneNames [|
      movementBoneNames, movementAnims
      generalBoneNames, generalAnims
    |]

  model.PlayerAnimClips <- clips

  printfn
    $"[player] rig: {playerModel.MeshCount} meshes, {clips.Clips.Length} clips ({movementAnims.Length} movement + {generalAnims.Length} general)"

  model.PlayerAnim <- Animation3DState.create playerModel clips "Idle_A" 60.0f

  // The rigs ship no texture — apply the shared mannequin albedo to every
  // material of the loaded model (native map write, mirroring the mipmapping
  // loop in Mibo's AssetsService.Model). Writing the model's own materials
  // means every draw path (GPU-skinned, shadow pass, legacy fallback) picks
  // the texture up via Material3D.fromRaylibMaterial. The albedo map color
  // must also be reset to white: the glb carries baseColorFactor 0.2, which
  // raylib keeps as the map tint and would darken the texture to near-black.
  let mannequinTex =
    assets.Texture(
      "assets/kaykit_character_animations/Textures/mannequin_texture.png"
    )

  for mi = 0 to playerModel.MaterialCount - 1 do
    let mat = NativePtr.get playerModel.Materials mi
    let mutable map = NativePtr.get mat.Maps (int MaterialMapIndex.Albedo)
    map.Texture <- mannequinTex
    map.Color <- Color.White
    NativePtr.set mat.Maps (int MaterialMapIndex.Albedo) map

  // Bone-attachment demo: build the shared GPU-skinning mesh once, plus a
  // second playback state to prove one Model can render two poses per frame.
  model.PlayerAnimatedMesh <- AnimatedMesh.fromModel playerModel

  model.PlayerAnim2 <-
    Animation3DState.create playerModel clips "Walking_A" 60.0f

  match model.PlayerAnimatedMesh with
  | ValueSome animMesh ->
    let boneLabel name =
      match AnimatedMesh.tryFindBoneIndex name animMesh with
      | ValueSome i -> $"#{i}"
      | ValueNone -> "MISSING"

    let slotR = boneLabel "handslot.r"
    let slotL = boneLabel "handslot.l"

    printfn
      $"[player] skeleton: {animMesh.BoneCount} bones, handslot.r={slotR}, handslot.l={slotL}"
  | ValueNone -> printfn "[player] no animated mesh (rig has no skeleton?)"

  // Props attached to the handslot sockets at draw time: sword in the right
  // hand, wand in the left. The kaykit weapons are raw-loaded .gltf files (the
  // fsproj copies assets/** to output; Raylib.LoadModel resolves the shared
  // weapons_bits_texture.png next to each .gltf). They're authored to snap
  // onto kaykit handslots with the grip at the origin — identity local
  // transform; tune visually if a grip sits off.
  let weaponSpecs = [|
    struct ("sword_A", "handslot.r", Raymath.MatrixIdentity())
    struct ("wand_A", "handslot.l", Raymath.MatrixIdentity())
  |]

  model.PlayerProps <-
    weaponSpecs
    |> Array.choose(fun struct (file, bone, local) ->
      let weaponModel =
        assets.Model($"assets/kaykit_fantasy_weapons/{file}.gltf")

      match firstMeshAndMaterial weaponModel with
      | ValueSome struct (mesh, material) ->
        printfn $"[weapons] {file}.gltf: {mesh.VertexCount} verts"

        Some {
          BoneName = bone
          LocalTransform = local
          Mesh = mesh
          Material = material
        }
      | ValueNone -> None)

  let target = spawnPosition + Vector3(0.0f, playerHeight * 0.5f, 0.0f)
  model.Physics.CameraTarget <- target

  model.Physics.CameraPosition <-
    computeCameraPosition
      target
      model.Physics.CameraYaw
      model.Physics.CameraPitch

  struct (model, Cmd.none)

let subscribe (ctx: GameContext) (model: Model) =
  InputMapper.subscribeStatic model.InputMap InputMapped ctx

let overlayView (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  Platformer3D.Raylib.MinimapView.view ctx model buffer
  Platformer3D.Raylib.DiagnosticsView.view ctx model buffer

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withAssetsBasePath AppContext.BaseDirectory
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo 3D Platformer"
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Tick
    |> Program.withRenderer(fun () ->
      let pipeline =
        ForwardPbrPipeline(
          shadowBiasConfig = {
            ShadowBiasConfig.defaults with
                DirectionalBias = 0.002f
                SlopeScaleBias = 0.0008f
          },
          shadowAtlasConfig = {
            ShadowAtlasConfig.defaults with
                Resolution = 1024 * 4
          }

        )

      Renderer3D.create pipeline Platformer3D.Raylib.View.view)
    |> Program.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear overlayView)

  let game = new RaylibGame<Model, Msg>(program)
  game.Run()
  0
