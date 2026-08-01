module AnimatedInstancing.Raylib.Program

#nowarn "9"

open System
open FSharp.NativeInterop
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Animation
open Mibo.Input
open Raylib_cs
open AnimatedInstancing
open AnimatedInstancing.Raylib.Types
open AnimatedInstancing.Raylib.Systems

let init(ctx: GameContext) =
  let inputMap: InputMap<GameAction> =
    InputMap.empty
    |> InputMap.key Tier1 KeyCode.D1
    |> InputMap.key Tier2 KeyCode.D2
    |> InputMap.key Tier3 KeyCode.D3
    |> InputMap.key Tier4 KeyCode.D4
    |> InputMap.key TierUp KeyCode.Equal
    |> InputMap.key TierDown KeyCode.Minus
    |> InputMap.key TogglePause KeyCode.Space
    |> InputMap.key ToggleShadows KeyCode.S

  let model = Model()
  model.InputMap <- inputMap

  let assets = GameContext.getService<IAssets> ctx

  // KayKit mannequin rig: one glb carries the meshes, the skeleton, and the
  // movement clips (Walking_A/Running_A); the general-purpose clips (Idle_A)
  // ship in a second rig file with the same skeleton but a DIFFERENT bone
  // order, so the merge must remap by bone name. Same dance as Platformer3D.
  let rigPath =
    "assets/kaykit_character_animations/Rig_Medium_MovementBasic.glb"

  let generalPath = "assets/kaykit_character_animations/Rig_Medium_General.glb"

  let rigModel = assets.Model(rigPath)
  let movementAnims = assets.ModelAnimations(rigPath)
  let generalAnims = assets.ModelAnimations(generalPath)

  // raylib 6 clips carry no bone names, so each file's order comes from its
  // own model skeleton (the General model is loaded only for its bone names).
  let movementBoneNames = Animation3DClips.boneNamesOf rigModel

  let generalBoneNames = Animation3DClips.boneNamesOf(assets.Model(generalPath))

  let clips =
    Animation3DClips.merge movementBoneNames [|
      movementBoneNames, movementAnims
      generalBoneNames, generalAnims
    |]

  printfn
    $"[crowd] rig: {rigModel.MeshCount} meshes, {clips.Clips.Length} clips ({movementAnims.Length} movement + {generalAnims.Length} general)"

  // The rig ships no texture — apply the shared mannequin albedo to every
  // material of the loaded model (native map write). The albedo map color
  // must also be reset to white: the glb carries baseColorFactor 0.2, which
  // raylib keeps as the map tint and would darken the texture to near-black.
  let mannequinTex =
    assets.Texture(
      "assets/kaykit_character_animations/Textures/mannequin_texture.png"
    )

  for mi = 0 to rigModel.MaterialCount - 1 do
    let mat = NativePtr.get rigModel.Materials mi
    let mutable map = NativePtr.get mat.Maps (int MaterialMapIndex.Albedo)
    map.Texture <- mannequinTex
    map.Color <- Color.White
    NativePtr.set mat.Maps (int MaterialMapIndex.Albedo) map

  model.Rig <- { Model = rigModel; Clips = clips }
  model.AnimMesh <- AnimatedMesh.fromModel rigModel

  match model.AnimMesh with
  | ValueSome animMesh ->
    printfn $"[crowd] skeleton: {animMesh.BoneCount} bones"
  | ValueNone -> printfn "[crowd] no animated mesh (rig has no skeleton?)"

  // Ground slab: unit cube mesh, scaled to the grid at draw time.
  let mutable groundMesh = Raylib.GenMeshCube(1.0f, 1.0f, 1.0f)
  Raylib.UploadMesh(&groundMesh, false)
  model.GroundMesh <- groundMesh

  model.DiagFont <- assets.Font("assets/Fonts/monogram.ttf")

  // Start at tier 1 (500 instances).
  model.Crowd <- Crowd.init model.Rig 0

  struct (model, Cmd.none)

let subscribe (ctx: GameContext) (model: Model) =
  InputMapper.subscribeStatic model.InputMap InputMapped ctx

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withAssetsBasePath AppContext.BaseDirectory
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo Animated Instancing (raylib)"
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

      Renderer3D.create pipeline AnimatedInstancing.Raylib.View.view)
    |> Program.withRenderer(fun () ->
      Renderer2D.createWith
        Renderer2DConfig.noClear
        AnimatedInstancing.Raylib.View.viewHud)

  let game = new RaylibGame<Model, Msg>(program)
  game.Run()
  0
